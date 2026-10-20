using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Dataflow;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class Pr275AdvisoryMarkerComparisonTests
{
    [TestCase(IrBinaryOperator.Equal, false, 0)]
    [TestCase(IrBinaryOperator.NotEqual, false, 1)]
    [TestCase(IrBinaryOperator.LessThan, false, 1)]
    [TestCase(IrBinaryOperator.LessThanOrEqual, false, 1)]
    [TestCase(IrBinaryOperator.GreaterThan, false, 0)]
    [TestCase(IrBinaryOperator.GreaterThanOrEqual, false, 0)]
    [TestCase(IrBinaryOperator.Equal, true, 0)]
    [TestCase(IrBinaryOperator.NotEqual, true, 1)]
    [TestCase(IrBinaryOperator.LessThan, true, 0)]
    [TestCase(IrBinaryOperator.LessThanOrEqual, true, 0)]
    [TestCase(IrBinaryOperator.GreaterThan, true, 1)]
    [TestCase(IrBinaryOperator.GreaterThanOrEqual, true, 1)]
    public void SeparatedIntervalsGiveTheMathematicalComparison(IrBinaryOperator operation, bool reverse, int expected)
    {
        var built = Build(operation);
        var leftMinimum = reverse ? 3 : 0;
        var rightMinimum = reverse ? 0 : 3;
        var result = new CoreIrAdvisoryInterpreter(built.Program, [built.Left, built.Right, built.Result])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty
                .Add(built.Left, IntervalValue.Range(leftMinimum, leftMinimum + 1))
                .Add(built.Right, IntervalValue.Range(rightMinimum, rightMinimum + 1)),
                markers: [built.Marker], token: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.RequireOutput(0).Values[built.Result], Is.EqualTo(IntervalValue.Constant(expected)));
        Assert.That(result.Markers.Length, Is.EqualTo(1));
        Assert.That(result.Markers[0].Marker, Is.SameAs(built.Marker));
        Assert.That(result.Markers[0].Value, Is.EqualTo(IntervalValue.Constant(expected)));
        Assert.That(result.Markers[0].PrefixHasGap, Is.False);

        for (var left = leftMinimum; left <= leftMinimum + 1; left++)
        {
            for (var right = rightMinimum; right <= rightMinimum + 1; right++)
            {
                var concrete = new IrProgramInterpreter(built.Program.Factory).Execute(built.Program,
                    global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(built.Left, built.Program.Factory.CreateIntegerValue(left)).SetItem(built.Right, built.Program.Factory.CreateIntegerValue(right)));
                Assert.That(concrete.ReturnValue!.Boolean, Is.EqualTo(expected != 0));
            }
        }
    }

    [TestCase(IrBinaryOperator.Equal)]
    [TestCase(IrBinaryOperator.NotEqual)]
    [TestCase(IrBinaryOperator.LessThan)]
    [TestCase(IrBinaryOperator.LessThanOrEqual)]
    [TestCase(IrBinaryOperator.GreaterThan)]
    [TestCase(IrBinaryOperator.GreaterThanOrEqual)]
    public void OverlappingIntervalsRetainUncertainty(IrBinaryOperator operation)
    {
        var built = Build(operation);
        var result = new CoreIrAdvisoryInterpreter(built.Program, [built.Left, built.Right, built.Result])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty
                .Add(built.Left, IntervalValue.Range(0, 2))
                .Add(built.Right, IntervalValue.Range(1, 3)),
                markers: [built.Marker], token: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.RequireOutput(0).Values[built.Result], Is.EqualTo(IntervalValue.Range(0, 1)));
        Assert.That(result.Markers.Length, Is.EqualTo(1));
        Assert.That(result.Markers[0].Marker, Is.SameAs(built.Marker));
        Assert.That(result.Markers[0].Value, Is.EqualTo(IntervalValue.Range(0, 1)));
        Assert.That(result.Markers[0].PrefixHasGap, Is.False);
        var observed = new HashSet<bool>();
        for (var left = 0; left <= 2; left++)
        {
            for (var right = 1; right <= 3; right++)
            {
                var concrete = new IrProgramInterpreter(built.Program.Factory).Execute(built.Program,
                    global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(built.Left, built.Program.Factory.CreateIntegerValue(left)).SetItem(built.Right, built.Program.Factory.CreateIntegerValue(right)));
                observed.Add(concrete.ReturnValue!.Boolean);
            }
        }
        Assert.That(observed.Count, Is.EqualTo(2));
        Assert.That(observed, Does.Contain(false));
        Assert.That(observed, Does.Contain(true));
    }

    [Test]
    public void OwnedNonComparisonMarkerRetainsUnknownBooleanInterval()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var resultVariable = factory.CreateVariable("result", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var marker = builder.Assign(entry, factory.CreateOperation(), resultVariable,
            factory.Binary(IrBinaryOperator.AndAlso, factory.Variable(flag), factory.Variable(flag)));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(resultVariable));
        var result = new CoreIrAdvisoryInterpreter(builder.Build(), [flag, resultVariable])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty.Add(flag, IntervalValue.Range(0, 1)),
                markers: [marker], token: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.RequireOutput(0).Values[resultVariable], Is.EqualTo(IntervalValue.Range(0, 1)));
        Assert.That(result.Markers.Length, Is.EqualTo(1));
        Assert.That(result.Markers[0].Value, Is.EqualTo(IntervalValue.Range(0, 1)));
        Assert.That(result.Markers[0].PrefixHasGap, Is.False);
    }

    [Test]
    public void OwnedBooleanLiteralMarkerRetainsSingletonTrue()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var resultVariable = factory.CreateVariable("result", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var marker = builder.Assign(entry, factory.CreateOperation(), resultVariable, factory.Boolean(true));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(resultVariable));
        var result = new CoreIrAdvisoryInterpreter(builder.Build(), [resultVariable])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty, markers: [marker], token: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.RequireOutput(0).Values[resultVariable], Is.EqualTo(IntervalValue.Constant(1)));
        Assert.That(result.Markers.Length, Is.EqualTo(1));
        Assert.That(result.Markers[0].Value, Is.EqualTo(IntervalValue.Constant(1)));
        Assert.That(result.Markers[0].PrefixHasGap, Is.False);
    }

    private static (IrProgram Program, IrVarId Left, IrVarId Right, IrVarId Result, IrAssignInstruction Marker)
        Build(IrBinaryOperator operation)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var left = factory.CreateVariable("left", factory.IntegerType);
        var right = factory.CreateVariable("right", factory.IntegerType);
        var result = factory.CreateVariable("result", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var marker = builder.Assign(entry, factory.CreateOperation(), result,
            factory.Binary(operation, factory.Variable(left), factory.Variable(right)));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(result));
        var program = builder.Build();
        Assert.That(program.Blocks[0].Instructions[0], Is.SameAs(marker));
        return (program, left, right, result, marker);
    }
}
