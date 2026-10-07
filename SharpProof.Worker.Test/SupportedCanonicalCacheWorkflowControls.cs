using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class SupportedCanonicalCacheWorkflowControls
{
    [TestCase("System.Array.Empty<int>() == System.Array.Empty<int>()", true)]
    [TestCase("System.Array.Empty<Alias>() == System.Array.Empty<int>()", true)]
    [TestCase("System.Array.Empty<object?>() == System.Array.Empty<object>()", true)]
    [TestCase("System.Array.Empty<int>() != System.Array.Empty<int>()", false)]
    public async Task SameRuntimeElementTypeKeepsOneCacheIdentity(string expression, bool expected)
    {
        var source = "#undef SHARPPROOF_CONTRACTS\n#nullable enable\nusing Alias = System.Int32; using SharpProof.Attributes; public static class Subject { " +
            "public static bool Target() { Contract.Ensures(Contract.Result<bool>()); return " + expression + "; } }";
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("SameCacheRuntime", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("SameCacheRuntime", isCollectible: true);
        try
        {
            var target = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!.CreateDelegate<Func<bool>>();
            Assert.That(target(), Is.EqualTo(expected));
        }
        finally { runtime.Unload(); }
        using var project = new ShadowTestProject(source, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(expected ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }
}
