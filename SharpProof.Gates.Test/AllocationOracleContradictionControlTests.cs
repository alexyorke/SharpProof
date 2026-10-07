using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Gates.Corpus;
using SharpProof.Host;
using SharpProof.Verify;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class AllocationOracleContradictionControlTests
{
    private const string Subject = "public static class C { [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] public static object Target(int x) { return new object(); } }";

    [Test]
    public async Task EscapingObjectRejectsForgedPositiveAllocationProof()
    {
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(Document(Subject), CancellationToken.None, allocations: true);
        Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Release));
        var measured = Measure(compilation);
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.Effects).Build();
        var target = discovery.Targets.Values.Single();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, RepositoryLayout.FindRoot(), "net9.0",
            WorkerFeatureSet.Effects, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var preparation = preparations.Single();
        var evidence = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets(), CancellationToken.None);
        Assert.That(evidence.Outcome, Is.InstanceOf<RefutedOutcome>());
        using var oracle = new NativeAllocationWitnessOracle(compilation);
        var observation = oracle.Check(target.Method, preparation.Total!, evidence, WorkerClaimOutcome.Proven, CancellationToken.None);
        await TestContext.Progress.WriteLineAsync($"escaping-object; noInlining=true; directBytes32={measured.Bytes}; directIL={measured.Il}; " +
            $"oracle={observation.RuntimeOracle}; oracleBytes32={observation.AllocatedBytes}; ILOracle={observation.IlOracle}");
        Assert.That(measured.Bytes, Is.GreaterThan(0));
        Assert.That(observation.RuntimeOracle, Is.EqualTo("Contradiction"));
        Assert.That(observation.AllocatedBytes, Is.GreaterThan(0));
        Assert.That(observation.RuntimeChecks, Is.EqualTo(1));
        Assert.That(observation.IlOracle, Is.EqualTo("PotentialAllocationOpcode"));
    }

    private static OpenSourceCorpusDocument Document(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var span = method.GetLocation().GetLineSpan();
        return new(2, [], [new("test", "sample.cs", "test", source)],
            [new("sample", "test", "sample.cs", span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
                "test", "Target", "effects", CorpusVerdict.Unknown, CorpusSupport.Supported)]);
    }

    private static (long Bytes, string Il) Measure(CSharpCompilation compilation)
    {
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new AssemblyLoadContext("EscapingObjectAllocationOracle", isCollectible: true);
        try
        {
            var method = runtime.LoadFromStream(image).GetType("C")!.GetMethod("Target")!;
            var target = method.CreateDelegate<Func<int, object>>();
            for (var repeat = 0; repeat < 3; repeat++)
            { _ = target(7); }
            object? result = null;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var repeat = 0; repeat < 32; repeat++)
            { result = target(7); }
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(result);
            Assert.That(result, Is.TypeOf<object>());
            return (bytes, Convert.ToHexString(method.GetMethodBody()!.GetILAsByteArray()!));
        }
        finally { runtime.Unload(); }
    }
}
