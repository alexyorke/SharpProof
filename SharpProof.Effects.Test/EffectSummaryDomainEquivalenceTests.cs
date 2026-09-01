using SharpProof.Dataflow;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class EffectSummaryDomainEquivalenceTests
{
    private const EffectAnalysisIncompleteReason BothReasons =
        EffectAnalysisIncompleteReason.BlockBudgetExceeded |
        EffectAnalysisIncompleteReason.CallPreconditionNotProven;

    [Test]
    public void DistinctIncompleteReasonsAreNotEquivalent()
    {
        var domain = EffectSummaryDomain.Instance;
        var blockBudgetExceeded = IncompleteSummary(
            EffectAnalysisIncompleteReason.BlockBudgetExceeded);
        var callPreconditionNotProven = IncompleteSummary(
            EffectAnalysisIncompleteReason.CallPreconditionNotProven);
        var joined = domain.Join(
            blockBudgetExceeded,
            callPreconditionNotProven);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                domain.AreEquivalent(
                    blockBudgetExceeded,
                    callPreconditionNotProven),
                Is.False);
            Assert.That(
                domain.LessThanOrEqual(
                    blockBudgetExceeded,
                    callPreconditionNotProven),
                Is.False);
            Assert.That(
                domain.LessThanOrEqual(
                    callPreconditionNotProven,
                    blockBudgetExceeded),
                Is.False);
            Assert.That(joined.AnalysisIncompleteReason, Is.EqualTo(BothReasons));
            Assert.That(domain.LessThanOrEqual(blockBudgetExceeded, joined), Is.True);
            Assert.That(domain.LessThanOrEqual(callPreconditionNotProven, joined), Is.True);
            Assert.That(domain.LessThanOrEqual(joined, domain.Top), Is.True);
            Assert.That(domain.Join(joined, domain.Top), Is.EqualTo(domain.Top));
        }
    }

    [Test]
    public void FixedPointPropagatesNewIncompleteReasonToDownstreamBlock()
    {
        var domain = EffectSummaryDomain.Instance;
        var initial = IncompleteSummary(
            EffectAnalysisIncompleteReason.BlockBudgetExceeded);
        var callPreconditionNotProven = IncompleteSummary(
            EffectAnalysisIncompleteReason.CallPreconditionNotProven);
        var receiverWrite = IncompleteSummary(
            EffectAnalysisIncompleteReason.CallPreconditionNotProven,
            writes: EffectRegionSet.Create(EffectRegionId.Receiver));
        var graph = new DataflowGraph<EffectSummary>(
            [
                new(0, state =>
                    (state.AnalysisIncompleteReason & BothReasons) == BothReasons
                        ? receiverWrite
                        : callPreconditionNotProven),
                new(1, state => state)
            ],
            [
                new(0, 0),
                new(0, 1)
            ]);

        var result = ForwardDataflowAnalysis.Analyze(graph, domain, initial);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.GetInputState(0).AnalysisIncompleteReason,
                Is.EqualTo(BothReasons));
            Assert.That(
                result.GetOutputState(1).Writes,
                Is.EqualTo(EffectRegionSet.Create(EffectRegionId.Receiver)));
        }
    }

    private static EffectSummary IncompleteSummary(
        EffectAnalysisIncompleteReason reason,
        EffectRegionSet writes = default)
    {
        return new EffectSummary(
            EffectRegionSet.Empty,
            writes,
            EffectAllocationKind.None,
            EffectCapabilitySet.Empty,
            EffectThrowSet.Empty,
            EffectTermination.Unknown,
            EffectCompleteness.Incomplete,
            EffectUncertainty.None,
            reason);
    }
}
