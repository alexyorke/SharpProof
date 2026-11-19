using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A readonly static that only its admitted initializer stores, and that holds
// an object no code can change (a boxed constant, a constant string, null or
// `new object()`), reads as an unknown reference. The read runs that
// initializer, which has no effect a body can observe, so it is not an
// ambient read. Any other static read stays Unknown.
[TestFixture]
public sealed class ImmutableStaticReadTests
{
    private const string Types = "using SharpProof.Attributes;\n" +
        "public static class Other { public static readonly object Zero = 0L; }\n" +
        "public enum Color { Red, Green }\n";

    private const string CachedResult =
        """
        public sealed class CachedResult<T>
        {
            private readonly T _value;
            private readonly System.Func<System.Exception> _failure;
            public bool CanContinue { get; }
            private CachedResult(System.Func<System.Exception> failure, bool canContinue)
            {
                _failure = failure;
                CanContinue = canContinue;
                _value = default!;
            }
            public static CachedResult<T> Empty { get; } = new CachedResult<T>(
                () => new System.InvalidOperationException(System.Environment.GetEnvironmentVariable("EXAMPLE")), false);
            public static int Identity(int value) => value;
            public System.Exception GetError() => _failure();
        }
        """;

    [TestCase("[EnforcePure] public static int M(int value) => CachedResult<int>.Identity(value);", true)]
    [TestCase("[DoesNotThrow] public static int M(int value) => CachedResult<int>.Identity(value);", true)]
    [TestCase("[ZeroAllocations] public static int M(int value) => CachedResult<int>.Identity(value);", false)]
    [TestCase("[EnforcePure] public static CachedResult<int> M() => CachedResult<int>.Empty;", false)]
    public async Task CachedResultKeepsInitializationAndReadEffectsSeparate(string members, bool proven)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        var result = await ResultAsync(members, CachedResult);
        Assert.That(result.Outcome, Is.EqualTo(proven ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Unknown), result.Reason.ToString());
    }

    [Test]
    public async Task ExecutingTheCallbackInTheConstructorKeepsInitializationUnknown()
    {
        var eager = CachedResult.Replace("_failure = failure;", "failure(); _failure = failure;", StringComparison.Ordinal);
        var result = await ResultAsync("[DoesNotThrow] public static int M(int value) => CachedResult<int>.Identity(value);", eager);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    // Newtonsoft's boxed-integer cache, Scriban's boxed Booleans, and the
    // other admitted shapes.
    [TestCase("private static readonly object Zero = 0L; private static readonly object One = 1L; " +
        "[EnforcePure] public static object M(long v) { return v switch { 0 => Zero, 1 => One, _ => null }; }")]
    [TestCase("private static readonly object True = true; private static readonly object False = false; " +
        "[EnforcePure] public static object M(bool v) { return v ? True : False; }")]
    [TestCase("private static readonly object True = true; private static readonly object False = false; " +
        "[DoesNotThrow] public static object M(bool v) { return v ? True : False; }")]
    [TestCase("private static readonly object True = true; " +
        "[AllowedCapabilities(SharpProofCapability.None)] public static object M() { return True; }")]
    [TestCase("public static object TrueObject { get; } = true; [EnforcePure] public static object M() { return TrueObject; }")]
    [TestCase("private static readonly object Red = Color.Red; [EnforcePure] public static object M() { return Red; }")]
    [TestCase("private static readonly object Half = 0.5; [DoesNotThrow] public static object M() { return Half; }")]
    [TestCase("private static readonly System.IComparable Letter = 'a'; [EnforcePure] public static System.IComparable M() { return Letter; }")]
    [TestCase("private static readonly object Boxed = (object)7; [EnforcePure] public static object M() { return Boxed; }")]
    [TestCase("private static readonly string Name = \"name\"; [EnforcePure] public static string M() { return Name; }")]
    [TestCase("private static readonly object Gate = new object(); [EnforcePure] public static object M() { return Gate; }")]
    [TestCase("private static readonly object Zero = 0L; [EnforcePure] public static bool M() { return Zero == null; }")]
    [TestCase("[EnforcePure] public static object M() { return Other.Zero; }")]
    [TestCase("[DoesNotThrow] public static object M() { return Other.Zero; }")]
    public async Task ReadsOfImmutableStaticsAreProven(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        var result = await ResultAsync(members);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [TestCase("private static object Mutable = 0L; [EnforcePure] public static object M() { return Mutable; }")]
    [TestCase("private static volatile object Volatile = 0L; [EnforcePure] public static object M() { return Volatile; }")]
    [TestCase("[System.ThreadStatic] private static readonly object PerThread = 0L; [EnforcePure] public static object M() { return PerThread; }")]
    [TestCase("private static readonly int[] Cache = new int[3]; [EnforcePure] public static int[] M() { return Cache; }")]
    [TestCase("private static readonly object Count = System.Environment.ProcessorCount; [EnforcePure] public static object M() { return Count; }")]
    [TestCase("private static readonly object Text = 1.ToString(); [EnforcePure] public static object M() { return Text; }")]
    [TestCase("private static readonly object Big = 1m; [EnforcePure] public static object M() { return Big; }")]
    [TestCase("private static readonly System.Threading.Tasks.Task Done = System.Threading.Tasks.Task.CompletedTask; " +
        "[EnforcePure] public static System.Threading.Tasks.Task M() { return Done; }")]
    [TestCase("private static readonly System.Threading.Tasks.Task<int> Done = System.Threading.Tasks.Task.FromResult(0); " +
        "[EnforcePure] public static System.Threading.Tasks.Task<int> M() { return Done; }")]
    // The static constructor stores something else into it.
    [TestCase("private static readonly object Zero = 0L; static C() { Zero = new int[1]; } [EnforcePure] public static object M() { return Zero; }")]
    // Another initializer of the type stores something else into it.
    [TestCase("private static readonly object Zero = 0L; private static readonly object Copy = Zero = new int[1]; " +
        "[EnforcePure] public static object M() { return Zero; }")]
    // A read in another type runs its fresh-values initializer, which allocates.
    [TestCase("[ZeroAllocations] public static object M() { return Other.Zero; }")]
    // The object is not modeled, so neither its type nor its contents are known.
    [TestCase("private static readonly object Zero = 0L; [DoesNotThrow] public static long M() { return (long)Zero; }")]
    [TestCase("private static readonly object Zero = 0L; " +
        "public static object M() { Contract.Ensures(Contract.Result<object>() != null); return Zero; }")]
    public async Task OtherStaticReadsStayUnknown(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        var result = await ResultAsync(members);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown), result.Reason.ToString());
    }

    private static async Task<WorkerClaimResult> ResultAsync(string members, string additionalTypes = "")
    {
        System.ArgumentNullException.ThrowIfNull(members);
        System.ArgumentNullException.ThrowIfNull(additionalTypes);
        using var project = new ShadowTestProject(Types + additionalTypes + "\npublic static class C { " + members + " }\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var claim = response.Manifest.Claims.Single(static claim => claim.CallableId.Contains("C.M", StringComparison.Ordinal) &&
            claim.Kind is WorkerClaimKind.Postcondition or WorkerClaimKind.Effect);
        return response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId);
    }
}
