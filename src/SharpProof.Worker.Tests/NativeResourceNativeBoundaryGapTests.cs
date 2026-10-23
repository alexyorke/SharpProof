using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    private static readonly ImmutableArray<long> BoundaryRuntimeQuantities = [2, 5, 3, 7, 11, 13, 17, 19, 23, 29, 31];

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task RuntimeBoundaryComparisonBudgetPublishesOnlyCompletedConditionalProofs(bool directComparisonFirst,
        bool configured)
    {
        var artifact = BoundaryRuntimeArtifact(configured);
        var target = Read(artifact);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var input = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "n", int.MaxValue));
        var upper = BoundaryRuntimeUpper(target.ForkJoinProjection!, input);
        var binding = NativeResourceTaskRuntimeBinding.Bind(target, cancellation);
        Assert.That(binding.Inputs, Is.Not.Null, binding.UnknownReason);
        var arithmetic = NativeTaskRuntimeCostVerifier.Check(target.ForkJoinProjection!, target.TotalEntry!,
            new("CoreCLR", "8.0.32", "x64", 8), binding.Inputs!, cancellation);
        Assert.That(arithmetic.UnknownReason, Is.Null);
        Assert.That(arithmetic.CumulativeUpper, Is.EqualTo(upper));
        var alternatives = Enumerable.Range(1, 1024).Select(offset => ResourceExpression.Difference(input, offset)).ToArray();
        while (alternatives.Length > 1)
        {
            var combined = new ResourceExpression[alternatives.Length / 2];
            for (var index = 0; index < combined.Length; index++)
            { combined[index] = ResourceExpression.Max(alternatives[index * 2], alternatives[index * 2 + 1]); }
            alternatives = combined;
        }
        // The final alternative is the independently summed valid byte bound.
        // Searching its equivalent Max order must not publish partial premises
        // when the earlier alternatives exhaust the comparison budget.
        var limit = directComparisonFirst ? ResourceExpression.Max(upper, alternatives[0]) :
            ResourceExpression.Max(alternatives[0], upper);
        var claim = artifact.Callables.Single().EffectClaims.Single();
        var constraint = claim.Constraint.ResourceUpperBound! with { UpperBound = ResourceExpressionCodec.Encode(limit, cancellation) };
        claim = claim with { Constraint = claim.Constraint with { ResourceUpperBound = constraint } };
        CompilerEffectClaimArtifactCodec.Seal(claim);
        artifact = artifact with
        {
            Callables = artifact.Callables.SetItem(0, artifact.Callables.Single() with { EffectClaims = [claim] }),
            Manifest = artifact.Manifest with
            {
                Claims = artifact.Manifest.Claims.SetItem(0, artifact.Manifest.Claims.Single() with { ResourceUpperBound = constraint })
            }
        };
        WorkerProtocolJson.SealManifest(artifact.Manifest);
        var result = (await NativeEffectClaims.VerifyAsync(Read(artifact), CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), cancellation)).Single();
        if (directComparisonFirst)
        { AssertBoundaryRuntimeProof(result, target, upper); }
        else
        {
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
            AssertBoundaryRuntimeUnknown(result, "ForkJoinResourceBudgetExceeded");
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RuntimeBoundaryLaterEmittedBodyIsRequiredBeforeContractEvidencePublication(bool configured)
    {
        var target = Read(BoundaryRuntimeArtifact(configured));
        var projection = target.ForkJoinProjection!;
        var shapes = projection.Carriers!.Shapes!;
        var inventory = shapes.EmittedFaultBodies!.Inventory!;
        Assert.That(inventory.Bodies, Has.Length.EqualTo(2));
        var incomplete = projection with
        {
            Carriers = projection.Carriers! with
            {
                Shapes = shapes with
                {
                    EmittedFaultBodies = new(inventory with { Bodies = inventory.Bodies.RemoveAt(1) }, null)
                }
            }
        };
        var changed = target with { ForkJoinProjection = incomplete };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        // The declared quantities still bind. Complete emitted source ownership
        // is a separate prerequisite before the contract can use any of them.
        var binding = NativeResourceTaskRuntimeBinding.Bind(changed, cancellation);
        Assert.That(binding.Inputs, Is.Not.Null, binding.UnknownReason);
        var rejected = (await NativeEffectClaims.VerifyAsync(changed, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), cancellation)).Single();
        Assert.That(rejected.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
        AssertBoundaryRuntimeUnknown(rejected, "TaskEmittedFaultBodyCoverageInvalid");

        var valid = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), cancellation)).Single();
        var input = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "n", int.MaxValue));
        AssertBoundaryRuntimeProof(valid, target, BoundaryRuntimeUpper(projection, input));
    }

    private static CompilerManifestArtifact BoundaryRuntimeArtifact(bool configured)
    {
        var attributes = new StringBuilder("[ResourceTaskDeployment(ResourceTaskDeploymentProfile.CoreClr8032LinuxX64OrdinaryJitStockClosed,\"");
        attributes.Append("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")
            .Append("\",\"explicit synthetic deployment hypothesis including cold initialization\")]");
        for (var index = 0; index < BoundaryRuntimeQuantities.Length; index++)
        {
            var quantity = ((WorkerResourceTaskRuntimeQuantity)(index + 1)).ToString();
            attributes.Append("[ResourceTaskRuntimeUpperBound(ResourceTaskRuntimeQuantity.").Append(quantity)
                .Append(',').Append(BoundaryRuntimeQuantities[index].ToString(CultureInfo.InvariantCulture))
                .Append("L,\"explicit synthetic quantity ").Append(quantity).Append("\")]");
        }
        return CreateArtifact("using SharpProof.Attributes;using System.Threading.Tasks;public static class C{" + attributes +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes,long.MaxValue)]public static " + (configured ? "async " : "") +
            "Task Target(int n)=>" + (configured ? "await " : "") +
            "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Empty()))" + (configured ? ".ConfigureAwait(false)" : "") +
            ";static void Child(int n){var values=new int[n];}static void Empty(){}}", reviewedRuntime: true, x64: true);
    }

    private static ResourceExpression BoundaryRuntimeUpper(CompilerForkJoinProjection projection, ResourceExpression input)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(input);
        var shapes = projection.Carriers!.Shapes!;
        // One int[n], one empty child, the owned Task[] and delegate/carrier
        // instances. Fixed runtime quantities are explicit fixture hypotheses.
        BigInteger compiler = 24 + 8 * new BigInteger(projection.Children.Length) + 64 * new BigInteger(shapes.MatchedCallbacks);
        foreach (var frame in shapes.CallbackFrames)
        { compiler += BigInteger.Max(24, 16 + 8 * new BigInteger(frame.Fields.Length)); }
        if (shapes.StateMachine is { IsValueType: false } state)
        { compiler += BigInteger.Max(24, 16 + 8 * new BigInteger(state.Fields.Length)); }
        var runtime = 2 * 17 + 19 + (projection.ConfiguredAwait ? 23 : 0) + 31 * (2 * 32 + 3 * 80 + 11 + 13) + 29;
        var source = ResourceExpression.Aligned(ResourceExpression.Add(ResourceExpression.Literal(24),
            ResourceExpression.Multiply(input, ResourceExpression.Literal(4))), 8);
        return ResourceExpression.Add(source, ResourceExpression.Literal(compiler + runtime));
    }

    private static void AssertBoundaryRuntimeProof(WorkerClaimResult result, CompilerCallablePreparation target, ResourceExpression upper)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(upper);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.UnknownReason, Is.Null);
        Assert.That(result.ResourceBound.InferredUpperBound, Is.EqualTo(upper.ToDisplayString()));
        Assert.That(result.ResourceBound.ExecutionScope, Is.EqualTo(WorkerResourceModels.TaskRuntimeScope));
        Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(2));
        Assert.That(result.ResourceBound.Asymptotic!.Lower!.UnknownReason, Is.EqualTo("ResourceLowerTaskRuntimeDomainUnproved"));
        Assert.That(result.ResourceBound.Expected!.UnknownReason, Is.EqualTo("ResourceExpectedTaskModelUnsupported"));
        var evidence = result.ResourceBound.RuntimePremises!;
        Assert.That(evidence, Is.Not.Null);
        Assert.That(evidence.Quantities, Has.Length.EqualTo(11));
        Assert.That(evidence.Quantities.Select(static quantity => quantity is null ? throw new ArgumentNullException(nameof(quantity)) : quantity.UpperBound),
            Is.EqualTo(BoundaryRuntimeQuantities));
        var declarations = target.ForkJoinProjection!.RuntimePremises!.Inventory!.Declarations;
        var usedIds = declarations.Quantities.Select(declaration => declaration is null ? throw new ArgumentNullException(nameof(declaration)) :
            ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(declarations, declaration)).ToHashSet(StringComparer.Ordinal);
        usedIds.Add(ResourceTaskRuntimePremiseIdentity.DeploymentAssumptionId(declarations));
        Assert.That(usedIds, Has.Count.EqualTo(12));
        usedIds.UnionWith(result.ResourceBound.MetadataAssumptions.Select(static dependency => dependency is null
            ? throw new ArgumentNullException(nameof(dependency)) : dependency.AssumptionId));
        Assert.That(result.Assumptions.Where(static assumption => assumption is not null && assumption.Used)
            .Select(static assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption.Id), Is.EquivalentTo(usedIds));
        Assert.That(result.ProofCore, Does.Contain("resource-task-runtime-model:" + NativeTaskRuntimeCostVerifier.Model));
        Assert.That(result.ProofCore, Does.Contain("resource-runtime-premise-record:" + WorkerProtocolJson.RuntimePremiseRecordSha256(evidence)));
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
        Assert.That(result.ResourceBound.Accounting, Is.Empty);
        Assert.That(result.EffectWitness, Is.Null);
    }

    private static void AssertBoundaryRuntimeUnknown(WorkerClaimResult result, string reason)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(reason);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo(reason));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
        Assert.That(result.ResourceBound.Asymptotic, Is.Null);
        Assert.That(result.ResourceBound.Expected, Is.Null);
        Assert.That(result.ResourceBound.Accounting, Is.Empty);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.ResourceBound.RuntimePremises, Is.Null);
        Assert.That(result.ProofCore, Is.Empty);
        Assert.That(result.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
        Assert.That(result.EffectWitness, Is.Null);
    }
}
