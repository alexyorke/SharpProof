using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Smt;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A type initializer that only fills the type's own readonly statics with
// fresh values has no effect a body can observe, so purity and capability
// claims accept it. It still allocates, so allocation claims do not. Every
// other initializer stays an entry effect, and the claim stays Unknown.
[TestFixture]
internal sealed class TypeInitializerEffectTests
{
    private const string Body = "public static int Target(int x) { return x; }";

    [TestCase("private static readonly int[] Cache = new int[3];")]
    [TestCase("private static readonly string[] Names = [\"First\", \"Second\"];")]
    [TestCase("private static readonly object Gate = new object();")]
    [TestCase("private static readonly object Boxed = 5;")]
    [TestCase("public static System.Func<int, int> Twice { get; } = x => x * 2;")]
    [TestCase("private static readonly System.Func<int, int> Same = Identity; private static int Identity(int x) => x;")]
    [TestCase("private static readonly Era Common = new Era(\"CE\");")]
    [TestCase("private static readonly Point Origin = new Point(0);")]
    [TestCase("private static readonly object Gate; static C() { Gate = new object(); }")]
    [TestCase("private static readonly int[] Copy = Admitted.Data;")]
    public async Task FreshValuesProvePurityAndCapabilities(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        var purity = await VerifyAsync(members, "[EnforcePure]", NativeEffectSiteVerifier.VerifyPurityAsync);
        var capabilities = await VerifyAsync(members, "[AllowedCapabilities(SharpProofCapability.None)]",
            NativeEffectSiteVerifier.VerifyCapabilitiesAsync);
        var allocation = await VerifyAsync(members, "[ZeroAllocations]", NativeEffectSiteVerifier.VerifyAsync);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(purity.Outcome, Is.TypeOf<ProvenOutcome>());
            Assert.That(capabilities.Outcome, Is.TypeOf<ProvenOutcome>());
            // The first call may run the initializer, which allocates.
            Assert.That(allocation.Outcome, Is.Null);
            Assert.That(allocation.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
        }
    }

    // Constant stores allocate nothing, so allocation claims accept them too.
    [TestCase("private static readonly string Name = \"CE\";")]
    [TestCase("static C() { }")]
    [TestCase("private static readonly int Limit; static C() { Limit = 3; }")]
    public async Task ConstantsProveAllocationClaims(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        var allocation = await VerifyAsync(members, "[ZeroAllocations]", NativeEffectSiteVerifier.VerifyAsync);
        Assert.That(allocation.Outcome, Is.TypeOf<ProvenOutcome>());
    }

    // A call, a read of a static whose initializer is effectful, a throw, a
    // cross-type cycle and a static constructor with logic.
    [TestCase("private static readonly int[] Cache = Create(); private static int[] Create() => new int[3];")]
    [TestCase("private static readonly System.Collections.Generic.Dictionary<int, int> Map = new() { { 1, 2 } };")]
    [TestCase("private static readonly int[] Copy = Effectful.Data;")]
    [TestCase("private static readonly object Value = (object)null ?? throw new System.InvalidOperationException();")]
    [TestCase("static C() { throw new System.InvalidOperationException(); }")]
    [TestCase("public static readonly object Gate = Cycle.Back;")]
    [TestCase("private static readonly int Limit; static C() { if (System.Environment.ProcessorCount > 1) { Limit = 1; } }")]
    [TestCase("private static readonly Counter Instance = new Counter();")]
    [TestCase("public static int State = 1;")]
    public async Task OtherInitializersLeavePurityUnknown(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        var purity = await VerifyAsync(members, "[EnforcePure]", NativeEffectSiteVerifier.VerifyPurityAsync);
        Assert.That(purity.Outcome, Is.Null);
        Assert.That(purity.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    // A body that writes state is still refuted; the initializer admission
    // only removes the entry gap.
    [TestCase("")]
    [TestCase("private static readonly int[] Cache = new int[3];")]
    public async Task AnImpureBodyIsStillRefuted(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        var purity = await VerifyAsync(members, "[EnforcePure]", NativeEffectSiteVerifier.VerifyPurityAsync,
            "public static void Target(int[] items) { items[0] = 1; }");
        Assert.That(purity.Outcome, Is.TypeOf<RefutedOutcome>());
    }

    // Admitting the initializer only removes the entry gap: the body gets
    // the verdict it gets in a type without one. MoreLINQ's Aggregate
    // (OSS0202) is Refuted this way once MoreEnumerable's string array no
    // longer masks it.
    [TestCase("public static int Target(System.Func<int, int> f, int x) { return f(x); }")]
    [TestCase("public static int Target(System.Collections.Generic.IEnumerable<int> source, System.Func<int, int, int> f) " +
        "{ var a = 0; foreach (var item in source) { a = f(a, item); } return a; }")]
    [TestCase("public static int Target(int[] items) { return items.Length; }")]
    [TestCase("public static int Target(int x) { return Helper(x); } private static int Helper(int x) { return x; }")]
    [TestCase("public static void Target(int[] items) { Store(items); } private static void Store(int[] items) { items[0] = 1; }")]
    public async Task AdmissionDoesNotChangeTheBodyVerdict(string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        var without = await VerifyAsync("", "[EnforcePure]", NativeEffectSiteVerifier.VerifyPurityAsync, body);
        var with = await VerifyAsync("private static readonly string[] Names = [\"First\"];", "[EnforcePure]",
            NativeEffectSiteVerifier.VerifyPurityAsync, body);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(with.Outcome?.GetType(), Is.EqualTo(without.Outcome?.GetType()));
            Assert.That(with.Reason, Is.EqualTo(without.Reason));
        }
    }

    private const string SameTypeCall = "public static int Target(int x) { return Helper(x); } private static int Helper(int x) { return x; }";

    // A callee declared in the caller's own type runs under the initializer
    // the caller already answers for, so it is inlined as if written there.
    [TestCase("private static readonly int[] Cache = new int[3];")]
    [TestCase("private static readonly object Gate = new object();")]
    [TestCase("private static readonly string Name = \"CE\";")]
    public async Task SameTypeCalleeIsInlinedUnderAnAdmittedInitializer(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        var purity = await VerifyAsync(members, "[EnforcePure]", NativeEffectSiteVerifier.VerifyPurityAsync, SameTypeCall);
        Assert.That(purity.Outcome, Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public async Task SameTypeCalleeStillPaysForFreshValueAllocation()
    {
        var allocation = await VerifyAsync("private static readonly int[] Cache = new int[3];", "[ZeroAllocations]",
            NativeEffectSiteVerifier.VerifyAsync, SameTypeCall);
        Assert.That(allocation.Outcome, Is.Null);
    }

    [TestCase("public static int State = 1;")]
    [TestCase("private static readonly int[] Cache = Create(); private static int[] Create() => new int[3];")]
    public async Task SameTypeCalleeDoesNotAdmitAnEffectfulInitializer(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        var purity = await VerifyAsync(members, "[EnforcePure]", NativeEffectSiteVerifier.VerifyPurityAsync, SameTypeCall);
        Assert.That(purity.Outcome, Is.Null);
    }

    // Another type's initializer can run in the middle of the body, and a
    // nested type's member can run before the enclosing initializer has, so
    // a callee outside the caller's own type keeps the stricter rule.
    [TestCase("private static readonly int[] Cache = new int[3]; private static class Nested { internal static int Helper(int x) { return x; } }",
        "public static int Target(int x) { return Nested.Helper(x); }")]
    [TestCase("", "public static int Target(int x) { return Other.Helper(x); }")]
    [TestCase("private static readonly int[] Cache = new int[3]; private static int Helper(int x) { return x; } " +
        "private static class Nested { internal static int Twice(int x) { return Helper(x); } }",
        "public static int Target(int x) { return Nested.Twice(x); }")]
    public async Task CalleeOutsideTheCallerTypeKeepsTheStricterRule(string members, string body)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        System.ArgumentNullException.ThrowIfNull(body);
        var purity = await VerifyAsync(members, "[EnforcePure]", NativeEffectSiteVerifier.VerifyPurityAsync, body);
        Assert.That(purity.Outcome, Is.Null);
    }

    private static async Task<PassiveCallableCheckResult> VerifyAsync(string members, string attribute,
        Func<CompilerCallablePreparation, WorkerBudgets, CancellationToken, MethodResourceBudget?, Task<PassiveCallableCheckResult>> verify,
        string body = Body)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        System.ArgumentNullException.ThrowIfNull(attribute);
        System.ArgumentNullException.ThrowIfNull(verify);
        System.ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; " +
            "public sealed class Era { public Era(string name) { Name = name; } public string Name { get; } } " +
            "public struct Point { private readonly int x; public Point(int x) { this.x = x; } } " +
            "public static class Admitted { public static readonly int[] Data = new int[2]; } " +
            "public static class Other { private static readonly int[] Table = new int[2]; public static int Helper(int x) { return x; } } " +
            "public static class Effectful { public static readonly int[] Data = Make(); private static int[] Make() => new int[1]; } " +
            (members.Contains("Cycle.", StringComparison.Ordinal) ? "public static class Cycle { public static readonly object Back = C.Gate; } " : "") +
            "public sealed class Counter { public static int Count; public Counter() { Count++; } } " +
            "public static class C { " + members + " " + attribute + " " + body + " }";
        return await verify(Prepare(source), new WorkerBudgets(), TestContext.CurrentContext.CancellationToken, null);
    }

    private static CompilerCallablePreparation Prepare(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact,
            cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations,
            cancellationToken: TestContext.CurrentContext.CancellationToken);
        return preparations.Single(preparation => preparation.EffectClaims.Length != 0);
    }
}
