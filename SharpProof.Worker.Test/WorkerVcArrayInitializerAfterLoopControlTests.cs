using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerVcArrayInitializerAfterLoopControlTests
{
    [TestCase("var values = new bool[] { true, false }; return values[0] ? 1 : 0;", 1, 1, WorkerClaimOutcome.Proven)]
    [TestCase("var values = new long[] { 3L, 4L }; return (int)values[1];", 4, 4, WorkerClaimOutcome.Proven)]
    [TestCase("var values = new uint[] { 3U, 4U }; return (int)values[1];", 4, 4, WorkerClaimOutcome.Proven)]
    [TestCase("var values = new int[] { 3, 4 }; values[0] = 7; return values[0];", 7, 7, WorkerClaimOutcome.Proven)]
    [TestCase("var values = new int[] { 3, 4 }; values[0] = 7; return values[0];", 3, 7, WorkerClaimOutcome.Refuted)]
    public async Task InitializationPreservesTypesAndLaterStores(string body, int claimed, int actual, WorkerClaimOutcome expected)
    {
        await CheckAsync("var previous = new int[2]; for (int i = 0; i < 2; i++) previous[i] = 7; " + body,
            claimed, actual, expected);
    }

    [Test]
    public async Task LaterElementWritingLoopInvalidatesEarlierInitializerValues()
    {
        await CheckAsync("var values = new int[] { 3, 4 }; for (int i = 0; i < 2; i++) values[i] = 7; return values[0];",
            3, 7, WorkerClaimOutcome.Refuted);
    }

    private static async Task CheckAsync(string body, int claimed, int actual, WorkerClaimOutcome expected)
    {
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("ArrayInitializerLoopControlRuntime",
            "public static class Subject { public static int Target() { " + body + " } }").Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("ArrayInitializerLoopControlRuntime", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("Subject")!;
            Assert.That(type.GetMethod("Target")!.Invoke(null, null), Is.EqualTo(actual));
        }
        finally { context.Unload(); }
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { " +
            "public static int Target() { Contract.Ensures(Contract.Result<int>() == " + claimed + "); " + body + " } }",
            cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }
}
