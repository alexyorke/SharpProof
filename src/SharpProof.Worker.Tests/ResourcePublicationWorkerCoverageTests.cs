using System.Collections.Immutable;
using System.Reflection;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task WorkerCoverageReplayBudgetBoundaryPublishesOnlyCompletedEvidence(bool exhausted)
    {
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var target = Read(CreateArtifact("using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.ArrayAllocations,0)] public static void Target() {var value=new int[1];} }"));
        var fallback = NativeResourceContractVerifier.Verify(target, target.EffectClaims.Single(), cancellation);
        Assert.That(fallback.ResourceBound!.UnknownReason, Is.EqualTo("ResourceBoundComparisonUnproved"));
        var candidate = await FindBoundaryViolation(target, ResourceExpression.Zero);
        Assert.That(candidate, Is.Not.Null);
        Assert.That(candidate!.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        var expression = exhausted ? ResourceExpression.InputSize(new ResourceInput(0,
            ResourceInputKind.NonNegativeScalar, new string('n', ResourceExpression.MaximumTextLength), int.MaxValue)) :
            ResourceExpression.One;
        Func<Task<WorkerClaimResult>> replay = async () =>
        {
            await Task.Yield();
            _ = expression.ToDisplayString();
            return candidate!;
        };
        var method = typeof(NativeResourceContractVerifier).GetMethod("WithinReplayBudgetAsync", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);

        var result = await (Task<WorkerClaimResult>)method!.Invoke(null, [fallback, replay, cancellation])!;

        if (!exhausted)
        {
            Assert.That(result, Is.SameAs(candidate));
            return;
        }
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ResourceReplayBudgetExceeded"));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
        Assert.That(result.ResourceBound.Asymptotic, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
        Assert.That(result.EffectWitness, Is.Null);
        Assert.That(result.ProofCore, Is.Empty);
        Assert.That(fallback.Reason, Is.EqualTo(WorkerClaimReason.SolverIncomplete));
        Assert.That(fallback.ResourceBound!.UnknownReason, Is.EqualTo("ResourceBoundComparisonUnproved"));
    }

    [Test]
    public async Task WorkerCoverageReplayBudgetBoundaryPropagatesCallerCancellation()
    {
        var target = Read(CreateArtifact("using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.ArrayAllocations,0)] public static void Target() {var value=new int[1];} }"));
        var fallback = NativeResourceContractVerifier.Verify(target, target.EffectClaims.Single(), TestContext.CurrentContext.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Func<Task<WorkerClaimResult>> replay = async () =>
        {
            await Task.Delay(1, cancellation.Token);
            return fallback;
        };
        var method = typeof(NativeResourceContractVerifier).GetMethod("WithinReplayBudgetAsync", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        OperationCanceledException? failure = null;

        try
        { _ = await (Task<WorkerClaimResult>)method!.Invoke(null, [fallback, replay, cancellation.Token])!; }
        catch (OperationCanceledException exception)
        { failure = exception; }

        Assert.That(failure, Is.Not.Null);
        Assert.That(failure!.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(fallback.Reason, Is.EqualTo(WorkerClaimReason.SolverIncomplete));
        Assert.That(fallback.ResourceBound!.UnknownReason, Is.EqualTo("ResourceBoundComparisonUnproved"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WorkerCoverageTaskComparisonBudgetCannotPublishPartialEvidence(bool directComparisonFirst)
    {
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric.LoopBodyEntries,3,nameof(n),1)]");
        var claim = target.EffectClaims.Single();
        var upper = ResourceExpressionCodec.DecodeForCallable(claim.Constraint.ResourceUpperBound!.UpperBound,
            target.Entry.CallableId, target.TotalEntry!, cancellation);
        var input = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "n", int.MaxValue));
        var alternatives = Enumerable.Range(1, 1024).Select(offset => ResourceExpression.Difference(input, offset)).ToArray();
        while (alternatives.Length > 1)
        {
            var combined = new ResourceExpression[alternatives.Length / 2];
            for (var index = 0; index < combined.Length; index++)
            { combined[index] = ResourceExpression.Max(alternatives[index * 2], alternatives[index * 2 + 1]); }
            alternatives = combined;
        }
        var limit = directComparisonFirst ? ResourceExpression.Max(upper, alternatives[0]) :
            ResourceExpression.Max(alternatives[0], upper);
        claim = claim with
        {
            Constraint = claim.Constraint with
            { ResourceUpperBound = claim.Constraint.ResourceUpperBound! with { UpperBound = ResourceExpressionCodec.Encode(limit, cancellation) } }
        };
        CompilerEffectClaimArtifactCodec.Seal(claim);

        var result = NativeResourceContractVerifier.Verify(target, claim, cancellation);

        Assert.That(result.Outcome, Is.EqualTo(directComparisonFirst ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Unknown),
            result.ResourceBound?.UnknownReason);
        if (directComparisonFirst)
        {
            Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo("(max(0, n) + 3)"));
            return;
        }
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ForkJoinResourceBudgetExceeded"));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
        Assert.That(result.ResourceBound.Asymptotic, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
        Assert.That(result.EffectWitness, Is.Null);
        Assert.That(result.ProofCore, Is.Empty);
    }

    [TestCase("lifetime-layout")]
    [TestCase("program")]
    [TestCase("allocation-layout")]
    public async Task WorkerCoverageReplayCandidatesCannotPublishWithoutIndependentPremises(string invalid)
    {
        ArgumentNullException.ThrowIfNull(invalid);
        var lifetime = invalid == "lifetime-layout";
        var target = Read(CreateArtifact("using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound(ResourceMetric.{(lifetime ? "RetainedBytes" : "AllocatedBytes")},0)] " +
            "public static int[] Target() {return new int[2];} }", reviewedRuntime: true, x64: true));
        var claim = target.EffectClaims.Single();
        var fallback = NativeResourceContractVerifier.Verify(target, claim, TestContext.CurrentContext.CancellationToken);
        Assert.That(fallback.ResourceBound!.UnknownReason, Is.EqualTo("ResourceBoundComparisonUnproved"));
        var candidate = await FindBoundaryViolation(target, ResourceExpression.Zero);
        Assert.That(candidate, Is.Not.Null);
        Assert.That(candidate!.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        var hostile = invalid == "program" ? target with { ResourceProgram = ResourceProgram.MetadataBoundary } :
            target with { Compilation = target.Compilation with { ResourceTargetPlatform = "X86" } };
        var method = typeof(NativeResourceContractVerifier).GetMethod("ProjectReplayViolation", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);

        var rejected = (WorkerClaimResult)method!.Invoke(null,
            [hostile, claim, fallback, candidate, TestContext.CurrentContext.CancellationToken])!;

        Assert.That(rejected, Is.SameAs(fallback));
        Assert.That(rejected.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(rejected.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(rejected.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(rejected.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(rejected.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
        Assert.That(rejected.EffectWitness, Is.Null);
        Assert.That(rejected.ProofCore, Is.Empty);
    }

    [TestCase("unbound-layout")]
    [TestCase("missing-bound")]
    [TestCase("unknown")]
    public void WorkerCoverageLayoutProjectionRejectsUnownedOrIncompleteByteEvidence(string invalid)
    {
        ArgumentNullException.ThrowIfNull(invalid);
        var declared = invalid == "unknown" ? 0 : 64;
        var target = Read(CreateArtifact("using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound(ResourceMetric.AllocatedBytes,{declared})] public static void Target() {{var value=new int[1];}} }}",
            reviewedRuntime: true, x64: true));
        var result = NativeResourceContractVerifier.Verify(target, target.EffectClaims.Single(), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.EqualTo(invalid == "unknown" ? WorkerClaimOutcome.Unknown : WorkerClaimOutcome.Proven));
        var bindingTarget = invalid == "unbound-layout" ? target with
        { Compilation = target.Compilation with { ResourceTargetPlatform = "X86" } } : target;
        var binding = new NativeResourceLayoutBinding(bindingTarget);
        Assert.That(binding.Bind(), invalid == "unbound-layout" ? Is.Null : Is.Not.Null);
        if (invalid == "missing-bound")
        { result = result with { ResourceBound = null }; }
        var originalBound = result.ResourceBound;
        var originalAssumptions = result.Assumptions;
        var originalCore = result.ProofCore;

        var failure = Assert.Throws<ArgumentException>((Action)(() => binding.Project(result)));

        Assert.That(failure!.ParamName, Is.EqualTo("result"));
        Assert.That(failure.Message, Does.StartWith("A byte proof requires its owned layout dependency."));
        Assert.That(result.ResourceBound, Is.SameAs(originalBound));
        Assert.That(result.Assumptions, Is.EqualTo(originalAssumptions));
        Assert.That(result.ProofCore, Is.EqualTo(originalCore));
        if (invalid == "unknown")
        {
            Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ResourceBoundComparisonUnproved"));
            Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
            Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
            Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
            Assert.That(result.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
            Assert.That(result.ProofCore, Is.Empty);
        }
        else if (invalid == "unbound-layout")
        {
            Assert.That(result.ResourceBound!.InferredUpperBound, Is.Not.Null.And.Not.Empty);
            Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(1));
            var dependency = result.ResourceBound.MetadataAssumptions.Single();
            Assert.That(result.Assumptions.Single(assumption => assumption is null
                ? throw new ArgumentNullException(nameof(assumption)) : assumption.Id == dependency.AssumptionId).Used, Is.True);
        }
        else
        { Assert.That(result.ResourceBound, Is.Null); }
    }

    [Test]
    public void WorkerCoverageByteProjectionRetainsAnAlreadyUsedOwnedDependency()
    {
        var target = Read(CreateArtifact("using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes,64)] public static void Target() {var value=new int[1];} }",
            reviewedRuntime: true, x64: true));
        var result = NativeResourceContractVerifier.Verify(target, target.EffectClaims.Single(), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.Assumptions.Any(static assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption.Used), Is.True);
        var binding = new NativeResourceLayoutBinding(target);
        Assert.That(binding.Bind(), Is.Not.Null);
        result = result with { ResourceBound = result.ResourceBound! with { MetadataAssumptions = [] } };

        var projected = binding.Project(result);

        Assert.That(projected.ResourceBound!.MetadataAssumptions, Has.Length.EqualTo(1));
        var dependency = projected.ResourceBound.MetadataAssumptions.Single();
        Assert.That(projected.Assumptions.Single(assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption.Id == dependency.AssumptionId).Used, Is.True);
        Assert.That(projected.ResourceBound.InferredUpperBound, Is.EqualTo(result.ResourceBound!.InferredUpperBound));
    }

    [Test]
    public void WorkerCoverageTaskByteClaimsRequireTheOwnedTaskRuntimePremise()
    {
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric.AllocatedBytes,100000)]", x64: true);
        target.Entry.Assumptions = [];

        var result = NativeResourceContractVerifier.Verify(target, target.EffectClaims.Single(), TestContext.CurrentContext.CancellationToken);

        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ForkJoinRuntimePremiseNotBound"));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.ProofCore, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WorkerCoverageEmittedSiteAvailabilityRequiresAValidatedInventory(bool present)
    {
        var check = new NativeTaskEmittedFaultSiteCheck(present ? ImmutableArray<NativeTaskEmittedFaultSite>.Empty : null,
            present ? null : "TaskEmittedFaultBodyEvidenceUnavailable");

        Assert.That(check.HasSites, Is.EqualTo(present));
        Assert.That(check.RequiredCompilerModel, present ? Is.EqualTo(NativeTaskEmittedFaultSiteVerifier.Model) : Is.Null);
    }

    [Test]
    public void WorkerCoverageEmittedSitesRequireTheIndependentSourceLayoutAdmission()
    {
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric.AllocatedBytes,100000)]", "var values=new int[2];", x64: true);

        var check = NativeTaskEmittedFaultSiteVerifier.Check(target.ForkJoinProjection!, target.TotalEntry!,
            new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "x86", 4), TestContext.CurrentContext.CancellationToken);

        Assert.That(check.HasSites, Is.False);
        Assert.That(check.Sites, Is.Null);
        Assert.That(check.RequiredCompilerModel, Is.Null);
        Assert.That(check.UnknownReason, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void WorkerCoverageArrayAccessRoutesRemainCandidatesWithoutRuntimeFaultProofs()
    {
        var target = TaskRoot("[ResourceUpperBound(ResourceMetric.AllocatedBytes,100000)]",
            "var values=new int[2]; int observed=values[count];", x64: true);

        var routes = NativeTaskPrimitiveHelperRouteVerifier.Check(target.ForkJoinProjection!, target.TotalEntry!,
            new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);

        Assert.That(routes.UnprovedAlternatives, Does.Contain(NativeTaskPrimitiveHelperAlternative.NullReferenceThrow));
        Assert.That(routes.UnprovedAlternatives, Does.Contain(NativeTaskPrimitiveHelperAlternative.RangeCheckThrow));
        Assert.That(routes.UnprovedAlternatives, Does.Contain(NativeTaskPrimitiveHelperAlternative.JitOrHardwareFault));
        var result = NativeResourceContractVerifier.Verify(target, target.EffectClaims.Single(), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ProofCore, Is.Empty);
    }
}
