using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Attributes;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractClauseInventoryTests
{
    private static readonly BoundContractKind[] InventoryKinds = [
        BoundContractKind.Requires,
        BoundContractKind.Ensures,
        BoundContractKind.Assume,
        BoundContractKind.Requires,
        BoundContractKind.Ensures,
        BoundContractKind.Assume
    ];
    private static readonly ContractClausePlacement[] InventoryPlacements = [
        ContractClausePlacement.ValidPrologue,
        ContractClausePlacement.ValidPrologue,
        ContractClausePlacement.Conditional,
        ContractClausePlacement.Late,
        ContractClausePlacement.NestedCallable,
        ContractClausePlacement.Unreachable
    ];
    private static readonly int[] InventoryOrdinals = [0, 0, 0, 1, 1, 1];
    private static readonly ContractClausePlacement[] MisplacedPlacements = [
        ContractClausePlacement.Misplaced,
        ContractClausePlacement.Late
    ];

    [Test]
    public void InventoryClassifiesEveryPlacementInStableSourceOrder()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static void Analyze(bool condition) {
                    Contract.Requires(condition);
                    Contract.Ensures(condition);
                    if (condition) {
                        Contract.Assume(condition);
                    }
                    Contract.Requires(condition);
                    void Nested() {
                        Contract.Ensures(condition);
                    }
                    Nested();
                    return;
                    Contract.Assume(condition);
                }
            }
            """;
        var inventory = CreateInventory(source, "Target", "Analyze");

        Assert.That(inventory.ContractApiAvailable, Is.True);
        Assert.That(inventory.HasPlacementErrors, Is.True);
        Assert.That(
            inventory.Clauses.Select(static clause => clause.Kind),
            Is.EqualTo(InventoryKinds));
        Assert.That(
            inventory.Clauses.Select(static clause => clause.Placement),
            Is.EqualTo(InventoryPlacements));
        Assert.That(
            inventory.Clauses.Select(static clause => clause.SourceOrdinal),
            Is.EqualTo(Enumerable.Range(0, 6)));
        Assert.That(
            inventory.Clauses.Select(static clause => clause.Ordinal),
            Is.EqualTo(InventoryOrdinals));
        Assert.That(inventory.Clauses[^1].IsValid, Is.False);
        Assert.That(
            inventory.Clauses.All(static clause =>
                clause.Location.IsInSource &&
                clause.Invocation.TargetMethod.Name ==
                clause.Kind.ToString()),
            Is.True);
    }

    [Test]
    public void UnconditionalNestedBlockIsMisplacedAndBreaksThePrologue()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static void Analyze(bool condition) {
                    {
                        Contract.Requires(condition);
                    }
                    Contract.Ensures(condition);
                }
            }
            """;
        var inventory = CreateInventory(source, "Target", "Analyze");

        Assert.That(
            inventory.Clauses.Select(static clause => clause.Placement),
            Is.EqualTo(MisplacedPlacements));
    }

    [Test]
    public void EmptyStatementDoesNotBreakTheContiguousPrologue()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static void Analyze(bool condition) {
                    Contract.Requires(condition);
                    ;
                    Contract.Ensures(condition);
                }
            }
            """;
        var inventory = CreateInventory(source, "Target", "Analyze");

        Assert.That(
            inventory.Clauses.Select(static clause => clause.Placement),
            Is.EqualTo([
                ContractClausePlacement.ValidPrologue,
                ContractClausePlacement.ValidPrologue
            ]));
    }

    [Test]
    public void LocalFunctionPrefixDoesNotBreakTheContiguousPrologue()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static void Analyze(bool condition) {
                    void Local() { }
                    Contract.Requires(condition);
                    Contract.Ensures(condition);
                }
            }
            """;
        var inventory = CreateInventory(source, "Target", "Analyze");

        Assert.That(
            inventory.Clauses.Select(static clause => clause.Placement),
            Is.EqualTo([
                ContractClausePlacement.ValidPrologue,
                ContractClausePlacement.ValidPrologue
            ]));
    }

    [Test]
    public void CompilerSymbolIdentityRejectsTextualShadows()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Shadow {
                public static class Contract {
                    public static void Requires(bool value) { }
                }
            }
            public static class Target {
                public static void Analyze(bool condition) {
                    Contract.Requires(condition);
                    Shadow.Contract.Requires(condition);
                }
            }
            """;
        var inventory = CreateInventory(source, "Target", "Analyze");

        Assert.That(inventory.Clauses, Has.Length.EqualTo(1));
        Assert.That(inventory.Clauses[0].Kind, Is.EqualTo(
            BoundContractKind.Requires));
        Assert.That(inventory.Clauses[0].IsValid, Is.True);
    }

    [Test]
    public void ExpressionBodiedClauseIsAValidPrologue()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static void Analyze(bool condition) =>
                    Contract.Requires(condition);
            }
            """;
        var inventory = CreateInventory(source, "Target", "Analyze");

        Assert.That(inventory.HasPlacementErrors, Is.False);
        Assert.That(inventory.Clauses.Single().Placement, Is.EqualTo(
            ContractClausePlacement.ValidPrologue));
    }

    [Test]
    public void TopLevelDirectClausesUseTheGlobalStatementPrologue()
    {
        const string source =
            """
            using SharpProof.Attributes;
            Contract.Ensures(true);
            System.Console.WriteLine();
            Contract.Ensures(true);
            """;
        var compilation = CreateCompilation(
            source, includeSharpProofReference: true,
            outputKind: OutputKind.ConsoleApplication);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var entry = compilation.GetEntryPoint(CancellationToken.None)!;
        var inventory = new ContractClauseInventoryBuilder(compilation)
            .Create(entry, model.GetOperation(tree.GetRoot()));
        ContractClausePlacement[] expected = [
            ContractClausePlacement.ValidPrologue,
            ContractClausePlacement.Late
        ];

        Assert.That(inventory.Clauses.Select(static clause => clause.Placement),
            Is.EqualTo(expected));
    }

    [Test]
    public void SourceDefinedRuntimeContractApiIsRejected()
    {
        const string source =
            """
            namespace SharpProof.Attributes {
                public static class Contract {
                    public static void Requires(bool value) { }
                    public static void Ensures(bool value) { }
                    public static void Assume(bool value) { }
                }
            }
            public static class Target {
                public static void Analyze(bool condition) {
                    SharpProof.Attributes.Contract.Requires(condition);
                }
            }
            """;
        var inventory = CreateInventory(
            source,
            "Target",
            "Analyze",
            includeSharpProofReference: false);

        Assert.That(inventory.ContractApiAvailable, Is.False);
        Assert.That(inventory.HasRejectedContractApiUsage, Is.True);
        Assert.That(inventory.ImplementationBody, Is.Not.Null);
        Assert.That(inventory.Clauses, Is.Empty);
    }

    [Test]
    public void ConditionalAncestorsOutsideLocalCallableAreIgnored()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static void Analyze(bool condition) {
                    if (condition) {
                        void Local() {
                            {
                                Contract.Requires(condition);
                            }
                        }
                        Local();
                    }
                }
            }
            """;
        var compilation = CreateCompilation(source, includeSharpProofReference: true);
        var tree = compilation.SyntaxTrees.Single();
        var localSyntax = tree.GetRoot().DescendantNodes()
            .OfType<LocalFunctionStatementSyntax>()
            .Single();
        var local = compilation.GetSemanticModel(tree)
            .GetDeclaredSymbol(localSyntax)!;
        var inventory = new ContractClauseInventoryBuilder(compilation)
            .Create(local);

        Assert.That(
            inventory.Clauses.Single().Placement,
            Is.EqualTo(ContractClausePlacement.Misplaced));
    }

    [TestCase("ref")]
    [TestCase("ref readonly")]
    public void RefExpressionBodyResolvesMethodBodyOperation(string returnKind)
    {
        var inventory = CreateInventory(
            $$"""
            public static class Target {
                private static int storage;
                public static {{returnKind}} int Read() => ref storage;
            }
            """,
            "Target",
            "Read",
            includeSharpProofReference: false);

        Assert.That(inventory.ImplementationBody, Is.Not.Null);
        Assert.That(inventory.Clauses, Is.Empty);
    }

    private static ContractClauseInventory CreateInventory(
        string source,
        string typeName,
        string methodName,
        bool includeSharpProofReference = true)
    {
        var compilation = CreateCompilation(
            source,
            includeSharpProofReference);
        var type = compilation.GetTypeByMetadataName(typeName) ??
                   throw new InvalidOperationException(typeName);
        var method = type.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Single();
        return new ContractClauseInventoryBuilder(compilation).Create(method);
    }

    private static CSharpCompilation CreateCompilation(
        string source,
        bool includeSharpProofReference,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(
                LanguageVersion.CSharp12,
                preprocessorSymbols: ["SHARPPROOF_CONTRACTS"]));
        var compilation = CSharpCompilation.Create(
            "ClauseInventory_" + Guid.NewGuid().ToString("N"),
            [syntaxTree],
            GetReferences(includeSharpProofReference),
            new CSharpCompilationOptions(
                outputKind,
                nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.That(
                errors,
            Is.Empty,
            string.Join(Environment.NewLine, errors.Select(
                static diagnostic => diagnostic.ToString())));
        return compilation;
    }

    private static ImmutableArray<MetadataReference> GetReferences(
        bool includeSharpProofReference)
    {
        var paths = ((string)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);
        if (includeSharpProofReference)
        {
            paths = [.. paths, typeof(Contract).Assembly.Location];
        }

        return [.. paths.Select(static path =>
            MetadataReference.CreateFromFile(path))
            .DistinctBy(static reference => reference.Display,
                StringComparer.OrdinalIgnoreCase)];
    }
}
