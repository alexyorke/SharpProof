using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class UnusedDelegateEmissionAuditTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task AllocationVerdictMatchesEmittedDelegateConstruction(bool release, bool escapes)
    {
        var body = escapes ? "return new System.Action(Sink);" :
            "System.Action unused = new System.Action(Sink); return null;";
        var source = "using SharpProof.Attributes; public static class Subject { " +
            "static void Sink() {} [ZeroAllocations, System.Runtime.CompilerServices.MethodImpl(" +
            "System.Runtime.CompilerServices.MethodImplOptions.NoInlining | " +
            "System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)] " +
            "public static object Target(int value) { " + body + " } }";
        var compilation = TestCompilation.Create("UnusedDelegateOracle", ("Subject.cs", source));
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(
            release ? OptimizationLevel.Release : OptimizationLevel.Debug));
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new AssemblyLoadContext("UnusedDelegateOracle", isCollectible: true);
        long measured;
        try
        {
            var method = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Target")!;
            var target = method.CreateDelegate<Func<int, object?>>();
            for (var repeat = 0; repeat < 3; repeat++)
            { _ = target(1); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            object? result = null;
            for (var repeat = 0; repeat < 32; repeat++)
            { result = target(1); }
            measured = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(result, escapes ? Is.InstanceOf<System.Action>() : Is.Null);
            Assert.That(measured, release && !escapes ? Is.Zero : Is.GreaterThan(0));
            await TestContext.Out.WriteLineAsync($"release={release}; escapes={escapes}; bytes32={measured}; IL={Convert.ToHexString(method.GetMethodBody()!.GetILAsByteArray()!)}");
        }
        finally { runtime.Unload(); }
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var native = await NativeEffectSiteVerifier.VerifyAsync(preparations.Single(), new WorkerBudgets());
        Assert.That(native.Outcome, release && !escapes ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>(),
            $"release={release}; escapes={escapes}; measured32={measured}; reason={native.Reason}");
    }
}