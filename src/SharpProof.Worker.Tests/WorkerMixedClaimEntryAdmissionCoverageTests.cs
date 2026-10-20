using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class WorkerTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task MixedRealEffectAndPostconditionRowsPreserveEntryAdmission(bool decimalEntry)
    {
        var condition = decimalEntry ? "(decimal)value >= 0m" : "value >= 0";
        var source = $$"""
            using SharpProof.Attributes;
            public static class Subject {
                [ZeroAllocations]
                public static int Target(int value) {
                    Contract.Requires({{condition}});
                    Contract.Ensures(true);
                    return value;
                }
            }
            """;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var preparation = preparations.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(preparation.Entry.ClaimIds, Has.Length.EqualTo(2));
            Assert.That(preparation.EffectClaims, Has.Length.EqualTo(1));
            Assert.That(preparation.TotalEntry == null, Is.EqualTo(decimalEntry));
            Assert.That(preparation.IsSuccess, Is.EqualTo(!decimalEntry));
            Assert.That(preparation.FailureReason, Is.EqualTo(decimalEntry
                ? WorkerClaimReason.UnsupportedExpression : WorkerClaimReason.None));
        }

        using var project = TestProject.Create(source);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(response.Manifest.Claims, Has.Length.EqualTo(2));
            Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
            Assert.That(response.ClaimResults.Select(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (result.Outcome)), Is.All.EqualTo(decimalEntry
                ? WorkerClaimOutcome.Unknown : WorkerClaimOutcome.Proven), WorkerProtocolJson.SerializeResponse(response));
            Assert.That(response.ClaimResults.Select(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (result.Reason)), Is.All.EqualTo(decimalEntry
                ? WorkerClaimReason.UnsupportedExpression : WorkerClaimReason.None));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
