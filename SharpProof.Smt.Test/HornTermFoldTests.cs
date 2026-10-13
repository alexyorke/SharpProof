namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class HornTermFoldTests
{
    [Test]
    public void EmptyTermSequenceHasNoFoldResult()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        Assert.That(SmtNativeUtilities.Fold(factory, IrBinaryOperator.Add, []), Is.Null);
    }

    [TestCase(IrBinaryOperator.AndAlso)]
    [TestCase(IrBinaryOperator.OrElse)]
    [TestCase(IrBinaryOperator.Add)]
    [TestCase(IrBinaryOperator.Multiply)]
    [TestCase(IrBinaryOperator.Subtract)]
    public void AValidSingletonSequenceRetainsTheExactFactoryTerm(IrBinaryOperator operation)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = operation is IrBinaryOperator.AndAlso or IrBinaryOperator.OrElse
            ? factory.BooleanType : factory.IntegerType;
        var term = factory.Variable(factory.CreateVariable("singleton", type));
        Assert.That(SmtNativeUtilities.Fold(factory, operation, [term]), Is.SameAs(term));
    }

    [TestCase(IrBinaryOperator.Add, 250UL, 10UL, 4UL)]
    [TestCase(IrBinaryOperator.Multiply, 2UL, 3UL, 6UL)]
    [TestCase(IrBinaryOperator.Subtract, 5UL, 7UL, 254UL)]
    public void TwoIntegerTermsHaveIndependentlyExpectedEightBitResults(
        IrBinaryOperator operation, ulong leftBits, ulong rightBits, ulong expectedBits)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(8, true);
        var left = factory.IntegerBits(type, leftBits);
        var right = factory.IntegerBits(type, rightBits);
        var result = SmtNativeUtilities.Fold(factory, operation, [left, right]);
        Assert.That(result, Is.SameAs(factory.IntegerBits(type, expectedBits)));
    }

    [TestCase(IrBinaryOperator.AndAlso, false)]
    [TestCase(IrBinaryOperator.OrElse, true)]
    public void TwoBooleanTermsHaveIndependentlyExpectedTruthValues(IrBinaryOperator operation, bool expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var result = SmtNativeUtilities.Fold(factory, operation, [factory.Boolean(true), factory.Boolean(false)]);
        Assert.That(result, Is.SameAs(factory.Boolean(expected)));
    }

    [Test]
    public void SubtractionAccumulatesInSourceSequenceOrder()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var result = SmtNativeUtilities.Fold(factory, IrBinaryOperator.Subtract,
            [factory.Integer(20), factory.Integer(5), factory.Integer(3)]);
        Assert.That(result, Is.SameAs(factory.Integer(12)));
    }
}
