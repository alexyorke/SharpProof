using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class NestedIteratorCalleeAdmissionTests
{
    [Test]
    public async Task UnusedNestedIteratorDoesNotHideOrdinaryCalleeBody()
    {
        const string source = "using System.Collections.Generic; using SharpProof.Attributes; " +
            "public static class C { [ZeroAllocations] public static int Target() { return Callee(); } " +
            "private static int Callee() { static IEnumerable<int> Nested() { yield break; } return 7; } }";
        using var project = new ShadowTestProject(source);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [TestCase("return 7;", true)]
    [TestCase("static IEnumerable<int> Nested() { yield break; } _ = Nested(); return 7;", false)]
    [TestCase("static int Nested() => 7; return Nested();", true)]
    public async Task CalleeOnlySkipsNestedCallableBodies(string body, bool proven)
    {
        var source = "using System.Collections.Generic; using SharpProof.Attributes; " +
            "public static class C { [ZeroAllocations] public static int Target() { return Callee(); } " +
            "private static int Callee() { " + body + " } }";
        using var project = new ShadowTestProject(source);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome == WorkerClaimOutcome.Proven, Is.EqualTo(proven), claim.Reason.ToString());
    }
}
