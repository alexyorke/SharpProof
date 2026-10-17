using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [TestCase("System.ArgumentException", WorkerClaimOutcome.Proven)]
    [TestCase("System.ArithmeticException", WorkerClaimOutcome.Refuted)]
    public async Task InstanceDelegateConstructionPreservesDeclaredExceptionBoundary(
        string allowedException, WorkerClaimOutcome expected)
    {
        System.ArgumentNullException.ThrowIfNull(allowedException);
        using var project = TestProject.Create($$"""
            using SharpProof.Attributes;
            public class Receiver { public void Sink() { } }
            public static class Subject {
                [AllowedExceptions(typeof({{allowedException}}))]
                public static System.Action Target(Receiver instance) {
                    return new System.Action(instance.Sink);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);
        var claim = AssertClaimVerdict(response, expected, WorkerClaimReason.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
            if (expected == WorkerClaimOutcome.Refuted)
            {
                Assert.That(claim.EffectWitness?.Kind, Is.EqualTo("implicit-throw"));
                Assert.That(claim.EffectWitness?.Detail, Is.EqualTo("System.ArgumentException"));
                Assert.That(claim.EffectWitness?.Effects, Is.EqualTo(WorkerEffectSet.Throws));
            }
            else
            {
                Assert.That(claim.ProofCore, Is.Not.Empty);
                Assert.That(claim.EffectWitness, Is.Null);
            }
        }
    }
}
