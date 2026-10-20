using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
internal sealed class IrAccessorSemanticsTests
{
    [TestCase(true, 17L)]
    [TestCase(false, 23L)]
    public void BlockTerminatorRetainsTheBuiltControlFlowInstruction(bool flag, long expected)
    {
        var factory = new IrFactory();
        var condition = factory.CreateVariable("condition", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var first = builder.CreateBlock("first");
        var second = builder.CreateBlock("second");
        var branch = builder.Branch(entry, factory.CreateOperation("choose"),
            factory.Variable(condition), first, second);
        var firstReturn = builder.Return(first, factory.CreateOperation("first-return"), factory.Integer(17));
        var secondReturn = builder.Return(second, factory.CreateOperation("second-return"), factory.Integer(23));
        var program = builder.Build();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(program.GetBlock(entry).Terminator, Is.SameAs(branch));
            Assert.That(program.GetBlock(first).Terminator, Is.SameAs(firstReturn));
            Assert.That(program.GetBlock(second).Terminator, Is.SameAs(secondReturn));
        }
        var result = new IrProgramInterpreter(factory).Execute(program,
            global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(condition, factory.CreateBooleanValue(flag)));
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ReturnValue!.Integer, Is.EqualTo(expected));
    }
    [TestCase(IrBinaryOperator.Add, IrBinaryOperator.Equal, -6L, true, "((-v0) + v1)")]
    [TestCase(IrBinaryOperator.Add, IrBinaryOperator.NotEqual, -6L, false, "((-v0) + v1)")]
    [TestCase(IrBinaryOperator.Subtract, IrBinaryOperator.Equal, -12L, true, "((-v0) - v1)")]
    [TestCase(IrBinaryOperator.Subtract, IrBinaryOperator.NotEqual, 5L, true, "((-v0) - v1)")]
    public void ArithmeticMetadataFlowsThroughInterningPrintingAndComparison(
        IrBinaryOperator arithmetic, IrBinaryOperator comparison, long comparedValue,
        bool expected, string expectedArithmeticText)
    {
        System.ArgumentNullException.ThrowIfNull(expectedArithmeticText);
        var factory = new IrFactory();
        var left = factory.CreateVariable("left", factory.IntegerType);
        var right = factory.CreateVariable("right", factory.IntegerType);
        var negated = factory.Unary(IrUnaryOperator.Negate, factory.Variable(left));
        var expression = factory.Binary(arithmetic, negated, factory.Variable(right));
        var repeated = factory.Binary(arithmetic, negated, factory.Variable(right));
        var differentOperand = factory.Binary(arithmetic, negated, factory.Integer(4));
        var comparisonTerm = factory.Binary(comparison, expression, factory.Integer(comparedValue));
        var result = new IrInterpreter(factory).Evaluate(comparisonTerm,
            global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(left, factory.CreateIntegerValue(9)).SetItem(right, factory.CreateIntegerValue(3)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(repeated, Is.SameAs(expression));
            Assert.That(differentOperand, Is.Not.SameAs(expression));
            Assert.That(new IrPrinter(factory).Print(expression), Is.EqualTo(expectedArithmeticText));
            Assert.That(factory.GetTypeInfo(expression.Type).Kind, Is.EqualTo(IrTypeKind.Integer));
            Assert.That(factory.GetTypeInfo(comparisonTerm.Type).Kind, Is.EqualTo(IrTypeKind.Boolean));
            Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(result.Value!.Boolean, Is.EqualTo(expected));
        }
    }
}
