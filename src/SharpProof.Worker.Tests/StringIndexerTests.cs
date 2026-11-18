using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// `s[i]` faults on a null string and on an index outside [0, s.Length), and
// otherwise reads a character whose value is not modeled.
[TestFixture]
public sealed class StringIndexerTests
{
    private const string Types = "using SharpProof.Attributes;\n";

    [TestCase("[DoesNotThrow] public static char M(string s) { if (s == null || s.Length == 0) { return default; } return s[0]; }")]
    [TestCase("[DoesNotThrow] public static char M(string s) { return s != null && s.Length > 2 ? s[2] : ' '; }")]
    [TestCase("[DoesNotThrow] public static char M(string s, int i) { return s != null && i >= 0 && i < s.Length ? s[i] : ' '; }")]
    [TestCase("[DoesNotThrow] public static int M(string s) { return s != null && s.Length > 2 ? s[2] : 0; }")]
    [TestCase("[EnforcePure] public static bool M(string s) { return s != null && s.Length > 0 && s[0] == 'a'; }")]
    public async Task GuardedReadsAreProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    [TestCase("[DoesNotThrow] public static char M(string s) { return s[0]; }")]
    [TestCase("[DoesNotThrow] public static char M(string s) { return s == null ? ' ' : s[s.Length]; }")]
    [TestCase("[DoesNotThrow] public static char M(string s, int i) { return s == null ? ' ' : s[i]; }")]
    public async Task UnguardedReadsAreRefuted(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
    }

    // The character itself is unknown, so a claim about it stays Unknown.
    [TestCase("public static char M() { Contract.Ensures(Contract.Result<char>() == 'a'); return \"abc\"[0]; }")]
    public async Task CharacterValuesStayUnknown(string member)
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
