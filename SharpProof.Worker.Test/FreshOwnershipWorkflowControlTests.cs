using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class FreshOwnershipWorkflowControlTests
{
    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public async Task AliasedFreshWritesNeedAnObservableViolation(bool array, bool escape, bool rebind)
    {
        var type = array ? "int[]" : "Cell";
        var fresh = array ? "new int[1]" : "new Cell()";
        var write = array ? "alias[0] = 7;" : "alias.Value = 7;";
        var source = "using SharpProof.Attributes; public sealed class Cell { public int Value; } public static class C { public static " + type + " Keep; " +
            "[EnforcePure] public static int Target(" + type + " input, bool flag) { Contract.Requires(input != null); " +
            (array ? "Contract.Requires(input.Length == 1); " : "") + "Contract.Requires(flag == " + (rebind ? "true" : "false") +
            "); var local = " + fresh + "; if (flag) local = input; var alias = local; " + (escape ? "Keep = alias; " : "") + write + " return 0; } }";
        using var project = new ShadowTestProject(source, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, escape || rebind ? Is.EqualTo(WorkerClaimOutcome.Refuted) : Is.Not.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
        if (escape || rebind)
        { Assert.That(claim.EffectWitness, Is.Not.Null); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExistingFreshOwnershipStillProvesPurity(bool array)
    {
        var source = "using SharpProof.Attributes; public sealed class Cell { public int Value; } public static class C { " +
            "[EnforcePure] public static int Target() { " + (array ? "var local = new int[1]; local[0] = 7;" : "var local = new Cell(); local.Value = 7;") + " return 0; } }";
        using var project = new ShadowTestProject(source, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
    }
}
