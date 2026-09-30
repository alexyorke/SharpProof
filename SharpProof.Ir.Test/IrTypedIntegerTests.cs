using NUnit.Framework;
using System.Globalization;
using System.Numerics;

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

    public static IEnumerable<object[]> BoxedIntegers()
    {
        yield return [8, true, (object)sbyte.MinValue, 128UL];
        yield return [8, false, (object)byte.MaxValue, 255UL];
        yield return [16, true, (object)short.MinValue, 32768UL];
        yield return [16, false, (object)ushort.MaxValue, 65535UL];
        yield return [32, true, (object)int.MinValue, 2147483648UL];
        yield return [32, false, (object)uint.MaxValue, 4294967295UL];
        yield return [64, true, (object)long.MinValue, 9223372036854775808UL];
        yield return [64, false, (object)ulong.MaxValue, ulong.MaxValue];
    }

    [TestCaseSource(nameof(BoxedIntegers))]
    public void UnboxingRetainsExactClrWidthSignAndHighBits(int width, bool isSigned, object boxed, ulong expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(width, isSigned);
        var variable = factory.CreateVariable("boxed", factory.ObjectType);
        var values = new Dictionary<IrVarId, IrValue>
        {
            [variable] = factory.CreateReferenceValue(factory.ObjectType, boxed)
        };
        var result = new IrInterpreter(factory).Evaluate(factory.Cast(type, factory.Variable(variable)), values);
        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(result.Value!.Type, Is.EqualTo(type));
        Assert.That(result.Value.IntegerBits, Is.EqualTo(expected));
        Assert.That(result.Value.IntegerNumericValue, Is.EqualTo(new BigInteger(Convert.ToDecimal(boxed, CultureInfo.InvariantCulture))));
    }

    [TestCase(IrBinaryOperator.LessThanOrEqual, true, true)]
    [TestCase(IrBinaryOperator.LessThanOrEqual, false, false)]
    [TestCase(IrBinaryOperator.GreaterThan, true, false)]
    [TestCase(IrBinaryOperator.GreaterThan, false, true)]
    public void ComparisonInterpretsTheSignBitBeforeFolding(IrBinaryOperator operation, bool isSigned, bool expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(8, isSigned);
        var left = factory.CreateVariable("left", type);
        var right = factory.CreateVariable("right", type);
        var values = new Dictionary<IrVarId, IrValue>
        {
            [left] = factory.CreateIntegerValueFromBits(type, 128),
            [right] = factory.CreateIntegerValueFromBits(type, 127)
        };
        var runtime = new IrInterpreter(factory).Evaluate(factory.Binary(operation,
            factory.Variable(left), factory.Variable(right)), values);
        var folded = (IrBooleanTerm)factory.Binary(operation, factory.IntegerBits(type, 128), factory.IntegerBits(type, 127));
        Assert.That(runtime.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(runtime.Value!.Boolean, Is.EqualTo(expected));
        Assert.That(folded.Value, Is.EqualTo(expected));
    }

    [TestCase(1UL, 255UL)]
    [TestCase(128UL, 128UL)]
    public void TypedNegationWrapsAndFoldsTheSameBits(ulong bits, ulong expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(8, true);
        var variable = factory.CreateVariable("value", type);
        var result = new IrInterpreter(factory).Evaluate(factory.Unary(IrUnaryOperator.Negate, factory.Variable(variable)),
            new Dictionary<IrVarId, IrValue> { [variable] = factory.CreateIntegerValueFromBits(type, bits) });
        var folded = (IrIntegerTerm)factory.Unary(IrUnaryOperator.Negate, factory.IntegerBits(type, bits));
        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(result.Value!.Type, Is.EqualTo(type));
        Assert.That(result.Value.IntegerBits, Is.EqualTo(expected));
        Assert.That(folded.Bits, Is.EqualTo(expected));
    }

    [Test]
    public void IntegerStorageRejectsBooleanTypesAndNonintegerOperations()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var exception = Assert.Throws<ArgumentException>((Action)(() => factory.CreateIntegerValueFromBits(factory.BooleanType, 0)));
        Assert.That(exception!.ParamName, Is.EqualTo("type"));
        var integer = IrInteger.FromBits(factory.GetTypeInfo(factory.IntegerType), 1);
        Assert.That(IrBitVectorOperations.Evaluate(IrBinaryOperator.StringConcat, integer, integer).Kind,
            Is.EqualTo(IrScalarResultKind.Unsupported));
    }
}
