using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Null patterns over type-parameter values, string constant patterns, and
// negated or empty property patterns over references.
[TestFixture]
public sealed class PatternFormTests
{
    private const string Types = """
        using SharpProof.Attributes;
        public class Block { public Block Parent; }
        public sealed class Paragraph : Block { }
        """;

    [TestCase("[EnforcePure] public static bool M<T>(T x) { return x is null; }")]
    [TestCase("[EnforcePure] public static bool M<T>(T x) where T : class { return x is not null; }")]
    [TestCase("[DoesNotThrow] public static T M<T>(T x) where T : class { if (x is null) { return null; } return x; }")]
    [TestCase("public static bool M(string s) { Contract.Ensures(Contract.Result<bool>() == (s == \".\" || s == \",\")); return s is \".\" or \",\"; }")]
    [TestCase("public static bool M(string s) { Contract.Ensures(Contract.Result<bool>() == !(s == \".\" || s == \",\")); return s is not (\".\" or \",\"); }")]
    [TestCase("[DoesNotThrow] public static int M(Block b) { if (b is not Paragraph p) { return 0; } return 1; }")]
    [TestCase("[DoesNotThrow] public static int M(Block b) { if (b == null || b.Parent is not { } parent) { return 0; } return parent.Parent == null ? 1 : 2; }")]
    [TestCase("public static int M(Block b) { Contract.Ensures(Contract.Result<int>() == (b == null ? 0 : 1)); return b is { } x ? 1 : 0; }")]
    // An inlined generic helper specialized to a scalar: the value is never null.
    [TestCase("private static bool IsNull<T>(T x) { return x is null; } public static bool M(int i) { Contract.Ensures(!Contract.Result<bool>()); return IsNull(i); }")]
    public async Task PatternsAreProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [TestCase("public static bool M<T>(T x) where T : class { Contract.Ensures(Contract.Result<bool>()); return x is null; }")]
    [TestCase("public static bool M(string s) { Contract.Ensures(Contract.Result<bool>()); return s is \".\"; }")]
    [TestCase("[DoesNotThrow] public static int M(Block b) { if (b is not { } x) { return b.Parent == null ? 0 : 1; } return 2; }")]
    [TestCase("[DoesNotThrow] public static int M(Block b) { return b.Parent is not { } parent ? 0 : 1; }")]
    public async Task WrongPatternClaimsAreRefuted(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
    }

    // A pattern constant that is not well-formed UTF-16 is left unmodeled.
    [TestCase("public static bool M(string s) { Contract.Ensures(!Contract.Result<bool>() || s != null); return s is \"\\uD800\"; }")]
    public async Task MalformedStringPatternsStayUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
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
