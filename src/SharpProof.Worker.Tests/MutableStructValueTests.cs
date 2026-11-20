using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A mutable source struct value is modeled as a readonly struct's is: a
// reference to an immutable object, with null for default(S). A store into
// a field or auto-property of a local or by-value parameter, or a mutating
// member called on one, builds a new object and assigns it to that variable
// alone, so a copy made before keeps the old value. A mutation through any
// other place (a field of an object, an element, a ref local, a nested
// struct field) stays Unknown.
[TestFixture]
public sealed class MutableStructValueTests
{
    private const string Types = """
        using SharpProof.Attributes;
        public struct Counter
        {
            public int Count;
            public Counter(int count) { Count = count; Total = 0; }
            public long Total { get; set; }
            public int Value { get => Count; set { Count = value; } }
            public void Add(int x) { Count += x; Total = Total + x; }
            public void Bump() { Count++; }
            public void Twice() { Bump(); this.Bump(); }
            public int Next() { Count++; return Count; }
            public readonly int Peek() => Count;
            public readonly int PeekPlus(int y) => Count + y;
            public readonly int Probe() { var copy = this; copy.Bump(); return Count; }
            public void Reset() { this = default; }
            public void Fail() { Count = 99; throw new System.InvalidOperationException(); }
        }
        public struct Pair { public Counter Left; public int Right; }
        public sealed class Holder { public Counter Field; }
        public interface ITick { void Tick(); }
        public struct Ticker : ITick
        {
            public int N;
            public void Tick() { N++; }
            public override string ToString() { N++; return ""; }
        }
        public struct Settable
        {
            public int F;
            [EnforcePure] public void Set() { F = 1; }
            [EnforcePure] public void Increment() { Bump(); }
            private void Bump() { F++; }
            [EnforcePure] public readonly int Get() => F;
            public readonly int Peek(int ignored) => F;
            public readonly bool ReadsLate(System.Func<int> f)
            {
                Contract.Ensures(Contract.Result<bool>());
                var before = F;
                return before == Peek(f());
            }
            public bool Same(System.Action action)
            {
                Contract.Ensures(Contract.Result<bool>());
                var before = F;
                action();
                return before == F;
            }
        }
        public readonly struct Fixed
        {
            public Fixed(int value) { Value = value; }
            public int Value { get; }
            public int Plus(int y) => Value + y;
            public bool Same(System.Action action)
            {
                Contract.Ensures(Contract.Result<bool>());
                var before = Value;
                action();
                return before == Value;
            }
        }
        public struct Loud
        {
            public static int Hits;
            static Loud() { Hits++; }
            public int P { get; set; }
        }
        public static class Helpers
        {
            public static void Change(Counter c) { c.Count = 9; }
            public static Counter Grow(Counter c) { c.Add(1); return c; }
        }
        """;

    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 1); var a = new Counter(1); var b = a; b.Count = 5; return a.Count; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 5); var a = new Counter(1); var b = a; b.Count = 5; return b.Count; }")]
    [TestCase("public static int M(int x) { Contract.Requires(x > -1000 && x < 1000); Contract.Ensures(Contract.Result<int>() == x + 1); " +
        "var a = new Counter(x); a.Count = a.Count + 1; return a.Count; }")]
    [TestCase("public static int M(int x) { Contract.Requires(x > -1000 && x < 1000); Contract.Ensures(Contract.Result<int>() == x + 1); " +
        "var a = new Counter(x); a.Count++; return a.Count; }")]
    [TestCase("public static int M(int x) { Contract.Requires(x > -1000 && x < 1000); Contract.Ensures(Contract.Result<int>() == x); " +
        "var a = new Counter(x); return a.Count++; }")]
    [TestCase("public static int M(int x) { Contract.Requires(x > -1000 && x < 1000); Contract.Ensures(Contract.Result<int>() == x + 3); " +
        "var a = new Counter(x); a.Count += 3; return a.Count; }")]
    // A mutating member changes the variable it is called on, and no copy.
    [TestCase("public static int M(int x) { Contract.Requires(x > -1000 && x < 1000); Contract.Ensures(Contract.Result<int>() == x + 3); " +
        "var a = new Counter(x); a.Add(3); return a.Count; }")]
    [TestCase("public static long M(int x) { Contract.Requires(x > -1000 && x < 1000); Contract.Ensures(Contract.Result<long>() == x); " +
        "var a = new Counter(0); a.Add(x); return a.Total; }")]
    [TestCase("public static int M(int x) { Contract.Requires(x > -1000 && x < 1000); Contract.Ensures(Contract.Result<int>() == x); " +
        "var a = new Counter(x); var b = a; b.Add(3); return a.Count; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 2); var a = new Counter(0); a.Twice(); return a.Count; }")]
    [TestCase("public static int M(int x) { Contract.Requires(x > -1000 && x < 1000); Contract.Ensures(Contract.Result<int>() == 2 * x + 2); " +
        "var a = new Counter(x); var r = a.Next(); return r + a.Count; }")]
    [TestCase("public static int M(int x) { Contract.Requires(x > -1000 && x < 1000); Contract.Ensures(Contract.Result<int>() == x); " +
        "var a = new Counter(0); a.Value = x; return a.Count; }")]
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x); var a = new Counter(0); a.Total = x; return a.Total; }")]
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 0); Counter a = default; a.Count = 4; return a.Total; }")]
    // A by-value parameter is the callee's own copy.
    [TestCase("public static int M(Counter p) { Contract.Ensures(Contract.Result<int>() == 7); p.Count = 7; return p.Count; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 1); var a = new Counter(1); Helpers.Change(a); return a.Count; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 3); var a = new Counter(1); var b = Helpers.Grow(a); return a.Count + b.Count; }")]
    // A box holds the value boxed, which later stores do not change.
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 1); var a = new Counter(1); object o = a; a.Count = 2; return ((Counter)o).Count; }")]
    // C# evaluates the right-hand side, which changes `a`, before it stores.
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 4); var a = new Counter(1); a.Total = a.Next(); return a.Count + a.Total; }")]
    // A variable is passed by reference: the callee sees what the arguments did to it.
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 4); var a = new Counter(1); return a.PeekPlus(a.Next()); }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 10); var a = new Fixed(1); return a.Plus((a = new Fixed(5)).Value); }")]
    // A readonly member calls a mutating one on a copy of `this`.
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x); return new Counter(x).Probe(); }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x); var a = new Counter(x); return a.Peek(); }")]
    // A mutating call on a call's value changes only that value.
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x); var a = new Counter(x); new Counter(5).Bump(); return a.Count; }")]
    // An override C# calls on the variable itself may change it.
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 1); Ticker t = default; t.ToString(); return t.N; }")]
    // Local mutable struct values change nothing a caller sees.
    [TestCase("[EnforcePure] public static int M(int x) { var a = new Counter(x); a.Add(1); a.Count = 3; a.Total++; return a.Count; }")]
    [TestCase("[EnforcePure] public static int M(Counter p) { p.Bump(); return p.Count; }")]
    [TestCase("[AllowedCapabilities(SharpProofCapability.None)] public static int M(int x) { var a = new Counter(x); a.Twice(); return a.Peek(); }")]
    public async Task CopySemanticsAreProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 5); var a = new Counter(1); var b = a; b.Count = 5; return a.Count; }")]
    [TestCase("public static int M(int x) { Contract.Requires(x > -1000 && x < 1000); Contract.Ensures(Contract.Result<int>() == x); " +
        "var a = new Counter(x); a.Add(3); return a.Count; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 9); var a = new Counter(1); Helpers.Change(a); return a.Count; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 2); var a = new Counter(1); object o = a; a.Count = 2; return ((Counter)o).Count; }")]
    // What reading the receiver before the arguments would give.
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 3); var a = new Counter(1); return a.PeekPlus(a.Next()); }")]
    public async Task WrongPostconditionsAreRefuted(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
    }

    // The CLR changes a box in place when an interface member runs on it,
    // and an override changes the variable it is called on.
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 0); Ticker t = default; ITick i = t; i.Tick(); return ((Ticker)i).N; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 0); Ticker t = default; t.ToString(); return t.N; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 0); Ticker t = default; t.Tick(); return t.N; }")]
    // An auto-property setter runs the struct's static constructor.
    [TestCase("[EnforcePure] public static int M() { Loud s = default; s.P = 1; return s.P; }")]
    public async Task InPlaceMutationsAreNotIgnored(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    // A mutation through a place other than a local, a by-value parameter or
    // a copy-backed `this` is not modeled.
    [TestCase("public static int M(Holder h) { Contract.Requires(h != null); Contract.Ensures(Contract.Result<int>() == 3); h.Field.Count = 3; return h.Field.Count; }")]
    [TestCase("public static int M(Holder h) { Contract.Requires(h != null); Contract.Ensures(Contract.Result<int>() == 1); h.Field = default; h.Field.Bump(); return h.Field.Count; }")]
    [TestCase("public static int M(Counter[] a) { Contract.Requires(a != null && a.Length > 0); Contract.Ensures(Contract.Result<int>() == 3); a[0].Count = 3; return a[0].Count; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 3); var a = new Counter(0); ref var r = ref a; r.Count = 3; return a.Count; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 3); var p = new Pair(); p.Left.Count = 3; return p.Left.Count; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 1); var p = new Pair(); p.Left.Bump(); return p.Left.Count; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 0); var a = new Counter(1); a.Reset(); return a.Count; }")]
    // A mutating call inside a try region is not written back.
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 99); var a = new Counter(1); try { a.Fail(); } catch (System.InvalidOperationException) { } return a.Count; }")]
    // A local assigned field by field has no value to copy the others from.
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 4); Counter a; a.Count = 4; return a.Count; }")]
    // The model allocates each new value, which the CLR does not.
    [TestCase("[ZeroAllocations] public static int M(int x) { Counter a = default; a.Count = x; return a.Count; }")]
    public async Task UnsupportedMutationsStayUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    // `this` of a struct member is the caller's storage: a store into it,
    // or a mutating call on it, is a write the caller sees, and other code
    // may change it.
    [TestCase("Settable.Set")]
    [TestCase("Settable.Increment")]
    [TestCase("Settable.Same")]
    [TestCase("Settable.ReadsLate")]
    [TestCase("Fixed.Same")]
    public async Task ReceiverStorageIsNotProven(string callable)
    {
        System.ArgumentNullException.ThrowIfNull(callable);
        Assert.That((await ResultAsync("", callable: callable)).Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    // A parameter's fields that a store leaves alone are copied from the
    // caller's value.
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 0); Counter a = default; return a.Total; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 1); Ticker t = default; t.N = 1; return t.N; }")]
    [TestCase("public static long M(Counter p) { Contract.Ensures(Contract.Result<long>() == 7); p.Total = 7; return p.Total; }")]
    [TestCase("public static int M(Counter p) { Contract.Ensures(Contract.Result<int>() == 7); var q = p; q.Count = 7; return q.Count; }")]
    [TestCase("public static int M(Counter p) { Contract.Ensures(Contract.Result<int>() == p.Count); return p.Count; }")]
    [TestCase("public static long M(Counter p) { Contract.Ensures(Contract.Result<long>() == Contract.Old(p.Total)); p.Count = 7; return p.Total; }")]
    public async Task ParameterCopiesAreProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [Test]
    public async Task ReadonlyMembersOfMutableStructsAreProvenPure()
    {
        var result = await ResultAsync("", callable: "Settable.Get");
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    private static async Task<WorkerClaimResult> ResultAsync(string member, string callable = "C.M")
    {
        System.ArgumentNullException.ThrowIfNull(member);
        System.ArgumentNullException.ThrowIfNull(callable);
        using var project = new ShadowTestProject(Types + "\npublic static class C { " + member + " }\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete),
            response.FailureReason + ": " + string.Join("; ", response.Errors.Select(static error => error.ToString())));
        var claim = response.Manifest.Claims.Single(claim => claim.CallableId.Contains(callable, StringComparison.Ordinal) &&
            claim.Kind is WorkerClaimKind.Postcondition or WorkerClaimKind.Effect);
        return response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId);
    }
}
