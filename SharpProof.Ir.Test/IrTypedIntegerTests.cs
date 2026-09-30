using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrTypedIntegerTests
{
    [Test]
    public void TypedInterningIncludesWidthAndSignAndKeepsLegacySeparate()
    {
        var factory = new IrFactory();
        var types = new HashSet<IrTypeId> { factory.IntegerType };
        foreach (var width in new[] { 8, 16, 32, 64 })
        {
            foreach (var isSigned in new[] { false, true })
            {
                var type = factory.GetOrCreateIntegerType(width, isSigned);
                Assert.That(factory.GetOrCreateIntegerType(width, isSigned), Is.EqualTo(type));
                Assert.That((factory.GetTypeInfo(type).Width, factory.GetTypeInfo(type).Signed),
                    Is.EqualTo((width, isSigned)));
                Assert.That(types.Add(type), Is.True);
            }
        }
        Assert.That(factory.GetTypeInfo(factory.IntegerType).Width, Is.Zero);
        var legacyOverflow = factory.Binary(IrBinaryOperator.Add, factory.Integer(long.MaxValue), factory.Integer(1));
        Assert.That(new IrInterpreter(factory).Evaluate(legacyOverflow).Exception!.Kind,
            Is.EqualTo(IrExceptionKind.Overflow));
    }

    [Test]
    public void FullUnsignedBitsAreStoredPrintedAndInternedWithoutSignedTruncation()
    {
        var factory = new IrFactory();
        var type = factory.GetOrCreateIntegerType(64, false);
        var term = factory.Integer(type, ulong.MaxValue);
        var value = factory.CreateIntegerValue(type, ulong.MaxValue);
        Assert.That(term.Bits, Is.EqualTo(ulong.MaxValue));
        Assert.That(value.IntegerBits, Is.EqualTo(ulong.MaxValue));
        Assert.That(factory.IntegerBits(type, ulong.MaxValue), Is.SameAs(term));
        Assert.That(new IrPrinter(factory).Print(term), Is.EqualTo("18446744073709551615"));
        Assert.Throws<OverflowException>((Action)(() => { _ = value.Integer; }));
        Assert.Throws<OverflowException>((Action)(() => { _ = term.Value; }));
    }

    [Test]
    public void InvalidWidthsRangesAndMixedArithmeticAreRejected()
    {
        var factory = new IrFactory();
        var signed = factory.GetOrCreateIntegerType(8, true);
        var unsigned = factory.GetOrCreateIntegerType(8, false);
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => factory.GetOrCreateIntegerType(7, true)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => factory.Integer(signed, 128L)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => factory.CreateIntegerValue(unsigned, -1L)));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => factory.IntegerBits(unsigned, 256)));
        Assert.Throws<ArgumentException>((Action)(() => factory.Binary(IrBinaryOperator.Add,
            factory.IntegerBits(signed, 1), factory.IntegerBits(unsigned, 1))));
    }

    [Test]
    public void SequenceElementsKeepTheirIntegerWidthAndSignedness()
    {
        var factory = new IrFactory();
        var type = factory.GetOrCreateIntegerType(16, false);
        var sequenceType = factory.GetOrCreateSequenceType(type);
        var variable = factory.CreateVariable("sequence", sequenceType);
        var result = new IrInterpreter(factory).Evaluate(
            factory.SequenceAccess(factory.Variable(variable), factory.Integer(0)),
            new Dictionary<IrVarId, IrValue>
            {
                [variable] = factory.CreateSequenceValue(sequenceType,
                    [factory.CreateIntegerValue(type, ushort.MaxValue)])
            });
        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(result.Value!.Type, Is.EqualTo(type));
        Assert.That(result.Value.IntegerBits, Is.EqualTo(ushort.MaxValue));
    }
}
