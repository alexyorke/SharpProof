using Microsoft.CodeAnalysis;
using SharpProof.Analyzer.Configuration;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Dataflow;
using SharpProof.Ir;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AdvisoryCallConsumerTests
{
    [TestCase("Need(0);", true)]
    [TestCase("Need(1);", false)]

    [TestCase("if (value > 0) Need(value);", true)]
    [TestCase("for (var i = 0; i < 2; i++) Need(i);", true)]
    [TestCase("var candidate = value > 0 ? 1 : 0; Need(candidate);", true)]
    [TestCase("var candidate = 1; candidate = 0; Need(candidate);", true)]
    public void GenuineSourceCallsProduceAdvisoryMarkerObservations(string body, bool mayViolate)
    {
        var source = "using SharpProof.Attributes; static class Subject { private static void Need(int candidate) { Contract.Requires(candidate > 0); } public static void Target(int value) { " + body + " } }";
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0027"]);
        var declaration = compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Target");
        var result = AdvisoryCallConsumer.Analyze(compilation, declaration, true);
        Assert.That(result.Enabled, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.Calls, Has.Length.EqualTo(1));
        Assert.That(result.Calls[0].MayViolate, Is.EqualTo(mayViolate));
        Assert.That(result.Calls[0].PrefixHasGap, Is.False);
        var program = result.Program!;
        var replayed = 0;
        foreach (var input in new[] { -2, -1, 0, 1, 2 })
        {
            var replay = new IrProgramReplayOptions(request => program.Factory.CreateIntegerValue(program.Factory.GetVariableInfo(request.Variable).Type, input))
            {
                AssignmentObserver = (assignment, value, tainted) =>
                {
                    if (!ReferenceEquals(assignment, result.Calls[0].Marker))
                    {
                        return;
                    }
                    replayed++;
                    Assert.That(tainted, Is.False);
                    Assert.That(result.Calls[0].Condition.Contains(value.Boolean ? 1 : 0), Is.True);
                }
            };
            var initial = program.GetBlock(program.Entry).Instructions.OfType<IrAssignInstruction>().Select(assignment => assignment.Value).OfType<IrVariableTerm>()
                .GroupBy(variable => variable.Variable).ToDictionary(group => group.Key, group => program.Factory.CreateIntegerValue(program.Factory.GetVariableInfo(group.Key).Type, input));
            _ = new IrProgramInterpreter(program.Factory).Execute(program, initial, 1000, replay);
        }
        Assert.That(replayed, Is.GreaterThan(0));
    }

    [TestCase("Need(0); Contract.Assume(false);")]
    [TestCase("Need(0); throw new System.Exception();")]
    public void UnsupportedSourceContinuationHasAnExplicitGapAndNoObservations(string body)
    {
        var source = "using SharpProof.Attributes; static class Subject { private static void Need(int candidate) { Contract.Requires(candidate > 0); } public static void Target(int value) { " + body + " } }";
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0027"]);
        var declaration = compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Target");
        var result = AdvisoryCallConsumer.Analyze(compilation, declaration, true);
        Assert.That(result.Gaps, Is.Not.Empty);
        Assert.That(result.Calls, Is.Empty);
    }
    [TestCase(false)]
    [TestCase(true)]
    public void AnalyzerInventoryPublishesOptionalAdvisoryObservationsOnce(bool enabled)
    {
        var compilation = AnalyzerTestHost.CreateCompilation("using SharpProof.Attributes; static class Subject { static void Need(int value) { Contract.Requires(value > 0); } public static void Target() { Need(0); } }", ["SP0027"]);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Target");
        var model = compilation.GetSemanticModel(tree);
        var owner = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        var observations = new List<AdvisoryCallAnalysis>();
        var session = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll, CancellationToken.None,
            advisoryCallObserver: enabled ? (_, analysis) => observations.Add(analysis) : null);
        var diagnostics = new List<Microsoft.CodeAnalysis.Diagnostic>();
        _ = RequiresCallSiteTreeAnalyzer.Analyze(owner, declaration, model, session, diagnostics.Add, CancellationToken.None);
        _ = RequiresCallSiteTreeAnalyzer.Analyze(owner, declaration, model, session, diagnostics.Add, CancellationToken.None);
        Assert.That(observations.Count, Is.EqualTo(enabled ? 1 : 0));
        Assert.That(diagnostics.Count(diagnostic => diagnostic.Id == "SP0027"), Is.EqualTo(1));
        if (enabled)
        {
            Assert.That(observations[0].Calls, Has.Length.EqualTo(1));
            Assert.That(observations[0].Calls[0].MayViolate, Is.True);
            Assert.That(observations[0].Gaps, Is.Empty);
        }
    }
    [Test]
    public void DisabledPathDoesNotInspectInputsOrLowerBodies()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.That(AdvisoryCallConsumer.Analyze(null!, null!, false, canceled.Token), Is.SameAs(AdvisoryCallAnalysis.Disabled));
    }

    [Test]
    public void PreCancellationPublishesNoObservations()
    {
        var compilation = AnalyzerTestHost.CreateCompilation("static class Subject { public static void Target() {} }", []);
        var declaration = compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>((Action)(() => AdvisoryCallConsumer.Analyze(compilation, declaration, true, canceled.Token)));
    }
}

[TestFixture]
public sealed class AdvisoryMarkerPrefixTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void MarkerBeforeAssumptionOrFaultRetainsItsExactPrefix(bool fault)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var markerValue = factory.CreateVariable("marker", factory.BooleanType);
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var array = factory.CreateVariable("array", sequenceType);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var marker = builder.Assign(block, factory.CreateOperation(), markerValue, factory.Boolean(false));
        if (fault)
        {
            builder.Allocate(block, factory.CreateOperation(), sequenceType, array, factory.Integer(-1));
        }
        else
        {
            builder.Assume(block, factory.CreateOperation(), factory.Boolean(false));
        }
        builder.Return(block, factory.CreateOperation());
        var result = new CoreIrAdvisoryInterpreter(builder.Build(), [markerValue, array]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty, markers: [marker]);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Markers, Has.Length.EqualTo(1));
        Assert.That(result.Markers[0].Value, Is.EqualTo(IntervalValue.Constant(0)));
        Assert.That(result.Markers[0].PrefixHasGap, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OnlyApproximationBeforeMarkerTaintsItsPrefix(bool before)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var markerValue = factory.CreateVariable("marker", factory.BooleanType);
        var unknown = factory.CreateVariable("unknown", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        if (before)
        {
            builder.Havoc(block, factory.CreateOperation(), IrHavocKind.Variables, IrHavocOrigin.Approximation, unknown);
        }
        var marker = builder.Assign(block, factory.CreateOperation(), markerValue, factory.Boolean(false));
        if (!before)
        {
            builder.Havoc(block, factory.CreateOperation(), IrHavocKind.Variables, IrHavocOrigin.Approximation, unknown);
        }
        builder.Return(block, factory.CreateOperation());
        var result = new CoreIrAdvisoryInterpreter(builder.Build(), [markerValue, unknown]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty, markers: [marker]);
        Assert.That(result.Markers, Has.Length.EqualTo(1));
        Assert.That(result.Markers[0].PrefixHasGap, Is.EqualTo(before));
    }

    [Test]
    public void BranchMergeUsesConvergedInputRatherThanOneTransientPredecessor()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var selector = factory.CreateVariable("selector", factory.BooleanType);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var markerValue = factory.CreateVariable("marker", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var left = builder.CreateBlock();
        var right = builder.CreateBlock();
        var join = builder.CreateBlock();
        builder.Branch(entry, factory.CreateOperation(), factory.Variable(selector), left, right);
        builder.Assign(left, factory.CreateOperation(), value, factory.Integer(0));
        builder.Goto(left, factory.CreateOperation(), join);
        builder.Assign(right, factory.CreateOperation(), value, factory.Integer(1));
        builder.Goto(right, factory.CreateOperation(), join);
        var marker = builder.Assign(join, factory.CreateOperation(), markerValue, factory.Binary(IrBinaryOperator.GreaterThan, factory.Variable(value), factory.Integer(0)));
        builder.Return(join, factory.CreateOperation());
        var program = builder.Build();
        var result = new CoreIrAdvisoryInterpreter(program, [selector, value, markerValue]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty, markers: [marker]);
        Assert.That(result.Markers, Has.Length.EqualTo(1));
        Assert.That(result.Markers[0].Value.Contains(0), Is.True);
        Assert.That(result.Markers[0].Value.Contains(1), Is.True);
        var rejected = new CoreIrAdvisoryInterpreter(program, [selector, value, markerValue]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty, maxIterations: 1, markers: [marker]);
        Assert.That(rejected.Accepted, Is.False);
        Assert.That(rejected.Markers, Is.Empty);
    }

    [Test]
    public void LoopObservationContainsEarlyFalseAndLaterTrueOccurrences()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var markerValue = factory.CreateVariable("marker", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var header = builder.CreateBlock();
        var body = builder.CreateBlock();
        var exit = builder.CreateBlock();
        builder.Assign(entry, factory.CreateOperation(), value, factory.Integer(0));
        builder.Goto(entry, factory.CreateOperation(), header);
        builder.Branch(header, factory.CreateOperation(), factory.Binary(IrBinaryOperator.LessThan, factory.Variable(value), factory.Integer(2)), body, exit);
        var marker = builder.Assign(body, factory.CreateOperation(), markerValue, factory.Binary(IrBinaryOperator.GreaterThan, factory.Variable(value), factory.Integer(0)));
        builder.Assign(body, factory.CreateOperation(), value, factory.Binary(IrBinaryOperator.Add, factory.Variable(value), factory.Integer(1)));
        builder.Goto(body, factory.CreateOperation(), header);
        builder.Return(exit, factory.CreateOperation());
        var result = new CoreIrAdvisoryInterpreter(builder.Build(), [value, markerValue]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty, markers: [marker]);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Markers, Has.Length.EqualTo(1));
        Assert.That(result.Markers[0].Value.Contains(0), Is.True);
        Assert.That(result.Markers[0].Value.Contains(1), Is.True);
        Assert.That(result.Markers[0].PrefixHasGap, Is.False);
    }
    [Test]
    public void PrefixReevaluationSpendsTheSameAggregateTermBudget()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateVariable("marker", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var marker = builder.Assign(block, factory.CreateOperation(), value, factory.Boolean(false));
        builder.Return(block, factory.CreateOperation());
        var program = builder.Build();
        var withoutMarkers = new CoreIrAdvisoryInterpreter(program, [value]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty, maximumTermWork: 1);
        Assert.That(withoutMarkers.Accepted, Is.True);
        var result = new CoreIrAdvisoryInterpreter(program, [value]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty, maximumTermWork: 1, markers: [marker]);
        Assert.That(result.Accepted, Is.False);
        Assert.That(result.Gaps, Does.Contain("aggregate term work budget"));
        Assert.That(result.Markers, Is.Empty);
    }
}
