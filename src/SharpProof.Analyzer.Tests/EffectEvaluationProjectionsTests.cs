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

            var actual = EffectEvaluationProjections.Classify(
                established,
                violated,
                valid,
                complete,
                trusted,
                EffectEvaluationReason.ResourceLimit);
            Assert.That(
                EffectEvaluationProducerTupleCatalog.IsDefined(
                    actual.Outcome, actual.Reason, actual.Certainty),
                Is.True,
                $"Boolean combination {bits} emitted an unsupported producer tuple.");
            Assert.That(
                actual,
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
        // Keep this oracle independent from the generated switch so precedence
        // regressions remain observable.
        if (established)
        {
            return (
                EffectEvaluationOutcome.Proven,
                EffectEvaluationReason.None,
                trusted
                    ? EffectEvaluationCertainty.TrustedCompleteBoundary
                    : EffectEvaluationCertainty.CompleteMayEffectSummary);
        }

        if (violated)
        {
            return (
                EffectEvaluationOutcome.Refuted,
                EffectEvaluationReason.None,
                EffectEvaluationCertainty.DefiniteViolation);
        }

        if (!valid)
        {
            return (
                EffectEvaluationOutcome.Unknown,
                EffectEvaluationReason.UnsupportedContract,
                EffectEvaluationCertainty.Unavailable);
        }

        if (trusted)
        {
            return (
                EffectEvaluationOutcome.Unknown,
                complete
                    ? EffectEvaluationReason.EffectContractNotEstablished
                    : EffectEvaluationReason.ResourceLimit,
                EffectEvaluationCertainty.TrustedCompleteBoundary);
        }

        return complete
            ? (
                EffectEvaluationOutcome.Unknown,
                EffectEvaluationReason.EffectContractNotEstablished,
                EffectEvaluationCertainty.CompleteMayEffectSummary)
            : (
                EffectEvaluationOutcome.Unknown,
                EffectEvaluationReason.ResourceLimit,
                EffectEvaluationCertainty.IncompleteMayEffectSummary);
    }
}
