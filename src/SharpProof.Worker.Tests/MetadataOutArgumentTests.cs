using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A call into another assembly may take out arguments that name a local or
// parameter of the caller, passed once, outside every try region. On normal
// return each such variable holds an unknown value.
[TestFixture]
public sealed class MetadataOutArgumentTests
{
    private const string Types = "using SharpProof.Attributes;\npublic static class S { public static int F; }\n";

    [TestCase("public static int M(string s) { Contract.Ensures(Contract.Result<int>() >= 0); return int.TryParse(s, out var v) && v >= 0 ? v : 0; }")]
    [TestCase("public static int M(string s) { Contract.Ensures(Contract.Result<int>() == 0 || Contract.Result<int>() == 1); return int.TryParse(s, out _) ? 1 : 0; }")]
    [TestCase("public static int M(System.Collections.Generic.Dictionary<string, int> d, string k) { Contract.Ensures(Contract.Result<int>() >= -1); return d != null && d.TryGetValue(k, out var v) && v >= 0 ? v : -1; }")]
    [TestCase("public static int M(string s, int seed) { Contract.Ensures(Contract.Result<int>() == seed); var v = seed; var keep = seed; int.TryParse(s, out v); return keep; }")]
    public async Task OutArgumentsAreModeled(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // The value the callee stores is unknown.
    [TestCase("public static int M(string s) { Contract.Ensures(Contract.Result<int>() == 0); int.TryParse(s, out var v); return v; }")]
    // A handler could see a value stored before a throw; a field may be read
    // by the callee itself.
    [TestCase("public static int M(string s) { Contract.Ensures(Contract.Result<int>() == 1); int v = 0; try { int.TryParse(s, out v); } catch (System.Exception) { } return 1; }")]
    [TestCase("public static int M(string s) { Contract.Ensures(Contract.Result<int>() == 1); int.TryParse(s, out S.F); return 1; }")]
    public async Task UnknownOrUnadmittedOutArgumentsAreNotProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    private static async Task<WorkerClaimResult> ResultAsync(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        using var project = new ShadowTestProject(Types + "public static class C { " + member + " }\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var claim = response.Manifest.Claims.Single(static claim => claim.CallableId.Contains("C.M", StringComparison.Ordinal) &&
            claim.Kind is WorkerClaimKind.Postcondition or WorkerClaimKind.Effect);
        return response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId);
    }
}
