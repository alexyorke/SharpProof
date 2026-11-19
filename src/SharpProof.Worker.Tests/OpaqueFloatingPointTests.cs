using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// `float`, `double` and `decimal` values are opaque. They flow through
// parameters, returns, fields and calls; conversions into them from integers,
// and `float`/`double` arithmetic and comparisons, never throw and give
// unknown values. Nothing about rounding, NaN or signed zeros is modeled.
[TestFixture]
public sealed class OpaqueFloatingPointTests
{
    private const string Types = "using SharpProof.Attributes;\n";

    [TestCase("[DoesNotThrow] public static double M(int x) { return x; }")]
    [TestCase("[DoesNotThrow] public static double M(ulong x) { return x; }")]
    [TestCase("[DoesNotThrow] public static float M(long x) { return checked((float)x); }")]
    [TestCase("[DoesNotThrow] public static decimal M(long x) { return x; }")]
    [TestCase("[DoesNotThrow] public static double M(int x) { return Forward((double)x); } private static double Forward(double value) { return value; }")]
    [TestCase("[DoesNotThrow] public static double M(double x) { return checked(7 * x / 0.0 - x % 2); }")]
    [TestCase("[DoesNotThrow] public static bool M(double x) { return x < 1.0 || -x >= 2.0 || x != x; }")]
    [TestCase("[DoesNotThrow] public static void M(Box b, int v) { if (b != null) { b.Value = v; } } public sealed class Box { public double Value; }")]
    [TestCase("[EnforcePure] public static double M(int x) { return Half(x); } private static double Half(double value) { return value / 2.0; }")]
    [TestCase("[EnforcePure] public static Box M(int v) { return new Box(v); } public sealed class Box { private readonly double _value; public Box(double value) { _value = value; } }")]
    public async Task ForwardingAndUnknownResultsAreProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // The forwarding helpers lower; the framework call they reach has no
    // specification, so its unknown effects keep purity Unknown, and a witness
    // through an unknown double argument is not replayable.
    [TestCase("[EnforcePure] public static System.TimeSpan M(int x) { return Seconds((double)x); } private static System.TimeSpan Seconds(double seconds) { return System.TimeSpan.FromSeconds(seconds); }")]
    [TestCase("[EnforcePure] public static System.TimeSpan M(int x) { return Weeks((double)x); } private static System.TimeSpan Weeks(double weeks) { return Days(7 * weeks); } private static System.TimeSpan Days(double days) { return System.TimeSpan.FromDays(days); }")]
    public async Task UnspecifiedFrameworkCallsKeepTheirEffects(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown), result.Reason.ToString());
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
    }

    // A fault before any floating value is computed still has a replayable witness.
    [TestCase("[DoesNotThrow] public static double M(string s, int x) { return s.Length + (double)x; }")]
    [TestCase("[DoesNotThrow] public static void M(Box b, int v) { b.Value = v; } public sealed class Box { public double Value; }")]
    public async Task FaultsBeforeFloatingValuesAreRefuted(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
    }

    // A claim that depends on a floating result is neither proven nor
    // refuted: the comparison is unknown, NaN is not equal to itself, and a
    // witness that relies on a chosen comparison result cannot be replayed.
    [TestCase("[DoesNotThrow] public static int M(int x) { double d = x; if (d != d) { throw new System.InvalidOperationException(); } return 1; }")]
    [TestCase("[DoesNotThrow] public static int M(double x) { if (x == x) { return 1; } throw new System.InvalidOperationException(); }")]
    [TestCase("[DoesNotThrow] public static int M(double x) { if (x < 0.0) { throw new System.ArgumentOutOfRangeException(nameof(x)); } return 1; }")]
    [TestCase("public static bool M(int x) { Contract.Ensures(Contract.Result<bool>()); double d = x; return d == d; }")]
    [TestCase("public static bool M(int x) { Contract.Ensures(!Contract.Result<bool>()); double d = x; return 0.0 > d * d; }")]
    public async Task FloatingDependentClaimsStayUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown), result.Reason.ToString());
    }

    // Conversions out of floating point and `decimal` arithmetic can throw or
    // give platform-dependent values; they are not modeled.
    [TestCase("[DoesNotThrow] public static int M(double x) { return checked((int)x); }")]
    [TestCase("[DoesNotThrow] public static int M(double x) { return (int)x; }")]
    [TestCase("[DoesNotThrow] public static decimal M(double x) { return (decimal)x; }")]
    [TestCase("[DoesNotThrow] public static decimal M(decimal x) { return x + 1m; }")]
    [TestCase("[DoesNotThrow] public static decimal M(decimal x) { return 1m / x; }")]
    public async Task UnmodeledFloatingOperationsStayUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown), result.Reason.ToString());
    }

    private static async Task<WorkerClaimResult> ResultAsync(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        using var project = new ShadowTestProject(Types + "public static class C { " + member + " }\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var claim = response.Manifest.Claims.Single(static claim => claim.CallableId.Contains("C.M", StringComparison.Ordinal) &&
            claim.Kind is WorkerClaimKind.Postcondition or WorkerClaimKind.Effect);
        return response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId);
    }
}
