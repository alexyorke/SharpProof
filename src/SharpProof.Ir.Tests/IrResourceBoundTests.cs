using System.Collections.Immutable;
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
        Assert.That(complete, Is.EqualTo(ImmutableArray.Create(entry)));
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
        var valueBuilder = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
        valueBuilder[choice] = factory.CreateBooleanValue(chooseFirst);
        var leafBuilder = ImmutableArray.CreateBuilder<IrTerm>();
        IrVarId first = default;
        for (var index = 0; index < leafCount; index++)
        {
            var variable = factory.CreateVariable("owner" + index, type);
            if (index == 0)
            { first = variable; }
            valueBuilder.Add(variable, sequence);
            leafBuilder.Add(factory.Variable(variable));
        }

        var leaves = leafBuilder.ToImmutable();
        while (leaves.Length > 1)
        {
            var next = ImmutableArray.CreateBuilder<IrTerm>();
            for (var index = 0; index < leaves.Length; index += 2)
            {
                next.Add(factory.Conditional(factory.Variable(choice), leaves[index], leaves[index + 1]));
            }
            leaves = next.ToImmutable();
        }

        return (new IrInterpreter(factory), factory.SequenceAccess(leaves[0], factory.Integer(0)),
            valueBuilder.ToImmutable(), heap, first);
    }
}
