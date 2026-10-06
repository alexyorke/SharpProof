using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class LateFieldFreshnessControlTests
{
    [TestCase("int[]", "var fresh = new int[1]; box.Data = fresh; return fresh == box.Data;", true, WorkerClaimOutcome.Proven)]
    [TestCase("int[]", "var fresh = new int[1]; box.Data = fresh; return fresh == box.Data;", false, WorkerClaimOutcome.Refuted)]
    [TestCase("int[]", "var fresh = new int[1]; var alias = box; alias.Data = fresh; return fresh == box.Data;", true, WorkerClaimOutcome.Proven)]
    [TestCase("int[]", "var fresh = new int[1]; var alias = box; alias.Data = fresh; return fresh == box.Data;", false, WorkerClaimOutcome.Refuted)]
    [TestCase("object", "object fresh = new int[1]; return fresh == box.Data;", false, WorkerClaimOutcome.Proven)]
    [TestCase("object", "object fresh = new int[1]; return fresh == box.Data;", true, WorkerClaimOutcome.Refuted)]
    [TestCase("Node", "var fresh = new Node(); return fresh == box.Data;", false, WorkerClaimOutcome.Proven)]
    [TestCase("Node", "var fresh = new Node(); return fresh == box.Data;", true, WorkerClaimOutcome.Refuted)]
    public async Task EntryFreshnessPreservesStoredAliasesAndReferenceViews(string fieldType, string body,
        bool expectedResult, WorkerClaimOutcome expected)
    {
        await Verify("public " + fieldType + " Data;", "", "Contract.Result<bool>() == " +
            (expectedResult ? "true" : "false"), body, expected);
    }

    [TestCase("Contract.Result<bool>() == !allocate", WorkerClaimOutcome.Proven)]
    [TestCase("Contract.Result<bool>() == allocate", WorkerClaimOutcome.Refuted)]
    public async Task FreshnessIsGuardedByAllocationReachability(string ensures, WorkerClaimOutcome expected)
    {
        await Verify("public int[] Data;", ", bool allocate", ensures,
            "int[] value; if (allocate) value = new int[1]; else value = box.Data; return value == box.Data;", expected);
    }

    [Test]
    public async Task StoredFreshArrayRemainsDistinctFromOldFieldSnapshot()
    {
        await Verify("public int[] Data;", "", "box.Data != Contract.Old(box.Data)",
            "box.Data = new int[1]; return true;", WorkerClaimOutcome.Proven);
    }

    private static async Task Verify(string field, string parameters, string ensures, string body, WorkerClaimOutcome expected)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public sealed class Node { } " +
            "public sealed class Box { " + field + " } public static class Subject { public static bool Target(Box box" +
            parameters + ") { Contract.Requires(box != null); Contract.Ensures(" + ensures + "); " + body + " } }",
            cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }
}
