using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerVcAllocationIdentityControlTests
{
    [TestCase("object first = new int[1]; object second = first; return first == second;", true, WorkerClaimOutcome.Proven)]
    [TestCase("object first = new int[1]; object second = first; return first == second;", false, WorkerClaimOutcome.Refuted)]
    [TestCase("object first = \"text\"; object second = new int[1]; return first == second;", false, WorkerClaimOutcome.Proven)]
    [TestCase("object first = new int[0]; object second = new bool[0]; return first == second;", false, WorkerClaimOutcome.Proven)]
    public async Task FreshnessPreservesAliasesAndOtherReferenceKinds(string body, bool contractResult,
        WorkerClaimOutcome expected)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { " +
            "public static bool Target() { Contract.Ensures(Contract.Result<bool>() == " +
            (contractResult ? "true" : "false") + "); " + body + " } }", cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }

    [Test]
    public async Task ConditionalAliasRemainsPossibleAfterAnotherAllocation()
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { " +
            "public static bool Target(bool alias) { Contract.Ensures(Contract.Result<bool>() == alias); " +
            "object first = new int[1]; object second; if (alias) second = first; else second = new bool[1]; " +
            "return first == second; } }", cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }
}
