using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerVcDefaultArrayTests
{
    [TestCase("int", "0")]
    [TestCase("uint", "0U")]
    [TestCase("long", "0L")]
    [TestCase("ulong", "0UL")]
    [TestCase("bool", "false")]
    public async Task FreshScalarArrayReadsItsDefaultValue(string type, string zero)
    {
        var source = "using SharpProof.Attributes; public static class Subject { " +
            "public static " + type + " Target(int length, int index) { " +
            "Contract.Requires(length > 0 && index >= 0 && index < length); " +
            "Contract.Ensures(Contract.Result<" + type + ">() == " + zero + "); " +
            "var values = new " + type + "[length]; return values[index]; } }";
        using var project = new ShadowTestProject(source, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }
    [TestCase("var values = new int[2]; values[0] = 7; return values[0];", "7", WorkerClaimOutcome.Proven)]
    [TestCase("var values = new int[2]; values[0] = 7; return values[0];", "0", WorkerClaimOutcome.Refuted)]
    [TestCase("var values = new int[] { 3, 4 }; return values[1];", "4", WorkerClaimOutcome.Proven)]
    [TestCase("var values = new int[2]; return values.Length;", "2", WorkerClaimOutcome.Proven)]
    [TestCase("var values = new int[2]; return values[0];", "1", WorkerClaimOutcome.Refuted)]
    [TestCase("var values = new int[2]; for (int i = 0; i < 2; i++) values[0] = 7; return values[0];", "0", WorkerClaimOutcome.Refuted)]
    [TestCase("var values = new object[2]; return values[0] == null ? 0 : 1;", "0", WorkerClaimOutcome.Unknown)]
    public async Task NeighboringArrayBehaviorIsPreserved(string body, string expected, WorkerClaimOutcome outcome)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { " +
            "public static int Target() { Contract.Ensures(Contract.Result<int>() == " + expected + "); " + body + " } }",
            cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(outcome), claim.Reason.ToString());
    }
    [TestCase("var values = new int[2]; values[1] = 7; return values[0];")]
    [TestCase("var values = new int[2]; var alias = values; alias[1] = 7; return values[0];")]
    [TestCase("var first = new int[2]; first[0] = 7; var second = new int[2]; return second[0];")]
    [TestCase("int[] values; if (flag) values = new int[2]; else values = new int[3]; return values[0];")]
    public async Task DefaultInitializationSurvivesUnrelatedStores(string body)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { " +
            "public static int Target(bool flag) { Contract.Ensures(Contract.Result<int>() == 0); " + body + " } }",
            cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public async Task FreshInitializationDoesNotConstrainPreexistingArrays()
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { " +
            "public static int Target([NotNull] int[] existing) { Contract.Requires(existing.Length > 0); " +
            "Contract.Ensures(Contract.Result<int>() == 0); var fresh = new int[2]; fresh[0] = 7; return existing[0]; } }",
            cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }
}
