using NUnit.Framework;
using SharpProof.Effects;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class EffectEvaluationProjectionsTests
{
    [Test]
    public void ClassificationCoversEveryBooleanCombinationAndPrecedence()
    {
        for (var bits = 0; bits < 32; bits++)
        {
            var established = (bits & 1) != 0;
            var violated = (bits & 2) != 0;
            var valid = (bits & 4) != 0;
            var complete = (bits & 8) != 0;
            var trusted = (bits & 16) != 0;
            var expected = ExpectedClassification(
                established,
                violated,
                valid,
                complete,
                trusted);

            Assert.That(
                EffectEvaluationProjections.Classify(
                    established,
                    violated,
                    valid,
                    complete,
                    trusted,
                    EffectEvaluationReason.ResourceLimit),
                Is.EqualTo(expected),
                $"Boolean combination {bits} changed classification precedence.");
        }
    }

    [Test]
    public void IncompleteReasonCoversEveryDefinedFlagCombination()
    {
        for (var bits = 0; bits < 16; bits++)
        {
            var reason = (EffectAnalysisIncompleteReason)bits;
            var expected = (bits & 3) != 0
                ? EffectEvaluationReason.ResourceLimit
                : (bits & 4) != 0
                    ? EffectEvaluationReason.UnsupportedBody
                    : EffectEvaluationReason.EffectSummaryIncomplete;

            Assert.That(
                EffectEvaluationProjections.MapIncompleteReason(reason),
                Is.EqualTo(expected),
                $"Incomplete-reason flags {bits} changed projection precedence.");
        }
    }

    private static (
        EffectEvaluationOutcome Outcome,
        EffectEvaluationReason Reason,
        EffectEvaluationCertainty Certainty) ExpectedClassification(
        bool established,
        bool violated,
        bool valid,
        bool complete,
        bool trusted)
    {
        return (established, violated, valid, complete, trusted) switch
        {
            (true, _, _, _, true) => (
                EffectEvaluationOutcome.Proven,
                EffectEvaluationReason.None,
                EffectEvaluationCertainty.TrustedCompleteBoundary),
            (true, _, _, _, _) => (
                EffectEvaluationOutcome.Proven,
                EffectEvaluationReason.None,
                EffectEvaluationCertainty.CompleteMayEffectSummary),
            (_, true, _, _, _) => (
                EffectEvaluationOutcome.Refuted,
                EffectEvaluationReason.None,
                EffectEvaluationCertainty.DefiniteViolation),
            (_, _, false, _, _) => (
                EffectEvaluationOutcome.Unknown,
                EffectEvaluationReason.UnsupportedContract,
                EffectEvaluationCertainty.Unavailable),
            (_, _, _, _, true) => (
                EffectEvaluationOutcome.Unknown,
                complete
                    ? EffectEvaluationReason.EffectContractNotEstablished
                    : EffectEvaluationReason.ResourceLimit,
                EffectEvaluationCertainty.TrustedCompleteBoundary),
            (_, _, _, false, _) => (
                EffectEvaluationOutcome.Unknown,
                EffectEvaluationReason.ResourceLimit,
                EffectEvaluationCertainty.IncompleteMayEffectSummary),
            _ => (
                EffectEvaluationOutcome.Unknown,
                EffectEvaluationReason.EffectContractNotEstablished,
                EffectEvaluationCertainty.CompleteMayEffectSummary)
        };
    }
}
