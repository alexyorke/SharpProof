using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Contracts;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275BulkContractsApiTests
{
    private static readonly BoundContractKind[] FullClauseKinds =
        [BoundContractKind.Requires, BoundContractKind.Requires, BoundContractKind.Ensures];
    private static readonly BoundContractKind[] RequiresClauseKinds =
        [BoundContractKind.Requires, BoundContractKind.Requires];
    private static readonly string[] FullClauseTexts =
        ["value != 0", "value >= 0", "Contract.Result<int>() == value"];
    private static readonly string[] RequiresClauseTexts = ["value != 0", "value >= 0"];

    [Test]
    public void SuccessfulSourceBindingReturnsTheWholeDirectContractSetAndRequiresView()
    {
        var compilation = CreateCompilation(
            "Pr275BulkContractsApiBinding",
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Target(int value) {
                    Contract.Requires(value != 0);
                    Contract.Requires(value >= 0);
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
                internal static int Plain(int value) => value;
            }
            """);
        var subject = compilation.GetTypeByMetadataName("Subject")!;
        var target = subject.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var plain = subject.GetMembers("Plain").OfType<IMethodSymbol>().Single();
        var binder = new ContractBinder(compilation, new IrFactory());

        var full = binder.Bind(target);

        Assert.That(full.IsSuccess, Is.True);
        Assert.That(full.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(full.Contracts, Is.Not.Null);
        var fullContracts = full.Contracts!;
        Assert.That(SymbolEqualityComparer.Default.Equals(fullContracts.Target, target), Is.True);
        Assert.That(fullContracts.Clauses.Select(static clause => clause.Kind), Is.EqualTo(FullClauseKinds));
        Assert.That(fullContracts.Clauses.Select(static clause => clause.DiagnosticText), Is.EqualTo(FullClauseTexts));
        Assert.That(fullContracts.Clauses.All(static clause =>
            clause.Evidence == BoundContractEvidence.CompilerBoundInvocation &&
            clause.SourceSyntax != null), Is.True);

        var requires = binder.BindRequires(target);

        Assert.That(requires.IsSuccess, Is.True);
        Assert.That(requires.Contracts, Is.Not.Null);
        var requiresContracts = requires.Contracts!;
        Assert.That(requiresContracts.Clauses.Select(static clause => clause.Kind), Is.EqualTo(RequiresClauseKinds));
        Assert.That(requiresContracts.Clauses.Select(static clause => clause.DiagnosticText), Is.EqualTo(RequiresClauseTexts));

        var empty = binder.Bind(plain);

        Assert.That(empty.IsSuccess, Is.True);
        Assert.That(empty.Contracts, Is.Not.Null);
        Assert.That(empty.Contracts!.Clauses, Is.Empty);
    }

    [Test]
    public void MaintainedBinderAndInventoryEntryPointsRejectMissingCallables()
    {
        var compilation = CreateCompilation(
            "Pr275BulkContractsApiRequiredInputs",
            "internal static class Subject { internal static void Target() { } }");
        var inventory = new ContractClauseInventoryBuilder(compilation);
        var binder = new ContractBinder(compilation, new IrFactory(), inventory);

        AssertMissingCallable("bind", "target", () => _ = binder.Bind(null!));
        AssertMissingCallable("requires", "target", () => _ = binder.BindRequires(null!));
        AssertMissingCallable("inventory query", "target", () => _ = binder.GetClauseInventory(null!));
        AssertMissingCallable("inventory create", "callable", () => _ = inventory.Create(null!));
    }

    [Test]
    public void ExpressionBinderUsesItsDefaultSpecializerForACompilerBoundOperation()
    {
        var compilation = CreateCompilation(
            "Pr275BulkContractsApiExpression",
            "internal static class Subject { internal static bool Predicate(int value) => value > 0; }");
        var method = compilation.GetTypeByMetadataName("Subject")!
            .GetMembers("Predicate").OfType<IMethodSymbol>().Single();
        var declaration = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences
            .Single().GetSyntax();
        var operation = compilation.GetSemanticModel(declaration.SyntaxTree)
            .GetOperation(declaration.ExpressionBody!.Expression);
        Assert.That(operation, Is.Not.Null);

        var api = ContractApiSymbols.TryCreate(compilation);
        Assert.That(api, Is.Not.Null);
        var binder = new ContractExpressionBinder(new IrFactory(), api!, method);
        var binding = binder.Bind(operation!);

        Assert.That(binding.IsSuccess, Is.True);
        Assert.That(binding.Term, Is.InstanceOf<IrBinaryTerm>());
        Assert.That(((IrBinaryTerm)binding.Term!).Operator, Is.EqualTo(IrBinaryOperator.GreaterThan));
        Assert.That(binding.Variables, Has.Count.EqualTo(1));
        Assert.That(binder.VariableBindings, Has.Length.EqualTo(1));
        Assert.That(SymbolEqualityComparer.Default.Equals(
            binder.VariableBindings.Single().Symbol,
            method.Parameters.Single()), Is.True);

        var missingOperation = Assert.Throws<ArgumentNullException>((Action)(() =>
            _ = binder.Bind(null!)));
        Assert.That(missingOperation!.ParamName, Is.EqualTo("operation"));
    }

    [TestCase("property", "17")]
    [TestCase("indexer", "index")]
    public void InventoryCreateUsesTheOwnedExpressionBodyOfARealAccessor(
        string memberKind,
        string expectedBody)
    {
        var compilation = CreateCompilation(
            "Pr275BulkContractsApiAccessor",
            """
            internal sealed class Subject {
                internal int Value => 17;
                internal int this[int index] => index;
            }
            """);
        var type = compilation.GetTypeByMetadataName("Subject")!;
        var property = memberKind == "property"
            ? type.GetMembers("Value").OfType<IPropertySymbol>().Single()
            : type.GetMembers().OfType<IPropertySymbol>().Single(static candidate => candidate.IsIndexer);
        var accessor = property.GetMethod!;
        var declaration = property.DeclaringSyntaxReferences.Single().GetSyntax();
        var expectedExpression = memberKind == "property"
            ? ((PropertyDeclarationSyntax)declaration).ExpressionBody!.Expression
            : ((IndexerDeclarationSyntax)declaration).ExpressionBody!.Expression;
        var body = ContractClauseInventoryBuilder.GetBody(declaration);
        var inventory = new ContractClauseInventoryBuilder(compilation).Create(accessor);

        Assert.That(body, Is.SameAs(expectedExpression));
        Assert.That(body!.ToString(), Is.EqualTo(expectedBody));
        Assert.That(SymbolEqualityComparer.Default.Equals(inventory.Callable, accessor), Is.True);
        Assert.That(inventory.ContractApiAvailable, Is.True);
        Assert.That(inventory.ImplementationBody, Is.Not.Null);
        Assert.That(inventory.ImplementationBody!.Syntax.ToString(), Is.EqualTo(expectedBody));
        Assert.That(inventory.Clauses, Is.Empty);
        Assert.That(inventory.HasPlacementErrors, Is.False);
    }

    private static CSharpCompilation CreateCompilation(string assemblyPrefix, string source)
    {
        var compilation = TestCompilation.Create(assemblyPrefix, source);
        TestCompilation.AssertNoErrors(compilation);
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.That(emitted.Diagnostics.Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        Assert.That(emitted.Success, Is.True);
        return compilation;
    }

    private static void AssertMissingCallable(
        string api,
        string expectedParameter,
        Action action)
    {
        var error = Assert.Throws<ArgumentNullException>((Action)(() => action()),
            $"{api} must reject a missing callable.");
        Assert.That(error!.ParamName, Is.EqualTo(expectedParameter));
    }
}
