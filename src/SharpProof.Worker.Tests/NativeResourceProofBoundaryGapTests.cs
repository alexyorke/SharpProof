using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Specs;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ProofBoundaryEmittedSitesRequireTheLaterChildSourceProof(bool configured)
    {
        var source = "using SharpProof.Attributes;using System.Threading.Tasks;public static class C{" +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes,100000)]public static " + (configured ? "async " : "") +
            "Task Target(int n)=>" + (configured ? "await " : "") +
            "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(n)))" + (configured ? ".ConfigureAwait(false)" : "") +
            ";static void Child(int n){var values=new int[1];}}";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var projection = target.ForkJoinProjection!;
        var shapes = projection.Carriers!.Shapes!;
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var layout = new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "x64", 8);
        var admitted = NativeTaskEmittedFaultSiteVerifier.Check(projection, target.TotalEntry!, layout, cancellation);
        Assert.That(admitted.UnknownReason, Is.Null);
        Assert.That(admitted.Sites!.Value, Is.Not.Empty);
        var child = projection.Children[1];
        projection = projection with
        {
            Children = projection.Children.SetItem(1, child with { Owner = child.Owner with { EffectsCompleteAtEntry = false } })
        };
        // The emitted module, body hashes and full callback coverage remain
        // owned. They cannot replace the independent source proof for a child.
        Assert.That(CompilerTaskEmittedFaultBodyOwnership.Validate(shapes.EmittedFaultBodies!.Inventory!, shapes,
            projection.FrameworkAssemblyIdentity, cancellation), Is.Null);
        var rejected = NativeTaskEmittedFaultSiteVerifier.Check(projection, target.TotalEntry!, layout, cancellation);
        Assert.That(rejected.UnknownReason, Is.EqualTo("TaskCompilerCarrierOwnershipUnproved"));
        Assert.That(rejected.Sites, Is.Null);
        Assert.That(rejected.RequiredCompilerModel, Is.Null);
        var routes = NativeTaskPrimitiveHelperRouteVerifier.Check(projection, target.TotalEntry!, layout, cancellation);
        Assert.That(routes.UnknownReason, Is.EqualTo(rejected.UnknownReason));
        Assert.That(routes.UnprovedAlternatives, Is.Empty);
        target = target with { ForkJoinProjection = projection };
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), cancellation)).Single();
        AssertProofBoundaryUnknown(result, "TaskCompilerCarrierOwnershipUnproved");
    }

    [Test]
    public async Task ProofBoundaryLayoutRebindingCannotReuseARevokedBytePremise()
    {
        var target = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedBytes,32)]",
            "var values2=new int[2];"), reviewedRuntime: true, x64: true));
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var proven = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), cancellation)).Single();
        Assert.That(proven.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), proven.ResourceBound?.UnknownReason);
        // The source owns one array: its 24-byte header plus two Int32 slots.
        Assert.That(proven.ResourceBound!.InferredUpperBound, Is.EqualTo("32"));
        var dependency = proven.ResourceBound.MetadataAssumptions.Single();
        Assert.That(dependency.WitnessIdentifier, Is.EqualTo(ResourceLayoutCatalog.Witness));
        var binding = new NativeResourceLayoutBinding(target);
        Assert.That(binding.Bind(), Is.Not.Null);
        target.Entry.Assumptions = target.Entry.Assumptions.Where(assumption => assumption is null ?
            throw new ArgumentNullException(nameof(assumption)) : assumption.Id != dependency.AssumptionId).ToImmutableArray();
        Assert.That(binding.Bind(), Is.Null, "Rebinding must discard the previously owned layout dependency.");
        Action staleProjection = () => binding.Project(proven);
        Assert.Throws<ArgumentException>(staleProjection);
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), cancellation)).Single();
        AssertProofBoundaryUnknown(result, "ResourceLayoutAssumptionsNotBound");
    }

    private static void AssertProofBoundaryUnknown(WorkerClaimResult result, string reason)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(reason);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo(reason));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
        Assert.That(result.ResourceBound.Accounting, Is.Empty);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.ResourceBound.RuntimePremises, Is.Null);
        Assert.That(result.ResourceBound.Asymptotic, Is.Null);
        Assert.That(result.ResourceBound.Expected, Is.Null);
        Assert.That(result.ProofCore, Is.Empty);
        Assert.That(result.EffectWitness, Is.Null);
        Assert.That(result.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
    }
}
