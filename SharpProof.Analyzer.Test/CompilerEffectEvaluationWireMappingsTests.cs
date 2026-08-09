extern alias AnalyzerCore;

using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;
using CoreEffectEvaluationCertainty =
    AnalyzerCore::SharpProof.Analyzer.EffectEvaluationCertainty;
using CoreEffectEvaluationContractKind =
    AnalyzerCore::SharpProof.Analyzer.EffectEvaluationContractKind;
using CoreEffectEvaluationOutcome =
    AnalyzerCore::SharpProof.Analyzer.EffectEvaluationOutcome;
using CoreEffectEvaluationReason =
    AnalyzerCore::SharpProof.Analyzer.EffectEvaluationReason;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerEffectEvaluationWireMappingsTests
{
    [Test]
    public void EveryNeutralEffectEvaluationValueHasAClosedWireMapping()
    {
        Assert.That(
            Enum.GetValues<CoreEffectEvaluationContractKind>()
                .Select(CompilerEffectEvaluationWireMappings.ToWorker),
            Is.EqualTo(Enum.GetValues<WorkerEffectContractKind>()
                .Where(static value => value !=
                    WorkerEffectContractKind.Unspecified)));
        Assert.That(
            Enum.GetValues<CoreEffectEvaluationOutcome>()
                .Select(CompilerEffectEvaluationWireMappings.ToWorker),
            Is.EqualTo(new[]
            {
                WorkerClaimOutcome.Proven,
                WorkerClaimOutcome.Refuted,
                WorkerClaimOutcome.Unknown
            }));
        Assert.That(
            Enum.GetValues<CoreEffectEvaluationReason>()
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
            Enum.GetValues<CoreEffectEvaluationCertainty>()
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
                (CoreEffectEvaluationContractKind)int.MaxValue)));
        Assert.Throws<ArgumentOutOfRangeException>(
            (Action)(() => CompilerEffectEvaluationWireMappings.ToWorker(
                (CoreEffectEvaluationOutcome)int.MaxValue)));
        Assert.Throws<ArgumentOutOfRangeException>(
            (Action)(() => CompilerEffectEvaluationWireMappings.ToWorker(
                (CoreEffectEvaluationReason)int.MaxValue)));
        Assert.Throws<ArgumentOutOfRangeException>(
            (Action)(() => CompilerEffectEvaluationWireMappings.ToWorker(
                (CoreEffectEvaluationCertainty)int.MaxValue)));
    }
}
