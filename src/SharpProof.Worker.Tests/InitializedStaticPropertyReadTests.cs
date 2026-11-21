using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A static auto-property, or a static getter that only returns a static
// field, of a type whose initializer only fills in its own statics with
// fresh values. The getter runs that initializer, which no body can observe,
// then reads the field as in a type with no initializer: an unknown value and
// a read of static state. An effectful initializer still refuses it.
[TestFixture]
public sealed class InitializedStaticPropertyReadTests
{
    private const string Types = """
        using SharpProof.Attributes;
        public static class Cache
        {
            private static int _hits = 5;
            public static System.Collections.Generic.List<int> Items { get; } = new System.Collections.Generic.List<int>();
            public static int Hits { get; set; } = 5;
            public static int Misses => _hits;
            public static int Total { get { return _hits; } }
        }
        public static class Loud
        {
            public static int Count { get; set; }
            static Loud() { System.Console.WriteLine(); }
        }
        public static class H
        {
            public static int Tag(object o, int x) { return x + 1; }
        }
        """;

    [TestCase("public static int M(int x) { Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == x + 1); return Cache.Hits >= 0 ? x + 1 : x + 1; }")]
    [TestCase("public static int M(int x) { Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == x + 1); return H.Tag(Cache.Items, x); }")]
    [TestCase("public static int M(int x) { Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == x + 1); return Cache.Misses >= 0 ? x + 1 : x + 1; }")]
    [TestCase("public static int M(int x) { Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == x + 1); return Cache.Total >= 0 ? x + 1 : x + 1; }")]
    public async Task AdmittedInitializerStaticPropertiesAreRead(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // The value is not known, the read is a read of static state, the
    // initializer allocates, and an effectful initializer is not admitted.
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 5); return Cache.Hits; }")]
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 5); return Cache.Misses; }")]
    [TestCase("public static object M() { Contract.Ensures(Contract.Result<object>() != null); return Cache.Items; }")]
    [TestCase("[EnforcePure] public static int M() { return Cache.Hits; }")]
    [TestCase("[EnforcePure] public static int M() { return Cache.Total; }")]
    [TestCase("[EnforcePure] public static object M() { return Cache.Items; }")]
    [TestCase("[ZeroAllocations] public static int M() { return Cache.Hits; }")]
    [TestCase("public static int M(int x) { Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == x + 1); return Loud.Count >= 0 ? x + 1 : x + 1; }")]
    public async Task TheReadStaysConservative(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // Newtonsoft's JToken.ToObject boxes the result of a user-defined
    // conversion to bool? inside a try block. Reading a static auto-property
    // first let its body lower that far. The conversion does not lower, so
    // the boxing is approximated, as an object rather than as a bool?.
    [TestCase("public static object M(J j) { Contract.Ensures(true); try { return (bool?)j; } catch (System.ArgumentException) { return null; } }")]
    [TestCase("public static object M(J j) { Contract.Ensures(true); return (bool?)j; }")]
    public async Task ABoxedUnsupportedNullableIsApproximatedAsTheBox(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member, "public sealed class J { public static explicit operator bool?(J v) { return null; } }\n");
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown), result.Reason.ToString());
    }

    private static async Task<WorkerClaimResult> ResultAsync(string member, string extra = "")
    {
        System.ArgumentNullException.ThrowIfNull(member);
        System.ArgumentNullException.ThrowIfNull(extra);
        using var project = new ShadowTestProject(Types + extra + "\npublic static class C { " + member + " }\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var claim = response.Manifest.Claims.Single(static claim => claim.CallableId.Contains("C.M", StringComparison.Ordinal) &&
            claim.Kind is WorkerClaimKind.Postcondition or WorkerClaimKind.Effect);
        return response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId);
    }
}
