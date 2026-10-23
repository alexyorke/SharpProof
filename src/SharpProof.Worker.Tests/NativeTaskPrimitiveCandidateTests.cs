using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class NativeTaskPrimitiveCandidateTests
{
    [TestCase(OptimizationLevel.Debug)]
    [TestCase(OptimizationLevel.Release)]
    public async Task OwnedArrayReadRetainsEveryCandidateWithoutARuntimeRootProof(OptimizationLevel optimization)
    {
        var target = Prepare(optimization);
        var projection = target.ForkJoinProjection!;
        Assert.That(projection.RuntimePremises, Is.Null, "The source declares no runtime premises.");
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var layout = new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "x64", 8);
        var emitted = NativeTaskEmittedFaultSiteVerifier.Check(projection, target.TotalEntry!, layout, cancellation);
        Assert.That(emitted.UnknownReason, Is.Null);
        var read = emitted.Sites!.Value.Single(static site => site is null ?
            throw new ArgumentNullException(nameof(site)) : site.OpCode == 0x94);
        Assert.That(read.Faults, Is.EqualTo(CompilerTaskPrimitiveFaultKind.NullReference | CompilerTaskPrimitiveFaultKind.RangeCheck),
            "The unused read must remain in emitted IL because it can fault.");
        var routes = NativeTaskPrimitiveHelperRouteVerifier.Check(projection, target.TotalEntry!, layout, cancellation);
        Assert.That(routes.UnprovedAlternatives, Is.EquivalentTo(new[]
        {
            NativeTaskPrimitiveHelperAlternative.NewArrayDirect,
            NativeTaskPrimitiveHelperAlternative.NewArrayValueDynamic,
            NativeTaskPrimitiveHelperAlternative.ReadyToRunNewArray,
            NativeTaskPrimitiveHelperAlternative.NullReferenceThrow,
            NativeTaskPrimitiveHelperAlternative.RangeCheckThrow,
            NativeTaskPrimitiveHelperAlternative.JitOrHardwareFault
        }));
        Assert.That(routes.UnknownReason, Is.EqualTo("TaskPrimitiveHelperDynamicArrayRouteUnproved"));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), cancellation)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo(routes.UnknownReason));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
        Assert.That(result.ResourceBound.Asymptotic, Is.Null);
        Assert.That(result.ResourceBound.Expected, Is.Null);
        Assert.That(result.ResourceBound.RuntimePremises, Is.Null);
        Assert.That(result.ResourceBound.Accounting, Is.Empty);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.ProofCore, Is.Empty);
        Assert.That(result.EffectWitness, Is.Null);
        Assert.That(result.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
    }

    private static CompilerCallablePreparation Prepare(OptimizationLevel optimization)
    {
        const string source = """
            using SharpProof.Attributes;
            using System.Threading.Tasks;
            public static class C
            {
                [ResourceUpperBound(ResourceMetric.AllocatedBytes,100000)]
                public static Task Target(int n) => Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3)));
                static void Child(int n) {var values=new int[n]; int value=values[n];}
            }
            """;
        var initial = TestCompilation.Create("PrimitiveCandidates", [("Subject.cs", source)]);
        const string references = "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.32/ref/net8.0";
        var compilation = initial.WithReferences(Directory.GetFiles(references, "*.dll")
            .Append(typeof(ResourceUpperBoundAttribute).Assembly.Location)
            .Select(static path => path is null ? throw new ArgumentNullException(nameof(path)) : MetadataReference.CreateFromFile(path)))
            .WithOptions(initial.Options.WithPlatform(Platform.X64).WithOptimizationLevel(optimization));
        TestCompilation.AssertNoErrors(compilation);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.All, cancellation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net8.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, cancellation);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellation);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations, cancellation);
        var target = preparations.Single();
        Assert.That(target.ForkJoinProjection, Is.Not.Null, target.ResourceUnknownReason);
        Assert.That(target.TotalEntry, Is.Not.Null);
        Assert.That(target.ForkJoinProjection!.Carriers!.Shapes!.EmittedFaultBodies!.UnknownReason, Is.Null);
        return target;
    }
}
