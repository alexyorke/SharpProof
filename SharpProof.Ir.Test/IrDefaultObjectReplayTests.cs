using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrDefaultObjectReplayTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void FreshDefaultsRecoverAfterPriorInvalidationAndExpireAfterAnotherWriter(bool boolean, bool invalidateAgain)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = boolean ? factory.BooleanType : factory.IntegerType;
        var owner = factory.CreateVariable("owner", factory.ObjectType);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Value", type, false);
        var value = factory.CreateReferenceValue(factory.ObjectType, new object());
        var heap = new IrHeap();
        heap.Invalidate();
        heap.RegisterFreshObject(value);
        if (invalidateAgain)
        { heap.Invalidate(); }
        var result = new IrInterpreter(factory).Evaluate(factory.PureOpaque(field, factory.Variable(owner)),
            new Dictionary<IrVarId, IrValue> { [owner] = value }, null, heap, default);
        Assert.That(result.Status, Is.EqualTo(invalidateAgain ? IrEvaluationStatus.Unsupported : IrEvaluationStatus.Value));
        Assert.That(heap.ConsumedApproximation, Is.EqualTo(invalidateAgain));
        if (!invalidateAgain)
        {
            if (boolean)
            { Assert.That(result.Value!.Boolean, Is.False); }
            else
            { Assert.That(result.Value!.Integer, Is.Zero); }
        }
    }

    [Test]
    public void FreshReferenceFieldsRemainOutsideTheScalarDefaultModel()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var owner = factory.CreateVariable("owner", factory.ObjectType);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Value", factory.ObjectType, false);
        var value = factory.CreateReferenceValue(factory.ObjectType, new object());
        var heap = new IrHeap();
        heap.RegisterFreshObject(value);
        var result = new IrInterpreter(factory).Evaluate(factory.PureOpaque(field, factory.Variable(owner)),
            new Dictionary<IrVarId, IrValue> { [owner] = value }, null, heap, default);
        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
    }
}
