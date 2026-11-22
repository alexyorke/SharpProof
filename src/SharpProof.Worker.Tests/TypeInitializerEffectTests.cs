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
    [TestCase("private static readonly object[] NoArguments = System.Array.Empty<object>();")]
    [TestCase("private static readonly Era[] NoEras = System.Array.Empty<Era>();")]
    [TestCase("private static readonly Era[] NoEras = [];")]
    [TestCase("private static readonly System.Type Key = typeof(Era);")]
    [TestCase("private static readonly System.Type[] NoTypes = System.Type.EmptyTypes;")]
    [TestCase("private static readonly System.Collections.Generic.IEqualityComparer<string> Names = System.StringComparer.Ordinal;")]
    [TestCase("private static readonly object Half = 0.5m;")]
    [TestCase("private static readonly System.Globalization.IdnMapping Mapping = new();")]
    [TestCase("private static readonly System.Buffers.SearchValues<char> Chars = System.Buffers.SearchValues.Create(\"<>&\");")]
    [TestCase("private static readonly int[] Copy = Exposed.Table;")]
    [TestCase("private static readonly System.Collections.Generic.IEnumerable<Era> NoEras = System.Linq.Enumerable.Empty<Era>();")]
    [TestCase("private static readonly System.Collections.Concurrent.ConcurrentQueue<Era> Pool = new();")]
    [TestCase("private static readonly System.Collections.Generic.Dictionary<System.Type, int> Codes = new() { { typeof(int), 1 }, { typeof(Era), 2 } };")]
    [TestCase("private static readonly System.Collections.Generic.HashSet<System.Type> Scalars = new() { typeof(bool), typeof(string) };")]
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
    [TestCase("private static readonly string Empty = string.Empty;")]
    [TestCase("private static readonly string Separator = System.Environment.NewLine;")]
    // Floating-point arithmetic, Math.Ceiling and an unchecked conversion to
    // long never throw, so the closed computation finishes.
    [TestCase("private static readonly double Scale = 3 / 1024.0; private static readonly long Bits = (long)System.Math.Ceiling(-Scale * 8);")]
    public async Task ConstantsProveAllocationClaims(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        var allocation = await VerifyAsync(members, "[ZeroAllocations]", NativeEffectSiteVerifier.VerifyAsync);
        Assert.That(allocation.Outcome, Is.TypeOf<ProvenOutcome>());
    }

    // A call, a read of a static whose initializer is effectful, a throw, a
    // cross-type cycle and a static constructor with logic.
    [TestCase("private static readonly int[] Cache = Create(); private static int[] Create() => new int[System.Environment.ProcessorCount];")]
    [TestCase("private static readonly System.Collections.Generic.Dictionary<int, int> Map = new() { { 1, 2 }, { 1, 3 } };")]
    [TestCase("private static readonly int[] Copy = Effectful.Data;")]
    [TestCase("private static readonly object Value = (object)null ?? throw new System.InvalidOperationException();")]
    [TestCase("static C() { throw new System.InvalidOperationException(); }")]
    [TestCase("public static readonly object Gate = Cycle.Back;")]
    [TestCase("private static readonly int Limit; static C() { if (System.Environment.ProcessorCount > 1) { Limit = 1; } }")]
    [TestCase("private static readonly Counter Instance = new Counter();")]
    [TestCase("[System.ThreadStatic] public static int State = 1;")]
    [TestCase("private static readonly System.StringComparer Names = System.StringComparer.CurrentCulture;")]
    [TestCase("private static readonly int[] Copy = Exposed.Settable;")]
    [TestCase("private static readonly System.Buffers.SearchValues<char> Chars = System.Buffers.SearchValues.Create(System.Environment.NewLine);")]
    [TestCase("private static readonly double Scale = 2.0; private static readonly long Bits = checked((long)Scale);")]
    [TestCase("private static readonly double Scale = 2.0; private static readonly int Sign = Scale > 0 ? 1 : 0;")]
    [TestCase("private static readonly System.Collections.Generic.Dictionary<System.Type, int> Codes = new() { { typeof(nint), 1 }, { typeof(System.IntPtr), 2 } };")]
    // The runtime refuses an explicit layout that overlaps a reference.
    [TestCase("[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)] private struct BadLayout { " +
        "[System.Runtime.InteropServices.FieldOffset(0)] public object Reference; [System.Runtime.InteropServices.FieldOffset(0)] public int Number; } " +
        "private static readonly System.Type Key = typeof(BadLayout);")]
    // A synchronized getter takes a lock.
    [TestCase("private static readonly int[] Copy = LockedTable.Table;")]
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

    [TestCase("[System.ThreadStatic] public static int State = 1;")]
    [TestCase("private static readonly int[] Cache = Create(); private static int[] Create() => new int[System.Environment.ProcessorCount];")]
    public async Task SameTypeCalleeDoesNotAdmitAnEffectfulInitializer(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        var purity = await VerifyAsync(members, "[EnforcePure]", NativeEffectSiteVerifier.VerifyPurityAsync, SameTypeCall);
        Assert.That(purity.Outcome, Is.Null);
    }

    // Another type's initializer can run in the middle of the body, and a
    // nested type's member can run before the enclosing initializer has, so
    // a callee outside the caller's own type is inlined only when that
    // type's initializer, and every enclosing one, is admitted on its own.
    [TestCase("private static readonly int Seed = System.Environment.ProcessorCount; private static class Nested { internal static int Helper(int x) { return x; } }",
        "public static int Target(int x) { return Nested.Helper(x); }")]
    [TestCase("", "public static int Target(int x) { return Effectful.Helper(x); }")]
    [TestCase("private static readonly int Seed = System.Environment.ProcessorCount; private static int Helper(int x) { return x; } " +
        "private static class Nested { internal static int Twice(int x) { return Helper(x); } }",
        "public static int Target(int x) { return Nested.Twice(x); }")]
    public async Task CalleeOutsideTheCallerTypeKeepsTheStricterRule(string members, string body)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        System.ArgumentNullException.ThrowIfNull(body);
        var purity = await VerifyAsync(members, "[EnforcePure]", NativeEffectSiteVerifier.VerifyPurityAsync, body);
        Assert.That(purity.Outcome, Is.Null);
    }

    // The same calls when every initializer they reach only makes fresh
    // values: purity ignores the allocation, an allocation claim does not.
    [TestCase("private static readonly int[] Cache = new int[3]; private static class Nested { internal static int Helper(int x) { return x; } }",
        "public static int Target(int x) { return Nested.Helper(x); }")]
    [TestCase("", "public static int Target(int x) { return Other.Helper(x); }")]
    [TestCase("private static readonly int[] Cache = new int[3]; private static int Helper(int x) { return x; } " +
        "private static class Nested { internal static int Twice(int x) { return Helper(x); } }",
        "public static int Target(int x) { return Nested.Twice(x); }")]
    public async Task CalleeOutsideTheCallerTypeIsInlinedUnderFreshValueInitializers(string members, string body)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        System.ArgumentNullException.ThrowIfNull(body);
        var purity = await VerifyAsync(members, "[EnforcePure]", NativeEffectSiteVerifier.VerifyPurityAsync, body);
        var allocation = await VerifyAsync(members, "[ZeroAllocations]", NativeEffectSiteVerifier.VerifyAsync, body);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(purity.Outcome, Is.TypeOf<ProvenOutcome>());
            Assert.That(allocation.Outcome, Is.Null);
        }
    }

    // A callee whose type's initializer is a closed computation that
    // allocates nothing is inlined for every claim: a validating factory
    // called with constants never throws.
    [Test]
    public async Task CalleeUnderAClosedComputationInitializerIsInlined()
    {
        const string members = "private readonly struct Span { private readonly int seconds; public static readonly Span Max = FromSeconds(64800); " +
            "public static Span FromSeconds(int seconds) { if (seconds < -64800 || seconds > 64800) { throw new System.ArgumentOutOfRangeException(); } " +
            "return new Span(seconds); } private Span(int seconds) { this.seconds = seconds; } public static int Twice(int x) { return x + x; } }";
        const string body = "public static int Target(int x) { return unchecked(Span.Twice(x)); }";
        var purity = await VerifyAsync(members, "[EnforcePure]", NativeEffectSiteVerifier.VerifyPurityAsync, body);
        var allocation = await VerifyAsync(members, "[ZeroAllocations]", NativeEffectSiteVerifier.VerifyAsync, body);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(purity.Outcome, Is.TypeOf<ProvenOutcome>());
            Assert.That(allocation.Outcome, Is.TypeOf<ProvenOutcome>());
        }
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
            "public static class Effectful { public static readonly int[] Data = Make(); private static int[] Make() => new int[System.Environment.ProcessorCount]; " +
            "public static int Helper(int x) { return x; } } " +
            (members.Contains("Cycle.", StringComparison.Ordinal) ? "public static class Cycle { public static readonly object Back = C.Gate; } " : "") +
            "public sealed class Counter { public static int Count; public Counter() { Count++; } } " +
            "public static class Exposed { public static int[] Table { get; } = new int[2]; public static int[] Settable { get; set; } = new int[2]; } " +
            "public static class LockedTable { public static int[] Table { [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.Synchronized)] get; } = new int[2]; } " +
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
