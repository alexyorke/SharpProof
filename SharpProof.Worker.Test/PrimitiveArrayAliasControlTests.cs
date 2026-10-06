using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class PrimitiveArrayAliasControlTests
{
    private static readonly string[][] Pairs =
    [
        ["sbyte", "byte", "-1", "byte.MaxValue"],
        ["byte", "sbyte", "byte.MaxValue", "-1"],
        ["short", "ushort", "-1", "ushort.MaxValue"],
        ["ushort", "short", "ushort.MaxValue", "-1"],
        ["int", "uint", "-1", "uint.MaxValue"],
        ["uint", "int", "uint.MaxValue", "-1"],
        ["long", "ulong", "-1L", "ulong.MaxValue"],
        ["ulong", "long", "ulong.MaxValue", "-1L"]
    ];

    private static IEnumerable<TestCaseData> CompatibleCases()
    {
        foreach (var pair in Pairs)
        {
            yield return new TestCaseData(pair[0], pair[1], pair[2], pair[3]);
        }
    }

    [TestCaseSource(nameof(CompatibleCases))]
    public async Task CompatibleViewStoreCannotProvePreservation(string left, string right, string stored, string expected)
    {
        _ = expected;
        var outcome = await Verify(left, right, stored, "0", "");
        Assert.That(outcome.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), outcome.Reason.ToString());
    }

    [TestCaseSource(nameof(CompatibleCases))]
    public async Task AliasedViewsObserveStoredBits(string left, string right, string stored, string expected)
    {
        var outcome = await Verify(left, right, stored, expected, " && (object)written == (object)observed");
        Assert.That(outcome.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), outcome.Reason.ToString());
    }

    [TestCaseSource(nameof(CompatibleCases))]
    public async Task SeparateViewsRetainEntryContents(string left, string right, string stored, string expected)
    {
        _ = expected;
        var outcome = await Verify(left, right, stored, "0", " && (object)written != (object)observed");
        Assert.That(outcome.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), outcome.Reason.ToString());
    }

    [TestCaseSource(nameof(CompatibleCases))]
    public void ClrAliasedViewsObserveStoredBits(string left, string right, string stored, string expected)
    {
        AssertRuntimeResult("public static class Subject { public static bool Witness() { var written = new " + left +
            "[1]; var observed = (" + right + "[])(object)written; written[0] = " + stored +
            "; return observed[0] == " + expected + "; } }");
    }

    [TestCase("short", "char", "1", "(char)0")]
    [TestCase("ushort", "char", "1", "(char)0")]
    [TestCase("byte", "bool", "1", "false")]
    [TestCase("int", "long", "1", "0L")]
    [TestCase("char", "ushort", "(char)1", "0")]
    public async Task UnrelatedArraysKeepStoresSeparate(string left, string right, string stored, string initial)
    {
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { public static " +
            right + " Target(" + left + "[] written, " + right + "[] observed) { " +
            "Contract.Requires(written != null && observed != null && written.Length == 1 && observed.Length == 1 && observed[0] == " + initial + "); " +
            "Contract.Ensures(Contract.Result<" + right + ">() == " + initial + "); written[0] = " + stored + "; return observed[0]; } }", cacheEnabled: false);
        var outcome = await VerifyProject(project);
        Assert.That(outcome.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), outcome.Reason.ToString());
    }

    [TestCase("int")]
    [TestCase("uint")]
    public async Task SameTypeAliasStillRefutesPreservation(string type)
    {
        var outcome = await Verify(type, type, "1", "0", " && written == observed");
        Assert.That(outcome.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), outcome.Reason.ToString());
    }

    private static async Task<WorkerClaimResult> Verify(string left, string right, string stored, string expected, string alias)
    {
        var branch = alias switch
        {
            " && (object)written == (object)observed" => "object writtenView = written; object observedView = observed; if (writtenView != observedView) return " + expected + "; ",
            " && (object)written != (object)observed" => "object writtenView = written; object observedView = observed; if (writtenView == observedView) return " + expected + "; ",
            _ => ""
        };
        var requiresAlias = branch.Length == 0 ? alias : "";
        using var project = new ShadowTestProject("using SharpProof.Attributes; public static class Subject { public static " +
            right + " Target(" + left + "[] written, " + right + "[] observed) { " +
            "Contract.Requires(written != null && observed != null && written.Length == 1 && observed.Length == 1 && observed[0] == 0" + requiresAlias + "); " +
            "Contract.Ensures(Contract.Result<" + right + ">() == " + expected + "); written[0] = " + stored + "; " + branch + "return observed[0]; } }", cacheEnabled: false);
        return await VerifyProject(project);
    }

    private static async Task<WorkerClaimResult> VerifyProject(ShadowTestProject project)
    {
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        await TestContext.Progress.WriteLineAsync(claim.Outcome + ": " + claim.Reason);
        return claim;
    }

    private static void AssertRuntimeResult(string source)
    {
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("PrimitiveArrayAliasControlRuntime", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("PrimitiveArrayAliasControlRuntime", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            Assert.That(assembly.GetType("Subject")!.GetMethod("Witness")!.Invoke(null, null), Is.EqualTo(true));
        }
        finally
        {
            context.Unload();
        }
    }
}
