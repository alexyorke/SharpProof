using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class Pr275AcyclicOrderCoverageTests
{
    [Test]
    public void ExhaustedBudgetReturnsResourceLimitWithoutPartialOrder()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        builder.Return(entry, factory.CreateOperation("return"));
        var charges = new List<int>();

        var order = IrBlockOrder.TryCreateAcyclicOrder(builder.Build(), cost =>
        {
            charges.Add(cost);
            return false;
        }, out var failure);

        Assert.That(order.IsDefault, Is.True);
        Assert.That(failure, Is.EqualTo(IrAcyclicOrderFailure.ResourceLimit));
        Assert.That(charges, Has.Count.EqualTo(1));
        Assert.That(charges[0], Is.EqualTo(1));
    }

    [Test]
    public void SharedSuccessorOccursOnceAndUnreachableBlocksAreOmitted()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var exit = builder.CreateBlock("shared exit");
        var unused = builder.CreateBlock("unreachable");
        builder.Branch(entry, factory.CreateOperation("branch"), factory.Boolean(true), exit, exit);
        builder.Return(exit, factory.CreateOperation("return"));
        builder.Return(unused, factory.CreateOperation("unused return"));
        var program = builder.Build();

        var order = IrBlockOrder.TryCreateAcyclicOrder(program, _ => true, out var failure);
        var successors = IrInstructionFacts.TryGetSuccessors(program.GetBlock(entry).Terminator);

        Assert.That(failure, Is.EqualTo(IrAcyclicOrderFailure.None));
        Assert.That(order, Is.EqualTo(new[] { entry, exit }));
        Assert.That(successors!.Value.First, Is.EqualTo(exit));
        Assert.That(successors.Value.Second, Is.Null);
    }

    [Test]
    public void ExceptionTargetIsOrderedAfterItsThrowSite()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("throw");
        var exit = builder.CreateBlock("exceptional exit");
        builder.Throw(entry, factory.CreateOperation("throw"), IrExceptionKind.NullReference, exit);
        builder.ExceptionalExit(exit, factory.CreateOperation("exit"));
        var program = builder.Build();

        var order = IrBlockOrder.TryCreateAcyclicOrder(program, _ => true, out var failure);

        Assert.That(failure, Is.EqualTo(IrAcyclicOrderFailure.None));
        Assert.That(order, Is.EqualTo(new[] { entry, exit }));
    }

    [Test]
    public void NullProgramAndBudgetReportTheirArgumentNames()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        builder.Return(entry, factory.CreateOperation("return"));

        Assert.That(Assert.Throws<ArgumentNullException>((Action)(() =>
            IrBlockOrder.TryCreateAcyclicOrder(null!, _ => true, out _)))!.ParamName,
            Is.EqualTo("program"));
        Assert.That(Assert.Throws<ArgumentNullException>((Action)(() =>
            IrBlockOrder.TryCreateAcyclicOrder(builder.Build(), null!, out _)))!.ParamName,
            Is.EqualTo("spend"));
    }
}
