using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;

namespace SharpProof.Worker.Test;

public sealed class NativeArrayRangeWriteTests
{
    [Test]
    public void LoopSearchClonePreservesEveryCellOfARangeWrite()
    {
        var subject = new PassiveCallableVcTests.ScalarSubject();
        var factory = subject.Factory;
        var builder = subject.Builder;
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var storage = factory.CreateVariable("data", type);
        var array = factory.Variable(storage);
        var entry = builder.CreateBlock();
        var header = builder.CreateBlock();
        var body = builder.CreateBlock();
        var exit = builder.CreateBlock();
        builder.Assign(entry, subject.Site, subject.Parameter.Current, factory.Variable(subject.Parameter.Entry));
        builder.Allocate(entry, subject.Site, type, storage, factory.Integer(3), [factory.Integer(3), factory.Integer(5), factory.Integer(7)]);
        builder.Goto(entry, subject.Site, header);
        builder.Branch(header, subject.Site, factory.Binary(IrBinaryOperator.GreaterThan, factory.Variable(subject.Parameter.Current), factory.Integer(0)), body, exit);
        builder.ElementStore(body, subject.Site, array, factory.Integer(0), factory.Integer(0), factory.Integer(2));
        builder.Assign(body, subject.Site, subject.Parameter.Current, factory.Binary(IrBinaryOperator.Subtract, factory.Variable(subject.Parameter.Current), factory.Integer(1)));
        builder.Goto(body, subject.Site, header);
        builder.Return(exit, subject.Site, factory.SequenceAccess(array, factory.Integer(1)));
        var candidate = new PassiveCallableCandidate("range-loop", builder.Build(), [subject.Parameter], subject.Result, [],
            [new(factory.Boolean(true), factory.Boolean(true), subject.Site)]);
        Assert.That(PassiveLoopCutter.TryCreate(candidate, out _, out var search, out var reason, TestContext.CurrentContext.CancellationToken), Is.True, reason.ToString());
        var inputs = ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>.Empty.Add(subject.Parameter.Entry, factory.CreateIntegerValue(1));
        foreach (var program in ImmutableArray.Create(candidate.Program, search!.Program))
        {
            var execution = new IrProgramInterpreter(factory).Execute(program, inputs, 1000);
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), execution.Unsupported?.Detail);
            Assert.That(execution.ReturnValue!.Integer, Is.Zero);
            Assert.That(execution.ConsumedApproximation, Is.False);
        }
    }

    [TestCase(1, 2, true)]
    [TestCase(4, 0, true)]
    [TestCase(-1, 0, false)]
    [TestCase(0, -1, false)]
    [TestCase(5, 0, false)]
    [TestCase(3, 2, false)]
    [TestCase(1, int.MaxValue, false)]
    [TestCase(int.MaxValue, int.MaxValue, false)]
    public async Task NativeRangeProofRequiresTheCompleteOperationalBounds(int start, int length, bool valid)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var variable = factory.CreateVariable("data", type);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var site = factory.CreateOperation("range-validity");
        builder.Allocate(entry, site, type, variable, factory.Integer(4));
        builder.ElementStore(entry, site, factory.Variable(variable), factory.Integer(start), factory.Integer(0), factory.Integer(length));
        builder.Return(entry, site);
        var candidate = new PassiveCallableCandidate("range-validity", builder.Build(), [], null, [],
            [new(factory.Boolean(true), factory.Boolean(true), site)]);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out _, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True);
        using var solver = new PassiveCallableSolver(plan!);
        var verified = await solver.VerifyEnsuresAsync(0, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(verified.Outcome is ProvenOutcome, Is.EqualTo(valid));
        var execution = new IrProgramInterpreter(factory).Execute(candidate.Program);
        Assert.That(execution.Status, Is.EqualTo(valid ? IrProgramExecutionStatus.Returned : IrProgramExecutionStatus.Unsupported));
    }
}
