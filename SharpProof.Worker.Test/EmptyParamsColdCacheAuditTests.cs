using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class EmptyParamsColdCacheAuditTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ColdEmptyArrayInitializationCannotProveZeroAllocations(bool explicitCall)
    {
        var body = explicitCall ? "return System.Array.Empty<Cell>().Length;" : "return Count();";
        var source = "using SharpProof.Attributes; public sealed class Cell { } public static class Subject { " +
            "[ZeroAllocations, System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | " +
            "System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)] public static int Target() { " + body + " } " +
            "[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | " +
            "System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)] public static int Control() => 0; " +
            "private static int Count(params Cell[] values) => values.Length; }";
        using var image = new MemoryStream();
        Assert.That(TestCompilation.Create("ColdParamsOracle", source).Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("ColdParamsOracle", isCollectible: true);
        try
        {
            var type = runtime.LoadFromStream(image).GetType("Subject")!;
            var target = type.GetMethod("Target")!.CreateDelegate<Func<int>>();
            var control = type.GetMethod("Control")!.CreateDelegate<Func<int>>();
            Assert.That(control(), Is.Zero);
            var beforeControl = GC.GetAllocatedBytesForCurrentThread();
            var controlResult = control();
            var controlBytes = GC.GetAllocatedBytesForCurrentThread() - beforeControl;
            var beforeCold = GC.GetAllocatedBytesForCurrentThread();
            var coldResult = target();
            var coldBytes = GC.GetAllocatedBytesForCurrentThread() - beforeCold;
            var beforeWarm = GC.GetAllocatedBytesForCurrentThread();
            var warmResult = target();
            var warmBytes = GC.GetAllocatedBytesForCurrentThread() - beforeWarm;
            Assert.That(controlResult, Is.Zero);
            Assert.That(controlBytes, Is.Zero);
            Assert.That(coldResult, Is.Zero);
            Assert.That(coldBytes, Is.GreaterThan(0), "Fresh element type's first empty-array cache use must allocate");
            Assert.That(warmResult, Is.Zero);
            Assert.That(warmBytes, Is.Zero);
            await TestContext.Progress.WriteLineAsync("explicit=" + explicitCall + "; cold=" + coldBytes + "; warm=" + warmBytes);
        }
        finally { runtime.Unload(); }
        using var project = new ShadowTestProject(source, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }
}
