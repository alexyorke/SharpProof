using NUnit.Framework;

namespace SharpProof.Ir.Test;

public sealed class IrKeyedFieldTests
{
    [TestCase(0L)]
    [TestCase(-1L)]
    [TestCase(long.MinValue)]
    [TestCase(long.MaxValue)]
    public void KeyedStoresUseReceiverFieldAndFullWidthKeyIdentity(long key)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var keyType = factory.GetOrCreateIntegerType(64, true);
        var owner = factory.CreateVariable("owner", factory.ObjectType);
        var alias = factory.CreateVariable("alias", factory.ObjectType);
        var other = factory.CreateVariable("other", factory.ObjectType);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:map", factory.IntegerType, false, keyType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Allocate(entry, factory.CreateOperation(), factory.ObjectType, owner);
        builder.Allocate(entry, factory.CreateOperation(), factory.ObjectType, other);
        builder.Assign(entry, factory.CreateOperation(), alias, factory.Variable(owner));
        builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field, factory.Variable(alias), field,
            factory.Integer(23), factory.Integer(keyType, key));
        builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field, factory.Variable(other), field,
            factory.Integer(99), factory.Integer(keyType, key));
        builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field, factory.Variable(owner), field,
            factory.Integer(42), factory.Integer(keyType, key == 0 ? 1 : 0));
        builder.Return(entry, factory.CreateOperation(), factory.PureOpaque(field, factory.Variable(owner), factory.Integer(keyType, key)));
        var result = new IrProgramInterpreter(factory).Execute(builder.Build());
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ReturnValue!.Integer, Is.EqualTo(23));
        Assert.That(result.ConsumedApproximation, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnknownWritesInvalidateKeyedCellsAndFreshDefaults(bool restore)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var owner = factory.CreateVariable("owner", factory.ObjectType);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:presence", factory.BooleanType, false, factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Allocate(entry, factory.CreateOperation(), factory.ObjectType, owner);
        builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field, factory.Variable(owner), field,
            factory.Boolean(true), factory.Integer(7));
        builder.Write(entry, factory.CreateOperation(), IrWriteRegion.Unknown);
        if (restore)
        { builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field, factory.Variable(owner), field, factory.Boolean(false), factory.Integer(7)); }
        builder.Return(entry, factory.CreateOperation(), factory.PureOpaque(field, factory.Variable(owner), factory.Integer(7)));
        var result = new IrProgramInterpreter(factory).Execute(builder.Build());
        Assert.That(result.Status, Is.EqualTo(restore ? IrProgramExecutionStatus.Returned : IrProgramExecutionStatus.Unsupported));
        Assert.That(result.ConsumedApproximation, Is.EqualTo(!restore));
        if (restore)
        { Assert.That(result.ReturnValue!.Boolean, Is.False); }
    }
    [Test]
    public void FieldStoresRejectMissingExtraAndWronglyTypedKeys()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var owner = factory.Variable(factory.CreateVariable("owner", factory.ObjectType));
        var keyed = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:keyed", factory.IntegerType, false, factory.IntegerType);
        var plain = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:plain", factory.IntegerType, false);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation();
        Assert.Throws<ArgumentException>((Action)(() => builder.FieldStore(block, site, IrWriteRegion.Field, owner, keyed, factory.Integer(1))));
        Assert.Throws<ArgumentException>((Action)(() => builder.FieldStore(block, site, IrWriteRegion.Field, owner, plain, factory.Integer(1), factory.Integer(0))));
        Assert.Throws<ArgumentException>((Action)(() => builder.FieldStore(block, site, IrWriteRegion.Field, owner, keyed, factory.Integer(1), factory.Boolean(false))));
    }

    [Test]
    public void NonIntegerOpaqueArgumentsDoNotBecomeHeapReads()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var owner = factory.CreateVariable("owner", factory.ObjectType);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:not-keyed-storage", factory.IntegerType, false, factory.BooleanType);
        var read = factory.PureOpaque(member, factory.Variable(owner), factory.Boolean(true));
        var heap = new IrHeap();
        var value = factory.CreateReferenceValue(factory.ObjectType, new object());
        heap.RegisterFreshObject(value);
        var result = new IrInterpreter(factory).Evaluate(read, new Dictionary<ScopedIrId<IrVariableTag>, IrValue> { [owner] = value }, null, heap, default);
        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
    }
}
