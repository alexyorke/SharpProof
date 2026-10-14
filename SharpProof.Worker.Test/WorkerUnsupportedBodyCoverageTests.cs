using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task UnsupportedDecimalBodyCannotEstablishAnEffectProof(bool effectSummary, bool decimalBody)
    {
        var annotation = effectSummary
            ? "[EffectContract(SharpProofEffect.None, Complete = true)]"
            : "[AllowedCapabilities(SharpProofCapability.None)]";
        var body = decimalBody ? "(int)((decimal)value * 2m)" : "unchecked(value * 2)";
        using var project = TestProject.Create($$"""
            using SharpProof.Attributes;
            public static class Subject {
                {{annotation}}
                public static int Target(int value) => {{body}};
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        AssertClaimVerdict(response,
            decimalBody ? WorkerClaimOutcome.Unknown : WorkerClaimOutcome.Proven,
            decimalBody ? WorkerClaimReason.UnsupportedBody : WorkerClaimReason.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
