using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerDynamicReferenceWriteWitnessTests
{
    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    public async Task FieldWriteIsRefutedBeforeALaterArrayAllocation(int length)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public class Subject { public bool Flag; " +
            "[EnforcePure] public void Target(int length) { Contract.Requires(length == " + length + "); " +
            "Flag = false; var values = new bool[length]; } }", cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }
}
