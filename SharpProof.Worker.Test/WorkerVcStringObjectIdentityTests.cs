using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerVcStringObjectIdentityTests
{
    [TestCase("object first = value; object second = value; return first == second;", true, true, WorkerClaimOutcome.Proven)]
    [TestCase("object first = value; object second = value; return first == second;", false, true, WorkerClaimOutcome.Refuted)]
    [TestCase("object first = value; object second = new int[1]; return first == second;", false, false, WorkerClaimOutcome.Proven)]
    [TestCase("object first = value; object second = other; return first == second;", true, false, WorkerClaimOutcome.Refuted)]
    public async Task WideningPreservesInputStringIdentity(string body, bool contractResult,
        bool runtimeResult, WorkerClaimOutcome expected)
    {
        const string prefix = "public static class Subject { public static bool Target(string value, string other) { ";
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("StringObjectRuntime", prefix + body + " } }").Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("StringObjectRuntime", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("Subject")!;
            var value = new string('x', 1);
            var other = new string('x', 1);
            Assert.That(ReferenceEquals(value, other), Is.False);
            Assert.That(type.GetMethod("Target")!.Invoke(null, new object[] { value, other }), Is.EqualTo(runtimeResult));
        }
        finally { context.Unload(); }
        using var project = new ShadowTestProject("using SharpProof.Attributes; " + prefix +
            "Contract.Requires(value != null && other != null && value == \"x\" && other == \"x\"); " +
            "Contract.Ensures(Contract.Result<bool>() == " + (contractResult ? "true" : "false") + "); " + body + " } }",
            cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }
}
