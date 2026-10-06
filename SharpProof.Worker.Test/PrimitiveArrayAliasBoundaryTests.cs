using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class PrimitiveArrayAliasBoundaryTests
{
    [Test]
    public async Task SignedArrayStoreCannotProveUnsignedAliasUnchanged()
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { " +
            "public static uint Target(int[] written, uint[] observed) { " +
            "Contract.Requires(written != null && observed != null && written.Length == 1 && observed.Length == 1 && observed[0] == 0U); " +
            "Contract.Ensures(Contract.Result<uint>() == 0U); written[0] = 1; return observed[0]; } }", cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public void ClrSignedAndUnsignedViewsSharePrimitiveArrayStorage()
    {
        const string source = "public static class Subject { " +
            "public static uint Target(int[] written, uint[] observed) { written[0] = 1; return observed[0]; } " +
            "public static uint Witness() { var values = new int[1]; return Target(values, (uint[])(object)values); } }";
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("PrimitiveArrayAliasRuntime", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("PrimitiveArrayAliasRuntime", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            Assert.That(assembly.GetType("Subject")!.GetMethod("Witness")!.Invoke(null, null), Is.EqualTo(1U));
        }
        finally
        {
            context.Unload();
        }
    }
}
