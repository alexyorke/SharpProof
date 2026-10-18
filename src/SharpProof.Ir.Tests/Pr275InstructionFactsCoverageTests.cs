using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class Pr275InstructionFactsCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void MemberLoadsAndStoresReadReceiverBeforeArguments(bool isStatic)
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var receiver = factory.Variable(factory.CreateVariable("receiver", factory.ObjectType));
        var argument = factory.Integer(2);
        var value = factory.Integer(7);
        var target = factory.CreateVariable("target", factory.IntegerType);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "Value", factory.IntegerType, isStatic, factory.IntegerType);
        var location = builder.MemberLocation(member, isStatic ? null : receiver, argument);
        var load = builder.Load(entry, factory.CreateOperation("load"), target, location);
        var store = builder.Store(entry, factory.CreateOperation("store"), location, value);
        IrTerm[] expectedRead = isStatic ? [argument] : [receiver, argument];

        Assert.That(IrInstructionFacts.ReadTerms(load), Is.EqualTo(expectedRead));
        Assert.That(IrInstructionFacts.ReadTerms(store), Is.EqualTo(expectedRead.Concat([value])));
        Assert.That(IrInstructionFacts.WrittenVariables(load), Is.EqualTo(new[] { target }));
        Assert.That(IrInstructionFacts.WrittenVariables(store), Is.Empty);
    }

    [Test]
    public void ArrayLocationsAndElementStoresPreserveSequenceIndexValueOrder()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var array = factory.Variable(factory.CreateVariable("array", sequenceType));
        var index = factory.Integer(2);
        var value = factory.Integer(9);
        var target = factory.CreateVariable("target", factory.IntegerType);
        var location = builder.SequenceLocation(array, index);
        var load = builder.Load(entry, factory.CreateOperation("load"), target, location);
        var store = builder.Store(entry, factory.CreateOperation("store"), location, value);
        var elementStore = builder.ElementStore(entry, factory.CreateOperation("element store"), array, index, value);

        Assert.That(IrInstructionFacts.ReadTerms(load), Is.EqualTo(new IrTerm[] { array, index }));
        Assert.That(IrInstructionFacts.ReadTerms(store), Is.EqualTo(new IrTerm[] { array, index, value }));
        Assert.That(IrInstructionFacts.ReadTerms(elementStore), Is.EqualTo(new IrTerm[] { array, index, value }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CallFactsIncludeAnOptionalReceiverAndOptionalResult(bool isStatic)
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var receiver = factory.Variable(factory.CreateVariable("receiver", factory.ObjectType));
        var argument = factory.Integer(5);
        var target = factory.CreateVariable("target", factory.IntegerType);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "Call", factory.IntegerType, isStatic, factory.IntegerType);
        var withResult = builder.Call(entry, factory.CreateOperation("call result"), target,
            member, isStatic ? null : receiver, argument);
        var discarded = builder.Call(entry, factory.CreateOperation("discard result"), null,
            member, isStatic ? null : receiver, argument);
        IrTerm[] expectedRead = isStatic ? [argument] : [receiver, argument];

        Assert.That(IrInstructionFacts.ReadTerms(withResult), Is.EqualTo(expectedRead));
        Assert.That(IrInstructionFacts.ReadTerms(discarded), Is.EqualTo(expectedRead));
        Assert.That(IrInstructionFacts.WrittenVariables(withResult), Is.EqualTo(new[] { target }));
        Assert.That(IrInstructionFacts.WrittenVariables(discarded), Is.Empty);
    }

    [Test]
    public void AllocationReadsItsLengthBeforeInitialElements()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var target = factory.CreateVariable("array", sequenceType);
        var length = factory.Integer(2);
        var first = factory.Integer(3);
        var second = factory.Integer(4);
        var allocation = builder.Allocate(entry, factory.CreateOperation("allocate"),
            sequenceType, target, length, [first, second]);
        var objectAllocation = builder.Allocate(entry, factory.CreateOperation("object"), factory.ObjectType);

        Assert.That(IrInstructionFacts.ReadTerms(allocation), Is.EqualTo(new[] { length, first, second }));
        Assert.That(IrInstructionFacts.WrittenVariables(allocation), Is.EqualTo(new[] { target }));
        Assert.That(IrInstructionFacts.ReadTerms(objectAllocation), Is.Empty);
        Assert.That(IrInstructionFacts.WrittenVariables(objectAllocation), Is.Empty);
    }
}
