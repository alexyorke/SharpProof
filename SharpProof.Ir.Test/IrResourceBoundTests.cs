using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrResourceBoundTests
{
    [Test]
    public void AcyclicOrderingDoesNotReturnAnIncompleteOrderWhenBudgetIsSpent()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Return(entry, factory.CreateOperation(), factory.Integer(1));
        var program = builder.Build();

        var exhausted = IrBlockOrder.TryCreateAcyclicOrder(program, _ => false, out var failure);
        Assert.That(failure, Is.EqualTo(IrAcyclicOrderFailure.ResourceLimit));
        Assert.That(exhausted.IsDefault, Is.True);

        var complete = IrBlockOrder.TryCreateAcyclicOrder(program, _ => true, out failure);
        Assert.That(failure, Is.EqualTo(IrAcyclicOrderFailure.None));
        Assert.That(complete, Is.EqualTo(new[] { entry }));
    }

    [TestCase(true, 3L)]
    [TestCase(false, 7L)]
    public void SnapshotSelectionReadsOnlyTheSelectedHeapVersion(bool chooseFirst, long expected)
    {
        var fixture = BuildSnapshotSelectionFixture(16, chooseFirst);
        var result = fixture.Interpreter.Evaluate(fixture.Read, fixture.Values, null,
            fixture.Heap, default, [fixture.First]);

        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(result.Value!.Integer, Is.EqualTo(expected));
    }

    [Test]
    public void SnapshotSelectionDoesNotUseCurrentHeapAfterSelectorBudgetExhaustion()
    {
        var fixture = BuildSnapshotSelectionFixture(2048, chooseFirst: true);
        var result = fixture.Interpreter.Evaluate(fixture.Read, fixture.Values, null,
            fixture.Heap, default, [fixture.First]);

        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
        Assert.That(result.Unsupported!.Reason, Is.EqualTo(IrUnsupportedReason.UnsupportedOperation));
        Assert.That(fixture.Heap.ConsumedApproximation, Is.False);
    }

    private static (IrInterpreter Interpreter, IrTerm Read, System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue> Values,
        IrHeap Heap, IrVarId First) BuildSnapshotSelectionFixture(int leafCount, bool chooseFirst)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var sequence = factory.CreateSequenceValue(type, [factory.CreateIntegerValue(3)]);
        var heap = new IrHeap();
        heap.StoreElement(sequence, 0, factory.CreateIntegerValue(7));
        var choice = factory.CreateVariable("choice", factory.BooleanType);
        var values = global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(choice, factory.CreateBooleanValue(chooseFirst));
        var leaves = System.Collections.Immutable.ImmutableList<IrTerm>.Empty;
        IrVarId first = default;
        for (var index = 0; index < leafCount; index++)
        {
            var variable = factory.CreateVariable("owner" + index, type);
            if (index == 0)
            { first = variable; }
            values = values.Add(variable, sequence);
            leaves = leaves.Add(factory.Variable(variable));
        }

        while (leaves.Count > 1)
        {
            var next = System.Collections.Immutable.ImmutableList<IrTerm>.Empty;
            for (var index = 0; index < leaves.Count; index += 2)
            {
                next = next.Add(factory.Conditional(factory.Variable(choice), leaves[index], leaves[index + 1]));
            }
            leaves = next;
        }

        return (new IrInterpreter(factory), factory.SequenceAccess(leaves[0], factory.Integer(0)),
            values, heap, first);
    }
}
