using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// The CLR rejects an array longer than its maximum element count with
// OutOfMemoryException ("Array dimensions exceeded supported range") whatever
// memory is free, so a nonnegative length alone does not make creation safe.
[TestFixture]
public sealed class ArrayCreationMaxLengthTests
{
    [TestCase("public static byte[] Target(int n) { Contract.Requires(n >= 0); return new byte[n]; }")]
    [TestCase("public static int Target(int n) { Contract.Requires(n >= 0); return new long[n].Length; }")]
    [TestCase("public static int Target(int z) { return new byte[int.MaxValue].Length; }")]
    [TestCase("public static int Target(int z) { int n = 2147483600; var a = new int[n]; return a.Length; }")]
    public async Task LengthBeyondTheRuntimeMaximumMayThrow(string declaration)
    {
        Assert.That(await Verify(declaration), Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    [TestCase("public static byte[] Target(int n) { Contract.Requires(n >= 0 && n <= 1000); return new byte[n]; }", WorkerClaimOutcome.Proven)]
    [TestCase("public static int Target(int n) { Contract.Requires(n >= 0 && n <= 2146435071); return new long[n].Length; }", WorkerClaimOutcome.Proven)]
    [TestCase("public static int Target(int z) { return new int[4].Length; }", WorkerClaimOutcome.Proven)]
    [TestCase("public static int Target(int z) { return new int[] { 1, 2 }.Length; }", WorkerClaimOutcome.Proven)]
    [TestCase("public static int Target(int n) { Contract.Requires(n == -1); return new int[n].Length; }", WorkerClaimOutcome.Refuted)]
    public async Task BoundedLengthsKeepTheirOutcome(string declaration, WorkerClaimOutcome expected)
    {
        Assert.That(await Verify(declaration), Is.EqualTo(expected));
    }

    [Test]
    public void RuntimeRejectsLengthsBeyondItsMaximum()
    {
        var length = int.MaxValue;
        Assert.Throws<OutOfMemoryException>(new Action(() => AllocateBytes(length)));
    }

    private static void AllocateBytes(int length)
    {
        _ = new byte[length];
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
