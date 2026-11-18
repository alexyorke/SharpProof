using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// An allocation records its object's exact runtime type and a box its value,
// so type tests, unboxing, `as` and downcasts on objects the program creates
// are exact. An object the program did not create keeps an unknown runtime
// type: tests on it are never refuted from a guessed type.
[TestFixture]
public sealed class RuntimeTypeTests
{
    private const string Types = """
        using SharpProof.Attributes;
        public readonly struct Span
        {
            private readonly long ticks;
            public Span(long ticks) { this.ticks = ticks; }
            public long Ticks => ticks;
            public override bool Equals(object? obj) => obj is Span other && other.ticks == ticks;
            public override int GetHashCode() => 0;
        }
        """;

    [TestCase("public static bool M(int x) { Contract.Ensures(Contract.Result<bool>()); object o = x; return o is int; }")]
    [TestCase("public static bool M(int x) { Contract.Ensures(!Contract.Result<bool>()); object o = x; return o is long; }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x); object o = x; return (int)o; }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x); object o = x; return o is int i ? i : 0; }")]
    [TestCase("public static bool M() { Contract.Ensures(Contract.Result<bool>()); object e = new System.ArgumentException(\"m\"); return e is System.Exception; }")]
    [TestCase("public static bool M() { Contract.Ensures(!Contract.Result<bool>()); object e = new System.Exception(\"m\"); return e is System.ArgumentException; }")]
    [TestCase("public static bool M(long x) { Contract.Ensures(Contract.Result<bool>()); return new Span(x).Equals(new Span(x)); }")]
    [TestCase("[DoesNotThrow] public static int M(int x) { object o = x; return (int)o; }")]
    [TestCase("[EnforcePure] public static int M(int x) { return (int)(object)x; }")]
    // Purity does not depend on a type test's answer, so it holds for any object.
    [TestCase("[EnforcePure] public static bool M(object o) { return o is int i && i > 0; }")]
    [TestCase("[EnforcePure] public static bool M(Span s, object o) { return s.Equals(o); }")]
    public async Task RuntimeTypeClaimsAreProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // A counterexample over an object the program created replays.
    [TestCase("[DoesNotThrow] public static long M(int x) { object o = x; return (long)o; }")]
    // The box holds x, so the faulting arm is reached at x == 0.
    [TestCase("[DoesNotThrow] public static int M(int x) { object o = x; return o is int k ? 10 / k : 0; }")]
    [TestCase("public static bool M(int x) { Contract.Ensures(Contract.Result<bool>()); object o = x; return o is long; }")]
    [TestCase("[DoesNotThrow] public static int M() { object e = new System.Exception(\"m\"); return ((System.ArgumentException)e).HResult; }")]
    public async Task WrongRuntimeTypeClaimsAreRefuted(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
    }

    // An object the program did not create: neither proven nor refuted from
    // a guessed runtime type.
    [TestCase("public static bool M(object o) { Contract.Ensures(!Contract.Result<bool>()); return o is int; }")]
    [TestCase("public static bool M(object o) { Contract.Ensures(Contract.Result<bool>()); return o == null || o is int; }")]
    [TestCase("[DoesNotThrow] public static int M(object o) { return (int)o; }")]
    public async Task UnknownObjectsStayUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    // A pattern that declares a string or array variable from an object
    // stays unmodeled, as `as` does, and leaves the rest of the run intact.
    [TestCase("[DoesNotThrow] public static int M(object o) { return o is string s ? s.Length : 0; }")]
    [TestCase("[DoesNotThrow] public static int M(object o) { return o is object[] a ? a.Length : 0; }")]
    [TestCase("[DoesNotThrow] public static int M(object o) { if (!(o is int[] a)) { return 0; } return a.Length; }")]
    // A checked call in the same body does not bring the failure back.
    [TestCase("public static int Positive(int x) { Contract.Requires(x >= 0); return x; } [DoesNotThrow] public static int M(object o) { return o is string s ? Positive(s.Length) : Positive(1); }")]
    public async Task StringAndArrayPatternVariablesStayUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    // Boxing allocates on the CLR too.
    [Test]
    public async Task BoxingIsAnAllocation()
    {
        Assert.That((await ResultAsync("[ZeroAllocations] public static bool M(int x) { object o = x; return o is int; }")).Outcome,
            Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    private static async Task<WorkerClaimResult> ResultAsync(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        using var project = new ShadowTestProject(Types + "\npublic static class C { " + member + " }\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete), response.FailureReason + " " + string.Join("; ", response.Errors.Select(static error => error.Code + ": " + error.Message)) + " | " +
            string.Join("; ", response.ClaimResults.Select(result => response.Manifest.Claims.Single(claim => claim.ClaimId == result.ClaimId).Kind + "/" + result.Outcome + "/" + result.Reason)));
        var claim = response.Manifest.Claims.Single(static claim => claim.CallableId.Contains("C.M", StringComparison.Ordinal) &&
            claim.Kind is WorkerClaimKind.Postcondition or WorkerClaimKind.Effect);
        return response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId);
    }
}
