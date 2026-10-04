using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrSnapshotReadTests
{
    [TestCase(false, 7)]
    [TestCase(true, 3)]
    public void ConditionalOwnerReadsItsSelectedSnapshot(bool chooseOld, int expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var current = factory.CreateVariable("current", factory.ObjectType);
        var old = factory.CreateVariable("old", factory.ObjectType);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Value", factory.IntegerType, false);
        var identity = new IrObjectState().WithField(field, factory.CreateIntegerValue(3));
        var value = factory.CreateReferenceValue(factory.ObjectType, identity);
        var variables = new Dictionary<IrVarId, IrValue> { [current] = value, [old] = value, [flag] = factory.CreateBooleanValue(chooseOld) };
        var heap = new IrHeap();
        heap.Fields.Add((identity, field), factory.CreateIntegerValue(7));
        var owner = factory.Conditional(factory.Variable(flag), factory.Variable(old), factory.Variable(current));
        var result = new IrInterpreter(factory).Evaluate(factory.PureOpaque(field, owner), variables, null, heap, default, [old]);
        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(result.Value!.Integer, Is.EqualTo(expected));
    }

    [Test]
    public void OldIndexDoesNotChangeCurrentArrayContents()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var array = factory.CreateVariable("array", type);
        var index = factory.CreateVariable("old-index", factory.IntegerType);
        var value = factory.CreateSequenceValue(type, [factory.CreateIntegerValue(3)]);
        var heap = new IrHeap();
        heap.StoreElement(value, 0, factory.CreateIntegerValue(7));
        var result = new IrInterpreter(factory).Evaluate(factory.SequenceAccess(factory.Variable(array), factory.Variable(index)),
            new Dictionary<IrVarId, IrValue> { [array] = value, [index] = factory.CreateIntegerValue(0) }, null, heap, default, [index]);
        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(result.Value!.Integer, Is.EqualTo(7));
    }

    [TestCase(false, false, 7)]
    [TestCase(true, false, 7)]
    [TestCase(false, true, 7)]
    [TestCase(true, true, 3)]
    public void SnapshotGuardDoesNotOverrideSelectedArrayOwner(bool choose, bool oldBranch, int expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var first = factory.CreateVariable("first", type);
        var second = factory.CreateVariable("second", type);
        var flag = factory.CreateVariable("old-flag", factory.BooleanType);
        var value = factory.CreateSequenceValue(type, [factory.CreateIntegerValue(3)]);
        var heap = new IrHeap();
        heap.StoreElement(value, 0, factory.CreateIntegerValue(7));
        var owner = factory.Conditional(factory.Variable(flag), factory.Variable(first), factory.Variable(second));
        var result = new IrInterpreter(factory).Evaluate(factory.SequenceAccess(owner, factory.Integer(0)),
            new Dictionary<IrVarId, IrValue> { [first] = value, [second] = value, [flag] = factory.CreateBooleanValue(choose) },
            null, heap, default, oldBranch ? [first, flag] : [flag]);
        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(result.Value!.Integer, Is.EqualTo(expected));
    }

    [Test]
    public void DeepSnapshotSelectionIsBoundedAndCanResumeCompletedMemoEntries()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var old = factory.CreateVariable("old", factory.ObjectType);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Next", factory.ObjectType, false);
        IrTerm owner = factory.Variable(old);
        for (var index = 0; index < 3000; index++)
        { owner = factory.PureOpaque(field, owner); }
        var memo = new Dictionary<IrId, IrTerm>();
        var work = 3200;
        Assert.That(IrSnapshotReads.TrySelector(factory, owner, variable => variable == old, static guard => guard,
            () => --work >= 0, memo, out var incomplete, default), Is.False);
        Assert.That(incomplete, Is.Null);
        Assert.That(memo.Count, Is.GreaterThan(0));
        Assert.That(memo.ContainsKey(owner.Id), Is.False);
        work = 7000;
        Assert.That(IrSnapshotReads.TrySelector(factory, owner, variable => variable == old, static guard => guard,
            () => --work >= 0, memo, out var complete, default), Is.True);
        Assert.That(complete, Is.TypeOf<IrBooleanTerm>().With.Property(nameof(IrBooleanTerm.Value)).True);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var spent = false;
        Assert.Throws<OperationCanceledException>((Action)(() => IrSnapshotReads.TrySelector(factory, owner, variable => variable == old,
            static guard => guard, () => { spent = true; return true; }, memo, out _, canceled.Token)));
        Assert.That(spent, Is.False);
    }
}
