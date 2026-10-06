using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerVcArrayInitializerAfterLoopTests
{
    [TestCase("new int[] { 3, 4 }", "return values[0];", 3)]
    [TestCase("new int[] { 3, 4 }", "return values[1];", 4)]
    [TestCase("new int[] { 3, 4 }", "values[0] = 7; return values[1];", 4)]
    [TestCase("new int[2]", "return values[0];", 0)]
    public async Task NewArrayInitializationSurvivesAnEarlierElementWritingLoop(string creation, string body, int expected)
    {
        const string members = "";
        var method = "var previous = new int[2]; for (int i = 0; i < 2; i++) previous[i] = 7; var values = " + creation + "; " + body;
        AssertRuntimeResult(members + "public static class Subject { public static int Target() { " + method + " } }", expected);
        using var project = new ShadowTestProject("using SharpProof.Attributes; " + members +
            "public static class Subject { public static int Target() { Contract.Ensures(Contract.Result<int>() == " +
            expected + "); " + method + " } }", cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    private static void AssertRuntimeResult(string source, int expected)
    {
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("ArrayInitializerAfterLoopRuntime", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("ArrayInitializerAfterLoopRuntime", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("Subject")!;
            Assert.That(type.GetMethod("Target")!.Invoke(null, null), Is.EqualTo(expected));
        }
        finally { context.Unload(); }
    }
}
