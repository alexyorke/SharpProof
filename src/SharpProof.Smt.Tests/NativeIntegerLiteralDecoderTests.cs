using Microsoft.Z3;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class NativeIntegerLiteralDecoderTests
{
    [TestCase("-1", 32, 4294967295UL)]
    [TestCase("-2147483648", 32, 2147483648UL)]
    [TestCase("2147483647", 32, 2147483647UL)]
    [TestCase("2147483648", 64, 2147483648UL)]
    [TestCase("-9223372036854775808", 64, 9223372036854775808UL)]
    [TestCase("9223372036854775807", 64, 9223372036854775807UL)]
    public void ActualIntegerNumeralsPreserveWidthAndMaskedBits(string text, int width, ulong bits)
    {
        System.ArgumentNullException.ThrowIfNull(text);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        using var context = new Context();
        using var literal = context.MkInt(text);
        var result = SmtNativeUtilities.DecodeIntegerLiteral(factory, literal, out var kind);
        Assert.That(kind, Is.EqualTo(literal.FuncDecl.DeclKind));
        AssertLiteral(factory, result, width, bits);
    }

    [TestCase(8, 255UL)]
    [TestCase(16, 65535UL)]
    [TestCase(32, 2147483648UL)]
    [TestCase(32, 4294967295UL)]
    [TestCase(64, 9223372036854775808UL)]
    [TestCase(64, 18446744073709551615UL)]
    public void ActualBitvectorNumeralsPreserveTheirUnsignedBitPatterns(int width, ulong bits)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        using var context = new Context();
        using var literal = context.MkBV(bits, (uint)width);
        var result = SmtNativeUtilities.DecodeIntegerLiteral(factory, literal, out var kind);
        Assert.That(kind, Is.EqualTo(literal.FuncDecl.DeclKind));
        AssertLiteral(factory, result, width, bits);
    }

    [TestCase(1)]
    [TestCase(128)]
    public void ActualUnsupportedBitvectorWidthsAreRejected(int width)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        using var context = new Context();
        using var literal = context.MkBV(1UL, (uint)width);
        Assert.That(SmtNativeUtilities.DecodeIntegerLiteral(factory, literal, out _), Is.Null);
    }

    [TestCase("9223372036854775808")]
    [TestCase("-9223372036854775809")]
    public void ActualOutOfRangeIntegerNumeralsAreRejected(string text)
    {
        System.ArgumentNullException.ThrowIfNull(text);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        using var context = new Context();
        using var literal = context.MkInt(text);
        Assert.That(SmtNativeUtilities.DecodeIntegerLiteral(factory, literal, out _), Is.Null);
    }

    [Test]
    public void ActualRealLiteralIsRejected()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        using var context = new Context();
        using var literal = context.MkReal("1/2");
        Assert.That(SmtNativeUtilities.DecodeIntegerLiteral(factory, literal, out _), Is.Null);
    }

    private static void AssertLiteral(IrFactory factory, IrIntegerTerm? result, int width, ulong bits)
    {
        System.ArgumentNullException.ThrowIfNull(result, nameof(result));
        System.ArgumentNullException.ThrowIfNull(factory);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Width, Is.EqualTo(width));
        Assert.That(result.Signed, Is.True);
        Assert.That(result.Bits, Is.EqualTo(bits));
        Assert.That(result.Type, Is.EqualTo(factory.GetOrCreateIntegerType(width, true)));
        Assert.That(result, Is.SameAs(factory.IntegerBits(result.Type, bits)));
    }
}
