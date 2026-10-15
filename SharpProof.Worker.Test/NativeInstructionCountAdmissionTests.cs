using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeInstructionCountAdmissionTests
{
    [TestCase(PassiveCallableVcBuilder.MaximumSteps - 1, true)]
    [TestCase(PassiveCallableVcBuilder.MaximumSteps, false)]
    public async Task AcyclicInstructionAdmissionKeepsTheDefaultBoundary(int assignments, bool admitted)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation("instruction-count-admission");
        var local = factory.CreateVariable("local", factory.IntegerType);
        var result = factory.CreateVariable("result", factory.IntegerType);
        for (var index = 0; index < assignments; index++)
        { builder.Assign(block, site, local, factory.Integer(0)); }
        builder.Return(block, site, factory.Integer(0));
        var program = builder.Build();
        Assert.That(program.Blocks, Has.Length.EqualTo(1));
        Assert.That(program.Blocks.Single().Instructions, Has.Length.EqualTo(assignments + 1));
        var execution = new IrProgramInterpreter(factory).Execute(program);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ReturnValue?.Integer, Is.Zero);
            Assert.That(execution.ConsumedApproximation, Is.False);
            Assert.That(execution.Unsupported, Is.Null);
            Assert.That(execution.Exception, Is.Null);
        }
        var candidate = new PassiveCallableCandidate("instruction-count-admission", program, [], result, [],
            [new(factory.Binary(IrBinaryOperator.Equal, factory.Variable(result), factory.Integer(0)), factory.Boolean(true), site)]);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.EqualTo(admitted));
        Assert.That(reason, Is.EqualTo(admitted ? WorkerClaimReason.None : WorkerClaimReason.ResourceLimit));
        Assert.That(plan == null, Is.EqualTo(!admitted));
        if (!admitted)
        { return; }
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        var proof = await solver.VerifyEnsuresAsync(0);
        Assert.That(proof.Outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(proof.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(solver.ConsumedResourceCount, Is.GreaterThan(0));
    }
}
