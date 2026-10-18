using System.Collections.Immutable;
using System.Linq;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Dataflow;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275AdvisoryDefaultWorkDagTests
{
    [Test]
    public void SharedDepthFourteenDagFitsDefaultWorkWithoutMarkerReplay()
    {
        var built = Build(14);
        var result = new CoreIrAdvisoryInterpreter(built.Program, [built.Input, built.MarkerVariable])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(built.Input, IntervalValue.Constant(0)), token: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.Iterations, Is.EqualTo(1));
        Assert.That(result.RequireOutput(0).Values[built.MarkerVariable], Is.EqualTo(IntervalValue.Constant(1)));
        Assert.That(result.Markers, Is.Empty);
    }

    [Test]
    public void SharedDepthThirteenDagReplaysOwnedMarkerWithinDefaultWork()
    {
        var built = Build(13);
        var result = new CoreIrAdvisoryInterpreter(built.Program, [built.Input, built.MarkerVariable])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(built.Input, IntervalValue.Constant(0)),
                markers: [built.Marker], token: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.RequireOutput(0).Values[built.MarkerVariable], Is.EqualTo(IntervalValue.Constant(1)));
        Assert.That(result.Markers.Length, Is.EqualTo(1));
        var snapshot = result.Markers.Single();
        Assert.That(snapshot.Marker, Is.SameAs(built.Marker));
        Assert.That(snapshot.Value, Is.EqualTo(IntervalValue.Constant(1)));
        Assert.That(snapshot.PrefixHasGap, Is.False);
    }

    [Test]
    public void SharedDepthFourteenDagPreservesDefaultWorkExhaustionAndClearsSnapshots()
    {
        var built = Build(14);
        var result = new CoreIrAdvisoryInterpreter(built.Program, [built.Input, built.MarkerVariable])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(built.Input, IntervalValue.Constant(0)),
                markers: [built.Marker], token: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Accepted, Is.False);
        Assert.That(result.Gaps.Length, Is.EqualTo(1));
        Assert.That(result.Gaps[0], Is.EqualTo("aggregate term work budget"));
        Assert.That(result.Inputs, Is.Empty);
        Assert.That(result.Outputs, Is.Empty);
        Assert.That(result.Markers, Is.Empty);
        Assert.That(result.TryGetOutput(0, out _), Is.False);
    }

    [Test]
    public void SharedDepthSeventeenDagPreservesNaturalNormalAnalysisWorkExhaustion()
    {
        var built = Build(17);
        var result = new CoreIrAdvisoryInterpreter(built.Program, [built.Input, built.MarkerVariable])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(built.Input, IntervalValue.Constant(0)), token: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Accepted, Is.False);
        Assert.That(result.Gaps.Length, Is.EqualTo(1));
        Assert.That(result.Gaps[0], Is.EqualTo("aggregate term work budget"));
        Assert.That(result.Inputs, Is.Empty);
        Assert.That(result.Outputs, Is.Empty);
        Assert.That(result.Markers, Is.Empty);
        Assert.That(result.TryGetOutput(0, out _), Is.False);
    }
    private static (IrProgram Program, IrVarId Input, IrVarId MarkerVariable, IrAssignInstruction Marker) Build(int depth)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var input = factory.CreateVariable("input", factory.IntegerType);
        var markerVariable = factory.CreateVariable("marker", factory.BooleanType);
        IrTerm term = factory.Variable(input);
        for (var level = 0; level < depth; level++)
        {
            var previous = term;
            term = factory.Binary(IrBinaryOperator.Add, previous, previous);
            Assert.That(term, Is.TypeOf<IrBinaryTerm>());
            var binary = (IrBinaryTerm)term;
            Assert.That(binary.Left, Is.SameAs(previous));
            Assert.That(binary.Right, Is.SameAs(previous));
        }
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var marker = builder.Assign(entry, factory.CreateOperation(), markerVariable,
            factory.Binary(IrBinaryOperator.LessThan, term, factory.Integer(1)));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(markerVariable));
        var program = builder.Build();
        Assert.That(program.Blocks.Length, Is.EqualTo(1));
        Assert.That(program.Blocks[0].Instructions.Length, Is.EqualTo(2));
        Assert.That(program.Blocks[0].Instructions[0], Is.SameAs(marker));
        return (program, input, markerVariable, marker);
    }
}
