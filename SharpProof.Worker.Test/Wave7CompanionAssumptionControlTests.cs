using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Wave7CompanionAssumptionControlTests
{
    [TestCase(false, true)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    [TestCase(true, false)]
    public async Task RegionReturnsPreserveThePrologueFilter(bool companion, bool valid)
    {
        var clauses = "Contract.Assume(value > 0); Contract.Ensures(Contract.Result<int>() > 0);";
        var source = "using SharpProof.Attributes; public static class Subject { public static int Target(int value) { " +
            (companion ? "" : clauses) + " try { return " + (valid ? "value" : "-1") + "; } finally { value = 0; } } } " +
            (companion ? "[ContractFor(typeof(Subject))] public static class Specification { public static int Target(int value) { " +
                clauses + " return 0; } }" : "");
        await Verify(source, valid ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task CompanionAssumptionUsesEntryStorageBeforeTargetMutation(bool valid)
    {
        await Verify("using SharpProof.Attributes; public static class Subject { public static int Target(int value) { value++; return value; } } " +
            "[ContractFor(typeof(Subject))] public static class Specification { public static int Target(int value) { " +
            "Contract.Assume(value == 7); Contract.Ensures(Contract.Old(value) == 7 && Contract.Result<int>() == " +
            (valid ? "8" : "7") + "); return 0; } }", valid ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted);
    }

    [Test]
    public async Task ContradictoryZeroParameterCompanionAssumptionFiltersNormalReturns()
    {
        await Verify("using SharpProof.Attributes; public static class Subject { public static int Target() => 0; } " +
            "[ContractFor(typeof(Subject))] public static class Specification { public static int Target() { " +
            "Contract.Assume(false); Contract.Ensures(Contract.Result<int>() == 42); return 0; } }",
            WorkerClaimOutcome.Proven, WorkerVacuityKind.NoModeledNormalReturn);
    }

    [Test]
    public async Task UndefinedCompanionAssumptionRetainsItsSafetyFilter()
    {
        await Verify("using SharpProof.Attributes; public static class Subject { public static int Target(int value) => -1; } " +
            "[ContractFor(typeof(Subject))] public static class Specification { public static int Target(int value) { " +
            "Contract.Requires(value == 0); Contract.Assume(10 / value == 10 / value); " +
            "Contract.Ensures(Contract.Result<int>() == 42); return 0; } }",
            WorkerClaimOutcome.Proven, WorkerVacuityKind.NoModeledNormalReturn);
    }

    [Test]
    public async Task CompanionAssumptionsDoNotLeakThroughSourceCalls()
    {
        await Verify("using SharpProof.Attributes; public static class Subject { " +
            "public static int Helper(int value) => value; public static int Target(int value) { " +
            "Contract.Ensures(Contract.Result<int>() > 0); return Helper(value); } } " +
            "[ContractFor(typeof(Subject))] public static class Specification { public static int Helper(int value) { " +
            "Contract.Assume(false); return 0; } }", WorkerClaimOutcome.Refuted, assumptions: 0);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task InstanceCompanionAssumptionsMapTheValueParameter(bool valid)
    {
        await Verify("using SharpProof.Attributes; public sealed class Subject { public int Target(int value) => " +
            (valid ? "value" : "-1") + "; } [ContractFor(typeof(Subject))] public static class Specification { " +
            "public static int Target(Subject receiver, int value) { Contract.Assume(value > 0); " +
            "Contract.Ensures(Contract.Result<int>() > 0); return 0; } }",
            valid ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted);
    }

    private static async Task Verify(string source, WorkerClaimOutcome expected,
        WorkerVacuityKind vacuity = WorkerVacuityKind.None, int assumptions = 1)
    {
        using var project = new ShadowTestProject(source, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
        Assert.That(claim.Vacuity, Is.EqualTo(vacuity));
        Assert.That(claim.Assumptions.Count(assumption => assumption.Kind == WorkerAssumptionKind.UserAssume), Is.EqualTo(assumptions));
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }
}
