using System.Numerics;
using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task CheckedCreditDepositsProveLinearWorkAndUncheckedOverflowRejectsAtomically(bool checkedDeposit)
    {
        const string loop = "for(int i=0;i<n;i++) { credit+=2; if(flush) { while(credit>0) { credit--; } } }";
        var body = "int credit=0; " + (checkedDeposit ? "checked { " + loop + " }" : loop);
        var source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,0,nameof(n),3)] public static void Target(int n,bool flush) { " +
            body + " } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        Assert.That(target.Total, Is.Not.Null);
        Assert.That(target.Total!.IsBodyAbstraction, Is.False);
        Assert.That(target.Total.EffectsCompleteAtEntry, Is.True);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), cancellation)).Single();
        Assert.That(result.ResourceBound, Is.Not.Null);
        var bound = result.ResourceBound!;
        if (!checkedDeposit)
        {
            Assert.That(target.ResourceProgram, Is.Null);
            Assert.That(target.ResourceUnknownReason, Is.EqualTo("ResourceCreditMayOverflow"));
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(bound.UnknownReason, Is.EqualTo("ResourceCreditMayOverflow"));
            Assert.That(bound.InferredUpperBound, Is.Null);
            Assert.That(bound.ObservedLowerBound, Is.Null);
            Assert.That(bound.EvaluatedUpperBound, Is.Null);
            Assert.That(bound.Accounting, Is.Empty);
            Assert.That(bound.Asymptotic, Is.Null);
            Assert.That(bound.Expected, Is.Null);
            Assert.That(bound.MetadataAssumptions, Is.Empty);
            Assert.That(bound.RuntimePremises, Is.Null);
            Assert.That(result.ProofCore, Is.Empty);
            Assert.That(result.EffectWitness, Is.Null);
            Assert.That(result.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
            return;
        }
        Assert.That(target.ResourceUnknownReason, Is.Null);
        Assert.That(target.ResourceProgram, Is.Not.Null);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), bound.UnknownReason);
        Assert.That(bound.UnknownReason, Is.Null);
        Assert.That(bound.InferredUpperBound, Is.Not.Null.And.Not.Empty);
        Assert.That(bound.Accounting, Has.Length.EqualTo(1));
        var evidence = bound.Accounting[0];
        Assert.That(evidence.Model, Is.EqualTo(NativeResourceAccountingVerifier.ModelId));
        Assert.That(evidence.InitialCredit, Is.EqualTo("0"));
        Assert.That(evidence.DepositPerIteration, Is.EqualTo("2"));
        Assert.That(evidence.CounterWidth, Is.EqualTo(32));
        Assert.That(evidence.CheckedDeposit, Is.True);
        Assert.That(evidence.MaximumSpend, Is.EqualTo("(max(0, n) * 2)"));
        Assert.That(result.ProofCore, Does.Contain("resource-accounting:" + NativeResourceAccountingVerifier.ModelId));

        var pending = new Stack<ResourceProgram>();
        var credits = new List<ResourceProgram>();
        pending.Push(target.ResourceProgram!);
        while (pending.Count != 0)
        {
            var region = pending.Pop();
            if (region.Kind == ResourceProgramKind.CreditLoop)
            { credits.Add(region); }
            foreach (var child in region.Children)
            { pending.Push(child); }
        }
        Assert.That(credits, Has.Count.EqualTo(1));
        Assert.That(credits[0].Credit!.CheckedDeposit, Is.True);
        var budget = NativeResourceAccountingVerifier.Budget(credits[0], cancellation);
        Assert.That(budget.Maximum, Is.EqualTo(new BigInteger(int.MaxValue) * 2));
        var costs = NativeResourceProgramVerifier.Verify(target.ResourceProgram!, target.Entry.CallableId,
            target.Total!, cancellation);
        Assert.That(costs.Verified, Is.True, costs.UnknownReason);
        Assert.That(costs.Cost!.LoopEntries.Maximum, Is.EqualTo(new BigInteger(int.MaxValue) * 3));
        foreach (var size in new[] { -2, 0, 1, 3, 17, int.MaxValue })
        {
            var nonnegative = new BigInteger(Math.Max(0, size));
            Assert.That(budget.Evaluate(_ => size, cancellation), Is.EqualTo(nonnegative * 2));
            Assert.That(costs.Cost.LoopEntries.Evaluate(_ => size, cancellation), Is.EqualTo(nonnegative * 3));
        }
    }
}
