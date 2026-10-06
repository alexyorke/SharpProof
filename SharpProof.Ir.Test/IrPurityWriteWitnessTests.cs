using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrPurityWriteWitnessTests
{
    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public void OnlyCallerOwnedStoresSupplyPurityWriteWitnesses(bool array, bool allocated, bool alias)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = array ? factory.GetOrCreateSequenceType(factory.IntegerType) : factory.ObjectType;
        var owner = factory.CreateVariable("owner", type);
        var other = factory.CreateVariable("alias", type);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Value", factory.IntegerType, false);
        var input = array ? factory.CreateSequenceValue(type, [factory.CreateIntegerValue(0)])
            : factory.CreateReferenceValue(type, new IrObjectState().WithField(field, factory.CreateIntegerValue(0)));
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        if (allocated)
        { builder.Allocate(block, factory.CreateOperation(), type, owner, array ? factory.Integer(1) : null); }
        if (alias)
        { builder.Assign(block, factory.CreateOperation(), other, factory.Variable(owner)); }
        var target = factory.Variable(alias ? other : owner);
        var site = factory.CreateOperation("actual-store");
        if (array)
        { builder.ElementStore(block, site, target, factory.Integer(0), factory.Integer(7)); }
        else
        { builder.FieldStore(block, site, IrWriteRegion.Parameter, target, field, factory.Integer(7)); }
        builder.Return(block, factory.CreateOperation(), array ? factory.SequenceAccess(target, factory.Integer(0)) : factory.PureOpaque(field, target));
        var writes = new List<OperationId>();
        var witnesses = new List<OperationId>();
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(),
            new Dictionary<IrVarId, IrValue> { [owner] = input }, 64, new IrProgramReplayOptions(_ => null)
            {
                WritePrefixObserver = (write, approximate) => { Assert.That(approximate, Is.False); writes.Add(write.Operation); },
                NonFreshWritePrefixObserver = (write, approximate) => { Assert.That(approximate, Is.False); witnesses.Add(write.Operation); }
            });
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ReturnValue!.Integer, Is.EqualTo(7));
        Assert.That(writes, Is.EqualTo(new[] { site }), "Ordinary effect replay observes the successful store regardless of ownership.");
        Assert.That(witnesses, allocated ? Is.Empty : Is.EqualTo(new[] { site }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnknownWriterForgetsAllocationOwnershipBeforeLaterStore(bool array)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = array ? factory.GetOrCreateSequenceType(factory.IntegerType) : factory.ObjectType;
        var owner = factory.CreateVariable("owner", type);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Value", factory.IntegerType, false);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        builder.Allocate(block, factory.CreateOperation(), type, owner, array ? factory.Integer(1) : null);
        var unknown = factory.CreateOperation("unknown-writer");
        builder.Write(block, unknown, IrWriteRegion.Unknown);
        var site = factory.CreateOperation("later-store");
        if (array)
        { builder.ElementStore(block, site, factory.Variable(owner), factory.Integer(0), factory.Integer(7)); }
        else
        { builder.FieldStore(block, site, IrWriteRegion.Field, factory.Variable(owner), field, factory.Integer(7)); }
        builder.Return(block, factory.CreateOperation());
        var witnesses = new List<OperationId>();
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), null, 64, new IrProgramReplayOptions(_ => null)
        { NonFreshWritePrefixObserver = (write, _) => witnesses.Add(write.Operation) });
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(witnesses, Is.EqualTo(new[] { unknown, site }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FailedStoreSuppliesNoEffectOrPurityWriteWitness(bool array)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = array ? factory.GetOrCreateSequenceType(factory.IntegerType) : factory.ObjectType;
        var owner = factory.CreateVariable("owner", type);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Value", factory.IntegerType, false);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        if (array)
        { builder.ElementStore(block, factory.CreateOperation(), factory.Variable(owner), factory.Integer(1), factory.Integer(7)); }
        else
        { builder.FieldStore(block, factory.CreateOperation(), IrWriteRegion.Field, factory.Variable(owner), field, factory.Integer(7)); }
        builder.Return(block, factory.CreateOperation());
        var observed = 0;
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(),
            new Dictionary<IrVarId, IrValue> { [owner] = array ? factory.CreateSequenceValue(type, [factory.CreateIntegerValue(0)]) : factory.CreateNullValue(type) },
            64, new IrProgramReplayOptions(_ => null)
            {
                WritePrefixObserver = (_, _) => observed++,
                NonFreshWritePrefixObserver = (_, _) => observed++
            });
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
        Assert.That(observed, Is.Zero);
    }
}
