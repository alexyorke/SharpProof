using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerDynamicReferenceSoundnessTests
{
    [TestCase("class", "string")]
    [TestCase("sealed class", "string")]
    [TestCase("class", "int[]")]
    [TestCase("sealed class", "int[]")]
    public async Task NonemptyBuiltinInputCannotAliasAnUnrelatedClass(string declaration, string valueType)
    {
        var members = "public " + declaration + " Cell { } public static class Subject { public static bool Target(Cell cell, " +
            valueType + " value) { ";
        const string body = "return (object)value != (object)cell;";
        AssertRuntimeResult(members + body + " } }", valueType);
        using var project = new ShadowTestProject("using SharpProof.Attributes; " + members +
            "Contract.Requires(value != null && value.Length == 1); Contract.Ensures(Contract.Result<bool>()); " +
            body + " } }", cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }

    private static void AssertRuntimeResult(string source, string valueType)
    {
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("DynamicReferenceSoundnessRuntime", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("DynamicReferenceSoundnessRuntime", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var cell = Activator.CreateInstance(assembly.GetType("Cell")!)!;
            object value = valueType == "string" ? "x" : new int[1];
            Assert.That(assembly.GetType("Subject")!.GetMethod("Target")!.Invoke(null, [cell, value]), Is.EqualTo(true));
        }
        finally { context.Unload(); }
    }
}
