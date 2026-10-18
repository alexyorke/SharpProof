using NUnit.Framework;
using SharpProof.Advisory.Core;
using SharpProof.Dataflow;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreTransferCoverageTests
{
    private static IrTypeInfo Int32()
    {
        var factory = new IrFactory();
        return factory.GetTypeInfo(factory.GetOrCreateIntegerType(32, signed: true));
    }

    [Test]
    public void EmptyOperandRemainsEmptyThroughNegationAndCast()
    {
        var type = Int32();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CoreIrScalarIntervalTransfer.Negate(type, IntervalValue.Bottom).IsBottom, Is.True);
            Assert.That(CoreIrScalarIntervalTransfer.Cast(type, type, IntervalValue.Bottom).IsBottom, Is.True);
            Assert.That(CoreIrScalarIntervalTransfer.Negate(type, IntervalValue.Constant(long.MaxValue)).IsBottom, Is.True);
            Assert.That(CoreIrScalarIntervalTransfer.Cast(type, type, IntervalValue.Constant(long.MinValue)).IsBottom, Is.True);
        }
    }

    [Test]
    public void ScalarArithmeticRejectsOperatorsOutsideItsContract()
    {
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => CoreIrScalarIntervalTransfer.Binary(
            IrBinaryOperator.Equal, Int32(), IntervalValue.Constant(1), IntervalValue.Constant(1))));
    }
}
