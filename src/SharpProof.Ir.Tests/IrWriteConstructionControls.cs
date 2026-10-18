using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrWriteConstructionControls
{
    [Test]
    public void EffectWritePreservesItsEmptyStoreProjectionAndRegionDiagnostic()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var invalid = Assert.Throws<ArgumentException>((Action)(() =>
            builder.Write(entry, factory.CreateOperation(), (IrWriteRegion)int.MaxValue)));
        Assert.That(invalid!.ParamName, Is.EqualTo("region"));
        Assert.That(invalid.Message, Does.StartWith("A write region is undefined."));
        var write = builder.Write(entry, factory.CreateOperation(), IrWriteRegion.Field);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(write.Region, Is.EqualTo(IrWriteRegion.Field));
            Assert.That(write.Target, Is.Null);
            Assert.That(write.Index, Is.Null);
            Assert.That(write.Field, Is.Null);
            Assert.That(write.Value, Is.Null);
            Assert.That(write.IsStore, Is.False);
            Assert.That(write.IsElementStore, Is.False);
            Assert.That(write.IsFieldStore, Is.False);
        }
    }

    [Test]
    public void ElementStorePreservesExactOperandReferencesAndNullDiagnostic()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var array = factory.Variable(factory.CreateVariable("array", factory.GetOrCreateSequenceType(factory.IntegerType)));
        var index = factory.Integer(0);
        var value = factory.Integer(42);
        var invalid = Assert.Throws<ArgumentNullException>((Action)(() =>
            builder.ElementStore(entry, factory.CreateOperation(), null!, index, value)));
        Assert.That(invalid!.ParamName, Is.EqualTo("sequence"));
        var write = builder.ElementStore(entry, factory.CreateOperation(), array, index, value);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(write.Region, Is.EqualTo(IrWriteRegion.Element));
            Assert.That(write.Target, Is.SameAs(array));
            Assert.That(write.Index, Is.SameAs(index));
            Assert.That(write.Field, Is.Null);
            Assert.That(write.Value, Is.SameAs(value));
            Assert.That(write.IsStore, Is.True);
            Assert.That(write.IsElementStore, Is.True);
            Assert.That(write.IsFieldStore, Is.False);
        }
    }

    [Test]
    public void FieldStorePreservesExactOperandReferencesAndNullDiagnostic()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var receiver = factory.Variable(factory.CreateVariable("owner", factory.ObjectType));
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "field:Value", factory.IntegerType, false);
        var value = factory.Integer(42);
        var invalid = Assert.Throws<ArgumentNullException>((Action)(() =>
            builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field, null!, field, value)));
        Assert.That(invalid!.ParamName, Is.EqualTo("receiver"));
        var write = builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field, receiver, field, value);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(write.Region, Is.EqualTo(IrWriteRegion.Field));
            Assert.That(write.Target, Is.SameAs(receiver));
            Assert.That(write.Index, Is.Null);
            Assert.That(write.Field, Is.EqualTo(field));
            Assert.That(write.Value, Is.SameAs(value));
            Assert.That(write.IsStore, Is.True);
            Assert.That(write.IsElementStore, Is.False);
            Assert.That(write.IsFieldStore, Is.True);
        }
    }
}
