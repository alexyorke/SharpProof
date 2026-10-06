using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerVcFieldFreshnessControlTests
{
    [TestCase("box != null", "if (box.Data == null || box.Data.Length == 0) return 0; " +
        "int before = box.Data[0]; var fresh = new int[1]; return box.Data[0] - before;", 0)]
    [TestCase("box != null && box.Data != null && box.Data.Length == 1 && box.Data[0] == 7", "var fresh = new int[1]; " +
        "box.Data = fresh; return box.Data[0];", 0)]
    [TestCase("box != null && box.Item != null && box.Item.Value == 7", "var fresh = new Cell(); return box.Item.Value;", 7)]
    public async Task FreshAllocationsPreservePriorFieldsAndAllowLaterFieldReplacement(string requires, string body, int expected)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; " +
            "public sealed class Cell { public int Value; } public sealed class Box { public int[] Data; public Cell Item; } " +
            "public static class Subject { public static int Target(Box box) { Contract.Requires(" + requires + "); " +
            "Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body + " } }", cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }
}
