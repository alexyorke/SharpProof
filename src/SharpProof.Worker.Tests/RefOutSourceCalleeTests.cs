using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A source callee with ref or out parameters is inlined when each such
// argument is a local or parameter of the caller passed once, outside any
// try region of the caller: the variable takes the callee's last assignment
// on normal return, and an exception that leaves the caller ends it.
[TestFixture]
public sealed class RefOutSourceCalleeTests
{
    private const string Types = """
        using SharpProof.Attributes;
        public static class T
        {
            public static int Counter;
            public static bool TryHalf(int x, out int half)
            {
                if (x % 2 != 0) { half = 0; return false; }
                half = x / 2;
                return true;
            }
            public static void Bump(ref int x) { x = x + 1; }
            public static void Twice(ref int x) { Bump(ref x); Bump(ref x); }
            public static void Swap(ref int a, ref int b) { var t = a; a = b; b = t; }
            public static void Noisy(ref int x) { Counter = 1; x = 1; }
            public static void Keep(ref int x) { }
            public static void Set(out int x) { x = 7; }
            public static void SetAndCount(ref int x) { x = 7; Counter = 1; }
            public static void Keep(ref int value, int unused) { }
        }
        """;

    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x + 1); var y = x; T.Bump(ref y); return y; }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x + 2); var y = x; T.Twice(ref y); return y; }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == Contract.Old(x) + 1); T.Bump(ref x); return x; }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == (x % 2 == 0 ? x / 2 : -1)); return T.TryHalf(x, out var h) ? h : -1; }")]
    [TestCase("public static bool M(int x) { Contract.Ensures(Contract.Result<bool>() == (x % 2 == 0)); return T.TryHalf(x, out _); }")]
    [TestCase("public static int M(int x, int y) { Contract.Ensures(Contract.Result<int>() == y - x); var a = x; var b = y; T.Swap(ref a, ref b); return a - b; }")]
    [TestCase("[EnforcePure] public static int M(int x) { var y = x; T.Bump(ref y); return y; }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x); var y = x; T.Keep(ref y); return y; }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == 7); T.Set(out var y); return y; }")]
    // The callee reads a ref argument when it is entered, after later
    // arguments have run.
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x); var y = 1; T.Keep(ref y, y = x); return y; }")]
    // A heap write in the callee does not forget a copy-backed ref parameter.
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 7); var y = 0; T.SetAndCount(ref y); return y; }")]
    public async Task ReferenceArgumentsAreModeled(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x); var y = x; T.Bump(ref y); return y; }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x / 2); return T.TryHalf(x, out var h) ? h : -1; }")]
    [TestCase("[EnforcePure] public static int M(int x) { var y = x; T.Noisy(ref y); return y; }")]
    // A postcondition reads a parameter's value at exit, which the call changed.
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x + 1); T.Bump(ref x); return x; }")]
    public async Task WrongClaimsAreNotProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    // A field passed by reference, a variable passed twice, and a call inside
    // a try region whose handler sees the variable stay unmodeled.
    [TestCase("public static int F; public static int M() { Contract.Ensures(Contract.Result<int>() == 1); F = 0; T.Bump(ref F); return F; }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x); var y = x; T.Swap(ref y, ref y); return y; }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x + 1); var y = x; try { T.Bump(ref y); } catch (System.Exception) { } return y; }")]
    public async Task UnadmittedReferenceArgumentsStayUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    private static async Task<WorkerClaimResult> ResultAsync(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        using var project = new ShadowTestProject(Types + "\npublic static class C { " + member + " }\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var claim = response.Manifest.Claims.Single(static claim => claim.CallableId.Contains("C.M", StringComparison.Ordinal) &&
            claim.Kind is WorkerClaimKind.Postcondition or WorkerClaimKind.Effect);
        return response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId);
    }
}
