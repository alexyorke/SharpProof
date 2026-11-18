using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A claimed method's own ref or out parameter of a scalar or reference type
// is the caller's storage. A store to it is an argument write that forgets
// memory and every other by-reference parameter, since the caller may pass a
// field, an element, or one variable twice. The parameter keeps its own
// stored value until another write.
[TestFixture]
public sealed class RootReferenceParameterTests
{
    private const string Types = """
        using SharpProof.Attributes;
        public static class S { public static int F; }
        """;

    [TestCase("[DoesNotThrow] public static bool M(int x, out int half) { if (x % 2 != 0) { half = 0; return false; } half = x / 2; return true; }")]
    [TestCase("[DoesNotThrow] public static void M(ref int x) { x = x + 1; }")]
    [TestCase("[DoesNotThrow] public static void M(ref int x) { x++; x += 2; }")]
    [TestCase("[ZeroAllocations] public static void M(ref int a, ref int b) { var t = a; a = b; b = t; }")]
    [TestCase("[DoesNotThrow] public static void M(ref string s) { s = null; }")]
    [TestCase("[DoesNotThrow] public static int M(int x, out bool even) { even = x % 2 == 0; return x; }")]
    [TestCase("[DoesNotThrow] public static bool M(string[] a, out string v) { if (a != null && a.Length > 0) { v = a[0]; return true; } v = null; return false; }")]
    // The parameter keeps its own store, even if it is an element of the array.
    [TestCase("[DoesNotThrow] public static int M(ref int x, int[] a) { x = 0; return a != null && a.Length > 0 ? a[x] : 0; }")]
    public async Task ReferenceParametersAreModeled(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // M(ref v, ref v, a) makes x five; M(ref S.F, a) does the same through a
    // static store; a store through the parameter writes the caller's state.
    [TestCase("[DoesNotThrow] public static int M(ref int x, ref int y, int[] a) { x = 0; y = 5; return a != null && a.Length == 1 ? a[x] : 0; }")]
    [TestCase("[DoesNotThrow] public static int M(ref int x, int[] a) { x = 0; S.F = 5; return a != null && a.Length == 1 ? a[x] : 0; }")]
    [TestCase("[DoesNotThrow] public static int M(ref int x, int[] a) { x = 0; if (a != null && a.Length == 1) { a[0] = 5; return a[x]; } return 0; }")]
    [TestCase("public static int M(ref int a, ref int b) { Contract.Ensures(Contract.Result<int>() == 1); a = 1; b = 2; return a; }")]
    [TestCase("[EnforcePure] public static void M(out int x) { x = 7; }")]
    [TestCase("[EnforcePure] public static bool M(int x, out int half) { half = x / 2; return x % 2 == 0; }")]
    public async Task CallerVisibleStoresAreNotProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [TestCase("[DoesNotThrow] public static int M(int a, out int q) { q = 10 / a; return q; }")]
    [TestCase("[DoesNotThrow] public static void M(ref int[] a) { a = new int[1]; a[1] = 0; }")]
    public async Task FaultsAfterReferenceStoresAreRefuted(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
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
