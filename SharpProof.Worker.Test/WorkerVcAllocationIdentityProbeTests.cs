using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerVcAllocationIdentityProbeTests
{
    [TestCase("new int[1]", "new bool[1]", false, WorkerClaimOutcome.Proven)]
    [TestCase("new int[1]", "new object()", false, WorkerClaimOutcome.Proven)]
    [TestCase("new object()", "new int[1]", false, WorkerClaimOutcome.Proven)]
    [TestCase("new int[1]", "new int[1]", false, WorkerClaimOutcome.Proven)]
    [TestCase("new int[1]", "new bool[1]", true, WorkerClaimOutcome.Refuted)]
    public async Task FreshAllocationsHaveDistinctObjectIdentities(string first, string second,
        bool expectedResult, WorkerClaimOutcome expected)
    {
        var body = "object first = " + first + "; object second = " + second + "; return first == second;";
        var declaration = "public static class Subject { public static bool Target() { ";
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("AllocationIdentityRuntime", declaration + body + " } }")
            .Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("AllocationIdentityRuntime", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            Assert.That(assembly.GetType("Subject")!.GetMethod("Target")!.Invoke(null, null), Is.False);
        }
        finally { context.Unload(); }
        using var project = new ShadowTestProject("using SharpProof.Attributes; " + declaration +
            "Contract.Ensures(Contract.Result<bool>() == " + (expectedResult ? "true" : "false") + "); " + body + " } }",
            cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }
}
