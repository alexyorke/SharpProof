using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class DiscardedDelegateReceiverTests
{
    [TestCase("ZeroAllocations", true)]
    [TestCase("DoesNotThrow", true)]
    [TestCase("EnforcePure", false)]
    public async Task ErasurePreservesReceiverEvaluation(string claim, bool proven)
    {
        var source = "using SharpProof.Attributes; public sealed class Receiver { public void Sink() {} } " +
            "public static class Subject { public static int State; " +
            "static Receiver GetReceiver() { State++; return null; } [" + claim +
            ", System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | " +
            "System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)] " +
            "public static object Target(int value) { System.Action unused = new System.Action(GetReceiver().Sink); return null; } }";
        var compilation = TestCompilation.Create("DiscardedReceiverOracle", ("Subject.cs", source));
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release));
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new AssemblyLoadContext("DiscardedReceiverOracle", isCollectible: true);
        try
        {
            var type = runtime.LoadFromStream(image).GetType("Subject")!;
            var target = type.GetMethod("Target")!.CreateDelegate<Func<int, object?>>();
            for (var repeat = 0; repeat < 3; repeat++)
            { _ = target(1); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            object? result = null;
            for (var repeat = 0; repeat < 32; repeat++)
            { result = target(1); }
            var measured = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(result, Is.Null);
            Assert.That(measured, Is.Zero);
            Assert.That(type.GetField("State")!.GetValue(null), Is.EqualTo(35));
            await TestContext.Out.WriteLineAsync($"claim={claim}; bytes32={measured}; receiverEvaluations=35");
        }
        finally { runtime.Unload(); }
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        ProofOutcome? outcome;
        if (claim == "DoesNotThrow")
        { outcome = (await NativeExceptionEffectVerifier.VerifyAsync(preparations.Single(), new WorkerBudgets())).Outcome; }
        else if (claim == "EnforcePure")
        { outcome = (await NativeEffectSiteVerifier.VerifyPurityAsync(preparations.Single(), new WorkerBudgets())).Outcome; }
        else
        { outcome = (await NativeEffectSiteVerifier.VerifyAsync(preparations.Single(), new WorkerBudgets())).Outcome; }
        Assert.That(outcome, proven ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>());
    }
}
