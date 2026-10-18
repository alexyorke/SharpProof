using System.Collections.Immutable;
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
        var variables = global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(current, value).SetItem(old, value).SetItem(flag, factory.CreateBooleanValue(chooseOld));
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
            global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(array, value).SetItem(index, factory.CreateIntegerValue(0)), null, heap, default, [index]);
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
            global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(first, value).SetItem(second, value).SetItem(flag, factory.CreateBooleanValue(choose)),
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
        var memo = ImmutableDictionary<IrId, IrTerm>.Empty;
        var work = 3200;
        var selection = IrSnapshotReads.TrySelector(factory, owner, variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : (variable == old), static guard => guard is null ? throw new System.ArgumentNullException(nameof(guard)) : (guard),
            () => --work >= 0, memo, default);
        memo = selection.Memo;
        Assert.That(selection.Success, Is.False);
        Assert.That(selection.Selector, Is.Null);
        Assert.That(memo.Count, Is.GreaterThan(0));
        Assert.That(memo.ContainsKey(owner.Id), Is.False);
        work = 7000;
        selection = IrSnapshotReads.TrySelector(factory, owner, variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : (variable == old), static guard => guard is null ? throw new System.ArgumentNullException(nameof(guard)) : (guard),
            () => --work >= 0, memo, default);
        memo = selection.Memo;
        Assert.That(selection.Success, Is.True);
        Assert.That(selection.Selector, Is.TypeOf<IrBooleanTerm>().With.Property(nameof(IrBooleanTerm.Value)).True);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var spent = false;
        Assert.Throws<OperationCanceledException>((Action)(() => IrSnapshotReads.TrySelector(factory, owner, variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : (variable == old),
            static guard => guard is null ? throw new System.ArgumentNullException(nameof(guard)) : (guard), () => { spent = true; return true; }, memo, canceled.Token)));
        Assert.That(spent, Is.False);
    }
}
