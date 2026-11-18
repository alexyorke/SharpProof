using System.Collections.Concurrent;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Smt;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A type initializer may fill its readonly statics with fresh framework
// collections, as Humanizer's ordinal tables and the caches of many
// libraries do. The constructors and Add calls it may make run no user code
// and cannot throw except by running out of memory, so purity and
// capability claims accept the initializer as they accept a fresh array.
// The collections are mutable, so a body that reads one still reads
// ambient state.
[TestFixture]
public sealed class CollectionInitializerTests
{
    private const string Generic = "System.Collections.Generic.";
    private const string Body = "public static int Target(int x) { return x; }";

    [TestCase("private static readonly " + Generic + "List<int> Items = new();")]
    [TestCase("private static readonly " + Generic + "List<Key> Items = new " + Generic + "List<Key>(4);")]
    [TestCase("private static readonly " + Generic + "Queue<string> Pending = new();")]
    [TestCase("private static readonly " + Generic + "Stack<Key> Frames = new(0);")]
    [TestCase("private static readonly " + Generic + "Dictionary<Key, int> Cache = new();")]
    [TestCase("private static readonly " + Generic + "HashSet<Key> Seen = new();")]
    [TestCase("private static readonly System.Collections.Concurrent.ConcurrentDictionary<Key, string> Cache = new();")]
    [TestCase("private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Cache = new(System.StringComparer.OrdinalIgnoreCase);")]
    [TestCase("private static readonly " + Generic + "HashSet<string> Reserved = new(System.StringComparer.Ordinal) { \"class\", \"end\", \"class\" };")]
    [TestCase("private static readonly " + Generic + "HashSet<string> Reserved = new(8, System.StringComparer.OrdinalIgnoreCase) { \"Class\", \"class\" };")]
    [TestCase("private static readonly " + Generic + "Dictionary<long, string> Ordinals = new() { { 1, \"first\" }, { 2, \"second\" }, { 12, \"twelfth\" } };")]
    [TestCase("private static readonly " + Generic + "Dictionary<char, string> Suffixes = new " + Generic + "Dictionary<char, string> { { 'a', \"first\" }, { 'e', \"second\" } };")]
    [TestCase("private static readonly " + Generic + "Dictionary<string, Shade> Shades = new(System.StringComparer.OrdinalIgnoreCase) { [\"red\"] = Shade.Red, [\"RED\"] = Shade.Dark };")]
    [TestCase("private static readonly " + Generic + "Dictionary<Shade, string> Names = new() { { Shade.Red, \"red\" }, { Shade.Dark, \"dark\" } };")]
    [TestCase("private static readonly " + Generic + "Dictionary<int, int[]> Tables = new() { { 1, new int[2] }, { 2, new[] { 3, 4 } } };")]
    [TestCase("private static readonly " + Generic + "Dictionary<string, System.Func<int, int>> Steps = new() { { \"twice\", x => x * 2 } };")]
    [TestCase("private static readonly " + Generic + "List<object> Gates = new() { new object(), \"name\", 3, null };")]
    public async Task FreshCollectionsProvePurityButNotAllocation(string members)
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
            Assert.That(allocation.Outcome, Is.Null);
            Assert.That(allocation.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
        }
    }

    // A caller in another type inlines the callee, as for Humanizer's
    // EnglishNumberToWordsConverter; the first call may still run the
    // initializer, which allocates.
    [Test]
    public async Task CallersInlineCalleesUnderAFreshCollectionInitializer()
    {
        const string body = "public static int Target(int x) { return Words.Twice(x); }";
        var purity = await VerifyAsync("", "[EnforcePure]", NativeEffectSiteVerifier.VerifyPurityAsync, body);
        var allocation = await VerifyAsync("", "[ZeroAllocations]", NativeEffectSiteVerifier.VerifyAsync, body);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(purity.Outcome, Is.TypeOf<ProvenOutcome>());
            Assert.That(allocation.Outcome, Is.Null);
        }
    }

    // Each shape that may run user code, read ambient state or throw.
    [TestCase("private static readonly " + Generic + "HashSet<string> Names = new(new Loose());")]
    [TestCase("private static readonly " + Generic + "HashSet<string> Names = new(" + Generic + "EqualityComparer<string>.Default);")]
    [TestCase("private static readonly " + Generic + "HashSet<string> Names = new(System.StringComparer.CurrentCulture);")]
    [TestCase("private static readonly " + Generic + "HashSet<string> Names = new(System.StringComparer.InvariantCultureIgnoreCase);")]
    [TestCase("private static readonly " + Generic + "Dictionary<Key, int> Map = new() { { new Key(), 1 } };")]
    [TestCase("private static readonly " + Generic + "HashSet<Key> Seen = new() { new Key() };")]
    [TestCase("private static readonly " + Generic + "Dictionary<double, int> Map = new() { { 0.5, 1 } };")]
    [TestCase("private static readonly " + Generic + "Dictionary<int, int> Map = new() { { 1, 2 }, { 1, 3 } };")]
    [TestCase("private static readonly " + Generic + "Dictionary<string, int> Map = new(System.StringComparer.OrdinalIgnoreCase) { { \"a\", 1 }, { \"A\", 2 } };")]
    [TestCase("private static readonly " + Generic + "Dictionary<string, int> Map = new(System.StringComparer.OrdinalIgnoreCase) { { \"é\", 1 } };")]
    [TestCase("private static readonly " + Generic + "Dictionary<string, int> Map = new() { { null, 1 } };")]
    [TestCase("private static readonly " + Generic + "Dictionary<string, int> Map = new() { [Name()] = 1 }; private static string Name() => \"a\";")]
    [TestCase("private static readonly " + Generic + "Dictionary<int, Key> Map = new() { { 1, new Key { Id = 2 } } };")]
    [TestCase("private static readonly " + Generic + "List<int> Items = new(-1);")]
    [TestCase("private static readonly " + Generic + "List<int> Items = new(1 << 20);")]
    [TestCase("private static readonly " + Generic + "List<int> Items = new() { Capacity = 4 };")]
    [TestCase("private static readonly " + Generic + "List<int> Items = new(new[] { 1, 2 });")]
    [TestCase("private static readonly " + Generic + "List<int> Items = new() { System.Environment.ProcessorCount };")]
    [TestCase("private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> Map = new(2, 8);")]
    [TestCase("private static readonly System.Collections.Generic.SortedDictionary<int, int> Map = new();")]
    [TestCase("private static readonly System.Text.RegularExpressions.Regex Pattern = new(\"a+\");")]
    [TestCase("private static readonly Bag Items = new() { 1 };")]
    [TestCase("private static " + Generic + "List<int> Items = new();")]
    public async Task OtherCollectionsLeavePurityUnknown(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        var purity = await VerifyAsync(members, "[EnforcePure]", NativeEffectSiteVerifier.VerifyPurityAsync);
        Assert.That(purity.Outcome, Is.Null);
        Assert.That(purity.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    // Admitting the initializer does not make the collection's contents
    // known: a read of it is a read of state other code can change.
    [TestCase("public static int Target(int x) { return Items.Count + x; }")]
    [TestCase("public static bool Target(long x) { return Ordinals.ContainsKey(x); }")]
    [TestCase("public static void Target(int x) { Items.Add(x); }")]
    public async Task ReadsOfTheCollectionsAreNotProven(string body)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        var purity = await VerifyAsync("private static readonly " + Generic + "List<int> Items = new(); " +
            "private static readonly " + Generic + "Dictionary<long, string> Ordinals = new() { { 1, \"first\" } };",
            "[EnforcePure]", NativeEffectSiteVerifier.VerifyPurityAsync, body);
        Assert.That(purity.Outcome, Is.Not.TypeOf<ProvenOutcome>());
    }

    // Constructing the admitted collections calls no member of the element
    // or key type and runs no initializer of it, even for a type whose
    // members would throw.
    [Test]
    public void ConstructionRunsNoCodeOfTheElementType()
    {
        Witness.Calls = 0;
        var made = new object[]
        {
            new List<Spy>(), new List<Spy>(4), new Queue<Spy>(), new Queue<Spy>(4), new Stack<Spy>(), new Stack<Spy>(4),
            new Dictionary<Spy, int>(), new Dictionary<Spy, int>(4), new HashSet<Spy>(), new HashSet<Spy>(4),
            new ConcurrentDictionary<Spy, int>(), new Dictionary<string, Spy>(StringComparer.Ordinal),
            new Dictionary<string, Spy>(4, StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(4, StringComparer.OrdinalIgnoreCase), new ConcurrentDictionary<string, Spy>(StringComparer.OrdinalIgnoreCase)
        };
        using (Assert.EnterMultipleScope())
        {
            Assert.That(made, Has.All.Not.Null);
            Assert.That(Witness.Calls, Is.Zero);
        }
    }

    // Dictionary.Add throws for a key it holds under its comparer, which is
    // why the initializer's keys must differ; HashSet.Add and the indexer
    // do not.
    [Test]
    public void DuplicateKeysThrowOnlyInDictionaryAdd()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(new Action(() => { _ = new Dictionary<int, int> { { 1, 2 }, { 1, 3 } }; }), Throws.ArgumentException);
            Assert.That(new Action(() => { _ = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { "a", 1 }, { "A", 2 } }; }), Throws.ArgumentException);
            Assert.That(new Action(() => { _ = new Dictionary<string, int> { { null!, 1 } }; }), Throws.ArgumentNullException);
            Assert.That(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["a"] = 1, ["A"] = 2 }["a"], Is.EqualTo(2));
            Assert.That(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Class", "class" }, Has.Count.EqualTo(1));
            Assert.That(new Action(() => { _ = new List<int>(-1); }), Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }

    private static class Witness
    {
        internal static int Calls;
    }

    // Spy's own statics are never touched, so its initializer runs only if a
    // collection runs it.
    private sealed class Spy
    {
        static Spy()
        { Witness.Calls++; }

        public override bool Equals(object? obj)
        {
            Witness.Calls++;
            return false;
        }

        public override int GetHashCode()
        {
            Witness.Calls++;
            return 0;
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
            "public sealed class Key { public int Id { get; set; } public override bool Equals(object other) => true; public override int GetHashCode() => Id; } " +
            "public sealed class Loose : " + Generic + "IEqualityComparer<string> { public bool Equals(string a, string b) => true; public int GetHashCode(string a) => 0; } " +
            "public sealed class Bag : System.Collections.IEnumerable { public void Add(int item) { } public System.Collections.IEnumerator GetEnumerator() => null; } " +
            "public enum Shade { Red, Dark } " +
            "public static class Words { private static readonly " + Generic + "Dictionary<long, string> Ordinals = new " + Generic + "Dictionary<long, string> " +
            "{ { 1, \"first\" }, { 2, \"second\" } }; private static readonly string[] Units = { \"zero\", \"one\" }; public static int Twice(int x) { return unchecked(x + x); } } " +
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
