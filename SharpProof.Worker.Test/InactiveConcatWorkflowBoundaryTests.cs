using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class InactiveConcatWorkflowBoundaryTests
{
    [TestCase("Contract.Result<bool>() == (unchecked(a.Length + b.Length + c.Length) >= 0)", WorkerClaimOutcome.Unknown)]
    [TestCase("Contract.Result<bool>() == true", WorkerClaimOutcome.Proven)]
    [TestCase("Contract.Result<bool>() == false", WorkerClaimOutcome.Refuted)]
    public async Task UntakenConcatenationCannotProveAnEntryLengthSumBound(string postcondition, WorkerClaimOutcome expected)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { " +
            "public static bool Target(bool flag, string a, string b, string c) { " +
            "Contract.Requires(a != null && b != null && c != null); Contract.Ensures(" + postcondition + "); " +
            "if (flag) { var joined = a + b + c; return joined == joined; } return true; } }", cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }
}
