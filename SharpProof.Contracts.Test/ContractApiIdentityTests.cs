using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractApiIdentityTests
{
    private static readonly CSharpParseOptions ParseOptions = new(
        LanguageVersion.CSharp12,
        preprocessorSymbols: [Contract.ConditionalSymbol]);
    private static readonly ImmutableArray<MetadataReference> PlatformReferences =
        [.. TestMetadataReferences.Platform.Where(static reference =>
            reference.Display is not { } display ||
            !string.Equals(
                Path.GetFileNameWithoutExtension(display),
                "SharpProof.Attributes",
                StringComparison.OrdinalIgnoreCase))];

    [Test]
    public void MatchingPackageReferenceIsAdmitted()
    {
        var compilation = CreateConsumer(
            MetadataReference.CreateFromFile(
                typeof(Contract).Assembly.Location));

        var inventory = CreateInventory(compilation);

        Assert.That(inventory.ContractApiAvailable, Is.True);
        Assert.That(inventory.HasRejectedContractApiUsage, Is.False);
        Assert.That(inventory.Clauses, Has.Length.EqualTo(1));
    }

    [TestCase("1.0.0.0", true, true)]
    [TestCase("2.0.0.0", true, true)]
    [TestCase("1.0.0.0", false, true)]
    public void ReferencedContractApiRequiresMatchingIdentityAndElisionMetadata(
        string version,
        bool conditional,
        bool rejected)
    {
        var reference = EmitContractReference(
            version,
            conditional
                ? "[Conditional(ConditionalSymbol)]"
                : string.Empty);
        var compilation = CreateConsumer(reference);

        var inventory = CreateInventory(compilation);

        Assert.That(inventory.ContractApiAvailable, Is.EqualTo(!rejected));
        Assert.That(inventory.HasRejectedContractApiUsage, Is.EqualTo(rejected));
        Assert.That(inventory.Clauses.Length, Is.EqualTo(rejected ? 0 : 1));
    }

    [Test]
    public void AdditionalConditionalSymbolIsRejected()
    {
        var reference = EmitContractReference(
            "1.0.0.0",
            """
            [Conditional(ConditionalSymbol)]
            [Conditional("OTHER_RUNTIME_CONTRACTS")]
            """);
        var inventory = CreateInventory(CreateConsumer(reference));

        Assert.That(inventory.ContractApiAvailable, Is.False);
        Assert.That(inventory.HasRejectedContractApiUsage, Is.True);
        Assert.That(inventory.Clauses, Is.Empty);
    }

    private static ContractClauseInventory CreateInventory(
        CSharpCompilation compilation)
    {
        var target = compilation.GetTypeByMetadataName("Target")!;
        var method = target.GetMembers("Read").OfType<IMethodSymbol>().Single();
        return new ContractClauseInventoryBuilder(compilation).Create(method);
    }

    private static CSharpCompilation CreateConsumer(
        PortableExecutableReference contractReference)
    {
        var tree = CSharpSyntaxTree.ParseText(
            """
            public static class Target {
                public static int Read(int value) {
                    SharpProof.Attributes.Contract.Ensures(value > 0);
                    return value;
                }
            }
            """,
            ParseOptions,
            "Consumer.cs");
        var compilation = CSharpCompilation.Create(
            "ContractIdentityConsumer",
            [tree],
            PlatformReferences.Add(contractReference),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        TestCompilation.AssertNoErrors(compilation);
        return compilation;
    }

    private static PortableExecutableReference EmitContractReference(
        string version,
        string conditionalAttributes)
    {
        var source =
            $$"""
            using System.Diagnostics;
            using System.Reflection;
            [assembly: AssemblyVersion("{{version}}")]
            namespace SharpProof.Attributes {
                public static class Contract {
                    public const string ConditionalSymbol =
                        "SHARPPROOF_CONTRACTS";

                    {{conditionalAttributes}}
                    public static void Requires(bool condition) {
                    }

                    {{conditionalAttributes}}
                    public static void Ensures(bool condition) {
                    }

                    {{conditionalAttributes}}
                    public static void Assume(bool condition) {
                    }

                    public static T Result<T>() => default!;
                    public static T Old<T>(T value) => value;
                }
            }
            """;
        var compilation = CSharpCompilation.Create(
            "SharpProof.Attributes",
            [CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.CSharp12),
                "Contract.cs")],
            PlatformReferences,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        TestCompilation.AssertNoErrors(compilation);
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.That(
            result.Success,
            Is.True,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static diagnostic =>
                    diagnostic.ToString())));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

}
