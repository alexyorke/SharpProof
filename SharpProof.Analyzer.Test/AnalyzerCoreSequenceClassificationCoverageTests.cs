using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreSequenceClassificationCoverageTests
{
    [Test]
    public void ASequenceAccessFromAnotherFactoryRetainsItsOwnershipRejection()
    {
        var owner = new IrFactory(IrExecutionSemantics.Total);
        var sequence = owner.CreateVariable("items", owner.GetOrCreateSequenceType(owner.IntegerType));
        var access = owner.SequenceAccess(owner.Variable(sequence), owner.Integer(0));
        var foreign = new IrFactory(IrExecutionSemantics.Total);
        var error = Assert.Throws<ArgumentException>((Action)(() =>
            TotalBodyLowering.ReadsElements(foreign, [access])));
        Assert.That(error!.ParamName, Is.EqualTo("id"));
    }

    [Test]
    public void AFactoryAdmittedSequenceAccessReadsElementState()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var items = factory.CreateVariable("items", sequenceType);
        var index = factory.CreateVariable("index", factory.IntegerType);
        var access = factory.SequenceAccess(factory.Variable(items), factory.Variable(index));
        Assert.That(factory.GetTypeInfo(sequenceType).Kind, Is.EqualTo(IrTypeKind.Sequence));
        Assert.That(TotalBodyLowering.ReadsElements(factory, [access]), Is.True);
    }

    [Test]
    public void StringsUseLengthTermsAndRejectSequenceAccess()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var text = factory.CreateVariable("text", factory.StringType);
        var value = factory.Variable(text);
        var error = Assert.Throws<ArgumentException>((Action)(() =>
            factory.SequenceAccess(value, factory.Integer(0))));
        Assert.That(error!.ParamName, Is.EqualTo("sequence"));
        Assert.That(TotalBodyLowering.ReadsElements(factory, [factory.Length(value)]), Is.False);
    }
}
