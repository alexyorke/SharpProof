using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;
using CoreEffectEvaluationCertainty =
    SharpProof.Analyzer.EffectEvaluationCertainty;
using CoreEffectEvaluationContractKind =
    SharpProof.Analyzer.EffectEvaluationContractKind;
using CoreEffectEvaluationOutcome =
    SharpProof.Analyzer.EffectEvaluationOutcome;
using CoreEffectEvaluationReason =
    SharpProof.Analyzer.EffectEvaluationReason;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerEffectEvaluationWireMappingsTests
{
    [Test]
    public void EveryNeutralEffectEvaluationValueHasAClosedWireMapping()
    {
        AssertClosedMapping(
            Enum.GetValues<CoreEffectEvaluationContractKind>(),
            value => CompilerEffectEvaluationWireMappings.ToWorker(value),
            Enum.GetValues<WorkerEffectContractKind>()
                .Where(static value => value != WorkerEffectContractKind.Unspecified));
        AssertClosedMapping(
            Enum.GetValues<CoreEffectEvaluationOutcome>(),
            value => CompilerEffectEvaluationWireMappings.ToWorker(value),
            new[]
            {
                WorkerClaimOutcome.Proven,
                WorkerClaimOutcome.Refuted,
                WorkerClaimOutcome.Unknown
            });
        AssertClosedMapping(
            Enum.GetValues<CoreEffectEvaluationReason>(),
            value => CompilerEffectEvaluationWireMappings.ToWorker(value),
            new[]
            {
                WorkerClaimReason.None,
                WorkerClaimReason.UnsupportedContract,
                WorkerClaimReason.EffectContractNotEstablished,
                WorkerClaimReason.EffectSummaryIncomplete,
                WorkerClaimReason.ResourceLimit,
                WorkerClaimReason.UnsupportedBody
            });
        AssertClosedMapping(
            Enum.GetValues<CoreEffectEvaluationCertainty>(),
            value => CompilerEffectEvaluationWireMappings.ToWorker(value),
            new[]
            {
                WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary,
                WorkerEffectEvidenceCertainty.CompleteMayEffectSummary,
                WorkerEffectEvidenceCertainty.TrustedCompleteBoundary,
                WorkerEffectEvidenceCertainty.DefiniteViolation,
                WorkerEffectEvidenceCertainty.Unavailable
            });
    }

    [Test]
    public void FutureNeutralEffectEvaluationValuesFailClosed()
    {
        AssertInvalid(() => CompilerEffectEvaluationWireMappings.ToWorker(
            (CoreEffectEvaluationContractKind)int.MaxValue));
        AssertInvalid(() => CompilerEffectEvaluationWireMappings.ToWorker(
            (CoreEffectEvaluationOutcome)int.MaxValue));
        AssertInvalid(() => CompilerEffectEvaluationWireMappings.ToWorker(
            (CoreEffectEvaluationReason)int.MaxValue));
        AssertInvalid(() => CompilerEffectEvaluationWireMappings.ToWorker(
            (CoreEffectEvaluationCertainty)int.MaxValue));
    }

    private static void AssertClosedMapping<TSource, TTarget>(
        IEnumerable<TSource> values,
        Func<TSource, TTarget> map,
        IEnumerable<TTarget> expected)
    {
        Assert.That(values.Select(map), Is.EqualTo(expected));
    }

    private static void AssertInvalid(Action action)
    {
        Assert.Throws<ArgumentOutOfRangeException>(action);
    }
}
