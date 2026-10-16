using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Dataflow;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275AdvisoryArgumentListTests
{
    [Test]
    public void InitialOwnedVariableOmittedFromTheListReturnsTheExistingIncompleteReason()
    {
        var built = Build();
        AssertValid(built);
        var result = new CoreIrAdvisoryInterpreter(built.Program, [built.IntegerTarget, built.BooleanTarget])
            .Run(Initial(built), markers: [built.BooleanMarker]);
        AssertIncomplete(result, "initial variable ownership");
    }

    [Test]
    public void ActualAssignmentTargetOmittedFromTheListReturnsGapTop()
    {
        var built = Build();
        AssertValid(built);
        var result = new CoreIrAdvisoryInterpreter(built.Program, [built.Input, built.IntegerTarget])
            .Run(Initial(built), markers: [built.BooleanMarker]);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps.Length, Is.EqualTo(1));
        Assert.That(result.Gaps[0], Is.EqualTo("target ownership"));
        var output = result.RequireOutput(0);
        Assert.That(output.Reachable, Is.True);
        Assert.That(output.HasGap, Is.True);
        Assert.That(output.Values, Is.Empty);
        Assert.That(result.Markers, Is.Empty);
    }

    [Test]
    public void DuplicateActualBooleanMarkerReturnsTheExistingSelectionReason()
    {
        var built = Build();
        AssertValid(built);
        var result = Interpreter(built).Run(Initial(built),
            markers: [built.BooleanMarker, built.BooleanMarker]);
        AssertIncomplete(result, "marker ownership");
    }

    [Test]
    public void ActualNonBooleanAssignmentSelectedAsMarkerReturnsTheExistingSelectionReason()
    {
        var built = Build();
        AssertValid(built);
        var result = Interpreter(built).Run(Initial(built), markers: [built.IntegerMarker]);
        AssertIncomplete(result, "marker ownership");
    }

    [Test]
    public void OversizedActualMarkerListKeepsCountAndDuplicateErrorsDistinct()
    {
        var built = Build();
        AssertValid(built);
        var duplicate = Interpreter(built).Run(Initial(built),
            markers: [built.BooleanMarker, built.BooleanMarker]);
        AssertIncomplete(duplicate, "marker ownership");
        var oversized = new IrAssignInstruction[4097];
        Array.Fill(oversized, built.BooleanMarker);
        var countRejected = Interpreter(built).Run(Initial(built),
            markers: ImmutableArray.CreateRange(oversized));
        AssertIncomplete(countRejected, "marker budget");
    }

    private static void AssertValid(BuiltProgram built)
    {
        var result = Interpreter(built).Run(Initial(built), markers: [built.BooleanMarker]);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        var output = result.RequireOutput(0);
        Assert.That(output.Reachable, Is.True);
        Assert.That(output.HasGap, Is.False);
        Assert.That(output.Values[built.IntegerTarget], Is.EqualTo(IntervalValue.Constant(3)));
        Assert.That(output.Values[built.BooleanTarget], Is.EqualTo(IntervalValue.Constant(1)));
        Assert.That(result.Markers.Length, Is.EqualTo(1));
        Assert.That(result.Markers[0].Marker, Is.SameAs(built.BooleanMarker));
        Assert.That(result.Markers[0].Value, Is.EqualTo(IntervalValue.Constant(1)));
        Assert.That(result.Markers[0].PrefixHasGap, Is.False);
    }

    private static void AssertIncomplete(CoreIrAdvisoryResult result, string reason)
    {
        Assert.That(result.Accepted, Is.False);
        Assert.That(result.Gaps.Length, Is.EqualTo(1));
        Assert.That(result.Gaps[0], Is.EqualTo(reason));
        Assert.That(result.Inputs, Is.Empty);
        Assert.That(result.Outputs, Is.Empty);
        Assert.That(result.Markers, Is.Empty);
        Assert.That(result.TryGetOutput(0, out _), Is.False);
    }

    private static CoreIrAdvisoryInterpreter Interpreter(BuiltProgram built)
    {
        return new(built.Program, [built.Input, built.IntegerTarget, built.BooleanTarget]);
    }

    private static ImmutableDictionary<IrVarId, IntervalValue> Initial(BuiltProgram built)
    {
        return ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(built.Input, IntervalValue.Constant(3));
    }

    private static BuiltProgram Build()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var input = factory.CreateVariable("input", factory.IntegerType);
        var integerTarget = factory.CreateVariable("integerTarget", factory.IntegerType);
        var booleanTarget = factory.CreateVariable("booleanTarget", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var integerMarker = builder.Assign(entry, factory.CreateOperation(), integerTarget, factory.Variable(input));
        var booleanMarker = builder.Assign(entry, factory.CreateOperation(), booleanTarget,
            factory.Binary(IrBinaryOperator.LessThan, factory.Variable(integerTarget), factory.Integer(5)));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(booleanTarget));
        var program = builder.Build();
        Assert.That(program.Blocks[0].Instructions[0], Is.SameAs(integerMarker));
        Assert.That(program.Blocks[0].Instructions[1], Is.SameAs(booleanMarker));
        return new(program, input, integerTarget, booleanTarget, integerMarker, booleanMarker);
    }

    private sealed record BuiltProgram(IrProgram Program, IrVarId Input, IrVarId IntegerTarget,
        IrVarId BooleanTarget, IrAssignInstruction IntegerMarker, IrAssignInstruction BooleanMarker);
}
