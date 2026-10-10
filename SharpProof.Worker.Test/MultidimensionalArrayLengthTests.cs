using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Total IR keeps a multidimensional array as an opaque reference, so its
// Length is not the intrinsic sequence length that single-dimensional arrays
// and strings lower to.
[TestFixture]
public sealed class MultidimensionalArrayLengthTests
{
    [TestCase("int Target(int[,] a)", "a != null", "Contract.Result<int>() == 1", "return a.Length;")]
    [TestCase("int Target(int[,] a)", "a != null && a.Length == 1", "Contract.Result<int>() == 0", "return 1;")]
    [TestCase("void Target(int[,] a)", "a != null", "a.Length == 1", "")]
    [TestCase("int Target(string[,] a)", "a != null && a.Length == 1", "Contract.Result<int>() == 0", "return 1;")]
    [TestCase("int Target(int[,,] a)", "a != null", "Contract.Result<int>() == 1", "return a.Length;")]
    [TestCase("int Target(int z)", "true", "Contract.Result<int>() == 1", "var a = new int[2, 2]; return a.Length;")]
    [TestCase("int Target(Box box)", "box != null && box.Cells != null", "Contract.Result<int>() == 1", "return box.Cells.Length;")]
    public async Task MultidimensionalLengthIsNotSequenceLength(string signature, string requires, string ensures, string body)
    {
        Assert.That(await Verify(signature, requires, ensures, body), Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    [TestCase("int Target(int[] a)", "a != null && a.Length == 1", "Contract.Result<int>() == 1", "return a.Length;", WorkerClaimOutcome.Proven)]
    [TestCase("int Target(int[] a)", "a != null && a.Length == 1", "Contract.Result<int>() == 2", "return a.Length;", WorkerClaimOutcome.Refuted)]
    [TestCase("void Target(int[] a)", "a != null && a.Length == 1", "a.Length == 1", "", WorkerClaimOutcome.Proven)]
    [TestCase("int Target(string s)", "s != null && s.Length == 3", "Contract.Result<int>() == 3", "return s.Length;", WorkerClaimOutcome.Proven)]
    [TestCase("int Target(int z)", "true", "Contract.Result<int>() == 4", "var a = new int[4]; return a.Length;", WorkerClaimOutcome.Proven)]
    [TestCase("int Target(int z)", "true", "Contract.Result<int>() == 5", "var a = new int[4]; return a.Length;", WorkerClaimOutcome.Refuted)]
    public async Task SingleDimensionalAndStringLengthsRemainExact(string signature, string requires, string ensures, string body,
        WorkerClaimOutcome expected)
    {
        Assert.That(await Verify(signature, requires, ensures, body), Is.EqualTo(expected));
    }

    private static async Task<WorkerClaimOutcome> Verify(string signature, string requires, string ensures, string body)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public sealed class Box { public int[,] Cells; } " +
            "public static class Subject { public static " + signature + " { Contract.Requires(" + requires + "); Contract.Ensures(" +
            ensures + "); " + body + " } }", cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        await TestContext.Out.WriteLineAsync(claim.Outcome + " " + claim.Reason);
        return claim.Outcome;
    }
}
