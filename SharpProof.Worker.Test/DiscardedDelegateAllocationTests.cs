using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class DiscardedDelegateAllocationTests
{
    [TestCase(false, "discard")]
    [TestCase(true, "discard")]
    [TestCase(false, "conditional")]
    [TestCase(true, "conditional")]
    [TestCase(false, "loop")]
    [TestCase(true, "loop")]
    [TestCase(false, "try")]
    [TestCase(true, "try")]
    [TestCase(false, "assignment")]
    [TestCase(true, "assignment")]
    [TestCase(false, "returned-local")]
    [TestCase(true, "returned-local")]
    [TestCase(false, "receiver-allocation")]
    [TestCase(true, "receiver-allocation")]
    public async Task VerdictMatchesMeasuredEmission(bool release, string shape)
    {
        var body = shape switch
        {
            "discard" => "_ = new System.Action(Sink); return null;",
            "conditional" => "if (value != 0) { System.Action unused = new System.Action(Sink); } return null;",
            "loop" => "while (value > 0) { System.Action unused = new System.Action(Sink); value--; } return null;",
            "try" => "try { System.Action unused = new System.Action(Sink); } finally { State++; } return null;",
            "assignment" => "System.Action unused; unused = new System.Action(Sink); return null;",
            "returned-local" => "System.Action used = new System.Action(Sink); return used;",
            "receiver-allocation" => "System.Action unused = new System.Action(GetReceiver().Sink); return null;",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var erased = shape == "discard" || release && shape is not ("returned-local" or "receiver-allocation");
        var source = "using SharpProof.Attributes; public sealed class Receiver { public void Sink() {} } " +
            "public static class Subject { public static int State; static void Sink() {} " +
            "static Receiver GetReceiver() { State++; return new Receiver(); } " +
            "[ZeroAllocations, System.Runtime.CompilerServices.MethodImpl(" +
            "System.Runtime.CompilerServices.MethodImplOptions.NoInlining | " +
            "System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)] " +
            "public static object Target(int value) { " + body + " } }";
        var compilation = TestCompilation.Create("DiscardedDelegateOracle", ("Subject.cs", source));
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(
            release ? OptimizationLevel.Release : OptimizationLevel.Debug));
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new AssemblyLoadContext("DiscardedDelegateOracle", isCollectible: true);
        long measured;
        try
        {
            var type = runtime.LoadFromStream(image).GetType("Subject")!;
            var method = type.GetMethod("Target")!;
            var target = method.CreateDelegate<Func<int, object?>>();
            for (var repeat = 0; repeat < 3; repeat++)
            { _ = target(1); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            object? result = null;
            for (var repeat = 0; repeat < 32; repeat++)
            { result = target(1); }
            measured = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(result, shape == "returned-local" ? Is.InstanceOf<System.Action>() : Is.Null);
            Assert.That(measured, erased ? Is.Zero : Is.GreaterThan(0));
            Assert.That(type.GetField("State")!.GetValue(null), Is.EqualTo(shape is "try" or "receiver-allocation" ? 35 : 0));
            await TestContext.Out.WriteLineAsync($"release={release}; shape={shape}; bytes32={measured}; IL={Convert.ToHexString(method.GetMethodBody()!.GetILAsByteArray()!)}");
        }
        finally { runtime.Unload(); }
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var native = await NativeEffectSiteVerifier.VerifyAsync(preparations.Single(), new WorkerBudgets());
        Assert.That(native.Outcome, erased ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>(),
            $"release={release}; shape={shape}; measured32={measured}; reason={native.Reason}");
    }
}
