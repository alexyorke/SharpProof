using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class InactiveConcatWorkflowControlTests
{
    [TestCase(false, false, WorkerClaimOutcome.Proven)]
    [TestCase(false, true, WorkerClaimOutcome.Refuted)]
    [TestCase(true, false, WorkerClaimOutcome.Proven)]
    [TestCase(true, true, WorkerClaimOutcome.Refuted)]
    public async Task SmallActiveAndConditionalConcatenationsRetainExactContent(bool conditional, bool invalid, WorkerClaimOutcome expected)
    {
        var expression = conditional ? "(flag ? a + b + c : a)" : "a + b + c";
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { " +
            "public static string Target(bool flag, string a, string b, string c) { " +
            "Contract.Requires(a == \"a\" && b == \"b\" && c == \"c\"); " +
            "Contract.Ensures(Contract.Result<string>() " + (invalid ? "!=" : "==") + " " + expression + "); " +
            "return " + expression + "; } }", cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }

    [Test]
    public void UntakenBranchLengthWitnessWrapsWithoutConstructingAnyConcatenation()
    {
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("ConcatLengthScalarWitness", "public static class Subject { " +
            "public static int Target(int a, int b, int c) { return unchecked(a + b + c); } }").Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("ConcatLengthScalarWitness", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var target = assembly.GetType("Subject")!.GetMethod("Target")!;
            Assert.That(target.Invoke(null, new object[] { 800_000_000, 800_000_000, 800_000_000 }), Is.EqualTo(-1_894_967_296));
        }
        finally { context.Unload(); }
    }
}
