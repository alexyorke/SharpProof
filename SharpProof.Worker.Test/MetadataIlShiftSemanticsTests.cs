using NUnit.Framework;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class MetadataIlShiftSemanticsTests
{
    [Test]
    public async Task LogicalRightShiftFromMetadataProvesMatchingClaim()
    {
        using var subject = new MetadataTestSubject(
            "public static class Library { public static int Target(int value) => value >>> 1; }",
            """
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Ensures(Contract.Result<int>() == (value >>> 1));
                    return Library.Target(value);
                }
            }
            """);
        Assert.That(subject.Invoke(-1), Is.EqualTo(int.MaxValue));
        Assert.That(subject.InvokeRoot(-1), Is.EqualTo(int.MaxValue));

        using var project = new ShadowTestProject(subject.CreateArtifact(), cacheEnabled: false);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        TestContext.WriteLine($"Metadata shift claim: {claim.Outcome}, {claim.Reason}");
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }
}
