using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [TestCase(false, false, WorkerClaimOutcome.Proven)]
    [TestCase(false, true, WorkerClaimOutcome.Refuted)]
    [TestCase(true, false, WorkerClaimOutcome.Proven)]
    [TestCase(true, true, WorkerClaimOutcome.Refuted)]
    public async Task LoopInvariantDistinguishesForbiddenAndUnreachableSynchronization(
        bool effectSummary, bool reached, WorkerClaimOutcome expected)
    {
        var annotation = effectSummary
            ? "[EffectContract(SharpProofEffect.Allocates, Complete = true)]"
            : "[AllowedCapabilities(SharpProofCapability.None)]";
        var guard = reached ? "i == 0" : "i < 0";
        using var project = TestProject.Create($$"""
            using SharpProof.Attributes;
            public static class Subject {
                {{annotation}}
                public static int Target(int n) {
                    var total = 0;
                    for (var i = 0; i < n; i++) {
                        if ({{guard}}) { lock (new object()) { } }
                        total++;
                    }
                    return total;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var claim = AssertClaimVerdict(response, expected, WorkerClaimReason.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
            if (reached)
            {
                Assert.That(claim.EffectWitness?.Kind, Is.EqualTo("synchronization-lock"));
                Assert.That(claim.EffectWitness?.Capabilities,
                    Is.EqualTo(WorkerEffectCapabilitySet.Synchronization));
            }
            else
            {
                Assert.That(claim.ProofCore, Is.Not.Empty);
                Assert.That(claim.EffectWitness, Is.Null);
            }
        }
    }
}
