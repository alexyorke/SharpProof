using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerVcFieldFreshnessSoundnessTests
{
    [TestCase("box.Data = fresh;")]
    [TestCase("if (replace) box.Data = fresh;")]
    public async Task AFieldReplacedAfterAllocationCanAliasThatAllocation(string replacement)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public sealed class Box { public int[] Data; } " +
            "public static class Subject { public static int Target(Box box, bool replace) { " +
            "Contract.Requires(box != null && box.Data != null && box.Data.Length == 1 && box.Data[0] == 7); " +
            "Contract.Ensures(Contract.Result<int>() == 7); var fresh = new int[1]; " + replacement +
            " return box.Data[0]; } }", cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }
}
