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
            public Span Plus(Span other) => this + other;
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
            private readonly long extra = 7;
            public Chained(long ticks) { this.ticks = ticks; }
            public Chained(int ticks) : this((long)ticks) { }
            public Chained(int a, int b) : this(a + b) { extra = extra + 1; }
            public Chained(int a, int b, int c) : this(a, b + c) { }
            public long Ticks => ticks;
            public long Extra => extra;
        }
        public readonly struct Zeroed
        {
            private readonly long ticks;
            public Zeroed(long ticks, bool keep) : this() { if (keep) { this.ticks = ticks; } }
            public long Ticks => ticks;
        }
        public sealed class Box
        {
            private readonly long v;
            private readonly long w = 3;
            public Box(long v) { this.v = v; }
            public Box(int v) : this(v * 2L) { w = w + v; }
            public long V => v;
            public long W => w;
        }
        public readonly struct Initialized
        {
            static Initialized() { C.State = 1; }
            public Initialized(long ticks) { Ticks = ticks; }
            public long Ticks { get; }
        }
        public sealed class Bumped
        {
            private readonly long w = Bump();
            public Bumped(long v) { V = v; }
            public Bumped(int v) : this((long)v) { }
            public long V { get; }
            private static long Bump() { C.State = 1; return 3; }
        }
        public readonly struct Limits
        {
            private readonly Span low;
            private readonly long high;
            private Limits(Span low, long high) { this.low = low; this.high = high; }
            public static Limits Wide { get; } = new Limits(new Span(-5), 9);
            public static readonly Limits Narrow = new Limits(Wide.low, 2);
            public static readonly long Edge = 40L * 3;
            public static readonly bool Flag = Edge > 100;
            public Span Low => low;
            public long High => high;
        }
        public static class Counter
        {
            public static long Shared = 3;
        }
        public readonly struct Late
        {
            public static readonly Late First = Make();
            private readonly long v;
            private Late(long v) { this.v = v; }
            public long V => v;
            public static Late Make() => new Late(First.v + 1);
        }
        public readonly struct Loud
        {
            static Loud() { C.State = 1; }
            public static Loud Four { get; } = new Loud(4);
            private Loud(long ticks) { Ticks = ticks; }
            public long Ticks { get; }
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
    // A target-typed `new(...)` builds the same value as `new Span(...)`.
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x); Span s = new(x); return s.Ticks; }")]
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 0); Span s = new(); return s.Ticks; }")]
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x + x); return Make(x).Plus(Make(x)).Ticks; } " +
        "private static Span Make(long x) => new(x);")]
    // `: this(...)` runs the other constructor on the same object: its field
    // initializers once, then its body, then the rest of this one.
    [TestCase("[EnforcePure] public static long M(int x) { return new Chained(x).Ticks; }")]
    [TestCase("[EnforcePure] public static long M(long x) { return new Zeroed(x, true).Ticks; }")]
    [TestCase("[EnforcePure] public static long M(int x) { return new Box(x).V; }")]
    // Field initializers write the object before anything has seen it.
    [TestCase("[EnforcePure] public static long M(long x) { return new Chained(x).Ticks; }")]
    [TestCase("[EnforcePure] public static long M(long x) { return new Box(x).V; }")]
    [TestCase("public static long M(int x) { Contract.Ensures(Contract.Result<long>() == x); return new Chained(x).Ticks; }")]
    [TestCase("public static long M(int x) { Contract.Ensures(Contract.Result<long>() == 7); return new Chained(x).Extra; }")]
    [TestCase("public static long M(int a, int b) { Contract.Requires(a >= 0 && a < 1000 && b >= 0 && b < 1000); " +
        "Contract.Ensures(Contract.Result<long>() == a + b + 8); var c = new Chained(a, b); return c.Ticks + c.Extra; }")]
    [TestCase("public static long M(int a, int b, int c) { Contract.Requires(a >= 0 && a < 1000 && b >= 0 && b < 1000 && c >= 0 && c < 1000); " +
        "Contract.Ensures(Contract.Result<long>() == a + b + c + 8); var v = new Chained(a, b, c); return v.Ticks + v.Extra; }")]
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == 0); return new Zeroed(x, false).Ticks; }")]
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x); return new Zeroed(x, true).Ticks; }")]
    [TestCase("public static long M(int x) { Contract.Ensures(Contract.Result<long>() == 2L * x + 3 + x); var b = new Box(x); return b.V + b.W; }")]
    // A readonly static, or a static get-only auto-property, holds the value
    // its type's initialization left, computed at analysis time.
    [TestCase("[EnforcePure] public static long M() { return Limits.Wide.High; }")]
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 4); return Limits.Wide.Low.Ticks + Limits.Wide.High; }")]
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == -3); return Limits.Narrow.Low.Ticks + Limits.Narrow.High; }")]
    [TestCase("public static bool M() { Contract.Ensures(Contract.Result<bool>()); return Limits.Edge == 120 && Limits.Flag; }")]
    [TestCase("public static bool M(long x) { Contract.Ensures(Contract.Result<bool>() == (x == -5)); return Limits.Wide.Low == new Span(x); }")]
    // Code the initialization did not run sees the finished value.
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 2); return Late.Make().V; }")]
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
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x + 1); Span s = new(x); return s.Ticks; }")]
    [TestCase("public static long M(int a, int b) { Contract.Ensures(Contract.Result<long>() == 7); return new Chained(a, b).Extra; }")]
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x); return new Zeroed(x, false).Ticks; }")]
    [TestCase("public static long M(int x) { Contract.Ensures(Contract.Result<long>() == 3); return new Box(x).W; }")]
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 8); return Limits.Wide.High; }")]
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 3); return Counter.Shared; }")]
    // Boxing a struct, even default(S), yields an object.
    [TestCase("public static object M() { Contract.Ensures(Contract.Result<object>() == null); return (object)default(Span); }")]
    [TestCase("public static object M(Span s) { Contract.Ensures(Contract.Result<object>() == null); return s; }")]
    [TestCase("public static long M(Span s) { Contract.Ensures(Contract.Result<long>() == 0); return s.Ticks; }")]
    public async Task WrongPostconditionsAreNotProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    // L's initialization runs M while First is still default(L), so M
    // returns 1 then and 2 afterwards. The contract is in a companion, so
    // the initialization can run M.
    [Test]
    public async Task StaticsAreNotValuedForCodeTheirInitializationRuns()
    {
        var result = await ResultAsync("public static long M() => L.First.V + 1;",
            "public readonly struct L { public static readonly L First = new L(C.M()); private readonly long v; " +
            "public L(long v) { this.v = v; } public long V => v; } " +
            "[ContractFor(typeof(C))] public static class Companion { " +
            "public static long M() { Contract.Ensures(Contract.Result<long>() == 2); return 0; } }");
        Assert.That(result.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    // The same claim holds for code the initialization does not run.
    [Test]
    public async Task StaticsAreValuedForCodeTheirInitializationDoesNotRun()
    {
        var result = await ResultAsync("public static long M() => L.First.V + 1;",
            "public readonly struct L { public static readonly L First = new L(Seed()); private readonly long v; " +
            "public L(long v) { this.v = v; } public long V => v; private static long Seed() => 1; } " +
            "[ContractFor(typeof(C))] public static class Companion { " +
            "public static long M() { Contract.Ensures(Contract.Result<long>() == 2); return 0; } }");
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // The CLR does not heap-allocate a struct value, but the model does, so
    // a claim that counts allocation can neither be proved nor refuted.
    [TestCase("[ZeroAllocations] public static long M(long x) { return new Span(x).Ticks; }")]
    [TestCase("[EffectContract(SharpProofEffect.None)] public static long M(long x) { return new Span(x).Ticks; }")]
    [TestCase("[ZeroAllocations] public static long M() { return Limits.Wide.High; }")]
    public async Task AllocationSensitiveClaimsStayUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    // Modeling struct values does not hide a real effect. Calling an
    // instance member of a struct runs its static constructor, so a getter
    // on default(S) or on a parameter has that constructor's effects.
    [TestCase("[EnforcePure] public static long M(long x) { return new Noisy(x).Ticks; }")]
    [TestCase("[EnforcePure] public static long M(long x) { State = 1; return new Span(x).Ticks; }")]
    [TestCase("[EnforcePure] public static long M() { return default(Initialized).Ticks; }")]
    [TestCase("[EnforcePure] public static long M(Initialized value) { return value.Ticks; }")]
    [TestCase("[EnforcePure] public static long M(long x) { return new Initialized(x).Ticks; }")]
    [TestCase("[EnforcePure] public static long M() { return Loud.Four.Ticks; }")]
    // An initializer's own effects stay effects, through a chain too.
    [TestCase("[EnforcePure] public static long M(long x) { return new Bumped(x).V; }")]
    [TestCase("[EnforcePure] public static long M(int x) { return new Bumped(x).V; }")]
    public async Task RealEffectsAreNotProvenPure(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    // A struct store into an array this body allocated is not a write the
    // caller can see, even when the array is returned; a store into the
    // caller's array is.
    [TestCase("[EnforcePure] public static Span[] M(long x) { var a = new Span[2]; a[0] = new Span(x); return a; }", false)]
    [TestCase("[EnforcePure] public static Limits[] M() { var a = new Limits[2]; for (int i = 0; i < a.Length; i++) { a[i] = Limits.Wide; } return a; }", false)]
    [TestCase("[EnforcePure] public static void M(Span[] a, long x) { a[0] = new Span(x); }", true)]
    public async Task ElementStoresAreRefutedOnlyForTheCallersArrays(string member, bool refuted)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var outcome = (await ResultAsync(member)).Outcome;
        Assert.That(outcome == WorkerClaimOutcome.Refuted, Is.EqualTo(refuted), outcome.ToString());
    }

    // A mutable struct is not modeled.
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x); return new Mutable(x).Ticks; }")]
    public async Task UnmodeledStructsStayUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    private static async Task<WorkerClaimResult> ResultAsync(string member, string outside = "")
    {
        System.ArgumentNullException.ThrowIfNull(member);
        System.ArgumentNullException.ThrowIfNull(outside);
        using var project = new ShadowTestProject(Types + "\npublic static class C { public static int State; " + member + " }\n" + outside + "\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var claim = response.Manifest.Claims.Single(static claim => claim.CallableId.Contains("C.M", StringComparison.Ordinal) &&
            claim.Kind is WorkerClaimKind.Postcondition or WorkerClaimKind.Effect);
        return response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId);
    }
}
