using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A source readonly struct value is modeled as a reference to an immutable
// object, with null standing for default(S). Construction, field and
// auto-property reads, instance members and user-defined operators are
// inlined. Building one is an IR allocation the CLR does not make, so
// allocation-sensitive claims stay Unknown for such a body.
[TestFixture]
public sealed class ReadonlyStructValueTests
{
    private const string Types = """
        using SharpProof.Attributes;
        public readonly struct Span
        {
            private readonly long ticks;
            public Span(long ticks) { this.ticks = ticks; }
            public long Ticks => ticks;
            public static Span operator +(Span left, Span right) => new Span(left.ticks + right.ticks);
            public static Span operator -(Span value) => new Span(-value.ticks);
            public static bool operator ==(Span left, Span right) => left.ticks == right.ticks;
            public static bool operator !=(Span left, Span right) => left.ticks != right.ticks;
            public Span Twice() => new Span(ticks * 2);
            public override bool Equals(object? other) => false;
            public override int GetHashCode() => 0;
        }
        public readonly struct Pair
        {
            public Pair(Span first, int second) { First = first; Second = second; }
            public Span First { get; }
            public int Second { get; }
        }
        public sealed class Holder
        {
            private readonly Span value;
            public Holder(Span value) { this.value = value; }
            public Span Value => value;
        }
        public struct Mutable
        {
            public long Ticks;
            public Mutable(long ticks) { Ticks = ticks; }
        }
        public readonly struct Chained
        {
            private readonly long ticks;
            public Chained(long ticks) { this.ticks = ticks; }
            public Chained(int ticks) : this((long)ticks) { }
            public long Ticks => ticks;
        }
        public readonly struct Noisy
        {
            public static int Count;
            private readonly long ticks;
            public Noisy(long ticks) { this.ticks = ticks; Count = 1; }
            public long Ticks => ticks;
        }
        """;

    [TestCase("[EnforcePure] public static long M(long x) { return new Span(x).Ticks; }")]
    [TestCase("[EnforcePure] public static long M(Span s) { return s.Ticks; }")]
    [TestCase("[AllowedCapabilities(SharpProofCapability.None)] public static long M(long x) { return (new Span(x) + new Span(1)).Ticks; }")]
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x); return new Span(x).Ticks; }")]
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x + x); return (new Span(x) + new Span(x)).Ticks; }")]
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == -x); return (-new Span(x)).Ticks; }")]
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == 2 * x); return new Span(x).Twice().Ticks; }")]
    [TestCase("public static bool M(long x) { Contract.Ensures(Contract.Result<bool>()); return new Span(x) == new Span(x); }")]
    [TestCase("public static bool M(long x, long y) { Contract.Ensures(Contract.Result<bool>() == (x != y)); return new Span(x) != new Span(y); }")]
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 0); return default(Span).Ticks; }")]
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 0); return new Span().Ticks; }")]
    [TestCase("public static bool M(long x) { Contract.Ensures(Contract.Result<bool>() == (x == 0)); return new Span(x) == default(Span); }")]
    [TestCase("public static long M(long x, int y) { Contract.Ensures(Contract.Result<long>() == x + y); var p = new Pair(new Span(x), y); return p.First.Ticks + p.Second; }")]
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x); return new Holder(new Span(x)).Value.Ticks; }")]
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x); var a = new Span(x); var b = a; return b.Ticks; }")]
    public async Task ModeledStructClaimsAreProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x + 1); return new Span(x).Ticks; }")]
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x); return (new Span(x) + new Span(x)).Ticks; }")]
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 1); return default(Span).Ticks; }")]
    [TestCase("public static bool M(long x, long y) { Contract.Ensures(Contract.Result<bool>()); return new Span(x) == new Span(y); }")]
    [TestCase("public static long M(Span s) { Contract.Ensures(Contract.Result<long>() == 0); return s.Ticks; }")]
    public async Task WrongPostconditionsAreNotProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    // The CLR does not heap-allocate a struct value, but the model does, so
    // a claim that counts allocation can neither be proved nor refuted.
    [TestCase("[ZeroAllocations] public static long M(long x) { return new Span(x).Ticks; }")]
    [TestCase("[EffectContract(SharpProofEffect.None)] public static long M(long x) { return new Span(x).Ticks; }")]
    public async Task AllocationSensitiveClaimsStayUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    // Modeling struct values does not hide a real effect.
    [TestCase("[EnforcePure] public static long M(long x) { return new Noisy(x).Ticks; }")]
    [TestCase("public static int State; [EnforcePure] public static long M(long x) { State = 1; return new Span(x).Ticks; }")]
    public async Task RealEffectsAreNotProvenPure(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    // A mutable struct, and a constructor that chains to another, are not modeled.
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x); return new Mutable(x).Ticks; }")]
    [TestCase("public static long M(int x) { Contract.Ensures(Contract.Result<long>() == x); return new Chained(x).Ticks; }")]
    public async Task UnmodeledStructsStayUnknown(string member)
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
