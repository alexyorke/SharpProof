using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerEffectEvaluationWireMappingsTests
{
    [Test]
    public void EveryNeutralEffectEvaluationValueHasAClosedWireMapping()
    {
        Assert.That(
            Enum.GetValues<EffectEvaluationContractKind>()
                .Select(CompilerEffectEvaluationWireMappings.ToWorker),
            Is.EqualTo(Enum.GetValues<WorkerEffectContractKind>()
                .Where(static value => value !=
                    WorkerEffectContractKind.Unspecified)));
        Assert.That(
            Enum.GetValues<EffectEvaluationOutcome>()
                .Select(CompilerEffectEvaluationWireMappings.ToWorker),
            Is.EqualTo(new[]
            {
                WorkerClaimOutcome.Proven,
                WorkerClaimOutcome.Refuted,
                WorkerClaimOutcome.Unknown
            }));
        Assert.That(
            Enum.GetValues<EffectEvaluationReason>()
                .Select(CompilerEffectEvaluationWireMappings.ToWorker),
            Is.EqualTo(new[]
            {
                WorkerClaimReason.None,
                WorkerClaimReason.UnsupportedContract,
                WorkerClaimReason.EffectContractNotEstablished,
                WorkerClaimReason.EffectSummaryIncomplete,
                WorkerClaimReason.ResourceLimit,
                WorkerClaimReason.UnsupportedBody
            }));
        Assert.That(
            Enum.GetValues<EffectEvaluationCertainty>()
                .Select(CompilerEffectEvaluationWireMappings.ToWorker),
            Is.EqualTo(new[]
            {
                WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary,
                WorkerEffectEvidenceCertainty.CompleteMayEffectSummary,
                WorkerEffectEvidenceCertainty.TrustedCompleteBoundary,
                WorkerEffectEvidenceCertainty.DefiniteViolation,
                WorkerEffectEvidenceCertainty.Unavailable
            }));
    }

    [Test]
    public void FutureNeutralEffectEvaluationValuesFailClosed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            (Action)(() => CompilerEffectEvaluationWireMappings.ToWorker(
                (EffectEvaluationContractKind)int.MaxValue)));
        Assert.Throws<ArgumentOutOfRangeException>(
            (Action)(() => CompilerEffectEvaluationWireMappings.ToWorker(
                (EffectEvaluationOutcome)int.MaxValue)));
        Assert.Throws<ArgumentOutOfRangeException>(
            (Action)(() => CompilerEffectEvaluationWireMappings.ToWorker(
                (EffectEvaluationReason)int.MaxValue)));
        Assert.Throws<ArgumentOutOfRangeException>(
            (Action)(() => CompilerEffectEvaluationWireMappings.ToWorker(
                (EffectEvaluationCertainty)int.MaxValue)));
    }
}
