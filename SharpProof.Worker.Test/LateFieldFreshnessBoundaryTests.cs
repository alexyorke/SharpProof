using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class LateFieldFreshnessBoundaryTests
{
    [TestCase(false, WorkerClaimOutcome.Proven)]
    [TestCase(true, WorkerClaimOutcome.Refuted)]
    public async Task FreshArrayCannotAliasAnEntryFieldFirstReadAfterAllocation(bool expectedResult, WorkerClaimOutcome expected)
    {
        const string body = "var fresh = new int[1]; return fresh == box.Data;";
        const string declarations = "public sealed class Box { public int[] Data; } public static class Subject { ";
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("LateFieldFreshnessRuntime", declarations +
            "public static bool Target(Box box) { " + body + " } }").Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("LateFieldFreshnessRuntime", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var boxType = assembly.GetType("Box")!;
            var box = Activator.CreateInstance(boxType)!;
            var target = assembly.GetType("Subject")!.GetMethod("Target")!;
            Assert.That(target.Invoke(null, new[] { box }), Is.False);
            boxType.GetField("Data")!.SetValue(box, new int[1]);
            Assert.That(target.Invoke(null, new[] { box }), Is.False);
        }
        finally { context.Unload(); }
        using var project = new ShadowTestProject("using SharpProof.Attributes; " + declarations +
            "public static bool Target(Box box) { Contract.Requires(box != null); " +
            "Contract.Ensures(Contract.Result<bool>() == " + (expectedResult ? "true" : "false") + "); " + body + " } }",
            cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }
}
