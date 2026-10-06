using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerVcDefaultFieldControlTests
{
    [TestCase("public int Value; public int Other;", "var cell = new Cell(); cell.Other = 7; return cell.Value;", true)]
    [TestCase("public int Value;", "var cell = new Cell(); var alias = cell; alias.Value = 7; return cell.Value;", false)]
    [TestCase("public int Value;", "var first = new Cell(); first.Value = 7; var second = new Cell(); return second.Value;", true)]
    [TestCase("public int Value; public Cell() { Value = 7; }", "var cell = new Cell(); return cell.Value;", false)]
    [TestCase("public int Value = 7;", "var cell = new Cell(); return cell.Value;", false)]
    [TestCase("public int Value; public virtual void Mutate() { Value = 7; }", "var cell = new Cell(); cell.Mutate(); return cell.Value;", false)]
    public async Task DefaultFieldsPreserveOtherHeapBehavior(string members, string body, bool proven)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public class Cell { " + members +
            " } public static class Subject { public static int Target() { " +
            "Contract.Ensures(Contract.Result<int>() == 0); " + body + " } }", cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        if (proven)
        { Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString()); }
        else
        { Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString()); }
    }

    [Test]
    public async Task FreshDefaultsDoNotConstrainPreexistingObjects()
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public sealed class Cell { public int Value; } " +
            "public static class Subject { public static int Target([NotNull] Cell existing) { " +
            "Contract.Ensures(Contract.Result<int>() == 0); var fresh = new Cell(); return existing.Value; } }", cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }
}
