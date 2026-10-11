using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class StringFromEndIndexBoundaryTests
{
    [Test]
    public async Task NullReceiverFromEndIndexMustNotProveDoesNotThrow()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                [DoesNotThrow]
                public static char Target(string value) => value[^1];
            }
            """;

        Assert.Throws<NullReferenceException>(new Action(() => { _ = Runtime(null!); }));
        Assert.Throws<IndexOutOfRangeException>(new Action(() => { _ = Runtime(""); }));

        using var project = new ShadowTestProject(source, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }

    [TestCase("value != null && value.Length > 0", WorkerClaimOutcome.Proven)]
    [TestCase("value != null && value.Length == 0", WorkerClaimOutcome.Refuted)]
    public async Task FromEndIndexRespectsStringLengthGuard(string precondition, WorkerClaimOutcome expected)
    {
        using var project = new ShadowTestProject(
            "using SharpProof.Attributes; public static class Subject { [DoesNotThrow] " +
            "public static char Target(string value) { Contract.Requires(" + precondition + "); return value[^1]; } }",
            cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }

    private static char Runtime(string value) { return value[^1]; }
}
