using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerCachedOpenClassSoundnessTests
{
    [TestCase("\"text\"")]
    [TestCase("System.Array.Empty<int>()")]
    public async Task CachedValueCannotAliasAnUnrelatedClass(string value)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public class Cell { } " +
            "public static class Subject { public static bool Target(Cell cell) { " +
            "Contract.Ensures(Contract.Result<bool>()); object cached = " + value + "; object other = cell; " +
            "return cached != other; } }", cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }
}
