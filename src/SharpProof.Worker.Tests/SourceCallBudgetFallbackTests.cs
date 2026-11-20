using System.Text;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A source callee whose expansion runs out of lowering work becomes an
// unknown call: its preconditions are still checked at the call, and it may
// do anything, throw included. The caller's own claims then go on.
[TestFixture]
public sealed class SourceCallBudgetFallbackTests
{
    private const string Types = "using System;\nusing SharpProof.Attributes;\n";
    private const int Depth = 10;

    // Big(x) runs 2^Depth calls of the leaf, each adding one, through helpers
    // that call the next level twice: too many to expand within the budget.
    private static string Chain(string leaf, string requires = "")
    {
        System.ArgumentNullException.ThrowIfNull(leaf);
        System.ArgumentNullException.ThrowIfNull(requires);
        var members = new StringBuilder("public static int L0(int x) { ").Append(leaf).Append(" return x + 1; } ");
        for (var level = 1; level <= Depth; level++)
        {
            members.Append("public static int L").Append(level).Append("(int x) { return L").Append(level - 1)
                .Append("(L").Append(level - 1).Append("(x)); } ");
        }
        return members.Append("public static int Big(int x) { ").Append(requires).Append(" return L").Append(Depth).Append("(x); }").ToString();
    }

    private static readonly string Pure = Chain("");
    private static readonly string Writes = "public static int Counter; " + Chain("Counter = x;");
    private static readonly string Throws = Chain("if (x == 7) { throw new InvalidOperationException(); }");
    private static readonly string Required = Chain("", "Contract.Requires(x > 0);");

    [Test]
    public async Task ACallerClaimThatDoesNotReadTheCallIsProven()
    {
        var result = await ResultAsync(Pure + " public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x); Big(x); return x; }");
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // The unknown call's result is unknown, even when the body would fix it.
    [Test]
    public async Task TheCallResultIsNotProven()
    {
        var result = await ResultAsync(Pure + " public static int M(int x) { Contract.Requires(x >= 0 && x < 1000); Contract.Ensures(Contract.Result<int>() == x + 1024); return Big(x); }");
        Assert.That(result.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [Test]
    public async Task AWritingCalleeIsNotProvenPure()
    {
        var result = await ResultAsync(Writes + " [EnforcePure] public static int M(int x) { return Big(x); }");
        Assert.That(result.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AThrowingCalleeIsNotProvenThrowFree(bool guarded)
    {
        var call = guarded ? "if (x != 7) { Big(x); }" : "Big(x);";
        var result = await ResultAsync(Throws + " [DoesNotThrow] public static int M(int x) { " + call + " return 0; }");
        Assert.That(result.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [Test]
    public async Task AViolatedCalleePreconditionIsStillRefuted()
    {
        var result = await CallPreconditionAsync("-1");
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
    }

    [Test]
    public async Task ASatisfiedCalleePreconditionIsProven()
    {
        var result = await CallPreconditionAsync("1");
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    private static async Task<WorkerClaimResult> CallPreconditionAsync(string argument)
    {
        System.ArgumentNullException.ThrowIfNull(argument);
        var response = await VerifyAsync(Required + " public static int M() { return Big(" + argument + "); }");
        var claims = response.Manifest.Claims.Where(static claim => claim.Kind == WorkerClaimKind.CallPrecondition &&
            claim.CallableId.Contains("C.M", StringComparison.Ordinal)).ToArray();
        Assert.That(claims, Has.Length.EqualTo(1));
        return response.ClaimResults.Single(result => result.ClaimId == claims[0].ClaimId);
    }

    private static async Task<WorkerVerifyResponse> VerifyAsync(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        using var project = new ShadowTestProject(Types + "public static class C { " + members + " }\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        return response;
    }

    private static async Task<WorkerClaimResult> ResultAsync(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        var response = await VerifyAsync(members);
        var claim = response.Manifest.Claims.Single(static claim => claim.CallableId.Contains("C.M", StringComparison.Ordinal) &&
            claim.Kind is WorkerClaimKind.Postcondition or WorkerClaimKind.Effect);
        return response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId);
    }
}
