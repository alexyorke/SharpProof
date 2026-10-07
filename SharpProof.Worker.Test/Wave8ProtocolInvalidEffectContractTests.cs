using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Wave8ProtocolInvalidEffectContractTests
{
    [TestCase(WorkerEffectContractKind.Unspecified)]
    [TestCase((WorkerEffectContractKind)int.MaxValue)]
    public async Task UnsupportedContractKindCannotValidateARefutationWitness(WorkerEffectContractKind kind)
    {
        const string source = "using SharpProof.Attributes; public static class Subject { " +
            "[DoesNotThrow] public static void Target() { throw new System.InvalidOperationException(); } }";
        using var project = new ShadowTestProject(source);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(claim.EffectWitness, Is.Not.Null);
        Assert.That(claim.EffectWitness!.Effects, Is.EqualTo(WorkerEffectSet.Throws));
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);

        response.Manifest.Claims.Single().EffectContractKind = kind;
        var validation = WorkerProtocolJson.Validate(response);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(validation.IsValid, Is.False);
            Assert.That(validation.Errors.Select(static error => error.Code), Contains.Item("manifest.claim_shape"));
            Assert.That(validation.Errors.Select(static error => error.Code), Contains.Item("response.effect_witness"));
        }
    }
}
