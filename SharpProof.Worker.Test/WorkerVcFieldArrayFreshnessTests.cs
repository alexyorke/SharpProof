using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerVcFieldArrayFreshnessTests
{
    [TestCase("var fresh = new int[1]; return box.Data[0];", 7, 7, WorkerClaimOutcome.Proven)]
    [TestCase("var fresh = new int[] { 0 }; return box.Data[0];", 7, 7, WorkerClaimOutcome.Proven)]
    [TestCase("var fresh = new int[1]; return box.Data[0];", 0, 7, WorkerClaimOutcome.Refuted)]
    [TestCase("var fresh = new int[1]; box.Data[0] = 9; return box.Data[0];", 9, 9, WorkerClaimOutcome.Proven)]
    public async Task FreshArrayDoesNotAliasAnArrayReachedThroughAField(string body, int contractResult,
        int runtimeResult, WorkerClaimOutcome expected)
    {
        const string prefix = "public sealed class Box { public int[] Data; } public static class Subject { ";
        var source = "using SharpProof.Attributes; " + prefix + "public static int Target(Box box) { " +
            "Contract.Requires(box != null && box.Data != null && box.Data.Length == 1 && box.Data[0] == 7); " +
            "Contract.Ensures(Contract.Result<int>() == " + contractResult + "); " + body + " } }";
        AssertRuntimeResult(prefix + "public static int Target(Box box) { " + body + " } }", runtimeResult);
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
        Assert.That(TestCompilation.Create("FieldArrayRuntime", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("FieldArrayRuntime", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var boxType = assembly.GetType("Box")!;
            var box = Activator.CreateInstance(boxType)!;
            var values = new int[1];
            values[0] = 7;
            boxType.GetField("Data")!.SetValue(box, values);
            var type = assembly.GetType("Subject")!;
            Assert.That(type.GetMethod("Target")!.Invoke(null, new[] { box }), Is.EqualTo(expected));
        }
        finally { context.Unload(); }
    }
}
