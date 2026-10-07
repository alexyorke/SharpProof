using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Wave7CompanionAssumptionAuditTests
{
    [TestCase(false, true)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    [TestCase(true, false)]
    public async Task CompanionAndDirectPrologueAssumptionsHaveTheSameMeaning(bool companion, bool valid)
    {
        var clauses = "Contract.Assume(value > 0); Contract.Ensures(Contract.Result<int>() > 0);";
        var source = "using SharpProof.Attributes; public static class Subject { public static int Target(int value) { " +
            (companion ? "" : clauses) + " return " + (valid ? "value" : "-1") + "; } } " +
            (companion ? "[ContractFor(typeof(Subject))] public static class Specification { public static int Target(int value) { " +
                clauses + " return 0; } }" : "");
        using var project = new ShadowTestProject(source, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        await TestContext.Out.WriteLineAsync($"companion={companion}; valid={valid}; outcome={claim.Outcome}; reason={claim.Reason}");
        Assert.That(claim.Outcome, Is.EqualTo(valid ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted), claim.Reason.ToString());
        Assert.That(claim.Assumptions.Single().Kind, Is.EqualTo(WorkerAssumptionKind.UserAssume));
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }
}
