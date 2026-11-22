using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A static of a type whose initializer only fills in its own statics with
// fresh values. The read runs that initializer, which no body can observe,
// then reads the static as in a type with no initializer: an unknown value
// and a read of static state. An effectful initializer still refuses it.
[TestFixture]
public sealed class InitializedStaticReadTests
{
    private const string Types = """
        using SharpProof.Attributes;
        public static class Cache
        {
            public static readonly System.Collections.Generic.List<int> Items = new System.Collections.Generic.List<int>();
            public static int Hits = 5;
        }
        public static class Loud
        {
            public static int Count;
            static Loud() { System.Console.WriteLine(); }
        }
        public static class Keys
        {
            public static readonly System.Type Key = typeof(Keys);
            public static readonly System.Buffers.SearchValues<char> Markup = System.Buffers.SearchValues.Create("<>&");
            public static int Hits = 3;
        }
        public static class Sizes
        {
            public static readonly double Scale = 3 / 1024.0;
            public static readonly long Bits = (long)System.Math.Ceiling(Scale * 8);
        }
        public static class H
        {
            public static int Tag(object o, int x) { return x + 1; }
        }
        """;

    [TestCase("public static int M(int x) { Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == x + 1); return Cache.Hits >= 0 ? x + 1 : x + 1; }")]
    [TestCase("public static int M(int x) { Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == x + 1); return H.Tag(Cache.Items, x); }")]
    [TestCase("public static int M(int x) { Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == x + 1); return Keys.Hits >= 0 ? x + 1 : x + 1; }")]
    [TestCase("public static int M(int x) { Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == x + 1); return H.Tag(Keys.Key, x); }")]
    [TestCase("public static int M(int x) { Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == x + 1); return Sizes.Bits >= 0 ? x + 1 : x + 1; }")]
    public async Task AdmittedInitializerStaticsAreRead(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // The value is not known, the read is a read of static state, the
    // initializer allocates, and an effectful initializer is not admitted.
    [TestCase("public static int M() { Contract.Ensures(Contract.Result<int>() == 5); return Cache.Hits; }")]
    [TestCase("public static object M() { Contract.Ensures(Contract.Result<object>() != null); return Cache.Items; }")]
    [TestCase("[EnforcePure] public static int M() { return Cache.Hits; }")]
    [TestCase("[EnforcePure] public static object M() { return Cache.Items; }")]
    [TestCase("[ZeroAllocations] public static int M() { return Cache.Hits; }")]
    [TestCase("public static int M(int x) { Contract.Requires(x < 1000 && x > -1000); Contract.Ensures(Contract.Result<int>() == x + 1); return Loud.Count >= 0 ? x + 1 : x + 1; }")]
    public async Task TheReadStaysConservative(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
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
