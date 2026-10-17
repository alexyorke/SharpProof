using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [TestCase("[ZeroAllocations]", false)]
    [TestCase("[ZeroAllocations]", true)]
    [TestCase("[AllowedCapabilities(SharpProofCapability.None)]", false)]
    [TestCase("[AllowedCapabilities(SharpProofCapability.None)]", true)]
    [TestCase("[EffectContract(SharpProofEffect.None, Complete = true)]", false)]
    [TestCase("[EffectContract(SharpProofEffect.None, Complete = true)]", true)]
    public async Task EntryInitializationCompletenessDistinguishesMutableAndReadonlyScalarState(
        string attribute, bool readonlyState)
    {
        System.ArgumentNullException.ThrowIfNull(attribute);
        var modifier = readonlyState ? "readonly " : "";
        var source = $$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static {{modifier}}int State = 1;
                {{attribute}}
                public static int Target(int value) { return value; }
            }
            """;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var target = artifact.Callables.Single(callable => callable is null ? throw new System.ArgumentNullException(nameof(callable)) : (callable.EffectClaims.Length != 0));
        Assert.That(target.Total, Is.Not.Null);
        Assert.That(target.Total!.EffectsCompleteAtEntry, Is.EqualTo(readonlyState));

        using var project = TestProject.Create(source);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);
        var result = response.ClaimResults.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(result.Outcome, Is.EqualTo(readonlyState ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Unknown),
                WorkerProtocolJson.SerializeResponse(response));
            Assert.That(result.Reason, Is.EqualTo(readonlyState ? WorkerClaimReason.None : WorkerClaimReason.UnsupportedBody));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
