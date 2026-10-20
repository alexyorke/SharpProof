using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    private static CompilerCallablePreparation TaskRoot(string attributes, string child = "for(int i=0;i<count;i++){}")
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(child);
        return Read(CreateArtifact("using SharpProof.Attributes; using System.Threading.Tasks; public static class C { " +
            attributes + " public static Task Target(int n) => Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3))); static void Child(int count) {" + child + "} }", reviewedRuntime: true));
    }

    [Test]
    public async Task TaskRootPublishesCheckedLoopUpperWithExplicitConditionalScope()
    {
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric.LoopBodyEntries,3,nameof(n),1)]");
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(target.Total, Is.Null);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo("(max(0, n) + 3)"));
        Assert.That(result.ResourceBound.ExecutionScope, Is.EqualTo(WorkerResourceModels.TaskJoinScope));
        Assert.That(result.ResourceBound.Asymptotic!.UpperBound, Is.EqualTo("O(n)"));
        Assert.That(result.ResourceBound.Asymptotic.Lower!.UnknownReason, Is.EqualTo("ResourceLowerTaskDomainUnproved"));
        Assert.That(result.ResourceBound.Expected!.UnknownReason, Is.EqualTo("ResourceExpectedTaskModelUnsupported"));
        Assert.That(result.ResourceBound.Accounting, Is.Empty);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        var dependency = result.ResourceBound.MetadataAssumptions.Single();
        Assert.That(dependency.RuntimeModel, Is.EqualTo(WorkerResourceModels.TaskJoinModel));
        Assert.That(result.Assumptions.Single().Id, Is.EqualTo(dependency.AssumptionId));
        Assert.That(result.Assumptions.Single().Used, Is.True);
        Assert.That(result.ProofCore, Has.Some.StartsWith("resource-task-projection:"));
    }

    [TestCase("ArrayAllocations")]
    [TestCase("AllocatedElements")]
    [TestCase("AllocatedBytes")]
    [TestCase("PeakAuxiliaryBytes")]
    [TestCase("RetainedBytes")]
    [TestCase("SourceActivationDepth")]
    [TestCase("SourceStackBytes")]
    public async Task TaskRootDoesNotPublishUnmodeledMetricBounds(string metric)
    {
        ArgumentNullException.ThrowIfNull(metric);
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric." + metric + ",100)]");
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ForkJoinResourceMetricUnsupported"));
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
    }

    [TestCase("bound", "ForkJoinBoundComparisonUnproved")]
    [TestCase("premise", "ForkJoinRuntimePremiseNotBound")]
    [TestCase("child", "ForkJoinChildOwnershipInvalid")]
    public async Task TaskRootFailuresReturnUnknownWithoutPartialOrExactEvidence(string mutation, string reason)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(reason);
        var target = TaskRoot(mutation == "bound" ? "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,2,nameof(n),1)]" :
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,3,nameof(n),1)]");
        if (mutation == "premise")
        { target.Entry.Assumptions = []; }
        if (mutation == "child")
        {
            var projection = target.ForkJoinProjection!;
            var child = projection.Children[1];
            target = target with { ForkJoinProjection = projection with { Children = [projection.Children[0], child with { Owner = child.Owner with { EffectsCompleteAtEntry = false } }] } };
        }
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo(reason));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.EffectWitness, Is.Null);
    }

    [TestCase(-5)]
    [TestCase(0)]
    [TestCase(7)]
    [TestCase(100)]
    public async Task TaskLoopSourceUpperContainsIndependentlyCountedConcurrentExecutions(int count)
    {
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric.LoopBodyEntries,3,nameof(n),1)]");
        var checkedCost = NativeForkJoinSourceCostVerifier.Check(target.ForkJoinProjection!, target.TotalEntry!, TestContext.CurrentContext.CancellationToken);
        var observed = 0;
        void Count(int bound)
        {
            for (var index = 0; index < bound; index++)
            { Interlocked.Increment(ref observed); }
        }
        await Task.WhenAll(Task.Run(() => Count(count)), Task.Run(() => Count(3)));
        var upper = checkedCost.Upper!.LoopEntries.Evaluate(input =>
        {
            ArgumentNullException.ThrowIfNull(input);
            return new System.Numerics.BigInteger(Math.Max(0, count));
        }, TestContext.CurrentContext.CancellationToken);
        Assert.That(new System.Numerics.BigInteger(observed), Is.LessThanOrEqualTo(upper));
    }

    [Test]
    public void TaskDeclaredLimitBindsToOwnedEntryWithoutInventingATotalBody()
    {
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric.LoopBodyEntries,3,nameof(n),1)]");
        var entry = target.TotalEntry!;
        var expression = ResourceExpressionCodec.DecodeForCallable(target.EffectClaims.Single().Constraint.ResourceUpperBound!.UpperBound,
            target.Entry.CallableId, entry, TestContext.CurrentContext.CancellationToken);
        Assert.That(expression.ToDisplayString(), Does.Contain("n"));
        Action wrongOwner = () => ResourceExpressionOwnership.Validate(expression, "foreign", entry, default);
        Assert.Throws<InvalidDataException>(wrongOwner);
        var badInput = ResourceExpression.InputSize(new ResourceInput(2, ResourceInputKind.NonNegativeScalar, "foreign", int.MaxValue));
        Action wrongInput = () => ResourceExpressionOwnership.Validate(badInput, entry.CallableId, entry, default);
        Assert.Throws<InvalidDataException>(wrongInput);
    }
}
