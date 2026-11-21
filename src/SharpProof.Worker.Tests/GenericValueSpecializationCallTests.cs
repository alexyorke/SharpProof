using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A generic source callee whose type parameter is instantiated with a string,
// object, array or struct is inlined with the parameter specialized to that
// type: a reference cast cannot carry such a value to the declaration. A
// value-type T is never null, and a call through a constraint on a mutable
// struct is not taken as leaving it unchanged.
[TestFixture]
public sealed class GenericValueSpecializationCallTests
{
    private const string Types = "using System;\nusing SharpProof.Attributes;\n";

    private const string Helpers = """
        public readonly struct P
        {
            public readonly int V;
            public P(int v) { V = v; }
        }
        public interface IBump { void Bump(); }
        public struct Q : IBump
        {
            public int V;
            public void Bump() { V++; }
        }
        public sealed class Holder<T>
        {
            public static T Echo(T x) { return x; }
        }
        public static class H
        {
            public static T Id<T>(T x) { return x; }
            public static T Def<T>() { return default(T); }
            public static object Box<T>(T x) { return x; }
            public static object BoxDef<T>() { return (object)default(T); }
            public static bool Eq<T>(T a, T b) { return System.Collections.Generic.EqualityComparer<T>.Default.Equals(a, b); }
            public static T Pick<T>(bool b, T x, T y) { return b ? x : y; }
            public static T Fail<T>(T x) { throw new InvalidOperationException(); }
            public static bool IsNull<T>(T x) { return x is null; }
            public static bool NullIn<T>(System.Collections.Generic.List<T> xs, T x) { return x is null; }
            public static bool Same<T>(T a, T b) where T : class { return a == b; }
            public static T Bumped<T>(T x) where T : IBump { x.Bump(); return x; }
        }
        """;

    [TestCase("public static string M(string s) { Contract.Ensures(Contract.Result<string>() == s); return H.Id(s); }")]
    [TestCase("public static object M(object o) { Contract.Ensures(Contract.Result<object>() == o); return H.Id(o); }")]
    [TestCase("public static int[] M(int[] a) { Contract.Ensures(Contract.Result<int[]>() == a); return H.Id(a); }")]
    [TestCase("public static int M(int v) { Contract.Ensures(Contract.Result<int>() == v); return H.Id(new P(v)).V; }")]
    [TestCase("[DoesNotThrow] public static DateTime M(DateTime d) { return H.Id(d); }")]
    [TestCase("public static string M(string a, string b) { Contract.Ensures(Contract.Result<string>() == b); return H.Pick(false, a, b); }")]
    [TestCase("public static string M(string s) { Contract.Ensures(Contract.Result<string>() == s); return Holder<string>.Echo(s); }")]
    [TestCase("public static bool M(string s) { Contract.Ensures(Contract.Result<bool>() == (s == null)); return H.IsNull(s); }")]
    [TestCase("public static bool M() { Contract.Ensures(!Contract.Result<bool>()); return H.IsNull(default(P)); }")]
    [TestCase("public static bool M(DateTime d) { Contract.Ensures(!Contract.Result<bool>()); return H.IsNull(d); }")]
    [TestCase("public static string M() { Contract.Ensures(Contract.Result<string>() == null); return H.Def<string>(); }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 0); return H.Def<P>().V; }")]
    // Converting a value-type T to object boxes it: the box is a new,
    // non-null object even for default(T).
    [TestCase("public static object M() { Contract.Ensures(Contract.Result<object>() != null); return H.BoxDef<P>(); }")]
    [TestCase("public static object M() { Contract.Ensures(Contract.Result<object>() != null); return H.BoxDef<int>(); }")]
    [TestCase("public static object M(P p) { Contract.Ensures(Contract.Result<object>() != null); return H.Box(p); }")]
    [TestCase("public static bool M(P p) { Contract.Ensures(!Contract.Result<bool>()); return H.Box(p) == H.Box(p); }")]
    [TestCase("public static object M() { Contract.Ensures(Contract.Result<object>() == null); return H.BoxDef<string>(); }")]
    public async Task SpecializedCalleesAreProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [TestCase("[DoesNotThrow] public static string M(string s) { return H.Fail(s); }")]
    [TestCase("public static string M(string a, string b) { Contract.Ensures(Contract.Result<string>() == a); return H.Pick(false, a, b); }")]
    [TestCase("public static bool M() { Contract.Ensures(Contract.Result<bool>()); return H.IsNull(default(P)); }")]
    [TestCase("[DoesNotThrow] public static P M(P p) { return H.Fail(p); }")]
    [TestCase("public static object M() { Contract.Ensures(Contract.Result<object>() == null); return H.BoxDef<P>(); }")]
    [TestCase("public static object M() { Contract.Ensures(Contract.Result<object>() == null); return H.BoxDef<int>(); }")]
    public async Task SpecializedCalleesAreRefuted(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
    }

    // `a == b` on a reference-constrained T compares references, which two
    // equal strings need not share; a constrained call may change a mutable
    // struct; a nullable value type stays unspecialized.
    [TestCase("public static bool M(string a, string b) { Contract.Ensures(Contract.Result<bool>() == (a == b)); return H.Same(a, b); }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 0); return H.Bumped(new Q()).V; }")]
    [TestCase("public static int? M(int? v) { Contract.Ensures(Contract.Result<int?>() == v); return H.Id(v); }")]
    [TestCase("public static bool M(System.Collections.Generic.List<int?> xs) { Contract.Ensures(!Contract.Result<bool>()); return H.NullIn(xs, null); }")]
    public async Task UnsupportedSpecializationsAreNotProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // Equal struct values built apart are distinct IR objects; the default
    // comparer's answer on them stays unknown.
    [TestCase("public static bool M() { Contract.Ensures(Contract.Result<bool>()); return H.Eq(new P(1), new P(1)); }")]
    [TestCase("public static bool M(string a) { Contract.Ensures(Contract.Result<bool>()); return H.Eq(a, a); }")]
    public async Task OpaqueEqualityIsNotRefuted(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
    }

    private static async Task<WorkerClaimResult> ResultAsync(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        using var project = new ShadowTestProject(Types + Helpers + "\npublic static class C { " + member + " }\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var claim = response.Manifest.Claims.Single(static claim => claim.CallableId.Contains("C.M", StringComparison.Ordinal) &&
            claim.Kind is WorkerClaimKind.Postcondition or WorkerClaimKind.Effect);
        return response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId);
    }
}
