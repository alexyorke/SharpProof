namespace SharpProof.Dataflow.Test;

[TestFixture]
public sealed class DataflowArgumentGuardContractTests
{
    private const string ParameterName = "value";
    private const string Detail = "A supplied object is required.";

    [Test]
    public void TestsTargetTheShippedAssemblyGuard()
    {
        var guard = typeof(global::SharpProof.Dataflow.ArgumentNullGuard);
        var marker = typeof(global::SharpProof.Dataflow.NullnessValue);
        Assert.That(guard.Assembly.GetName().Name, Is.EqualTo("SharpProof.Dataflow"));
        Assert.That(guard.Module, Is.SameAs(marker.Module));
        Assert.That(guard.IsPublic, Is.False);
    }

    [TestCase(0, TestName = "NonnegativeIntAcceptsZero")]
    [TestCase(int.MaxValue, TestName = "NonnegativeIntPreservesMaximum")]
    public void NonnegativeIntPreservesValidValue(int value)
    {
        Assert.That(global::SharpProof.Dataflow.ArgumentNullGuard.RequireNonnegative(value, ParameterName), Is.EqualTo(value));
    }

    [TestCase(-1, TestName = "NonnegativeIntRejectsMinusOne")]
    [TestCase(int.MinValue, TestName = "NonnegativeIntRejectsMinimum")]
    public void NonnegativeIntRejectsNegativeValue(int value)
    {
        AssertOutOfRange(() => { _ = global::SharpProof.Dataflow.ArgumentNullGuard.RequireNonnegative(value, ParameterName); });
    }

    [TestCase(0L, TestName = "NonnegativeLongAcceptsZero")]
    [TestCase(long.MaxValue, TestName = "NonnegativeLongPreservesMaximum")]
    public void NonnegativeLongPreservesValidValue(long value)
    {
        Assert.That(global::SharpProof.Dataflow.ArgumentNullGuard.RequireNonnegative(value, ParameterName), Is.EqualTo(value));
    }

    [TestCase(-1L, TestName = "NonnegativeLongRejectsMinusOne")]
    [TestCase(long.MinValue, TestName = "NonnegativeLongRejectsMinimum")]
    public void NonnegativeLongRejectsNegativeValue(long value)
    {
        AssertOutOfRange(() => { _ = global::SharpProof.Dataflow.ArgumentNullGuard.RequireNonnegative(value, ParameterName); });
    }

    [TestCase(0, 1, TestName = "IndexAcceptsFirstElement")]
    [TestCase(2, 3, TestName = "IndexAcceptsLastElement")]
    [TestCase(int.MaxValue - 1, int.MaxValue, TestName = "IndexPreservesLargestValidBoundary")]
    public void IndexPreservesValidValue(int value, int length)
    {
        Assert.That(global::SharpProof.Dataflow.ArgumentNullGuard.RequireIndex(value, length, ParameterName), Is.EqualTo(value));
    }

    [TestCase(-1, 3, TestName = "IndexRejectsNegativeValue")]
    [TestCase(3, 3, TestName = "IndexRejectsLengthBoundary")]
    [TestCase(0, 0, TestName = "IndexRejectsEmptyLength")]
    [TestCase(int.MinValue, int.MaxValue, TestName = "IndexRejectsMinimumValue")]
    [TestCase(int.MaxValue, int.MaxValue, TestName = "IndexRejectsMaximumLengthBoundary")]
    public void IndexRejectsValueOutsideLength(int value, int length)
    {
        AssertOutOfRange(() => { _ = global::SharpProof.Dataflow.ArgumentNullGuard.RequireIndex(value, length, ParameterName); });
    }

    [TestCase(1, TestName = "PositiveIntAcceptsOne")]
    [TestCase(int.MaxValue, TestName = "PositiveIntPreservesMaximum")]
    public void PositiveIntPreservesValidValue(int value)
    {
        Assert.That(global::SharpProof.Dataflow.ArgumentNullGuard.RequirePositive(value, ParameterName), Is.EqualTo(value));
    }

    [TestCase(0, TestName = "PositiveIntRejectsZero")]
    [TestCase(-1, TestName = "PositiveIntRejectsMinusOne")]
    [TestCase(int.MinValue, TestName = "PositiveIntRejectsMinimum")]
    public void PositiveIntRejectsNonpositiveValue(int value)
    {
        AssertOutOfRange(() => { _ = global::SharpProof.Dataflow.ArgumentNullGuard.RequirePositive(value, ParameterName); });
    }

    [TestCase(1L, TestName = "PositiveLongAcceptsOne")]
    [TestCase(long.MaxValue, TestName = "PositiveLongPreservesMaximum")]
    public void PositiveLongPreservesValidValue(long value)
    {
        Assert.That(global::SharpProof.Dataflow.ArgumentNullGuard.RequirePositive(value, ParameterName), Is.EqualTo(value));
    }

    [TestCase(0L, TestName = "PositiveLongRejectsZero")]
    [TestCase(-1L, TestName = "PositiveLongRejectsMinusOne")]
    [TestCase(long.MinValue, TestName = "PositiveLongRejectsMinimum")]
    public void PositiveLongRejectsNonpositiveValue(long value)
    {
        AssertOutOfRange(() => { _ = global::SharpProof.Dataflow.ArgumentNullGuard.RequirePositive(value, ParameterName); });
    }

    [TestCase(1U, TestName = "PositiveUnsignedAcceptsOne")]
    [TestCase(uint.MaxValue, TestName = "PositiveUnsignedPreservesMaximum")]
    public void PositiveUnsignedPreservesValidValue(uint value)
    {
        Assert.That(global::SharpProof.Dataflow.ArgumentNullGuard.RequirePositive(value, ParameterName), Is.EqualTo(value));
    }

    [Test]
    public void PositiveUnsignedRejectsZero()
    {
        AssertOutOfRange(() => { _ = global::SharpProof.Dataflow.ArgumentNullGuard.RequirePositive(0U, ParameterName); });
    }

    [TestCase(0, TestName = "DefinedEnumPreservesFirstMember")]
    [TestCase(3, TestName = "DefinedEnumPreservesLastMember")]
    public void DefinedEnumPreservesActualMember(int raw)
    {
        var value = (global::SharpProof.Dataflow.NullnessValue)raw;
        Assert.That(global::SharpProof.Dataflow.ArgumentNullGuard.RequireDefined(value, ParameterName), Is.EqualTo(value));
    }

    [TestCase(-1, TestName = "DefinedEnumRejectsNegativeValue")]
    [TestCase(int.MaxValue, TestName = "DefinedEnumRejectsUndefinedMaximum")]
    public void DefinedEnumRejectsUndefinedActualValue(int raw)
    {
        var value = (global::SharpProof.Dataflow.NullnessValue)raw;
        AssertOutOfRange(() => { _ = global::SharpProof.Dataflow.ArgumentNullGuard.RequireDefined(value, ParameterName); });
    }

    [TestCase(false, TestName = "NotNullPreservesReferenceWithoutDetail")]
    [TestCase(true, TestName = "NotNullPreservesReferenceWithDetail")]
    public void NotNullPreservesReferenceIdentity(bool customMessage)
    {
        var value = new object();
        var actual = customMessage
            ? global::SharpProof.Dataflow.ArgumentNullGuard.NotNull(value, ParameterName, Detail)
            : global::SharpProof.Dataflow.ArgumentNullGuard.NotNull(value, ParameterName);
        Assert.That(actual, Is.SameAs(value));
    }

    [TestCase(false, TestName = "NotNullRejectsNullWithoutDetail")]
    [TestCase(true, TestName = "NotNullRejectsNullWithDetail")]
    public void NotNullRejectsNullWithExactContract(bool customMessage)
    {
        var error = Assert.Throws<ArgumentNullException>((Action)(() =>
        {
            if (customMessage)
            { _ = global::SharpProof.Dataflow.ArgumentNullGuard.NotNull<object>(null, ParameterName, Detail); }
            else
            { _ = global::SharpProof.Dataflow.ArgumentNullGuard.NotNull<object>(null, ParameterName); }
        }));
        Assert.That(error!.ParamName, Is.EqualTo(ParameterName));
        if (customMessage)
        { Assert.That(error.Message, Does.StartWith(Detail)); }
        else
        { Assert.That(error.Message, Does.Not.Contain(Detail)); }
    }

    private static void AssertOutOfRange(Action action)
    {
        System.ArgumentNullException.ThrowIfNull(action);
        var error = Assert.Throws<ArgumentOutOfRangeException>(action);
        Assert.That(error!.ParamName, Is.EqualTo(ParameterName));
    }
}
