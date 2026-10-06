using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerVcStringLiteralIdentityControlTests
{
    [TestCase(true, WorkerClaimOutcome.Proven)]
    [TestCase(false, WorkerClaimOutcome.Refuted)]
    public async Task WidenedLiteralKeepsItsAlias(bool contractResult, WorkerClaimOutcome expected)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { " +
            "public static bool Target() { Contract.Ensures(Contract.Result<bool>() == " +
            (contractResult ? "true" : "false") + "); object first = \"text\"; object second = first; " +
            "var unrelated = new int[1]; return first == second; } }", cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }
}
