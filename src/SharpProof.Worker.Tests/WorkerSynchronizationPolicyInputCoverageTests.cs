using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class WorkerTests
{
    private const string SynchronizationPolicyBoundarySource = """
        using SharpProof.Attributes;
        public static class Subject {
            [AllowedCapabilities(SharpProofCapability.Synchronization)]
            public static int Target(int value) {
                Contract.Requires(value >= 0);
                return value;
            }
        }
        """;

    [Test]
    public async Task SynchronizationProjectionRejectsAnUnrelatedCapability()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(SynchronizationPolicyBoundarySource);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparations.Single());
        Assert.That(candidate, Is.Not.Null);
        var result = await NativeEffectSiteVerifier.VerifySynchronizationProjectionAsync(candidate!,
            WorkerEffectCapabilitySet.IO, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.Null);
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedContract));
        }
    }

    [Test]
    public async Task NamedSynchronizationCapabilityIsAcceptedByThePublicWorker()
    {
        using var project = TestProject.Create(SynchronizationPolicyBoundarySource);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var result = response.ClaimResults.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven),
                WorkerProtocolJson.SerializeResponse(response));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
