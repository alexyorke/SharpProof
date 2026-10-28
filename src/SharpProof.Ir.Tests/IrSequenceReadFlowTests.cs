using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrSequenceReadFlowTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void AReplacementDoesNotHideAnEarlierReadOfTheInput(bool allocate)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var input = factory.CreateVariable("input", type);
        var size = factory.CreateVariable("size", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var before = builder.Assign(entry, factory.CreateOperation(), size, factory.Length(factory.Variable(input)));
        if (allocate)
        { builder.Allocate(entry, factory.CreateOperation(), type, input, factory.Integer(1)); }
        else
        { builder.Assign(entry, factory.CreateOperation(), input, factory.EmptyArray(type)); }
        builder.Return(entry, factory.CreateOperation(), factory.Length(factory.Variable(input)));
        Assert.That(IrSequenceReads.Readers(builder.Build()), Is.EquivalentTo(new[] { before.Id }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AReadAfterAJoinRequiresAllocationOnEveryIncomingPath(bool both)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var input = factory.CreateVariable("input", type);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var first = builder.CreateBlock();
        var second = builder.CreateBlock();
        var join = builder.CreateBlock();
        builder.Branch(entry, factory.CreateOperation(), factory.Variable(flag), first, second);
        builder.Allocate(first, factory.CreateOperation(), type, input, factory.Integer(1));
        builder.Goto(first, factory.CreateOperation(), join);
        if (both)
        { builder.Allocate(second, factory.CreateOperation(), type, input, factory.Integer(2)); }
        builder.Goto(second, factory.CreateOperation(), join);
        var read = builder.Return(join, factory.CreateOperation(), factory.Length(factory.Variable(input)));
        Assert.That(IrSequenceReads.Readers(builder.Build()), both ? Is.Empty : Is.EquivalentTo(new[] { read.Id }));
    }

    [Test]
    public void AnAliasCapturedBeforeReplacementRetainsItsIncomingOwnership()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var input = factory.CreateVariable("input", type);
        var alias = factory.CreateVariable("alias", type);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Assign(entry, factory.CreateOperation(), alias, factory.Variable(input));
        builder.Allocate(entry, factory.CreateOperation(), type, input, factory.Integer(1));
        var read = builder.Return(entry, factory.CreateOperation(), factory.Length(factory.Variable(alias)));
        Assert.That(IrSequenceReads.Readers(builder.Build()), Is.EquivalentTo(new[] { read.Id }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ALoopBackedgeCannotMakeTheFirstIterationInputFresh(bool allocatedOnEntry)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var input = factory.CreateVariable("input", type);
        var size = factory.CreateVariable("size", factory.IntegerType);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var loop = builder.CreateBlock();
        var exit = builder.CreateBlock();
        if (allocatedOnEntry)
        { builder.Allocate(entry, factory.CreateOperation(), type, input, factory.Integer(2)); }
        builder.Goto(entry, factory.CreateOperation(), loop);
        var read = builder.Assign(loop, factory.CreateOperation(), size, factory.Length(factory.Variable(input)));
        builder.Allocate(loop, factory.CreateOperation(), type, input, factory.Integer(1));
        builder.Branch(loop, factory.CreateOperation(), factory.Variable(flag), loop, exit);
        builder.Return(exit, factory.CreateOperation());
        Assert.That(IrSequenceReads.Readers(builder.Build()), allocatedOnEntry ? Is.Empty : Is.EquivalentTo(new[] { read.Id }));
    }
}
