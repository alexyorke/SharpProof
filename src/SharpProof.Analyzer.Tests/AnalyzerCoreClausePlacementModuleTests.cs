using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Contracts;

namespace SharpProof.Analyzer.Test;

[TestFixture]
internal sealed class AnalyzerCoreClausePlacementModuleTests
{
    private const string AnonymousReason = "preconditions inside lambda and anonymous methods cannot be enforced at delegate invocation sites";
    private const string LateReason = "expected the clause before every non-contract statement";
    private const string ConditionalReason = "expected an unconditional prologue statement";

    [TestCase("named", "requires", ContractClausePlacement.ValidPrologue, 0, null)]
    [TestCase("local", "requires", ContractClausePlacement.ValidPrologue, 0, null)]
    [TestCase("lambda", "requires", ContractClausePlacement.ValidPrologue, 1, AnonymousReason)]
    [TestCase("delegate", "requires", ContractClausePlacement.ValidPrologue, 1, AnonymousReason)]
    [TestCase("named", "late-requires", ContractClausePlacement.Late, 1, LateReason)]
    [TestCase("local", "late-requires", ContractClausePlacement.Late, 1, LateReason)]
    [TestCase("lambda", "late-requires", ContractClausePlacement.Late, 1, LateReason)]
    [TestCase("delegate", "late-requires", ContractClausePlacement.Late, 1, LateReason)]
    [TestCase("named", "ensures", ContractClausePlacement.ValidPrologue, 0, null)]
    [TestCase("local", "ensures", ContractClausePlacement.ValidPrologue, 0, null)]
    [TestCase("lambda", "ensures", ContractClausePlacement.ValidPrologue, 0, null)]
    [TestCase("delegate", "ensures", ContractClausePlacement.ValidPrologue, 0, null)]
    [TestCase("named", "late-ensures", ContractClausePlacement.Late, 1, LateReason)]
    [TestCase("local", "late-ensures", ContractClausePlacement.Late, 1, LateReason)]
    [TestCase("lambda", "late-ensures", ContractClausePlacement.Late, 1, LateReason)]
    [TestCase("delegate", "late-ensures", ContractClausePlacement.Late, 1, LateReason)]
    [TestCase("named", "conditional", ContractClausePlacement.Conditional, 1, ConditionalReason)]
    [TestCase("local", "conditional", ContractClausePlacement.Conditional, 1, ConditionalReason)]
    [TestCase("lambda", "conditional", ContractClausePlacement.Conditional, 1, ConditionalReason)]
    [TestCase("delegate", "conditional", ContractClausePlacement.Conditional, 1, ConditionalReason)]
    [TestCase("named", "empty", ContractClausePlacement.ValidPrologue, 0, null)]
    [TestCase("local", "empty", ContractClausePlacement.ValidPrologue, 0, null)]
    [TestCase("lambda", "empty", ContractClausePlacement.ValidPrologue, 0, null)]
    [TestCase("delegate", "empty", ContractClausePlacement.ValidPrologue, 0, null)]
    public void WholePolicyUsesActualCallableAndClausePlacement(string shape, string scenario,
        ContractClausePlacement placement, int count, string? reason)
    {
        System.ArgumentNullException.ThrowIfNull(scenario);
        System.ArgumentNullException.ThrowIfNull(shape);
        var (compilation, method, inventory) = Compile(shape, scenario);
        Assert.That(inventory.Callable.MethodKind, Is.EqualTo(method.MethodKind));
        Assert.That(inventory.Clauses, Has.Length.EqualTo(scenario == "empty" ? 0 : 1));
        if (scenario != "empty")
        {
            Assert.That(inventory.Clauses.Single().Placement, Is.EqualTo(placement));
        }
        var reported = new List<Diagnostic>();
        new ContractClausePlacementDiagnostics(compilation, TestContext.CurrentContext.CancellationToken).Report(inventory, reported.Add);
        Assert.That(reported, Has.Count.EqualTo(count));
        if (count != 0)
        {
            var diagnostic = reported.Single();
            var clause = inventory.Clauses.Single();
            Assert.That(diagnostic.Id, Is.EqualTo("SP0024"));
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(diagnostic.Location, Is.EqualTo(clause.Location));
            Assert.That(diagnostic.GetMessage(CultureInfo.InvariantCulture), Is.EqualTo(
                $"SharpProof contract 'Contract.{clause.Kind}' has invalid argument '<placement>': {reason}"));
        }
    }

    [TestCase("named", 0)]
    [TestCase("local", 0)]
    [TestCase("lambda", 1)]
    [TestCase("delegate", 1)]
    public void AnonymousPreconditionReportingIsOwnedByOnePolicyInstance(string shape, int count)
    {
        System.ArgumentNullException.ThrowIfNull(shape);
        var (compilation, _, inventory) = Compile(shape, "requires");
        var policy = new ContractClausePlacementDiagnostics(compilation, TestContext.CurrentContext.CancellationToken);
        var reported = new List<Diagnostic>();
        policy.ReportAnonymousRequires(inventory, reported.Add);
        policy.ReportAnonymousRequires(inventory, reported.Add);
        policy.Report(inventory, reported.Add);
        Assert.That(reported, Has.Count.EqualTo(count));
        var independent = new List<Diagnostic>();
        new ContractClausePlacementDiagnostics(compilation, TestContext.CurrentContext.CancellationToken)
            .ReportAnonymousRequires(inventory, independent.Add);
        Assert.That(independent, Has.Count.EqualTo(count));
        if (count != 0)
        {
            Assert.That(reported.Single().Location, Is.EqualTo(inventory.Clauses.Single().Location));
            Assert.That(reported.Single().GetMessage(CultureInfo.InvariantCulture), Is.EqualTo(
                "SharpProof contract 'Contract.Requires' has invalid argument '<placement>': " + AnonymousReason));
            Assert.That(independent.Single().GetMessage(CultureInfo.InvariantCulture),
                Is.EqualTo(reported.Single().GetMessage(CultureInfo.InvariantCulture)));
        }
    }

    [TestCase("local", "requires", 0)]
    [TestCase("lambda", "requires", 0)]
    [TestCase("delegate", "requires", 0)]
    [TestCase("local", "late-requires", 1)]
    [TestCase("lambda", "late-requires", 1)]
    [TestCase("delegate", "late-requires", 1)]
    public void OuterInventoryFindsActualNestedClauseOwner(string shape, string scenario, int count)
    {
        System.ArgumentNullException.ThrowIfNull(scenario);
        System.ArgumentNullException.ThrowIfNull(shape);
        var (compilation, _, _) = Compile(shape, scenario);
        var tree = compilation.SyntaxTrees.Single();
        var root = tree.GetRoot();
        var model = compilation.GetSemanticModel(tree);
        var outer = (IMethodSymbol)model.GetDeclaredSymbol(root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single())!;
        var inventory = new ContractClauseInventoryBuilder(compilation).Create(outer);
        Assert.That(inventory.Clauses, Has.Length.EqualTo(1));
        Assert.That(inventory.Clauses.Single().Placement, Is.EqualTo(ContractClausePlacement.NestedCallable));
        var reported = new List<Diagnostic>();
        new ContractClausePlacementDiagnostics(compilation, TestContext.CurrentContext.CancellationToken).Report(inventory, reported.Add);
        Assert.That(reported, Has.Count.EqualTo(count));
        if (count != 0)
        {
            Assert.That(reported.Single().Id, Is.EqualTo("SP0024"));
            Assert.That(reported.Single().Location, Is.EqualTo(inventory.Clauses.Single().Location));
            Assert.That(reported.Single().GetMessage(CultureInfo.InvariantCulture), Is.EqualTo(
                "SharpProof contract 'Contract.Requires' has invalid argument '<placement>': " + LateReason));
        }
    }

    [TestCase("local")]
    [TestCase("lambda")]
    [TestCase("delegate")]
    public async Task CancelledNestedOwnerLookupReportsNoDiagnostics(string shape)
    {
        System.ArgumentNullException.ThrowIfNull(shape);
        var (compilation, _, _) = Compile(shape, "late-requires");
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var outer = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(
            root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single())!;
        var inventory = new ContractClauseInventoryBuilder(compilation).Create(outer);
        Assert.That(inventory.Clauses.Single().Placement, Is.EqualTo(ContractClausePlacement.NestedCallable));
        using var cancellation = new CancellationTokenSource();
        var policy = new ContractClausePlacementDiagnostics(compilation, cancellation.Token);
        await cancellation.CancelAsync();
        var reported = new List<Diagnostic>();
        Action validate = () => policy.Report(inventory, reported.Add);
        var exception = Assert.Throws<OperationCanceledException>(validate);
        Assert.That(exception!.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(reported, Is.Empty);
    }

    private static (Compilation Compilation, IMethodSymbol Method, ContractClauseInventory Inventory) Compile(
        string shape, string scenario)
    {
        var clauses = scenario switch
        {
            "requires" => "Contract.Requires(condition);",
            "late-requires" => "_ = condition; Contract.Requires(condition);",
            "ensures" => "Contract.Ensures(condition);",
            "late-ensures" => "_ = condition; Contract.Ensures(condition);",
            "conditional" => "if (condition) Contract.Requires(condition);",
            "empty" => "_ = condition;",
            _ => throw new ArgumentException("Unknown frozen source scenario", nameof(scenario))
        };
        var body = shape switch
        {
            "named" => clauses,
            "local" => "void Local() { " + clauses + " } Local();",
            "lambda" => "Action callback = () => { " + clauses + " }; callback();",
            "delegate" => "Action callback = delegate { " + clauses + " }; callback();",
            _ => throw new ArgumentException("Unknown frozen callable shape", nameof(shape))
        };
        var source = "using System; using SharpProof.Attributes; public static class Fixture { " +
            "public static void Target(bool condition) { " + body + " } }";
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var emitted = new MemoryStream();
        Assert.That(compilation.Emit(emitted).Success, Is.True);
        var tree = compilation.SyntaxTrees.Single();
        var root = tree.GetRoot();
        var model = compilation.GetSemanticModel(tree);
        var method = shape switch
        {
            "named" => (IMethodSymbol)model.GetDeclaredSymbol(root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single())!,
            "local" => (IMethodSymbol)model.GetDeclaredSymbol(root.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single())!,
            "lambda" => ((IAnonymousFunctionOperation)model.GetOperation(root.DescendantNodes().OfType<ParenthesizedLambdaExpressionSyntax>().Single())!).Symbol,
            "delegate" => ((IAnonymousFunctionOperation)model.GetOperation(root.DescendantNodes().OfType<AnonymousMethodExpressionSyntax>().Single())!).Symbol,
            _ => throw new ArgumentException("Unknown frozen callable shape", nameof(shape))
        };
        Assert.That(SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, compilation.Assembly), Is.True);
        Assert.That(method.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
        var inventory = new ContractClauseInventoryBuilder(compilation).Create(method);
        Assert.That(inventory.ContractApiAvailable, Is.True);
        Assert.That(inventory.HasRejectedContractApiUsage, Is.False);
        return (compilation, method, inventory);
    }
}
