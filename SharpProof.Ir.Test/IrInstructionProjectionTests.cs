using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrInstructionProjectionTests
{
    [Test]
    public void LoadsAndStoresExposeLocationsAndStoredValuesInEvaluationOrder()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var type = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Receiver");
        var receiver = factory.Variable(factory.CreateVariable("receiver", type));
        var index = factory.Variable(factory.CreateVariable("index", factory.IntegerType));
        var target = factory.CreateVariable("target", factory.IntegerType);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), type, "Read", factory.IntegerType, false, factory.IntegerType);
        var location = builder.MemberLocation(member, receiver, index);
        var load = builder.Load(block, factory.CreateOperation(), target, location);
        Assert.That(IrInstructionFacts.WrittenVariables(load), Is.EqualTo(new[] { target }));
        Assert.That(IrInstructionFacts.ReadTerms(load), Is.EqualTo(new[] { receiver, index }));
        var value = factory.Integer(10);
        var store = builder.Store(block, factory.CreateOperation(), location, value);
        Assert.That(IrInstructionFacts.ReadTerms(store), Is.EqualTo(new IrTerm[] { receiver, index, value }));
        Assert.That(IrInstructionFacts.WrittenVariables(store), Is.Empty);
        var staticMember = factory.GetOrCreateMember(factory.CreateIdentity(), type, "Static", factory.IntegerType, true, factory.IntegerType);
        var staticLoad = builder.Load(block, factory.CreateOperation(), target, builder.MemberLocation(staticMember, null, index));
        Assert.That(IrInstructionFacts.ReadTerms(staticLoad), Is.EqualTo(new[] { index }));
        var sequence = factory.Variable(factory.CreateVariable("array", factory.GetOrCreateSequenceType(factory.IntegerType)));
        var sequenceLoad = builder.Load(block, factory.CreateOperation(), target, builder.SequenceLocation(sequence, index));
        Assert.That(IrInstructionFacts.ReadTerms(sequenceLoad), Is.EqualTo(new[] { sequence, index }));
        var returned = builder.Return(block, factory.CreateOperation());
        Assert.That(IrInstructionFacts.ReadTerms(returned), Is.Empty);
        Assert.That(IrInstructionFacts.TryGetSuccessors(load), Is.Null);
        Assert.That(builder.Build().Blocks.Length, Is.EqualTo(1));
    }

    [Test]
    public void SnapshotSelectorsFollowCastsAndShareRepeatedLeaves()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var elementType = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Element");
        var type = factory.GetOrCreateSequenceType(elementType);
        var old = factory.CreateVariable("old", type);
        var sequence = factory.Variable(old);
        var owner = factory.Cast(factory.ObjectType, factory.SequenceAccess(sequence, factory.Integer(0)));
        var memo = new Dictionary<IrId, IrTerm>();
        Assert.That(IrSnapshotReads.TrySelector(factory, owner, variable => variable == old,
            guard => guard, () => true, memo, out var selector, default), Is.True);
        Assert.That(selector, Is.SameAs(factory.Boolean(true)));
        var unsupported = factory.PureOpaque(factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "UnknownOwner", factory.ObjectType, true), null);
        Assert.That(IrSnapshotReads.TrySelector(factory, unsupported, _ => false,
            guard => guard, () => true, memo, out _, default), Is.False);
        Assert.That(IrSnapshotReads.TrySelector(factory, factory.Null(factory.ObjectType), _ => true,
            guard => guard, () => true, memo, out selector, default), Is.True);
        Assert.That(selector, Is.SameAs(factory.Boolean(false)));
    }
}
