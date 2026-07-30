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
            Is.All.TypeOf<WorkerEffectContractKind>());
        Assert.That(
            Enum.GetValues<EffectEvaluationOutcome>()
                .Select(CompilerEffectEvaluationWireMappings.ToWorker),
            Is.All.TypeOf<WorkerClaimOutcome>());
        Assert.That(
            Enum.GetValues<EffectEvaluationReason>()
                .Select(CompilerEffectEvaluationWireMappings.ToWorker),
            Is.All.TypeOf<WorkerClaimReason>());
        Assert.That(
            Enum.GetValues<EffectEvaluationCertainty>()
                .Select(CompilerEffectEvaluationWireMappings.ToWorker),
            Is.All.TypeOf<WorkerEffectEvidenceCertainty>());
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
