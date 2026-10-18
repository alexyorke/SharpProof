using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ContractApiIdentityResolverTests
{
    [Test]
    public void ExactStrongNamedPackagePayloadIsAccepted()
    {
        var assembly = typeof(SharpProof.Attributes.Contract).Assembly;
        var reference = MetadataReference.CreateFromFile(assembly.Location);
        var resolver = ContractApiIdentityResolver.ForCompilation(
            CreateConsumer(reference));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Convert.ToHexString(assembly.GetName().GetPublicKeyToken()!), Is.EqualTo("75825C3E099F5FAC"));
            Assert.That(resolver.Contract, Is.Not.Null);
            Assert.That(
                resolver.ResolveAttribute(ContractApiMetadata.NotNull),
                Is.Not.Null);
        }
    }

    [Test]
    public void StrongNameIdentityDoesNotAuthorizeModifiedPackageBytes()
    {
        var authentic = typeof(SharpProof.Attributes.Contract).Assembly;
        var image = File.ReadAllBytes(authentic.Location);
        using (var pe = new PEReader(new MemoryStream(image, writable: false)))
        {
            var signature = pe.PEHeaders.CorHeader!.StrongNameSignatureDirectory;
            Assert.That(pe.PEHeaders.TryGetDirectoryOffset(signature, out var offset), Is.True);
            image[offset] ^= 1;
        }
        using var temporary = new TempDirectory("SharpProof.SignedPayload.Test-");
        var path = Path.Combine(temporary.FullName, "SharpProof.Attributes.dll");
        File.WriteAllBytes(path, image);
        Assert.That(AssemblyName.GetAssemblyName(path).FullName, Is.EqualTo(authentic.GetName().FullName));
        var resolver = ContractApiIdentityResolver.ForCompilation(CreateConsumer(MetadataReference.CreateFromFile(path)));
        Assert.That(resolver.Contract, Is.Null);
        Assert.That(resolver.ResolveAttribute(ContractApiMetadata.NotNull), Is.Null);
        Assert.That(resolver.UnreadableContractApiReason, Is.Null,
            "A changed payload is an identity rejection, not an unreadable-file diagnostic.");
    }

    [Test]
    public void ExactInMemoryPackagePayloadIsAccepted()
    {
        var assembly = typeof(SharpProof.Attributes.Contract).Assembly;
        var reference = MetadataReference.CreateFromImage(
            File.ReadAllBytes(assembly.Location));
        var resolver = ContractApiIdentityResolver.ForCompilation(
            CreateConsumer(reference));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reference.FilePath, Is.Null);
            Assert.That(resolver.Contract, Is.Not.Null);
            Assert.That(
                resolver.ResolveAttribute(ContractApiMetadata.NotNull),
                Is.Not.Null);
        }
    }

    [Test]
    public void DuplicateAuthenticReferenceViewsRemainAccepted()
    {
        var assembly = typeof(SharpProof.Attributes.Contract).Assembly;
        var reference = MetadataReference.CreateFromFile(assembly.Location);
        var resolver = ContractApiIdentityResolver.ForCompilation(
            CreateConsumer(reference, duplicateReference: true));

        Assert.That(resolver.Contract, Is.Not.Null);
    }

    [Test]
    public void UnapprovedInMemoryContractPayloadIsRejected()
    {
        var reference = MetadataReference.CreateFromImage(
            EmitContractImage(validContractShape: true));
        var resolver = ContractApiIdentityResolver.ForCompilation(
            CreateConsumer(reference));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reference.FilePath, Is.Null);
            Assert.That(resolver.Contract, Is.Null);
            Assert.That(
                resolver.ResolveAttribute(ContractApiMetadata.NotNull),
                Is.Null);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnapprovedContractPayloadRejectsSamePackageAttributes(
        bool validContractShape)
    {
        using var temporary = new TempDirectory("SharpProof.Frontend.Test-");
        var path = Path.Combine(
            temporary.FullName,
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
                parameter is null ? throw new System.ArgumentNullException(nameof(parameter)) : (parameter.GetAttributes())))
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
            System.ArgumentNullException.ThrowIfNull(attribute);
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

    private static CSharpCompilation CreateConsumer(
        PortableExecutableReference contractReference,
        bool duplicateReference = false)
    {
        System.ArgumentNullException.ThrowIfNull(contractReference);
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
            duplicateReference
                ? TestMetadataReferences.WithoutSharpProof
                    .Add(contractReference).Add(contractReference)
                : TestMetadataReferences.WithoutSharpProof.Add(contractReference),
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
                        "{{SharpProof.Attributes.Contract.ConditionalSymbol}}";

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
            TestMetadataReferences.WithoutSharpProof,
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
                    diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString()))));
        return stream.ToArray();
    }

    private static void AssertNoErrors(Compilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error))
            .ToImmutableArray();
        Assert.That(
            errors,
            Is.Empty,
            string.Join(
                Environment.NewLine,
                errors.Select(static diagnostic =>
                    diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.ToString()))));
    }
}
