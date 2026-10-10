using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// The CLR rejects a string longer than its maximum length (0x3FFFFFDF) with
// OutOfMemoryException whatever memory is free, so String.Concat of operands
// whose total length exceeds it throws although each operand exists.
[TestFixture]
public sealed class StringConcatenationMaxLengthTests
{
    [TestCase("public static string Target(string a, string b) { return a + b; }")]
    [TestCase("public static string Target(string a, string b) { Contract.Requires(a != null && b != null); return a + b; }")]
    [TestCase("public static string Target(string a) { return a + \"!\"; }")]
    [TestCase("public static string Target(string a, string b) { return string.Concat(a, b); }")]
    [TestCase("public static int Target(string a, string b, string c) { return (a + b + c).Length; }")]
    public async Task TotalBeyondTheRuntimeMaximumMayThrow(string declaration)
    {
        Assert.That(await Verify(declaration), Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    [TestCase("public static string Target(string a, string b) { Contract.Requires(a != null && b != null && a.Length <= 1000 && b.Length <= 1000); return a + b; }", WorkerClaimOutcome.Proven)]
    [TestCase("public static string Target(string a, string b) { Contract.Requires(a != null && b != null && a.Length <= 0x3FFFFFDF && b.Length <= 0x3FFFFFDF && a.Length + b.Length <= 0x3FFFFFDF); return a + b; }", WorkerClaimOutcome.Proven)]
    [TestCase("public static string Target(string a, string b) { Contract.Requires(b == \"\"); return a + b; }", WorkerClaimOutcome.Proven)]
    [TestCase("public static int Target(string a) { Contract.Requires(a != null && a.Length < 100); return (a + \"!\" + a).Length; }", WorkerClaimOutcome.Proven)]
    [TestCase("public static int Target(int z) { return (\"x\" + \"y\").Length; }", WorkerClaimOutcome.Proven)]
    [TestCase("public static string Target(string a) { return a + \"\"; }", WorkerClaimOutcome.Proven)]
    [TestCase("public static int Target(string a, string b) { Contract.Requires(a != null && b != null && a.Length == 1 && b.Length == 1); return 10 / ((a + b).Length - 2); }", WorkerClaimOutcome.Refuted)]
    public async Task BoundedTotalsKeepTheirOutcome(string declaration, WorkerClaimOutcome expected)
    {
        Assert.That(await Verify(declaration), Is.EqualTo(expected));
    }

    // A concatenation merged with another branch's value must replay: the
    // merge keeps its value expression rather than equating a fresh reference
    // with a new allocation.
    [TestCase("flag", true, WorkerClaimOutcome.Refuted)]
    [TestCase("!flag", true, WorkerClaimOutcome.Refuted)]
    [TestCase("flag", false, WorkerClaimOutcome.Proven)]
    [TestCase("!flag", false, WorkerClaimOutcome.Proven)]
    public async Task MergedConcatenationsReplayOnEitherBranch(string guard, bool invalid, WorkerClaimOutcome expected)
    {
        const string Expression = "(flag ? a + b + c : a)";
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { " +
            "public static string Target(bool flag, string a, string b, string c) { " +
            "Contract.Requires(" + guard + " && a == \"a\" && b == \"b\" && c == \"c\"); " +
            "Contract.Ensures(Contract.Result<string>() " + (invalid ? "!=" : "==") + " " + Expression + "); " +
            "return " + Expression + "; } }", cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }

    [Test]
    public void RuntimeRejectsStringsBeyondItsMaximumLength()
    {
        var length = 0x3FFFFFE0;
        Assert.Throws<OutOfMemoryException>(new Action(() => AllocateString(length)));
    }

    private static void AllocateString(int length)
    {
        _ = new string('a', length);
    }

    private static async Task<WorkerClaimOutcome> Verify(string declaration)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { [DoesNotThrow] " +
            declaration + " }", cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        await TestContext.Out.WriteLineAsync(claim.Outcome + " " + claim.Reason);
        return claim.Outcome;
    }
}
