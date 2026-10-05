using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrHeapReplayTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void ExplicitDefaultCellsAreKnownAfterInvalidation(bool array, bool reference)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var elementType = reference ? factory.ObjectType : factory.IntegerType;
        var type = array ? factory.GetOrCreateSequenceType(elementType) : factory.ObjectType;
        var owner = factory.CreateVariable("owner", type);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Value", elementType, false);
        var initial = reference ? factory.CreateReferenceValue(elementType, new object()) : factory.CreateIntegerValue(3);
        var value = array ? factory.CreateSequenceValue(type, [initial])
            : factory.CreateReferenceValue(type, new IrObjectState().WithField(field, initial));
        var replacement = reference ? factory.CreateNullValue(elementType) : factory.CreateIntegerValue(0);
        var heap = new IrHeap();
        heap.Invalidate();
        if (array)
        { heap.StoreElement(value, 0, replacement); }
        else
        { heap.Fields[(value.Reference, field)] = replacement; }
        var term = array ? factory.SequenceAccess(factory.Variable(owner), factory.Integer(0)) : factory.PureOpaque(field, factory.Variable(owner));
        var result = new IrInterpreter(factory).Evaluate(term, new Dictionary<IrVarId, IrValue> { [owner] = value }, null, heap, default);
        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(result.Value, Is.SameAs(replacement));
        Assert.That(heap.ConsumedApproximation, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ConditionalOwnerDemandsOnlyTheSelectedContents(bool chooseKnown)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var known = factory.CreateVariable("known", type);
        var unknown = factory.CreateVariable("unknown", type);
        var choose = factory.CreateVariable("choose", factory.BooleanType);
        var first = factory.CreateSequenceValue(type, [factory.CreateIntegerValue(3)]);
        var second = factory.CreateSequenceValue(type, [factory.CreateIntegerValue(4)]);
        var heap = new IrHeap();
        heap.Invalidate();
        heap.StoreElement(first, 0, factory.CreateIntegerValue(7));
        var owner = factory.Conditional(factory.Variable(choose), factory.Variable(known), factory.Variable(unknown));
        var result = new IrInterpreter(factory).Evaluate(factory.SequenceAccess(owner, factory.Integer(0)),
            new Dictionary<IrVarId, IrValue> { [known] = first, [unknown] = second, [choose] = factory.CreateBooleanValue(chooseKnown) },
            null, heap, default);
        Assert.That(result.Status, Is.EqualTo(chooseKnown ? IrEvaluationStatus.Value : IrEvaluationStatus.Unsupported));
        Assert.That(heap.ConsumedApproximation, Is.EqualTo(!chooseKnown));
        if (chooseKnown)
        { Assert.That(result.Value!.Integer, Is.EqualTo(7)); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InvalidatedContentsRecoverOnlyExplicitlyStoredCells(bool array)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = array ? factory.GetOrCreateSequenceType(factory.IntegerType) : factory.ObjectType;
        var owner = factory.CreateVariable("owner", type);
        var old = factory.CreateVariable("old", type);
        var first = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:First", factory.IntegerType, false);
        var second = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Second", factory.IntegerType, false);
        var value = array ? factory.CreateSequenceValue(type, [factory.CreateIntegerValue(3), factory.CreateIntegerValue(4)])
            : factory.CreateReferenceValue(type, new IrObjectState().WithField(first, factory.CreateIntegerValue(3)).WithField(second, factory.CreateIntegerValue(4)));
        var variables = new Dictionary<IrVarId, IrValue> { [owner] = value, [old] = value };
        var heap = new IrHeap();
        heap.Invalidate();
        if (array)
        { heap.StoreElement(value, 0, factory.CreateIntegerValue(7)); }
        else
        { heap.Fields[(value.Reference, first)] = factory.CreateIntegerValue(7); }
        IrTerm Read(IrVarId receiver, bool other)
        {
            return array ? factory.SequenceAccess(factory.Variable(receiver), factory.Integer(other ? 1 : 0))
                : factory.PureOpaque(other ? second : first, factory.Variable(receiver));
        }
        var interpreter = new IrInterpreter(factory);
        var restored = interpreter.Evaluate(Read(owner, false), variables, null, heap, default, [old]);
        Assert.That(restored.Value!.Integer, Is.EqualTo(7));
        Assert.That(heap.ConsumedApproximation, Is.False);
        var snapshot = interpreter.Evaluate(Read(old, false), variables, null, heap, default, [old]);
        Assert.That(snapshot.Value!.Integer, Is.EqualTo(3));
        Assert.That(heap.ConsumedApproximation, Is.False);
        var unknown = interpreter.Evaluate(Read(owner, true), variables, null, heap, default, [old]);
        Assert.That(unknown.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
        Assert.That(heap.ConsumedApproximation, Is.True);
        heap.Invalidate();
        Assert.That(heap.ConsumedApproximation, Is.True);
        Assert.That(interpreter.Evaluate(Read(owner, false), variables, null, heap, default).Status,
            Is.EqualTo(IrEvaluationStatus.Unsupported));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FreshArrayContentsStayKnownUntilAnotherWriter(bool invalidateAgain)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var array = factory.CreateVariable("array", type);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "Write", factory.IntegerType, true);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Call(entry, factory.CreateOperation("opaque-call:4:Write"), null, member, null);
        builder.Allocate(entry, factory.CreateOperation(), type, array, factory.Integer(1));
        if (invalidateAgain)
        { builder.Call(entry, factory.CreateOperation("opaque-call:4:Write"), null, member, null); }
        builder.Return(entry, factory.CreateOperation(), factory.SequenceAccess(factory.Variable(array), factory.Integer(0)));
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), null, 100, new IrProgramReplayOptions(_ => null));
        Assert.That(result.Status, Is.EqualTo(invalidateAgain ? IrProgramExecutionStatus.Unsupported : IrProgramExecutionStatus.Returned));
        Assert.That(result.ConsumedApproximation, Is.EqualTo(invalidateAgain));
        if (!invalidateAgain)
        { Assert.That(result.ReturnValue!.Integer, Is.Zero); }
    }

    [TestCase(IrWriteRegion.Field)]
    [TestCase(IrWriteRegion.Element)]
    [TestCase(IrWriteRegion.Parameter)]
    [TestCase(IrWriteRegion.Unknown)]
    [TestCase(IrWriteRegion.Static)]
    public void EffectOnlyWritesForgetModeledContentsExceptAmbientStaticState(IrWriteRegion region)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var array = factory.CreateVariable("array", type);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Write(entry, factory.CreateOperation(), region);
        builder.Return(entry, factory.CreateOperation(), factory.SequenceAccess(factory.Variable(array), factory.Integer(0)));
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(),
            new Dictionary<IrVarId, IrValue> { [array] = factory.CreateSequenceValue(type, [factory.CreateIntegerValue(3)]) });
        Assert.That(result.Status, Is.EqualTo(region == IrWriteRegion.Static ? IrProgramExecutionStatus.Returned : IrProgramExecutionStatus.Unsupported));
        Assert.That(result.ConsumedApproximation, Is.EqualTo(region != IrWriteRegion.Static));
    }
}
