using Microsoft.Z3;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class NativeNonnegativeInt32AdmissionTests
{
    [TestCase(0UL, 32, 0)]
    [TestCase(2147483647UL, 32, int.MaxValue)]
    public void ActualBoundedUnsignedNumeralsConvertWithoutWitnessAllocation(ulong bits, int width, int expected)
    {
        using var context = new Context();
        using var numeral = context.MkBV(bits, (uint)width);
        Assert.That(SmtNativeUtilities.ReadNonnegativeInt32(numeral), Is.EqualTo(expected));
    }

    [TestCase(2147483648UL, 32)]
    [TestCase(4294967295UL, 32)]
    [TestCase(18446744073709551615UL, 64)]
    public void ActualUnsignedNumeralsAboveClrLengthRangeAreRejected(ulong bits, int width)
    {
        using var context = new Context();
        using var numeral = context.MkBV(bits, (uint)width);
        Assert.That(SmtNativeUtilities.ReadNonnegativeInt32(numeral), Is.Null);
    }

    [TestCase("0")]
    [TestCase("-1")]
    public void ActualArithmeticIntegerNumeralsAreNotUnsignedBitvectorLengths(string text)
    {
        using var context = new Context();
        using var numeral = context.MkInt(text);
        Assert.That(SmtNativeUtilities.ReadNonnegativeInt32(numeral), Is.Null);
    }

    [Test]
    public void ActualRealLiteralIsNotAnUnsignedBitvectorLength()
    {
        using var context = new Context();
        using var numeral = context.MkReal("1/2");
        Assert.That(SmtNativeUtilities.ReadNonnegativeInt32(numeral), Is.Null);
    }
}
