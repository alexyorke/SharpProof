namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class ArrayValueOverlayTests
{
    [Test]
    public void EmptyEntriesLeaveAnEmptyBufferEmpty()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        IrValue[] buffer = [];
        SmtNativeUtilities.ApplyArrayObservations(factory, factory.BooleanType, buffer, []);
        Assert.That(buffer, Is.Empty);
    }

    [Test]
    public void AOneElementBooleanBufferRetainsTheExactObservedValue()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateBooleanValue(true);
        IrValue[] buffer = [factory.CreateBooleanValue(false)];
        SmtNativeUtilities.ApplyArrayObservations(factory, factory.BooleanType, buffer, new() { [0] = value });
        Assert.That(buffer[0], Is.SameAs(value));
        Assert.That(buffer[0].Boolean, Is.True);
    }

    [Test]
    public void TwoIntegerEntriesFillTheirIndicesWithoutReplacingSameTypeValues()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(8, true);
        var first = factory.CreateIntegerValueFromBits(type, 128UL);
        var second = factory.CreateIntegerValueFromBits(type, 255UL);
        IrValue[] buffer = [factory.CreateIntegerValueFromBits(type, 0), factory.CreateIntegerValueFromBits(type, 0)];
        var entries = new Dictionary<int, IrValue> { [1] = second, [0] = first };
        SmtNativeUtilities.ApplyArrayObservations(factory, type, buffer, entries);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(buffer[0], Is.SameAs(first));
            Assert.That(buffer[1], Is.SameAs(second));
            Assert.That(buffer[0].IntegerBits, Is.EqualTo(128UL));
            Assert.That(buffer[1].IntegerBits, Is.EqualTo(255UL));
        }
    }

    [TestCase(true, false, 255)]
    [TestCase(false, true, -1)]
    public void IntegerSignednessViewsRetainBitsAndUseTheTargetType(
        bool sourceSigned, bool targetSigned, int expectedNumericValue)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var sourceType = factory.GetOrCreateIntegerType(8, sourceSigned);
        var targetType = factory.GetOrCreateIntegerType(8, targetSigned);
        var unchanged = factory.CreateIntegerValueFromBits(targetType, 0UL);
        var value = factory.CreateIntegerValueFromBits(sourceType, 255UL);
        IrValue[] buffer = [unchanged, factory.CreateIntegerValueFromBits(targetType, 0UL)];
        SmtNativeUtilities.ApplyArrayObservations(factory, targetType, buffer, new() { [1] = value });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(buffer[0], Is.SameAs(unchanged));
            Assert.That(buffer[1].Type, Is.EqualTo(targetType));
            Assert.That(buffer[1].IntegerBits, Is.EqualTo(255UL));
            Assert.That(buffer[1].IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expectedNumericValue)));
        }
    }

    [TestCase(-1, 0)]
    [TestCase(-1, 1)]
    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(2, 2)]
    public void IndicesOutsideTheActualBufferRejectBeforeWriting(int index, int count)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var initial = factory.CreateBooleanValue(false);
        var buffer = Enumerable.Repeat(initial, count).ToArray();
        var value = factory.CreateBooleanValue(true);
        Assert.Throws<UnsupportedIrEncodingException>((Action)(() =>
            SmtNativeUtilities.ApplyArrayObservations(factory, factory.BooleanType, buffer, new() { [index] = value })));
        Assert.That(buffer.All(element => ReferenceEquals(element, initial)), Is.True);
    }

    [Test]
    public void ARejectedLaterEntryPreservesWritesFromEarlierEnumeratedEntries()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var first = factory.CreateBooleanValue(true);
        IrValue[] buffer = [factory.CreateBooleanValue(false)];
        var entries = new Dictionary<int, IrValue> { [0] = first, [1] = factory.CreateBooleanValue(false) };
        Assert.That(entries.First().Key, Is.EqualTo(0));
        Assert.Throws<UnsupportedIrEncodingException>((Action)(() =>
            SmtNativeUtilities.ApplyArrayObservations(factory, factory.BooleanType, buffer, entries)));
        Assert.That(buffer[0], Is.SameAs(first));
    }
}
