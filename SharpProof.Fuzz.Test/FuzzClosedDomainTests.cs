using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
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
        Assert.That(FiniteDomainSmtDifferentialOracle.IsDefinedForAllAssignments(factory, formula, cancellationToken: CancellationToken.None),
            Is.True);
        Assert.That(FiniteDomainSmtDifferentialOracle.TryPrepareForCampaign(factory, formula,
            CancellationToken.None, out var prepared), Is.True);
        var direct = await FiniteDomainSmtDifferentialOracle.CompareAsync(factory, formula, cancellationToken: CancellationToken.None);
        var cached = await FiniteDomainSmtDifferentialOracle.ComparePreparedAsync(factory, prepared!, cancellationToken: CancellationToken.None);

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
        var direct = await FiniteDomainSmtDifferentialOracle.CompareAsync(factory, formula, cancellationToken: CancellationToken.None);
        var cached = await FiniteDomainSmtDifferentialOracle.ComparePreparedAsync(factory, prepared!, cancellationToken: CancellationToken.None);

        Assert.That(cached, Is.EqualTo(direct));
        Assert.That(direct.Status, Is.EqualTo(FuzzOracleStatus.Agreement));
        Assert.That(direct.Actual, Is.EqualTo(FiniteDomainSatisfiability.Satisfiable));
        Assert.That(direct.FiniteDomainAssumptions, Is.EqualTo(2));
        Assert.That(prepared!.Domain.AssignmentCount, Is.EqualTo(10));
        Assert.That(prepared.Domain.Factory, Is.SameAs(factory));
        Func<Task> wrongFactory = async () =>
            await FiniteDomainSmtDifferentialOracle.ComparePreparedAsync(
                new IrFactory(IrExecutionSemantics.Total), prepared, cancellationToken: CancellationToken.None);
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
            lowering.Term, cancellationToken: CancellationToken.None), Is.False);
        Assert.That(FiniteDomainSmtDifferentialOracle.TryPrepareForCampaign(factory,
            lowering.Term, CancellationToken.None, out var prepared), Is.False);
        Assert.That(prepared, Is.Null);
        Action requirePrepared = () => FiniteDomainCampaignAdmission.RequireDefinedFormula(
            factory, lowering.Term, CancellationToken.None);
        var rejection = Assert.Throws<InvalidOperationException>(requirePrepared);
        Assert.That(rejection!.Message,
            Is.EqualTo("The generated total finite-domain formula could not be prepared."));
        var comparison = await FiniteDomainSmtDifferentialOracle.CompareAsync(factory,
            lowering.Term, cancellationToken: CancellationToken.None);
        Assert.That(comparison.Status, Is.EqualTo(FuzzOracleStatus.Abstained));
        Assert.That(comparison.Actual, Is.Null);
        Assert.That(comparison.Detail, Is.EqualTo("SMT abstained: UnsupportedEncoding."));
        var candidates = IrStructuralShrinker.GetCandidates(factory, lowering.Term);
        Assert.That(candidates, Is.Not.Empty);
        var retained = await SmtFailureRetention.RetainFiniteDomainAsync(
            factory, lowering.Term, 7, 12345, cancellationToken: CancellationToken.None);
        var printed = new IrPrinter(factory).Print(lowering.Term);
        Assert.That(retained, Is.EqualTo(new FuzzFailure(7, 12345,
            "finite-domain-smt", printed, printed, comparison.Detail)));
        var replay = await FiniteDomainSmtDifferentialOracle.CompareAsync(factory,
            lowering.Term, cancellationToken: CancellationToken.None);
        Assert.That(replay, Is.EqualTo(comparison));
        foreach (var candidate in candidates)
        {
            Assert.That(candidate.Type, Is.EqualTo(factory.BooleanType));
            Assert.That(candidate, Is.Not.InstanceOf<IrOpaqueTerm>());
            Assert.That(IrStructuralShrinker.StructuralSize(candidate),
                Is.LessThan(IrStructuralShrinker.StructuralSize(lowering.Term)));
            var smaller = await FiniteDomainSmtDifferentialOracle.CompareAsync(
                factory, candidate, cancellationToken: CancellationToken.None);
            Assert.That(smaller.Status, Is.EqualTo(FuzzOracleStatus.Agreement), smaller.Detail);
        }

        var emission = GeneratedCompilationEmitter.Emit(compilation, CancellationToken.None);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True,
            string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        var context = new AssemblyLoadContext("FuzzClosedDomainCallReference", isCollectible: true);
        try
        {
            image.Position = 0;
            var runtime = context.LoadFromStream(image).GetType("Calls");
            Assert.That(runtime, Is.Not.Null);
            var target = runtime!.GetMethod("Target", BindingFlags.Public | BindingFlags.Static);
            Assert.That(target, Is.Not.Null);
            object?[] arguments = [0L];
            var runtimeResult = target!.Invoke(null, arguments);
            var runtimeCompleted = runtimeResult is bool;
            Assert.That(runtimeCompleted, Is.True);
            Assert.That(runtimeResult, Is.TypeOf<bool>());
            Assert.That(runtimeResult, Is.EqualTo(true));
            var scenario = ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>.Empty;
            foreach (var variable in IrTermAnalysis.CollectVariables(lowering.Term))
            {
                var binding = lowering.Variables.Single(entry => entry.Variable == variable);
                Assert.That(binding.Symbol, Is.InstanceOf<IParameterSymbol>());
                var parameter = (IParameterSymbol)binding.Symbol;
                Assert.That(parameter.Ordinal, Is.Zero);
                Assert.That(parameter.Type.SpecialType, Is.EqualTo(SpecialType.System_Int64));
                scenario = scenario.Add(variable,
                    factory.CreateIntegerValue(factory.GetVariableInfo(variable).Type, (long)arguments[parameter.Ordinal]!));
            }
            var evaluated = new IrInterpreter(factory).Evaluate(lowering.Term, scenario);
            Assert.That(evaluated.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
            Assert.That(evaluated.Unsupported!.Reason, Is.EqualTo(IrUnsupportedReason.OpaqueTerm));
            var reference = (bool)runtimeResult!
                ? PartialTermSemanticOutcome.DefinedTrue
                : PartialTermSemanticOutcome.DefinedFalse;
            var partial = await PartialTermSmtDifferentialOracle.CompareAsync(factory,
                new PartialTermSmtCase(lowering.Term, factory.Boolean(runtimeCompleted), [scenario], [reference]), cancellationToken: CancellationToken.None);
            Assert.That(partial.Status, Is.EqualTo(FuzzOracleStatus.Mismatch));
            Assert.That(partial.ScenarioCount, Is.EqualTo(1));
            Assert.That(partial.Detail,
                Is.EqualTo("The guarded interpreter disagrees with C# execution in scenario 0."));
            Assert.That(partial.DefinedTrueCount + partial.DefinedFalseCount + partial.UndefinedCount, Is.Zero);
            Assert.That(FiniteDomainSmtDifferentialOracle.ValidatedFiniteDomain.TryCreate(
                factory, lowering.Term, out var opaqueDomain, out var domainRejection), Is.True);
            Assert.That(domainRejection, Is.Null);
            Assert.That(opaqueDomain!.Factory, Is.SameAs(factory));
            Assert.That(opaqueDomain!.Variables, Is.EqualTo(IrTermAnalysis.CollectVariables(lowering.Term)
                .OrderBy(variable => variable.Value)));
            var concreteOpaque = await FiniteDomainSmtDifferentialOracle.CheckConcreteDomainAsync(
                opaqueDomain!, lowering.Term, CancellationToken.None);
            Assert.That(concreteOpaque, Is.Null);
            var trueFormula = factory.Boolean((bool)runtimeResult!);
            Assert.That(FiniteDomainSmtDifferentialOracle.ValidatedFiniteDomain.TryCreate(
                factory, trueFormula, out var trueDomain, out var trueRejection), Is.True);
            Assert.That(trueRejection, Is.Null);
            Assert.That(await FiniteDomainSmtDifferentialOracle.CheckConcreteDomainAsync(
                trueDomain!, trueFormula, CancellationToken.None), Is.EqualTo(FiniteDomainSatisfiability.Satisfiable));
            object?[] falseArguments = [-2L];
            var falseRuntimeResult = target!.Invoke(null, falseArguments);
            Assert.That(falseRuntimeResult, Is.TypeOf<bool>());
            Assert.That(falseRuntimeResult, Is.EqualTo(false));
            var falseFormula = factory.Boolean((bool)falseRuntimeResult!);
            Assert.That(FiniteDomainSmtDifferentialOracle.ValidatedFiniteDomain.TryCreate(
                factory, falseFormula, out var falseDomain, out var falseRejection), Is.True);
            Assert.That(falseRejection, Is.Null);
            Assert.That(await FiniteDomainSmtDifferentialOracle.CheckConcreteDomainAsync(
                falseDomain!, falseFormula, CancellationToken.None), Is.EqualTo(FiniteDomainSatisfiability.Unsatisfiable));
        }
        finally
        {
            context.Unload();
        }
    }
}
