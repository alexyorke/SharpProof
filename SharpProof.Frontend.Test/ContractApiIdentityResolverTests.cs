using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ContractApiIdentityResolverTests
{
    private static readonly ImmutableArray<MetadataReference>
        PlatformReferences = CreatePlatformReferences();

    [Test]
    public void ExactUnsignedPackagePayloadIsAccepted()
    {
        var assembly = typeof(SharpProof.Attributes.Contract).Assembly;
        var reference = MetadataReference.CreateFromFile(assembly.Location);
        var resolver = ContractApiIdentityResolver.ForCompilation(
            CreateConsumer(reference));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(assembly.GetName().GetPublicKey(), Is.Empty);
            Assert.That(resolver.Contract, Is.Not.Null);
            Assert.That(
                resolver.ResolveAttribute(ContractApiMetadata.NotNull),
                Is.Not.Null);
        }
    }

    [Test]
    public void PayloadHashMustBindToTheMetadataReferenceImage()
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        var trustedPath = typeof(SharpProof.Attributes.Contract).Assembly.Location;
        var path = Path.Combine(
            temporaryDirectory,
            "SharpProof.Attributes.dll");
        try
        {
            File.WriteAllBytes(path, EmitContractImage(validContractShape: true));
            var reference = MetadataReference.CreateFromFile(path);
            var compilation = CreateConsumer(reference);
            Assert.That(
                compilation.GetTypeByMetadataName(
                    ContractApiMetadata.Contract),
                Is.Not.Null);
            File.Copy(trustedPath, path, overwrite: true);

            var resolver = ContractApiIdentityResolver.ForCompilation(compilation);

            Assert.That(resolver.Contract, Is.Null);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnapprovedContractPayloadRejectsSamePackageAttributes(
        bool validContractShape)
    {
        var temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(
                temporaryDirectory,
                "SharpProof.Attributes.dll");
            File.WriteAllBytes(
                path,
                EmitContractImage(validContractShape));
            var reference = MetadataReference.CreateFromFile(path);
            var compilation = CreateConsumer(reference);
            var resolver =
                ContractApiIdentityResolver.ForCompilation(compilation);
            var method = compilation.GetTypeByMetadataName("Target")!
                .GetMembers("Read")
                .OfType<IMethodSymbol>()
                .Single();
            var attributes = method.GetAttributes()
                .Concat(method.Parameters.SelectMany(static parameter =>
                    parameter.GetAttributes()))
                .Concat(method.GetReturnTypeAttributes())
                .ToImmutableArray();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(resolver.Contract, Is.Null);
                Assert.That(
                    resolver.ResolveAttribute(ContractApiMetadata.NotNull),
                    Is.Null);
                Assert.That(
                    resolver.ResolveAttribute(ContractApiMetadata.Positive),
                    Is.Null);
                Assert.That(
                    resolver.ResolveAttribute(ContractApiMetadata.InRange),
                    Is.Null);
                Assert.That(
                    resolver.ResolveAttribute(ContractApiMetadata.EffectContract),
                    Is.Null);
                Assert.That(
                    resolver.ResolveAttribute(ContractApiMetadata.Trusted),
                    Is.Null);
            }

            var rejected = attributes.Select(attribute =>
            {
                Assert.That(
                    resolver.TryGetRejectedAttributeMetadataName(
                        attribute,
                        out var metadataName),
                    Is.True);
                return metadataName;
            });
            Assert.That(
                rejected,
                Is.EquivalentTo(new[]
                {
                    ContractApiMetadata.Trusted,
                    ContractApiMetadata.EffectContract,
                    ContractApiMetadata.NotNull,
                    ContractApiMetadata.Positive,
                    ContractApiMetadata.InRange,
                    ContractApiMetadata.NotNull
                }));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static CSharpCompilation CreateConsumer(
        PortableExecutableReference contractReference)
    {
        var tree = CSharpSyntaxTree.ParseText(
            """
            public static class Target {
                [SharpProof.Attributes.SharpProofTrusted("reviewed")]
                [SharpProof.Attributes.EffectContract(
                    SharpProof.Attributes.SharpProofEffect.None,
                    Complete = true)]
                [return: SharpProof.Attributes.NotNull]
                public static string Read(
                    [SharpProof.Attributes.NotNull] string text,
                    [SharpProof.Attributes.Positive] int positive,
                    [SharpProof.Attributes.InRange(1, 5)] int range) =>
                    text;
            }
            """,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            "Consumer.cs");
        var compilation = CSharpCompilation.Create(
            "MalformedContractConsumer",
            [tree],
            PlatformReferences.Add(contractReference),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        AssertNoErrors(compilation);
        return compilation;
    }

    private static byte[] EmitContractImage(bool validContractShape)
    {
        var version =
            typeof(ContractApiIdentityResolver).Assembly
                .GetName().Version ??
            throw new InvalidOperationException(
                "SharpProof.Frontend has no assembly version.");
        var conditional = validContractShape
            ? """
              [System.Diagnostics.Conditional(
                  ConditionalSymbol)]
              """
            : string.Empty;
        var source =
            $$"""
            using System.Reflection;

            [assembly: AssemblyVersion("{{version}}")]

            namespace SharpProof.Attributes {
                public static class Contract {
                    public const string ConditionalSymbol =
                        "SHARPPROOF_CONTRACTS";

                    {{conditional}}
                    public static void Requires(bool condition) {
                    }

                    {{conditional}}
                    public static void Ensures(bool condition) {
                    }

                    {{conditional}}
                    public static void Assume(bool condition) {
                    }

                    public static T Result<T>() => default!;
                    public static T Old<T>(T value) => value;
                }

                public enum SharpProofEffect {
                    None = 0
                }

                [System.AttributeUsage(
                    System.AttributeTargets.Parameter |
                    System.AttributeTargets.ReturnValue)]
                public sealed class NotNullAttribute : System.Attribute {
                }

                [System.AttributeUsage(
                    System.AttributeTargets.Parameter |
                    System.AttributeTargets.ReturnValue)]
                public sealed class PositiveAttribute : System.Attribute {
                }

                [System.AttributeUsage(
                    System.AttributeTargets.Parameter |
                    System.AttributeTargets.ReturnValue)]
                public sealed class InRangeAttribute : System.Attribute {
                    public InRangeAttribute(
                        long minimum,
                        long maximum) {
                    }
                }

                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class EffectContractAttribute :
                    System.Attribute {
                    public EffectContractAttribute(
                        SharpProofEffect effects) {
                    }

                    public bool Complete {
                        get;
                        set;
                    }
                }

                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class SharpProofTrustedAttribute :
                    System.Attribute {
                    public SharpProofTrustedAttribute(string reason) {
                    }
                }
            }
            """;
        var compilation = CSharpCompilation.Create(
            "SharpProof.Attributes",
            [CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.CSharp12),
                "SharpProof.Attributes.cs")],
            PlatformReferences,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        AssertNoErrors(compilation);
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.That(
            result.Success,
            Is.True,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static diagnostic =>
                    diagnostic.ToString())));
        return stream.ToArray();
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "SharpProof.Frontend.Test"));
        var path = Path.GetFullPath(Path.Combine(
            root,
            Guid.NewGuid().ToString("N")));
        var expectedPrefix =
            root.TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!path.StartsWith(
                expectedPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Temporary test directory escaped its intended root.");
        }

        Directory.CreateDirectory(path);
        return path;
    }

    private static ImmutableArray<MetadataReference>
        CreatePlatformReferences()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException(
                "Trusted platform assemblies are unavailable.");
        return [.. trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Where(static path => !string.Equals(
                Path.GetFileNameWithoutExtension(path),
                "SharpProof.Attributes",
                StringComparison.OrdinalIgnoreCase))
            .Select(static path => MetadataReference.CreateFromFile(path))];
    }

    private static void AssertNoErrors(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        Assert.That(
            errors,
            Is.Empty,
            string.Join(
                Environment.NewLine,
                errors.Select(static diagnostic =>
                    diagnostic.ToString())));
    }
}
