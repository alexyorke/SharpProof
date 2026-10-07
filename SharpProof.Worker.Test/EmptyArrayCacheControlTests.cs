using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class EmptyArrayCacheControlTests
{
    [TestCase("[ZeroAllocations]", "return 7;", WorkerClaimOutcome.Proven)]
    [TestCase("[ZeroAllocations]", "return new Cell[0].Length;", WorkerClaimOutcome.Refuted)]
    [TestCase("[ZeroAllocations]", "return Count(new Cell[0]);", WorkerClaimOutcome.Refuted)]
    [TestCase("[ZeroAllocations]", "Contract.Requires(x != 0); if (x == 0) return System.Array.Empty<Cell>().Length; return 7;", WorkerClaimOutcome.Proven)]
    [TestCase("[ZeroAllocations]", "Contract.Requires(x != 0); if (x == 0) return Count(); return 7;", WorkerClaimOutcome.Proven)]
    [TestCase("", "Contract.Ensures(Contract.Result<int>() == 0); return System.Array.Empty<Cell>().Length;", WorkerClaimOutcome.Proven)]
    [TestCase("", "Contract.Ensures(Contract.Result<int>() == 1); return System.Array.Empty<Cell>().Length;", WorkerClaimOutcome.Refuted)]
    [TestCase("", "Contract.Ensures(Contract.Result<int>() == 0); return Count();", WorkerClaimOutcome.Proven)]
    [TestCase("", "Contract.Ensures(Contract.Result<int>() == 1); return Count();", WorkerClaimOutcome.Refuted)]
    [TestCase("", "Contract.Ensures(Contract.Result<int>() == 1); return System.Array.Empty<Cell>() == System.Array.Empty<Cell>() ? 1 : 0;", WorkerClaimOutcome.Proven)]
    [TestCase("", "Contract.Ensures(Contract.Result<int>() == 0); return System.Array.Empty<Cell>() == System.Array.Empty<Cell>() ? 1 : 0;", WorkerClaimOutcome.Refuted)]
    public async Task ExactValueAndExecutedAllocationControls(string attributes, string body, WorkerClaimOutcome expected)
    {
        var source = "using SharpProof.Attributes; public sealed class Cell {} public static class Subject { " + attributes +
            " public static int Target(int x) { " + body + " } private static int Count(params Cell[] values) => values.Length; }";
        using var project = new ShadowTestProject(source, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }
}
