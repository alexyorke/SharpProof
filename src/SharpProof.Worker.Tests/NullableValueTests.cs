using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// T? over a scalar or a modeled readonly struct is a reference to an
// immutable object holding the value; null is "no value". HasValue, Value,
// GetValueOrDefault, lifted operators, null tests and patterns are modeled.
// Reading the value of an empty T? builds an InvalidOperationException
// through runtime resources, so that path keeps unknown effects.
[TestFixture]
public sealed class NullableValueTests
{
    private const string Types = """
        using SharpProof.Attributes;
        public readonly struct Span
        {
            private readonly long ticks;
            public Span(long ticks) { this.ticks = ticks; }
            public long Ticks => ticks;
        }
        public readonly struct Tagged
        {
            private readonly int tag;
            public Tagged(int tag) { this.tag = tag; }
            public int Tag => tag;
            public static implicit operator Tagged?(string? text) => new Tagged(5);
        }
        """;

    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x); int? n = x; return n.Value; }")]
    [TestCase("public static bool M(int? n) { Contract.Ensures(Contract.Result<bool>() == n.HasValue); return n != null; }")]
    [TestCase("public static int M(int? n) { Contract.Ensures(Contract.Result<int>() == (n.HasValue ? n.Value : 3)); return n ?? 3; }")]
    [TestCase("public static int M(int? n) { Contract.Ensures(Contract.Result<int>() == n.GetValueOrDefault()); return n is int v ? v : 0; }")]
    [TestCase("public static int? M(int? a, int? b) { Contract.Ensures(Contract.Result<int?>().HasValue == (a.HasValue && b.HasValue)); return a + b; }")]
    [TestCase("public static int? M(int? a, int? b) { Contract.Ensures(Contract.Result<int?>() == a + b); return a + b; }")]
    [TestCase("public static bool M(int? a, int b) { Contract.Ensures(Contract.Result<bool>() == (a.HasValue && a.Value < b)); return a < b; }")]
    [TestCase("public static bool M() { Contract.Ensures(Contract.Result<bool>()); int? a = null; int? b = null; return a == b; }")]
    [TestCase("public static bool M() { Contract.Ensures(!Contract.Result<bool>()); int? a = null; int? b = null; return a <= b; }")]
    [TestCase("public static long M(Span? s) { Contract.Ensures(Contract.Result<long>() == (s.HasValue ? s.Value.Ticks : -1)); return s.HasValue ? s.Value.Ticks : -1; }")]
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x); Span? s = new Span(x); return s.Value.Ticks; }")]
    [TestCase("public static int M(int? n) { Contract.Ensures(Contract.Result<int>() == n.GetValueOrDefault(4)); return n ?? 4; }")]
    [TestCase("public static int M(int? n) { Contract.Ensures(!n.HasValue || Contract.Result<int>() == (int)n); return n.GetValueOrDefault(); }")]
    [TestCase("public static long? M(int? n) { Contract.Ensures(Contract.Result<long?>() == n); return n; }")]
    [TestCase("[EnforcePure] public static int M(int? n) { return n ?? 0; }")]
    [TestCase("[EnforcePure] public static int M(int x) { int? n = x; return n.Value; }")]
    [TestCase("[EnforcePure] public static int M(int? n) { return n.HasValue ? n.Value : 0; }")]
    [TestCase("[DoesNotThrow] public static int M(int? n) { return n.GetValueOrDefault(); }")]
    [TestCase("[ZeroAllocations] public static int M(int? n) { return n ?? 0; }")]
    [TestCase("public static bool M(bool? a) { Contract.Ensures(Contract.Result<bool>()); return (a & false) == false; }")]
    [TestCase("public static bool M(bool? a) { Contract.Ensures(Contract.Result<bool>()); return (a | true) == true; }")]
    [TestCase("public static int M(int? n) { Contract.Ensures(Contract.Result<int>() == (n.HasValue ? n.Value : -1)); n++; return n ?? -1; }")]
    [TestCase("public static bool M(int? n) { Contract.Ensures(Contract.Result<bool>() == (n.HasValue && n.Value == 3)); return n.Equals(3); }")]
    [TestCase("public static int M(int? n) { Contract.Ensures(Contract.Result<int>() == (n.HasValue ? n.Value : 0)); return n.GetHashCode(); }")]
    [TestCase("public static int M(long? n) { Contract.Ensures(Contract.Result<int>() == (n.HasValue ? (int)n.Value : -1)); return ((int?)n) ?? -1; }")]
    public async Task NullableClaimsAreProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // A counterexample with a nullable input replays through the model.
    [TestCase("public static int M(int? n) { Contract.Ensures(Contract.Result<int>() == 0); return n ?? 0; }")]
    [TestCase("public static bool M(int? a, int? b) { Contract.Ensures(Contract.Result<bool>()); return a == b; }")]
    [TestCase("public static int? M(int? a, int? b) { Contract.Ensures(Contract.Result<int?>().HasValue); return a + b; }")]
    // A constant pattern over int? is modeled, so the faulting arm is reached at x == 3.
    [TestCase("[DoesNotThrow] public static int M(int x) { int? n = x; return n is 3 ? 10 / (x - x) : 0; }")]
    public async Task WrongNullableClaimsAreRefuted(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
    }

    // A possibly empty Value read is neither pure (its message lookup) nor
    // throw-free; the throw passes the unknown lookup, so no replayable
    // counterexample refutes DoesNotThrow.
    [TestCase("[EnforcePure] public static int M(int? n) { return n.Value; }")]
    [TestCase("[EnforcePure] public static int M(int? n) { return (int)n; }")]
    [TestCase("[DoesNotThrow] public static int M(int? n) { return n.Value; }")]
    // Boxing for Equals allocates; formatting reads the current culture.
    [TestCase("[ZeroAllocations] public static bool M(int? n) { return n.Equals(3); }")]
    [TestCase("[EnforcePure] public static string M(int? n) { return n.ToString(); }")]
    // A user-defined conversion from null runs and returns a value.
    [TestCase("public static bool M() { Contract.Ensures(!Contract.Result<bool>()); Tagged? t = (string?)null; return t.HasValue; }")]
    public async Task AnEmptyValueReadIsNotProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    // Wrapping a value allocates in the model and not on the CLR, so a claim
    // that counts allocation can neither be proved nor refuted.
    [TestCase("[ZeroAllocations] public static int M(int x) { int? n = x; return n.Value; }")]
    [TestCase("[EffectContract(SharpProofEffect.None)] public static int M(int x) { int? n = x; return n.Value; }")]
    public async Task AllocationSensitiveClaimsStayUnknown(string member)
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
