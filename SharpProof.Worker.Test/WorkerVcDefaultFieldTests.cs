using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerVcDefaultFieldTests
{
    [TestCase("public int Value;", "return cell.Value;", 0, 0, WorkerClaimOutcome.Proven)]
    [TestCase("public bool Value;", "return cell.Value ? 1 : 0;", 0, 0, WorkerClaimOutcome.Proven)]
    [TestCase("public int Value;", "cell.Value = 7; return cell.Value;", 7, 7, WorkerClaimOutcome.Proven)]
    [TestCase("public int Value;", "return cell.Value;", 1, 0, WorkerClaimOutcome.Refuted)]
    public async Task FreshFieldsRespectRuntimeInitialization(string field, string body, int contractResult,
        int runtimeResult, WorkerClaimOutcome expected)
    {
        var source = "using SharpProof.Attributes; public sealed class Cell { " + field +
            " } public static class Subject { public static int Target() { " +
            "Contract.Ensures(Contract.Result<int>() == " + contractResult + "); " +
            "var cell = new Cell(); " + body + " } }";
        var runtimeSource = "public sealed class Cell { " + field +
            " } public static class Subject { public static int Target() { " +
            "var cell = new Cell(); " + body + " } }";
        AssertRuntimeResult(runtimeSource, runtimeResult);
        using var project = new ShadowTestProject(source, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }

    private static void AssertRuntimeResult(string source, int expected)
    {
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("DefaultFieldRuntime", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("DefaultFieldRuntime", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("Subject")!;
            Assert.That(type.GetMethod("Target")!.Invoke(null, null), Is.EqualTo(expected));
        }
        finally { context.Unload(); }
    }
}
