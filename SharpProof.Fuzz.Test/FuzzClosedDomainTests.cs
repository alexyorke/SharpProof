using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof;
using SharpProof.Frontend;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzClosedDomainTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ConstantFormulaHasOneAssignmentAndNoDomainAssumptions(bool value)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var formula = factory.Boolean(value);
        Assert.That(FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(factory, formula),
            Is.True);
        Assert.That(FiniteDomainSmtDifferentialOracle.TryPrepareForCampaign(factory, formula,
            CancellationToken.None, out var prepared), Is.True);
        var direct = await FiniteDomainSmtDifferentialOracle.CompareAsync(factory, formula);
        var cached = await FiniteDomainSmtDifferentialOracle.ComparePreparedAsync(factory, prepared!);

        Assert.That(cached, Is.EqualTo(direct));
        Assert.That(direct.Status, Is.EqualTo(FuzzOracleStatus.Agreement));
        Assert.That(direct.Expected, Is.EqualTo(value
            ? FiniteDomainSatisfiability.Satisfiable : FiniteDomainSatisfiability.Unsatisfiable));
        Assert.That(direct.Actual, Is.EqualTo(direct.Expected));
        Assert.That(direct.FiniteDomainAssumptions, Is.Zero);
        Assert.That(prepared!.Domain.AssignmentCount, Is.EqualTo(1));
    }

    [Test]
    public async Task MixedDomainRetainsFactoryAndActualBooleanIntegerAssignments()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var enabled = factory.CreateVariable("enabled", factory.BooleanType);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var formula = factory.Binary(IrBinaryOperator.AndAlso, factory.Variable(enabled),
            factory.Binary(IrBinaryOperator.Equal, factory.Variable(value), factory.Integer(-2)));
        Assert.That(FiniteDomainSmtDifferentialOracle.TryPrepareForCampaign(factory, formula,
            CancellationToken.None, out var prepared), Is.True);
        var direct = await FiniteDomainSmtDifferentialOracle.CompareAsync(factory, formula);
        var cached = await FiniteDomainSmtDifferentialOracle.ComparePreparedAsync(factory, prepared!);

        Assert.That(cached, Is.EqualTo(direct));
        Assert.That(direct.Status, Is.EqualTo(FuzzOracleStatus.Agreement));
        Assert.That(direct.Actual, Is.EqualTo(FiniteDomainSatisfiability.Satisfiable));
        Assert.That(direct.FiniteDomainAssumptions, Is.EqualTo(2));
        Assert.That(prepared!.Domain.AssignmentCount, Is.EqualTo(10));
        Assert.That(prepared.Domain.Factory, Is.SameAs(factory));
        Func<Task> wrongFactory = async () =>
            await FiniteDomainSmtDifferentialOracle.ComparePreparedAsync(
                new IrFactory(IrExecutionSemantics.Total), prepared);
        var exception = Assert.ThrowsAsync<ArgumentException>(wrongFactory);
        Assert.That(exception!.ParamName, Is.EqualTo("formula"));
    }

    [Test]
    public async Task PreparedConstantStillObservesCancellationBeforeSolving()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        Assert.That(FiniteDomainSmtDifferentialOracle.TryPrepareForCampaign(factory,
            factory.Boolean(true), CancellationToken.None, out var prepared), Is.True);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Func<Task> compare = async () =>
            await FiniteDomainSmtDifferentialOracle.ComparePreparedAsync(factory, prepared!,
                cancellation.Token);

        Assert.ThrowsAsync<OperationCanceledException>(compare);
    }

    [Test]
    public async Task RealSourceCallIsRejectedByDefinedCampaignAdmission()
    {
        const string source = "public static class Calls { " +
            "public static bool Target(long value) => IsPositive(value + 1); " +
            "private static bool IsPositive(long value) => value > 0; }";
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("FuzzClosedDomainCall", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var root = await tree.GetRootAsync();
        var syntax = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Target");
        var model = compilation.GetSemanticModel(tree);
        var operation = model.GetOperation(syntax.ExpressionBody!.Expression);
        Assert.That(operation, Is.Not.Null);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var lowering = new RoslynOperationLowerer(factory).Lower(operation!);

        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Term, Is.InstanceOf<IrOpaqueTerm>());
        Assert.That(FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(factory,
            lowering.Term), Is.False);
        Assert.That(FiniteDomainSmtDifferentialOracle.TryPrepareForCampaign(factory,
            lowering.Term, CancellationToken.None, out var prepared), Is.False);
        Assert.That(prepared, Is.Null);
        var comparison = await FiniteDomainSmtDifferentialOracle.CompareAsync(factory,
            lowering.Term);
        Assert.That(comparison.Status, Is.EqualTo(FuzzOracleStatus.Abstained));
        Assert.That(comparison.Actual, Is.Null);
        Assert.That(comparison.Detail, Is.EqualTo("SMT abstained: UnsupportedEncoding."));
        var candidates = IrStructuralShrinker.GetCandidates(factory, lowering.Term);
        Assert.That(candidates, Is.Not.Empty);
        var retained = await SmtFailureRetention.RetainFiniteDomainAsync(
            factory, lowering.Term, 7, 12345);
        var printed = new IrPrinter(factory).Print(lowering.Term);
        Assert.That(retained, Is.EqualTo(new FuzzFailure(7, 12345,
            "finite-domain-smt", printed, printed, comparison.Detail)));
        var replay = await FiniteDomainSmtDifferentialOracle.CompareAsync(factory,
            lowering.Term);
        Assert.That(replay, Is.EqualTo(comparison));
        foreach (var candidate in candidates)
        {
            Assert.That(candidate.Type, Is.EqualTo(factory.BooleanType));
            Assert.That(candidate, Is.Not.InstanceOf<IrOpaqueTerm>());
            Assert.That(IrStructuralShrinker.StructuralSize(candidate),
                Is.LessThan(IrStructuralShrinker.StructuralSize(lowering.Term)));
            var smaller = await FiniteDomainSmtDifferentialOracle.CompareAsync(
                factory, candidate);
            Assert.That(smaller.Status, Is.EqualTo(FuzzOracleStatus.Agreement), smaller.Detail);
        }
    }
}
