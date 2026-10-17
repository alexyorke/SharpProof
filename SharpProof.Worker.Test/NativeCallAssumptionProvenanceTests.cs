using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeCallAssumptionProvenanceTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task ActualAcyclicAssumptionsRemainConditionalCallEvidence(bool assumed)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        PassiveParameterBinding Parameter(string name)
        {
            System.ArgumentNullException.ThrowIfNull(name);
            return new(factory.CreateVariable(name + "-entry", factory.BooleanType),
                factory.CreateVariable(name + "-current", factory.BooleanType), factory.CreateVariable(name + "-old", factory.BooleanType));
        }
        var first = Parameter("first");
        var second = Parameter("second");
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var structural = factory.CreateOperation("assumption-control");
        var firstSite = factory.CreateOperation("first-assumption");
        var secondSite = factory.CreateOperation("second-assumption");
        var site = factory.CreateOperation("conditional-call");
        var target = factory.CreateVariable("call-marker", factory.BooleanType);
        builder.Assign(block, structural, first.Current, factory.Variable(first.Entry));
        builder.Assign(block, structural, second.Current, factory.Variable(second.Entry));
        // Reverse emission order makes the canonical provenance ordering explicit.
        if (assumed)
        {
            builder.Assume(block, secondSite, factory.Variable(second.Current));
            builder.Assume(block, firstSite, factory.Variable(first.Current));
        }
        var value = factory.Binary(IrBinaryOperator.AndAlso, factory.Variable(first.Current), factory.Variable(second.Current));
        var safe = factory.Boolean(true);
        var marker = builder.Assign(block, site, target, factory.Binary(IrBinaryOperator.AndAlso, safe, value));
        builder.Return(block, structural);
        // This internal module obligation owns the actual guarded assignment.
        // Assumed proofs are conditional evidence, with no compiler publication claim.
        var candidate = new PassiveCallableCandidate("conditional-call", builder.Build(), [first, second], null, [], [],
            callPreconditions: [new(marker.Id, value, safe)]);
        foreach (var a in new[] { false, true })
        {
            foreach (var b in new[] { false, true })
            {
                var execution = new IrProgramInterpreter(factory).Execute(candidate.Program,
                    new Dictionary<IrVarId, IrValue> { [first.Entry] = factory.CreateBooleanValue(a), [second.Entry] = factory.CreateBooleanValue(b) });
                var stopped = assumed && !(a && b);
                Assert.That(execution.Status, Is.EqualTo(stopped ? IrProgramExecutionStatus.AssumptionViolated : IrProgramExecutionStatus.Returned));
                Assert.That(execution.ConsumedApproximation, Is.False);
                Assert.That(execution.Unsupported, Is.Null);
                Assert.That(execution.Exception, Is.Null);
                Assert.That(execution.GetCurrentValue(target) == null, Is.EqualTo(stopped));
                if (!stopped)
                { Assert.That(execution.GetCurrentValue(target)!.Boolean, Is.EqualTo(a && b)); }
            }
        }
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True);
        Assert.That(reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(plan!.EntryQuery().Assumptions, Is.Empty);
        using var solver = new PassiveCallableSolver(plan);
        var entry = await solver.VerifyEntryAsync();
        Assert.That(entry.Outcome, Is.TypeOf<RefutedOutcome>());
        Assert.That(entry.BodyAssumptions, Is.Empty);
        Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        var proof = await solver.VerifyCallPreconditionAsync(0);
        Assert.That(proof.Outcome, assumed ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>());
        Assert.That(proof.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(proof.BodyAssumptions, Is.EqualTo(assumed ? new[] { firstSite, secondSite } : []));
        Assert.That(proof.Core.Any(label => label is null ? throw new System.ArgumentNullException(nameof(label)) : (label.StartsWith("assume:", StringComparison.Ordinal))), Is.EqualTo(assumed));
        Assert.That(proof.CallPreconditionWitness, assumed ? Is.Null : Is.EqualTo(site));
        if (!assumed)
        {
            Assert.That(proof.EntryModel[first.Entry].Boolean && proof.EntryModel[second.Entry].Boolean, Is.False);
            Assert.That(plan.ReplayCallPrecondition(0, proof.EntryModel, CancellationToken.None), Is.EqualTo(site));
        }
        Assert.That(solver.ConsumedResourceCount, Is.GreaterThan(0));
    }
}
