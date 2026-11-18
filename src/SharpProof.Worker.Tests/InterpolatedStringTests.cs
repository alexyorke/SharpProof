using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// An interpolated string converted to string. With only string holes it is
// String.Concat over its parts, with exact content. With char, bool, integer
// or reference holes it is a fresh string of unknown content: integers format
// through the ambient culture (an ambient read that cannot throw), and a
// reference's ToString is an unknown call.
[TestFixture]
public sealed class InterpolatedStringTests
{
    private const string Types = "using SharpProof.Attributes;\n";

    [TestCase("[DoesNotThrow] public static string M(int x) { return $\"line-{x}\"; }")]
    [TestCase("[DoesNotThrow] public static string M(int x) { var text = $\"{x}\"; return text; }")]
    [TestCase("[DoesNotThrow] public static string M(long x, byte y, ulong z) { return $\"{x}:{y}:{z}\"; }")]
    [TestCase("[DoesNotThrow] public static int M(int x) { return $\"{x}\".Length; }")]
    [TestCase("[DoesNotThrow] public static string M(string name, int start) { Contract.Requires(name == null || name.Length < 1000); return $\"{name}({start})\"; }")]
    [TestCase("[DoesNotThrow] public static string M(char c, bool flag) { return $\"{c}={flag}\"; }")]
    [TestCase("[DoesNotThrow] public static string M(string s) { Contract.Requires(s == null || s.Length < 1000); return $\"a{s}b\"; }")]
    [TestCase("[DoesNotThrow] public static string M(string s) { return $\"{s}\"; }")]
    [TestCase("[EnforcePure] public static string M(char c, bool flag) { return $\"{c}={flag}\"; }")]
    [TestCase("[EnforcePure] public static string M(string s, char c) { Contract.Requires(s == null || s.Length < 1000); return $\"{s}{c}\"; }")]
    [TestCase("[EnforcePure] public static string M(string s) { Contract.Requires(s == null || s.Length < 1000); return $\"a{s}\"; }")]
    [TestCase("[EnforcePure] public static string M() { return $\"proof\"; }")]
    [TestCase("[DoesNotThrow] public static string M(bool c, int a, int b) { return $\"{(c ? a : b)}-{a}\"; }")]
    [TestCase("[DoesNotThrow] public static string M(bool c, string s, string t) { return $\"{(c ? s : t)}\"; }")]
    [TestCase("public static string M(string s) { Contract.Requires(s == \"x\"); Contract.Ensures(Contract.Result<string>() == \"<x>\"); return $\"<{s}>\"; }")]
    [TestCase("public static string M(string s) { Contract.Ensures(Contract.Result<string>() != null); return $\"{s}\"; }")]
    [TestCase("public static string M(int x) { Contract.Ensures(Contract.Result<string>() != null); return $\"{x}\"; }")]
    public async Task AdmittedInterpolationsAreProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // An integer reads the ambient culture; a reference runs unknown code;
    // the formatted text is unknown; a string hole may reach the runtime's
    // maximum string length; alignment and format clauses are not modeled.
    [TestCase("[EnforcePure] public static string M(int x) { return $\"{x}\"; }")]
    [TestCase("[EnforcePure] public static string M(uint x) { return $\"n={x}\"; }")]
    [TestCase("[DoesNotThrow] public static string M(object o) { return $\"[{o}]\"; }")]
    [TestCase("[EnforcePure] public static string M(object o) { return $\"[{o}]\"; }")]
    [TestCase("[DoesNotThrow] public static string M(string s, int x) { return $\"{s}{x}\"; }")]
    [TestCase("[DoesNotThrow] public static string M(string s, string t) { return $\"{s}{t}\"; }")]
    [TestCase("[DoesNotThrow] public static string M(int x) { return $\"{x,5}\"; }")]
    [TestCase("[DoesNotThrow] public static string M(int x) { return $\"{x:X}\"; }")]
    [TestCase("[DoesNotThrow] public static string M(double d) { return $\"{d}\"; }")]
    [TestCase("[DoesNotThrow] public static System.FormattableString M(int x) { return $\"{x}\"; }")]
    [TestCase("[DoesNotThrow] public static string M(int x) { return \"a\" + $\"{x}\"; }")]
    [TestCase("[DoesNotThrow] public static string M(int x) { var s = \"a\"; s += $\"{x}\"; return s; }")]
    [TestCase("public static string M(int x) { Contract.Requires(x == 1); Contract.Ensures(Contract.Result<string>() == \"1\"); return $\"{x}\"; }")]
    [TestCase("public static string M(bool flag) { Contract.Ensures(Contract.Result<string>() == \"True\" || Contract.Result<string>() == \"False\"); return $\"{flag}\"; }")]
    [TestCase("public static int M(char c) { Contract.Ensures(Contract.Result<int>() == 1); return $\"{c}\".Length; }")]
    // A hole with control flow is evaluated before an earlier hole is
    // formatted in the model, but after it on the runtime: o.ToString() runs
    // before the division that always throws.
    [TestCase("[EnforcePure] public static string M(object o, bool b) { var z = 0; return $\"{o}{(b ? 1 / z : 2 / z)}\"; }")]
    public async Task UnmodeledFormattingStaysUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown), result.Reason.ToString());
    }

    // Formatting allocates the result; a fault after an interpolation is not
    // hidden. The formatted value is an approximation, so a counterexample
    // that reads it is not replayed and the claim may stay Unknown.
    [TestCase("[ZeroAllocations] public static string M(char c) { return $\"{c}\"; }")]
    [TestCase("[ZeroAllocations] public static string M(int x) { return $\"{x}\"; }")]
    [TestCase("[ZeroAllocations] public static string M(object o) { return $\"{o}\"; }")]
    [TestCase("[DoesNotThrow] public static int M(int x, string s) { var text = $\"{x}\"; return s.Length; }")]
    [TestCase("[DoesNotThrow] public static int M(bool flag, int d) { var text = $\"{flag}\"; return 10 / d; }")]
    public async Task AllocationsAndLaterFaultsAreNotProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
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
