using System.Collections.Immutable;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrTerminatorAdmissionTests
{
    private static readonly ImmutableArray<IrInstructionKind> s_terminalKinds =
    [
        IrInstructionKind.Branch, IrInstructionKind.Goto, IrInstructionKind.Throw,
        IrInstructionKind.Return, IrInstructionKind.ExceptionalExit
    ];

    [Test]
    public void PublicGraphRetainsEveryTerminalReferenceAndSuccessorOrder()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var go = builder.CreateBlock("go");
        var thrown = builder.CreateBlock("throw");
        var returned = builder.CreateBlock("return");
        var exceptional = builder.CreateBlock("exceptional");
        builder.Branch(entry, factory.CreateOperation(), factory.Boolean(true), go, returned);
        builder.Goto(go, factory.CreateOperation(), thrown);
        builder.Throw(thrown, factory.CreateOperation(), IrExceptionKind.Overflow, exceptional);
        builder.Return(returned, factory.CreateOperation(), factory.Integer(42));
        builder.ExceptionalExit(exceptional, factory.CreateOperation());
        var afterTerminal = Assert.Throws<InvalidOperationException>((Action)(() =>
            builder.Goto(returned, factory.CreateOperation(), exceptional)));
        Assert.That(afterTerminal!.Message, Is.EqualTo("No instruction can follow a block terminator."));
        var program = builder.Build();
        foreach (var block in program.Blocks)
        {
            Assert.That(block.Terminator, Is.SameAs(block.Instructions[block.Instructions.Length - 1]));
            Assert.That(block.Instructions.Length, Is.EqualTo(1));
            Assert.That(program.GetBlock(block.Id), Is.SameAs(block));
        }
        Assert.That(program.Blocks.Select(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Terminator.Kind)), Is.EqualTo(s_terminalKinds));
        var spent = 0;
        var order = IrBlockOrder.TryCreateAcyclicOrder(program, units =>
        {
            Assert.That(units, Is.EqualTo(1));
            spent += units;
            return spent <= 10;
        }, out var failure);
        Assert.That(failure, Is.EqualTo(IrAcyclicOrderFailure.None));
        Assert.That(spent, Is.EqualTo(10));
        Assert.That(order, Is.EqualTo(new[] { entry, returned, go, thrown, exceptional }));
    }

    [Test]
    public void BuilderStillRejectsMissingTerminalBeforeFreezingBlocks()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var target = factory.CreateVariable("target", factory.IntegerType);
        builder.Assign(entry, factory.CreateOperation(), target, factory.Integer(42));
        var exception = Assert.Throws<InvalidOperationException>((Action)(() => builder.Build()));
        Assert.That(exception!.Message, Is.EqualTo("Every program block must end in a control-flow terminator."));
        var returned = builder.Return(entry, factory.CreateOperation(), factory.Variable(target));
        var program = builder.Build();
        Assert.That(program.GetBlock(entry).Terminator, Is.SameAs(returned));
        Assert.That(program.GetBlock(entry).Instructions.Length, Is.EqualTo(2));
        var result = new IrProgramInterpreter(factory).Execute(program);
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ReturnValue!.Integer, Is.EqualTo(42));
    }
}
