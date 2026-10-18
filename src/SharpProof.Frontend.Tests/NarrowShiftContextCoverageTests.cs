using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class NarrowShiftContextCoverageTests
{
    [TestCase(false, (byte)255, 254)]
    [TestCase(true, (byte)255, 254)]
    [TestCase(true, (byte)2, 4)]
    public void ExplicitUncheckedNarrowShiftPreservesStorageConversion(bool expression, byte input, int expected)
    {
        var members = expression
            ? "byte Target(byte value) => unchecked(value <<= 1);"
            : "byte Target(byte value) { unchecked { value <<= 1; } return value; }";
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Invoke([input]), Is.EqualTo((byte)expected));
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var execution = subject.Execute(lowering, [input]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }
}
