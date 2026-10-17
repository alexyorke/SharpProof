using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Attributes;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractClauseInventoryTests
{
    private static readonly BoundContractKind[] TopLevelInventoryKinds = [
        BoundContractKind.Ensures,
        BoundContractKind.Ensures
    ];
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
            inventory.Clauses.Select(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)),
            Is.EqualTo(InventoryKinds));
        Assert.That(
            inventory.Clauses.Select(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Placement)),
            Is.EqualTo(InventoryPlacements));
        Assert.That(
            inventory.Clauses.Select(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.SourceOrdinal)),
            Is.EqualTo(Enumerable.Range(0, 6)));
        Assert.That(
            inventory.Clauses.Select(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Ordinal)),
            Is.EqualTo(InventoryOrdinals));
        Assert.That(
            inventory.Clauses.All(static clause =>
                clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Location.IsInSource &&
                clause.Invocation.TargetMethod.Name ==
                clause.Kind.ToString())),
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
            inventory.Clauses.Select(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Placement)),
            Is.EqualTo(MisplacedPlacements));
    }

    [Test]
    public void EmptyStatementBreaksTheContiguousPrologue()
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
            inventory.Clauses.Select(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Placement)),
            Is.EqualTo([
                ContractClausePlacement.ValidPrologue,
                ContractClausePlacement.Late
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

        Assert.That(inventory.Clauses.Select(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Placement)),
            Is.EqualTo(expected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TopLevelDirectClausesUseTheGlobalStatementPrologue(bool asynchronous)
    {
        var source =
            $$"""
            using SharpProof.Attributes;
            Contract.Ensures(true);
            {{(asynchronous ? "await System.Threading.Tasks.Task.Yield();" : "System.Console.WriteLine();")}}
            Contract.Ensures(true);
            public static class Other {
                public static void Analyze() { }
            }
            """;
        var compilation = CreateCompilation(
            source, includeSharpProofReference: true,
            outputKind: OutputKind.ConsoleApplication);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var entry = compilation.GetEntryPoint(CancellationToken.None)!;
        var body = model.GetOperation(tree.GetRoot());
        Assert.That(body, Is.Not.Null);
        Assert.That(entry.DeclaringSyntaxReferences.Any(reference =>
            reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (reference.GetSyntax() is CompilationUnitSyntax unit &&
            unit.SyntaxTree == tree && unit.Span == body!.Syntax.Span)), Is.True);
        var builder = new ContractClauseInventoryBuilder(compilation);
        var inventory = builder.Create(entry, body);
        var other = compilation.GetTypeByMetadataName("Other")!
            .GetMembers("Analyze").OfType<IMethodSymbol>().Single();
        var wrongCallable = builder.Create(other, body);
        var declaredOther = builder.Create(other);
        ContractClausePlacement[] expected = [
            ContractClausePlacement.ValidPrologue,
            ContractClausePlacement.Late
        ];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(inventory.Callable, Is.SameAs(entry));
            Assert.That(inventory.ContractApiAvailable, Is.True);
            Assert.That(inventory.HasRejectedContractApiUsage, Is.False);
            Assert.That(inventory.ImplementationBody, Is.SameAs(body));
            Assert.That(inventory.Clauses.Select(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind)),
                Is.EqualTo(TopLevelInventoryKinds));
            Assert.That(inventory.Clauses.Select(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Ordinal)),
                Is.EqualTo(Enumerable.Range(0, 2)));
            Assert.That(inventory.Clauses.Select(static clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Placement)),
                Is.EqualTo(expected));
            Assert.That(body!.SemanticModel!.Compilation, Is.SameAs(compilation));
            Assert.That(other.ContainingAssembly, Is.SameAs(compilation.Assembly));
            Assert.That(other.DeclaringSyntaxReferences.Single().SyntaxTree, Is.SameAs(tree));
            Assert.That(wrongCallable.HasRejectedContractApiUsage, Is.True);
            Assert.That(wrongCallable.ImplementationBody, Is.Null);
            Assert.That(wrongCallable.Clauses, Is.Empty);
            Assert.That(declaredOther.Callable, Is.SameAs(other));
            Assert.That(declaredOther.ContractApiAvailable, Is.True);
            Assert.That(declaredOther.HasRejectedContractApiUsage, Is.False);
            Assert.That(declaredOther.ImplementationBody, Is.Not.Null);
            Assert.That(declaredOther.Clauses, Is.Empty);
        }
    }

    [Test]
    public void NonRootOperationCannotPromoteConditionalClauseToPrologue()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Target {
                public static void Analyze(bool condition) {
                    if (condition) {
                        Contract.Requires(condition);
                    }
                }
            }
            """;
        var compilation = CreateCompilation(
            source,
            includeSharpProofReference: true);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var method = compilation.GetTypeByMetadataName("Target")!
            .GetMembers("Analyze")
            .OfType<IMethodSymbol>()
            .Single();
        var invocation = tree.GetRoot().DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single();
        var operation = model.GetOperation(invocation)!;

        var inventory = new ContractClauseInventoryBuilder(compilation)
            .Create(method, operation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(inventory.HasRejectedContractApiUsage, Is.True);
            Assert.That(inventory.ImplementationBody, Is.Null);
            Assert.That(inventory.Clauses, Is.Empty);
        }
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
    public void NestedRejectedContractApiUsageBelongsToNestedCallable()
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
                    void Local() {
                        SharpProof.Attributes.Contract.Requires(condition);
                    }
                    Local();
                }
            }
            """;
        var compilation = CreateCompilation(
            source,
            includeSharpProofReference: false);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var target = compilation.GetTypeByMetadataName("Target")!;
        var outer = target.GetMembers("Analyze").OfType<IMethodSymbol>().Single();
        var localSyntax = tree.GetRoot().DescendantNodes()
            .OfType<LocalFunctionStatementSyntax>()
            .Single();
        var local = model.GetDeclaredSymbol(localSyntax)!;
        var builder = new ContractClauseInventoryBuilder(compilation);

        var outerInventory = builder.Create(outer);
        var localInventory = builder.Create(local);

        Assert.That(outerInventory.HasRejectedContractApiUsage, Is.False);
        Assert.That(localInventory.HasRejectedContractApiUsage, Is.True);
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
        System.ArgumentNullException.ThrowIfNull(returnKind);
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

    [Test]
    public void ForeignCallableReturnsRejectedInventoryWithoutRetainingBody()
    {
        var (ownerCompilation, foreignCompilation) = CreateForeignFixture();
        var foreign = foreignCompilation.GetTypeByMetadataName("Foreign")!
            .GetMembers("Analyze")
            .OfType<IMethodSymbol>()
            .Single();

        var inventory = new ContractClauseInventoryBuilder(ownerCompilation)
            .Create(foreign);

        Assert.That(inventory.HasRejectedContractApiUsage, Is.True);
        Assert.That(inventory.ImplementationBody, Is.Null);
        Assert.That(inventory.Clauses, Is.Empty);
    }

    [Test]
    public void ForeignImplementationBodyReturnsRejectedInventoryWithoutRetainingBody()
    {
        var (ownerCompilation, foreignCompilation) = CreateForeignFixture();
        var owner = ownerCompilation.GetTypeByMetadataName("Owner")!
            .GetMembers("Analyze")
            .OfType<IMethodSymbol>()
            .Single();
        var foreignTree = foreignCompilation.SyntaxTrees.Single();
        var foreignBody = foreignTree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single()
            .Body!;
        var foreignOperation = foreignCompilation.GetSemanticModel(foreignTree)
            .GetOperation(foreignBody)!;

        var inventory = new ContractClauseInventoryBuilder(ownerCompilation)
            .Create(owner, foreignOperation);

        Assert.That(inventory.HasRejectedContractApiUsage, Is.True);
        Assert.That(inventory.ImplementationBody, Is.Null);
        Assert.That(inventory.Clauses, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SharedSyntaxTreeDoesNotAuthorizeForeignSemanticObjects(bool foreignCallable)
    {
        var owner = CreateCompilation("""
            using SharpProof.Attributes;
            public static class Target {
                public static void Analyze(int value) {
                    Contract.Requires(Contract.Old(value) > 0);
                }
            }
            """, includeSharpProofReference: true);
        var foreign = owner.WithAssemblyName("Foreign");
        var tree = owner.SyntaxTrees.Single();
        var body = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single().Body!;
        var callable = (foreignCallable ? foreign : owner)
            .GetTypeByMetadataName("Target")!.GetMembers("Analyze")
            .OfType<IMethodSymbol>().Single();
        var inventory = new ContractClauseInventoryBuilder(owner).Create(
            callable,
            foreignCallable ? null : foreign.GetSemanticModel(tree).GetOperation(body));
        Assert.That(inventory.HasRejectedContractApiUsage, Is.True);
        Assert.That(inventory.ImplementationBody, Is.Null);
        Assert.That(inventory.Clauses, Is.Empty);

        var ownCallable = owner.GetTypeByMetadataName("Target")!
            .GetMembers("Analyze").OfType<IMethodSymbol>().Single();
        var binding = new ContractBinder(owner, new SharpProof.Ir.IrFactory()).Bind(
            ownCallable, owner.GetSemanticModel(tree).GetOperation(body));
        Assert.That(binding.Failure, Is.EqualTo(ContractBindingFailure.OldOutsideEnsures));
        Assert.That(new ContractBinder(owner, new SharpProof.Ir.IrFactory()).Bind(
            callable, foreignCallable ? null : foreign.GetSemanticModel(tree).GetOperation(body))
            .IsSuccess, Is.False);
    }

    private static (CSharpCompilation Owner, CSharpCompilation Foreign)
        CreateForeignFixture()
    {
        return (
            CreateCompilation(
                "public static class Owner { public static void Analyze() { } }",
                includeSharpProofReference: true),
            CreateCompilation(
                """
                using SharpProof.Attributes;
                public static class Foreign {
                    public static void Analyze(bool condition) {
                        Contract.Requires(condition);
                    }
                }
                """,
                includeSharpProofReference: true));
    }

    private static ContractClauseInventory CreateInventory(
        string source,
        string typeName,
        string methodName,
        bool includeSharpProofReference = true)
    {
        System.ArgumentNullException.ThrowIfNull(methodName);
        System.ArgumentNullException.ThrowIfNull(source);
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
        System.ArgumentNullException.ThrowIfNull(source);
        return TestCompilation.Create(
            "ClauseInventory",
            source,
            outputKind: outputKind,
            includeSharpProofReference: includeSharpProofReference);
    }

}
