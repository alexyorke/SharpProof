using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Dataflow;
using SharpProof.Ir;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;

namespace SharpProof.Analyzer.Test;

[TestFixture]
internal sealed class AnalyzerCoreInterpreterCoverageTests
{
    [TestCase(IrBinaryOperator.NotEqual, 1, 4)]
    [TestCase(IrBinaryOperator.NotEqual, 4, 1)]
    [TestCase(IrBinaryOperator.NotEqual, 1, 2)]
    [TestCase(IrBinaryOperator.LessThan, 1, 4)]
    [TestCase(IrBinaryOperator.LessThan, 4, 1)]
    [TestCase(IrBinaryOperator.LessThan, 1, 2)]
    [TestCase(IrBinaryOperator.GreaterThanOrEqual, 1, 4)]
    [TestCase(IrBinaryOperator.GreaterThanOrEqual, 4, 1)]
    [TestCase(IrBinaryOperator.GreaterThanOrEqual, 1, 2)]
    public void ComparisonIntervalsContainEveryConcreteResult(IrBinaryOperator operation, int left, int right)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var first = factory.CreateVariable("first", factory.IntegerType);
        var second = factory.CreateVariable("second", factory.IntegerType);
        var output = factory.CreateVariable("output", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var comparison = builder.Assign(entry, factory.CreateOperation(), output,
            factory.Binary(operation, factory.Variable(first), factory.Variable(second)));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(output));
        var program = builder.Build();
        var result = new CoreIrAdvisoryInterpreter(program, [first, second, output]).Run(
            ImmutableDictionary<IrVarId, IntervalValue>.Empty
                .Add(first, IntervalValue.Range(left, left + 1))
                .Add(second, IntervalValue.Range(right, right + 1)), markers: [comparison]);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        var interval = result.RequireOutput(0).Values[output];
        var marker = result.Markers.Single().Value;
        for (var firstValue = left; firstValue <= left + 1; firstValue++)
        {
            for (var secondValue = right; secondValue <= right + 1; secondValue++)
            {
                var concrete = new IrProgramInterpreter(factory).Execute(program,
                    new Dictionary<IrVarId, IrValue>
                    {
                        [first] = factory.CreateIntegerValue(firstValue),
                        [second] = factory.CreateIntegerValue(secondValue)
                    });
                var concreteValue = concrete.ReturnValue!.Boolean ? 1 : 0;
                Assert.That(interval.Contains(concreteValue), Is.True);
                Assert.That(marker.Contains(concreteValue), Is.True);
            }
        }
    }

    [TestCase(null, null)]
    [TestCase(null, "value")]
    [TestCase("value", null)]
    [TestCase("value", "value")]
    [TestCase("first", "second")]
    public void StringEqualityNullnessContainsTheConcreteResult(string? left, string? right)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var first = factory.CreateVariable("first", factory.StringType);
        var second = factory.CreateVariable("second", factory.StringType);
        var output = factory.CreateVariable("output", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Assign(entry, factory.CreateOperation(), first,
            left == null ? factory.Null(factory.StringType) : factory.String(left));
        builder.Assign(entry, factory.CreateOperation(), second,
            right == null ? factory.Null(factory.StringType) : factory.String(right));
        builder.Assign(entry, factory.CreateOperation(), output, factory.Binary(IrBinaryOperator.StringEquals,
            factory.Variable(first), factory.Variable(second)));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(output));
        var program = builder.Build();
        var result = new CoreIrAdvisoryInterpreter(program, [first, second, output]).Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        var concrete = new IrProgramInterpreter(factory).Execute(program);
        Assert.That(result.Accepted, Is.True);
        Assert.That(result.Gaps, Is.Empty);
        Assert.That(result.RequireOutput(0).Values[output].Contains(concrete.ReturnValue!.Boolean ? 1 : 0), Is.True);
        Assert.That(result.TryGetOutput(-1, out _), Is.False);
        Assert.That(result.TryGetOutput(result.Outputs.Length, out _), Is.False);
    }
}
