using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Dataflow;
using SharpProof.Ir;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreAdmissionCoverageTests
{
    [Test]
    public void ExhaustedMarkerReplayDoesNotPublishPartialStates()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var (program, value, assignment) = Program(factory, boolean: true);
        var interpreter = new CoreIrAdvisoryInterpreter(program, [value]);
        var rejected = interpreter.Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty,
            markers: [assignment], maximumTermWork: 1);
        Rejected(rejected, "aggregate term work budget");
        Assert.That(rejected.Inputs, Is.Empty);
        Assert.That(rejected.Markers, Is.Empty);

        var accepted = interpreter.Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty,
            markers: [assignment], maximumTermWork: 2);
        Assert.That(accepted.Accepted, Is.True);
        Assert.That(accepted.Gaps, Is.Empty);
        Assert.That(accepted.Markers, Has.Length.EqualTo(1));
        Assert.That(accepted.RequireOutput(0).Values[value].SingletonValue, Is.EqualTo(0));
    }

    [TestCase(0, 8192)]
    [TestCase(-1, 8192)]
    [TestCase(65536, -1)]
    public void InvalidWorkArgumentsAreRejectedBeforePublishingStates(int termWork, int syntheticEdges)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var (program, value, _) = Program(factory);
        Rejected(new CoreIrAdvisoryInterpreter(program, [value])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty,
                maximumTermWork: termWork, maximumSyntheticEdges: syntheticEdges), "invalid work budget");
    }

    [Test]
    public void AnOversizedMarkerRequestIsRejectedBeforePublishingStates()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var (program, value, assignment) = Program(factory, boolean: true);
        Rejected(new CoreIrAdvisoryInterpreter(program, [value])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty,
                markers: [.. Enumerable.Repeat(assignment, 4097)]), "marker budget");
    }

    [Test]
    public void DuplicateTrackedVariablesAreRejected()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var (program, value, _) = Program(factory);
        Rejected(new CoreIrAdvisoryInterpreter(program, [value, value])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty), "variable budget/ownership");
    }

    [Test]
    public void ForeignFactoryVariableIsRejected()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var (program, _, _) = Program(factory);
        var foreignFactory = new IrFactory(IrExecutionSemantics.Total);
        var foreign = foreignFactory.CreateVariable("foreign", foreignFactory.IntegerType);
        Rejected(new CoreIrAdvisoryInterpreter(program, [foreign])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty), "variable ownership");
    }

    [Test]
    public void InitialFactForAnUntrackedVariableIsRejected()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var (program, value, _) = Program(factory);
        var outside = factory.CreateVariable("outside", factory.IntegerType);
        Rejected(new CoreIrAdvisoryInterpreter(program, [value])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(outside, IntervalValue.Constant(1))),
            "initial variable ownership");
    }

    [Test]
    public void AnInitialScalarOutsideItsTypeRangeIsRejected()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var (program, value, _) = Program(factory);
        Rejected(new CoreIrAdvisoryInterpreter(program, [value])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(value, IntervalValue.Constant(long.MaxValue))),
            "invalid initial scalar facts");
    }

    [Test]
    public void MarkerFromAnotherRealProgramIsRejected()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var (program, value, _) = Program(factory, boolean: true);
        var (_, _, foreignMarker) = Program(factory, boolean: true);
        Rejected(new CoreIrAdvisoryInterpreter(program, [value])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty, markers: [foreignMarker]), "marker ownership");
    }

    [Test]
    public void AProgramTargetMissingFromTheTrackedSetRemainsAnExplicitGap()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var (program, value, _) = Program(factory);
        var result = new CoreIrAdvisoryInterpreter(program, [])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Does.Contain("target ownership"));
        Assert.That(result.RequireOutput(0).HasGap, Is.True);
        var domain = new CoreIrAdvisoryDomain(factory, [value]);
        var concrete = new IrProgramInterpreter(factory).Execute(program).ReturnValue!.Integer;
        Assert.That(domain.Get(result.RequireOutput(0), value).Contains(concrete), Is.True);
    }

    private static (IrProgram Program, IrVarId Value, IrAssignInstruction Assignment) Program(IrFactory factory, bool boolean = false)
    {
        var value = factory.CreateVariable("value", boolean ? factory.BooleanType : factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var assignment = builder.Assign(entry, factory.CreateOperation(), value,
            boolean ? factory.Boolean(false) : factory.Integer(0));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(value));
        return (builder.Build(), value, assignment);
    }

    private static void Rejected(CoreIrAdvisoryResult result, string reason)
    {
        Assert.That(result.Accepted, Is.False);
        Assert.That(result.Gaps, Does.Contain(reason));
        Assert.That(result.Outputs, Is.Empty);
    }
}
