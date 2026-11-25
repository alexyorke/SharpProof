using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Calls inside an async body are call-precondition roots like calls in any
// other method: the body runs as SEMANTICS.md, async methods, describes, so a
// call reached before any suspension, or after an await whose effects are
// known, is checked exactly. After an unknown await, memory may have changed.
[TestFixture]
public sealed class AsyncCallPreconditionTests
{
    private const string Types = """
        using System.Threading.Tasks;
        using SharpProof.Attributes;
        public sealed class Box { public int F; }
        public static class Subject
        {
            public static int Parse(string s) { Contract.Requires(s.Length > 0); return s.Length; }
            public static int Positive(int x) { Contract.Requires(x > 0); return x; }
            public static async Task<int> Next(int x) { return x + 1; }
        """;

    [TestCase("public static async Task<int> Caller() { return Parse(\"\"); }", "Refuted")]
    [TestCase("public static async Task<int> Caller(int x) { await Next(x); return Parse(\"\"); }", "Refuted")]
    // After an unknown call, a violation's replay depends on that call
    // returning normally, an approximation: Unknown, as for the synchronous
    // `s.Flush(); return Parse("");`.
    [TestCase("public static async Task<int> Caller(System.IO.Stream s) { await s.FlushAsync().ConfigureAwait(false); return Parse(\"\"); }", "Unknown")]
    [TestCase("public static int Caller(System.IO.Stream s) { s.Flush(); return Parse(\"\"); }", "Unknown")]
    [TestCase("public static async Task<int> Caller(int x) { if (x < 1 || x > 1000) { return 0; } var y = Positive(x); return Positive(await Next(y)); }", "Proven")]
    [TestCase("public static async Task<int> Caller(int x) { var y = await Next(x < 1 || x > 1000 ? 1 : x); return Positive(y); }", "Proven")]
    [TestCase("public static async Task<int> Caller(Box b, Task t) { if (b.F < 1) { return 0; } await t; return Positive(b.F); }", "Unknown")]
    public async Task CallsInAsyncBodiesAreChecked(string caller, string outcome)
    {
        System.ArgumentNullException.ThrowIfNull(caller);
        System.ArgumentNullException.ThrowIfNull(outcome);
        using var project = new ShadowTestProject(Types + caller + "\n}\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var claims = response.Manifest.Claims.Where(static claim => claim.Kind == WorkerClaimKind.CallPrecondition &&
            claim.CallableId.Contains("Subject.Caller", StringComparison.Ordinal)).ToArray();
        Assert.That(claims, Is.Not.Empty);
        var outcomes = claims.Select(claim => response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId).Outcome).ToArray();
        var expected = Enum.Parse<WorkerClaimOutcome>(outcome);
        if (expected == WorkerClaimOutcome.Proven)
        { Assert.That(outcomes, Has.All.EqualTo(WorkerClaimOutcome.Proven)); }
        else
        { Assert.That(outcomes, Does.Contain(expected)); }
        Assert.That(outcomes, Has.None.EqualTo(expected == WorkerClaimOutcome.Unknown ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Unknown));
    }
}
