using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrSnapshotEvaluationDepthBoundaryTests
{
    [TestCase(false, 7)]
    [TestCase(true, 3)]
    public void CachedOwnerSnapshotSelectionRespectsTheFixedEvaluationDepth(bool chooseOld, int expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var current = factory.CreateVariable("current", factory.ObjectType);
        var old = factory.CreateVariable("old", factory.ObjectType);
        var flag = factory.CreateVariable("choose-old", factory.BooleanType);
        var descend = factory.CreateVariable("descend", factory.BooleanType);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "field:Value", factory.IntegerType, false);
        var identity = new IrObjectState().WithField(field, factory.CreateIntegerValue(3));
        var value = factory.CreateReferenceValue(factory.ObjectType, identity);
        var variables = global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(current, value).SetItem(old, value).SetItem(flag, factory.CreateBooleanValue(chooseOld)).SetItem(descend, factory.CreateBooleanValue(true));
        var heap = new IrHeap();
        heap.Fields.Add((identity, field), factory.CreateIntegerValue(7));
        var owner = factory.Conditional(factory.Variable(flag), factory.Variable(old), factory.Variable(current));
        var warmOwner = factory.Binary(IrBinaryOperator.NotEqual, owner, factory.Null(factory.ObjectType));
        IrTerm CreateRead(int wrappers)
        {
            IrTerm read = factory.PureOpaque(field, owner);
            for (var index = 0; index < wrappers; index++)
            {
                read = factory.Conditional(factory.Variable(descend), read, factory.Integer(0));
            }
            return factory.Conditional(warmOwner, read, factory.Integer(-1));
        }
        var interpreter = new IrInterpreter(factory);
        var snapshots = ImmutableHashSet.Create(old);
        var nearby = interpreter.Evaluate(CreateRead(252), variables, null, heap, default, snapshots);
        var boundaryTerm = CreateRead(253);
        var currentOnly = interpreter.Evaluate(boundaryTerm, variables, null, heap, default);
        var boundary = interpreter.Evaluate(boundaryTerm, variables, null, heap, default, snapshots);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(nearby.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(nearby.Value!.Integer, Is.EqualTo(expected));
            Assert.That(currentOnly.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(currentOnly.Value!.Integer, Is.EqualTo(7L));
            Assert.That(boundary.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
            Assert.That(boundary.Unsupported!.Reason, Is.EqualTo(IrUnsupportedReason.UnsupportedOperation));
            Assert.That(boundary.Unsupported.Detail, Is.EqualTo("The term nests deeper than 256 levels."));
        }
    }
}
