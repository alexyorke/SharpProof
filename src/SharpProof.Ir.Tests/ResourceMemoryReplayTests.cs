using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class ResourceMemoryReplayTests
{
    private static readonly ResourceRuntimeLayoutContext Layout = new("CoreCLR", "8.0.32", "x64", 8);

    [TestCase(false, 0, 32, 32)]
    [TestCase(true, 32, 40, 72)]
    public void CompletedConcreteArraysHaveObservedRetainedAndAuxiliaryCosts(bool temporary, int peak, int retained, int cumulative)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var array = factory.CreateVariable("array", type);
        var alias = factory.CreateVariable("alias", type);
        if (temporary)
        { builder.Allocate(entry, factory.CreateOperation(), type, factory.CreateVariable("dead", type), factory.Integer(1)); }
        builder.Allocate(entry, factory.CreateOperation(), type, array, factory.Integer(temporary ? 3 : 1));
        builder.Assign(entry, factory.CreateOperation(), alias, factory.Variable(array));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(alias));
        var program = builder.Build();
        var replay = new ResourceMemoryReplay(program);
        var options = new IrProgramReplayOptions(static _ => null) { CompletedInstructionObserver = replay.Observe };
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 1000, options, TestContext.CurrentContext.CancellationToken);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        var observed = replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(observed.Bounds, Is.EqualTo(new ResourceMemoryBounds(cumulative, cumulative, peak, peak, retained, retained)));
        Assert.That(observed.Bounds, Is.EqualTo(ResourceMemoryProgram.Evaluate(program, Layout, TestContext.CurrentContext.CancellationToken).Bounds));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExceptionalAndTruncatedExecutionsCannotSupplyLifetimeNumbers(bool loop)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        if (loop)
        { builder.Goto(entry, factory.CreateOperation(), entry); }
        else
        {
            var type = factory.GetOrCreateSequenceType(factory.IntegerType);
            builder.Allocate(entry, factory.CreateOperation(), type, factory.CreateVariable("array", type), factory.Integer(-1));
            builder.Return(entry, factory.CreateOperation());
        }
        var program = builder.Build();
        var replay = new ResourceMemoryReplay(program);
        var options = new IrProgramReplayOptions(static _ => null) { CompletedInstructionObserver = replay.Observe };
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 10, options, TestContext.CurrentContext.CancellationToken);
        Assert.That(execution.Status, Is.EqualTo(loop ? IrProgramExecutionStatus.StepLimit : IrProgramExecutionStatus.Exception));
        Assert.That(replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken).Bounds, Is.Null);
    }

    [Test]
    public void ApproximateResultsCannotSupplyLifetimeNumbers()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var value = factory.CreateVariable("value", factory.BooleanType);
        builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, value);
        builder.Return(entry, factory.CreateOperation(), factory.Variable(value));
        var program = builder.Build();
        var replay = new ResourceMemoryReplay(program);
        var options = new IrProgramReplayOptions(_ => factory.CreateBooleanValue(false)) { CompletedInstructionObserver = replay.Observe };
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 100, options, TestContext.CurrentContext.CancellationToken);
        Assert.That(execution.ConsumedApproximation, Is.True);
        Assert.That(replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken).Bounds, Is.Null);
    }

    [Test]
    public void UnobservedAndForeignInstructionsCannotBecomeWitnesses()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Return(entry, factory.CreateOperation());
        var program = builder.Build();
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 10, TestContext.CurrentContext.CancellationToken);
        Assert.That(new ResourceMemoryReplay(program).Complete(execution, Layout, TestContext.CurrentContext.CancellationToken).Bounds, Is.Null);
        var foreign = new IrProgramBuilder(factory);
        var other = foreign.CreateBlock();
        var returned = foreign.Return(other, factory.CreateOperation());
        var replay = new ResourceMemoryReplay(program);
        replay.Observe(returned, null, false);
        Assert.That(replay.Complete(execution, Layout, TestContext.CurrentContext.CancellationToken).Bounds, Is.Null);
    }
}
