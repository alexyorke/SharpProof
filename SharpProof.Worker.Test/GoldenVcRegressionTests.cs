using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class GoldenVcRegressionTests
{
    public static IEnumerable<string> Cases()
    {
        return GoldenTest.Cases("vc");
    }

    [TestCaseSource(nameof(Cases))]
    public async Task TypedVerificationPreservesRetiredRegression(string caseName)
    {
        var fixture = GoldenTest.Load("vc", caseName);
        var depthLimited = fixture.Source.StartsWith("// vc-depth-limit: 3", StringComparison.Ordinal);
        using var project = new ShadowTestProject(CompilerTotalCallableArtifactTests.CreateArtifact(fixture.Source,
            depthLimited ? 3 : WorkerBudgets.DefaultMaximumExpressionDepth));
        if (depthLimited)
        {
            project.Request.Budgets.MaximumExpressionDepth = 3;
        }
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var snapshot = project.Bind();
        var response = await worker.VerifyAsync(project.Request, snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty, string.Join(",", response.Errors.Select(error => error.Code + ":" + error.Message)));
        Assert.That(response.ClaimResults, Is.Not.Empty);
        if (depthLimited)
        {
            Assert.That(response.ClaimResults.Select(result => result.Outcome),
                Is.EqualTo(new[] { WorkerClaimOutcome.Proven, WorkerClaimOutcome.Unknown }));
            Assert.That(response.ClaimResults[1].Reason, Is.EqualTo(WorkerClaimReason.DeepPostcondition));
        }
        else
        {
            Assert.That(response.ClaimResults.Select(result => result.Outcome), Is.All.EqualTo(WorkerClaimOutcome.Proven),
                string.Join(",", response.ClaimResults.Select(result => result.Reason)));
        }
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var rows = response.Manifest.Claims.Zip(response.ClaimResults, (claim, result) =>
            claim.CallableId + " " + result.Outcome + "/" + result.Reason + "/" + result.Vacuity +
            " core=[" + string.Join(",", result.ProofCore) + "]" +
            " assumptions=[" + string.Join(",", result.Assumptions.Select(assumption => assumption.Kind + ":" + assumption.Used)) + "]");
        GoldenTest.Compare(fixture, string.Join("\n", rows) + "\n");
    }
}
