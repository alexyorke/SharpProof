using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A store, compound assignment or increment of a mutable static of a type
// whose initializer only fills in its own statics with fresh values. The
// store runs that initializer, which no body can observe, then writes the
// static as in a type with no initializer: a write of static state. A
// readonly static cannot be stored, every later read is an unknown value,
// and an effectful initializer still refuses the store.
[TestFixture]
public sealed class InitializedStaticWriteTests
{
    private const string Types = """
        using SharpProof.Attributes;
        public static class Cache
        {
            public static readonly System.Collections.Generic.List<int> Items = new System.Collections.Generic.List<int>();
            public static int Hits = 5;
            public static long Total;
            public static object Last;
            public static int Count { get; set; } = 3;
        }
        public static class Loud
        {
            public static int Count;
            static Loud() { System.Console.WriteLine(); }
        }
        public static class Tally
        {
            public static readonly long Edge = 40L * 3;
            public static long Hits;
        }
        public static class Seeded
        {
            public static readonly long Late = Make();
            public static long Hits;
            public static long Make() { Hits = 7; return 1; }
        }
        public static class Plain
        {
            public static int Count;
        }
        """;

    private const string Bounded = "Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == x + 1); ";

    [TestCase("public static int M(int x) { " + Bounded + "Cache.Hits = x; return x + 1; }")]
    [TestCase("public static int M(int x) { " + Bounded + "Cache.Hits++; return x + 1; }")]
    [TestCase("public static int M(int x) { " + Bounded + "--Cache.Hits; return x + 1; }")]
    [TestCase("public static int M(int x) { " + Bounded + "Cache.Total += x; return x + 1; }")]
    [TestCase("public static int M(int x) { " + Bounded + "Cache.Last = Cache.Items; return x + 1; }")]
    [TestCase("public static int M(int x) { " + Bounded + "Cache.Count = x; Cache.Count++; return x + 1; }")]
    [TestCase("public static int M(int x) { " + Bounded + "var old = Cache.Hits++; return x + 1; }")]
    [TestCase("public static int M(int x) { " + Bounded + "Plain.Count += x; return x + 1; }")]
    public async Task AdmittedInitializerStaticsAreWritten(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // The store, and a discarded int increment, is a write of static state.
    // Any other increment or compound assignment also reads static state,
    // which replay does not reproduce, so its refutation stays Unknown, as
    // in a type with no initializer.
    [TestCase("[EnforcePure] public static void M(int x) { Cache.Hits = x; }", true)]
    [TestCase("[EnforcePure] public static void M(int x) { Cache.Count = x; }", true)]
    [TestCase("[EnforcePure] public static void M() { Cache.Last = null; }", true)]
    [TestCase("[EnforcePure] public static void M() { Cache.Hits++; }", true)]
    [TestCase("[EnforcePure] public static int M() { return Cache.Hits++; }", false)]
    [TestCase("[EnforcePure] public static void M(long x) { Cache.Total += x; }", false)]
    [TestCase("[EnforcePure] public static void M() { Plain.Count += 1; }", false)]
    public async Task TheWriteIsAnEffect(string member, bool refuted)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(refuted ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Unknown), result.Reason.ToString());
    }

    // A later read does not see the initializer's value, nor a known value
    // of the readonly static past the store; the stored value is not
    // remembered either, since a static read is an unknown value. An
    // effectful initializer is not admitted.
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == 5); Cache.Total = x; return Cache.Hits; }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == x); Cache.Hits = x; return Cache.Hits; }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == 6); Cache.Hits++; return Cache.Hits; }")]
    [TestCase("public static int M(int x) { Contract.Ensures(Contract.Result<int>() == 3); Cache.Hits = x; return Cache.Count; }")]
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 0); return Tally.Hits; }")]
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x); Tally.Hits = x; return Tally.Hits; }")]
    [TestCase("public static int M(int x) { " + Bounded + "Seeded.Hits = x; return x + 1; }")]
    [TestCase("public static int M(int x) { " + Bounded + "Loud.Count = x; return x + 1; }")]
    [TestCase("public static int M(int x) { " + Bounded + "Loud.Count++; return x + 1; }")]
    [TestCase("public static int M(int x) { " + Bounded + "Loud.Count += x; return x + 1; }")]
    public async Task TheWriteStaysConservative(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // A readonly static's initialized value is still known after a body
    // stores another static of its type: the store cannot reach it, and
    // the initialization stores no static a body can write.
    [TestCase("public static bool M() { Contract.Ensures(Contract.Result<bool>()); return Tally.Edge == 120; }")]
    [TestCase("public static bool M(long x) { Contract.Ensures(Contract.Result<bool>()); Tally.Hits = x; return Tally.Edge == 120; }")]
    [TestCase("public static bool M(long x) { Contract.Ensures(Contract.Result<bool>()); Tally.Hits += x; return Tally.Edge == 120; }")]
    public async Task AReadonlyStaticKeepsItsValue(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
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
