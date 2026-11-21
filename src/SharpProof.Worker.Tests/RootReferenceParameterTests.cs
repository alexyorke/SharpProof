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
        public static class S
        {
            public static int F;
            public static void Bump(ref int x) { x = x + 1; }
            public static int Split(ref int a, ref int b) { a = 0; b = 1; return 1 / (a - 1); }
            public static bool TryHalf(int x, out int half) { half = x / 2; return x % 2 == 0; }
            public static void Set(ref int x, int value) { x = value; }
            public static void ZeroThenStore(ref int x) { x = 0; F = 5; }
            public static void Twice(ref int x) { Bump(ref x); Bump(ref x); }
            public static void Through(ref int x) { ZeroThenStore(ref x); }
            public static int Mapped(in int number, in int divisor, out int remainder) { remainder = number % divisor; return number / divisor; }
            public static int Read(in int x) { return x; }
            public static int StoreThenIndex(in int x, int[] a) { if (x != 0 || a == null || a.Length != 1) { return 0; } F = 5; return a[x]; }
        }
        """;

    [TestCase("[DoesNotThrow] public static bool M(int x, out int half) { if (x % 2 != 0) { half = 0; return false; } half = x / 2; return true; }")]
    [TestCase("[DoesNotThrow] public static void M(ref int x) { x = x + 1; }")]
    [TestCase("[DoesNotThrow] public static void M(ref int x) { x++; x += 2; }")]
    [TestCase("[ZeroAllocations] public static void M(ref int a, ref int b) { var t = a; a = b; b = t; }")]
    [TestCase("[DoesNotThrow] public static void M(ref string s) { s = null; }")]
    [TestCase("[DoesNotThrow] public static int M(int x, out bool even) { even = x % 2 == 0; return x; }")]
    [TestCase("[DoesNotThrow] public static bool M(string[] a, out string v) { if (a != null && a.Length > 0) { v = a[0]; return true; } v = null; return false; }")]
    // A type-parameter value is opaque but can be stored and returned.
    [TestCase("[DoesNotThrow] public static bool M<T>(T x, bool ok, out T v) where T : class { if (ok) { v = x; return true; } v = null; return false; }")]
    [TestCase("[DoesNotThrow] public static bool M<T>(T x, out T v) { v = x; return true; }")]
    [TestCase("[ZeroAllocations] public static void M<T>(ref T a, ref T b) { var t = a; a = b; b = t; }")]
    // The parameter keeps its own store, even if it is an element of the array.
    [TestCase("[DoesNotThrow] public static int M(ref int x, int[] a) { x = 0; return a != null && a.Length > 0 ? a[x] : 0; }")]
    // A source callee shares the parameter itself: its stores are the
    // root's stores.
    [TestCase("[DoesNotThrow] public static void M(ref int x) { S.Bump(ref x); }")]
    [TestCase("[DoesNotThrow] public static bool M(int x, out int half) { return S.TryHalf(x, out half); }")]
    [TestCase("[DoesNotThrow] public static int M(ref int x, int[] a) { S.Set(ref x, 0); return a != null && a.Length > 0 ? a[x] : 0; }")]
    [TestCase("[DoesNotThrow] public static int M(in int x) { return S.Read(x); }")]
    [TestCase("[DoesNotThrow] public static void M(ref int x) { S.Twice(ref x); }")]
    [TestCase("[DoesNotThrow] public static int M(in int number, out int remainder) { return S.Mapped(number, 100, out remainder); }")]
    [TestCase("[ZeroAllocations] public static void M(ref int a, ref int b) { S.Set(ref a, 1); S.Set(ref b, 2); }")]
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
    [TestCase("[EnforcePure] public static void M<T>(out T x) { x = default; }")]
    // A root's by-reference parameter is not forwarded to a copy-backed
    // callee: M(ref v, ref v) makes a and b one variable, and a is 1.
    [TestCase("[DoesNotThrow] public static int M(ref int x, ref int y) { return S.Split(ref x, ref y); }")]
    [TestCase("[EnforcePure] public static void M(ref int x) { S.Bump(ref x); }")]
    [TestCase("[EnforcePure] public static bool M(int x, out int half) { return S.TryHalf(x, out half); }")]
    // The same aliases through a callee that shares the parameter:
    // M(ref v, ref v, a), M(ref S.F, a) and M(in S.F, a) index a[5].
    [TestCase("[DoesNotThrow] public static int M(ref int x, ref int y, int[] a) { S.Set(ref x, 0); S.Set(ref y, 5); return a != null && a.Length == 1 ? a[x] : 0; }")]
    [TestCase("[DoesNotThrow] public static int M(ref int x, int[] a) { S.ZeroThenStore(ref x); return a != null && a.Length == 1 ? a[x] : 0; }")]
    [TestCase("[DoesNotThrow] public static int M(in int x, int[] a) { return S.StoreThenIndex(x, a); }")]
    // A callee that shares the parameter shares it again with its own callee.
    [TestCase("[DoesNotThrow] public static int M(ref int x, int[] a) { S.Through(ref x); return a != null && a.Length == 1 ? a[x] : 0; }")]
    [TestCase("[EnforcePure] public static void M(ref int x) { S.Twice(ref x); }")]
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

    // A postcondition on a method with an out parameter still needs a body
    // that touches no memory: the out argument may be the field it reads.
    [TestCase("public static int M(out int x) { Contract.Ensures(Contract.Result<int>() == 0); x = 0; return S.F * 0; }")]
    public async Task OutParameterClausesKeepTheMemoryFreeRule(string member)
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
