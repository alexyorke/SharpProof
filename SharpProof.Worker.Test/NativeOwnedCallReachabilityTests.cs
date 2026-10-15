using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeOwnedCallReachabilityTests
{
    [TestCase("disconnected", false)]
    [TestCase("straight", true)]
    [TestCase("conditional-false", false)]
    [TestCase("conditional-true", true)]
    public async Task OwnedCallObligationsFollowActualOriginalMarkerReach(string topology, bool reached)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var call = builder.CreateBlock("call");
        var exit = builder.CreateBlock("exit");
        var structural = factory.CreateOperation("call-reach-control");
        var site = factory.CreateOperation("owned-call-obligation");
        var target = factory.CreateVariable("call-marker", factory.BooleanType);
        var value = factory.Boolean(false);
        var safe = factory.Boolean(true);
        var marker = builder.Assign(call, site, target, factory.Binary(IrBinaryOperator.AndAlso, safe, value));
        builder.Goto(call, structural, exit);
        builder.Return(exit, structural);
        switch (topology)
        {
            case "disconnected":
                builder.Goto(entry, structural, exit);
                break;
            case "straight":
                builder.Goto(entry, structural, call);
                break;
            default:
                builder.Branch(entry, structural, factory.Boolean(reached), call, exit);
                break;
        }
        // This maintained internal module contract owns its actual guarded
        // assignment. It carries no compiler claim or publication identity.
        var candidate = new PassiveCallableCandidate("owned-call-reachability", builder.Build(), [], null, [], [],
            callPreconditions: [new(marker.Id, value, safe)]);
        var execution = new IrProgramInterpreter(factory).Execute(candidate.Program);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.Unsupported, Is.Null);
        Assert.That(execution.Exception, Is.Null);
        // The fresh target has exactly one writer and no readers. Its actual
        // interpreted value exists precisely when the original marker ran.
        Assert.That(execution.GetCurrentValue(target) == null, Is.EqualTo(!reached));
        if (reached)
        { Assert.That(execution.GetCurrentValue(target)!.Boolean, Is.False); }
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True);
        Assert.That(reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(plan!.CallPreconditionCount, Is.EqualTo(1));
        Assert.That(plan.CallPreconditionQueries(0), Has.Length.EqualTo(1));
        using var solver = new PassiveCallableSolver(plan);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        var proof = await solver.VerifyCallPreconditionAsync(0);
        Assert.That(proof.Outcome, reached ? Is.TypeOf<RefutedOutcome>() : Is.TypeOf<ProvenOutcome>());
        Assert.That(proof.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(proof.CallPreconditionWitness, reached ? Is.EqualTo(site) : Is.Null);
        Assert.That(plan.ReplayCallPrecondition(0, proof.EntryModel, CancellationToken.None), reached ? Is.EqualTo(site) : Is.Null);
        Assert.That(solver.ConsumedResourceCount, Is.GreaterThan(0));
    }
}
