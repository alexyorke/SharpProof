using System.Reflection;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class RuntimePremiseWorkerCoverageTests
{
    [TestCase("projection")]
    [TestCase("entry")]
    [TestCase("capture")]
    public void RuntimeBindingRequiresTheOwnedRootAndDeclarationCapture(string missing)
    {
        ArgumentNullException.ThrowIfNull(missing);
        var target = Prepare();
        Assert.That(NativeResourceTaskRuntimeBinding.Bind(target, TestContext.CurrentContext.CancellationToken).Inputs, Is.Not.Null);
        target = missing switch
        {
            "projection" => target with { ForkJoinProjection = null },
            "entry" => target with { TotalEntry = null },
            "capture" => target with { ForkJoinProjection = target.ForkJoinProjection! with { RuntimePremises = null } },
            _ => throw new ArgumentOutOfRangeException(nameof(missing))
        };

        var rejected = NativeResourceTaskRuntimeBinding.Bind(target, TestContext.CurrentContext.CancellationToken);

        AssertUnbound(rejected, "TaskRuntimePremisesNotDeclared");
        Assert.That(target.Entry.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
    }

    [Test]
    public void RuntimeBindingValidatesDeploymentDeclarationsBeforeBindingTheirAssumptions()
    {
        var target = Prepare();
        var projection = target.ForkJoinProjection!;
        var inventory = projection.RuntimePremises!.Inventory!;
        var declarations = inventory.Declarations with { Profile = (WorkerResourceTaskDeploymentProfile)999 };
        var reason = ResourceTaskRuntimePremiseIdentity.Validate(declarations);
        Assert.That(reason, Is.Not.Null.And.Not.Empty);
        target = target with
        {
            ForkJoinProjection = projection with
            { RuntimePremises = new(inventory with { Declarations = declarations }, null) }
        };

        var rejected = NativeResourceTaskRuntimeBinding.Bind(target, TestContext.CurrentContext.CancellationToken);

        AssertUnbound(rejected, reason!);
        Assert.That(target.Entry.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
    }

    [TestCase("runtime")]
    [TestCase("layout")]
    public void RuntimeBindingRequiresBothIndependentReferencePremises(string missing)
    {
        ArgumentNullException.ThrowIfNull(missing);
        var target = Prepare();
        var runtime = NativeResourceTaskBinding.Bind(target, TestContext.CurrentContext.CancellationToken);
        Assert.That(runtime, Is.Not.Null);
        Assert.That(new NativeResourceLayoutBinding(target).Bind(), Is.Not.Null);
        target = missing == "layout"
            ? target with { Compilation = target.Compilation with { ResourceTargetPlatform = "X86" } }
            : target with
            {
                Entry = target.Entry with
                {
                    Assumptions = [.. target.Entry.Assumptions.Where(assumption => assumption is null
                        ? throw new ArgumentNullException(nameof(assumption)) : assumption.Id != runtime!.AssumptionId)]
                }
            };

        var rejected = NativeResourceTaskRuntimeBinding.Bind(target, TestContext.CurrentContext.CancellationToken);

        AssertUnbound(rejected, "TaskRuntimeReferencePremiseNotBound");
        Assert.That(target.Entry.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
    }

    [TestCase("inputs")]
    [TestCase("evidence")]
    [TestCase("reason")]
    [TestCase("unknown")]
    [TestCase("resource-bound")]
    public void ConditionalPublicationRequiresACompleteBindingAndACompletedProof(string missing)
    {
        ArgumentNullException.ThrowIfNull(missing);
        var target = Prepare();
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var binding = NativeResourceTaskRuntimeBinding.Bind(target, cancellation);
        Assert.That(binding.Inputs, Is.Not.Null);
        Assert.That(binding.Evidence, Is.Not.Null);
        var result = NativeResourceContractVerifier.Verify(target, target.EffectClaims.Single(), cancellation);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.RuntimePremises, Is.Not.Null);
        switch (missing)
        {
            case "inputs":
                binding = binding with { Inputs = null };
                break;
            case "evidence":
                binding = binding with { Evidence = null };
                break;
            case "reason":
                binding = binding with { UnknownReason = "RejectedRuntimePremiseCapture" };
                break;
            case "unknown":
                var zero = ReplaceLimit(target.EffectClaims.Single(), ResourceExpression.Zero);
                result = NativeResourceContractVerifier.Verify(target, zero, cancellation);
                AssertUnknown(result, "TaskRuntimeBoundComparisonUnproved");
                break;
            case "resource-bound":
                result = result with { ResourceBound = null };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(missing));
        }
        var originalBound = result.ResourceBound;
        var originalAssumptions = result.Assumptions;
        var originalCore = result.ProofCore;

        var failure = Assert.Throws<ArgumentException>((Action)(() => NativeResourceTaskRuntimeBinding.Project(target, result, binding)));

        Assert.That(failure!.ParamName, Is.EqualTo("binding"));
        Assert.That(failure.Message, Does.StartWith("Conditional runtime publication requires complete bound premises and a proof."));
        Assert.That(result.ResourceBound, Is.SameAs(originalBound));
        Assert.That(result.Assumptions, Is.EqualTo(originalAssumptions));
        Assert.That(result.ProofCore, Is.EqualTo(originalCore));
        if (missing == "unknown")
        { AssertUnknown(result, "TaskRuntimeBoundComparisonUnproved"); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ConditionalTaskComparisonBudgetCannotPublishAnEarlierPremisePrefix(bool directComparisonFirst)
    {
        var target = Prepare();
        var input = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "n", int.MaxValue));
        var alternatives = Enumerable.Range(1, 1024).Select(offset => ResourceExpression.Difference(input, offset)).ToArray();
        while (alternatives.Length > 1)
        {
            var combined = new ResourceExpression[alternatives.Length / 2];
            for (var index = 0; index < combined.Length; index++)
            { combined[index] = ResourceExpression.Max(alternatives[index * 2], alternatives[index * 2 + 1]); }
            alternatives = combined;
        }
        var direct = ResourceExpression.Literal(1000000);
        var limit = directComparisonFirst ? ResourceExpression.Max(direct, alternatives[0]) : ResourceExpression.Max(alternatives[0], direct);
        var claim = ReplaceLimit(target.EffectClaims.Single(), limit);

        var result = NativeResourceContractVerifier.Verify(target, claim, TestContext.CurrentContext.CancellationToken);

        if (!directComparisonFirst)
        {
            AssertUnknown(result, "ForkJoinResourceBudgetExceeded");
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
            return;
        }
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.RuntimePremises, Is.Not.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(2));
        Assert.That(result.Assumptions.Count(static assumption => assumption is null
            ? throw new ArgumentNullException(nameof(assumption)) : assumption.Kind == WorkerAssumptionKind.UserAssume && assumption.Used), Is.EqualTo(12));
        Assert.That(result.ProofCore, Does.Contain("resource-runtime-premise-record:" +
            WorkerProtocolJson.RuntimePremiseRecordSha256(result.ResourceBound.RuntimePremises!)));
    }

    [Test]
    public void RejectedLaterChildStorageCannotPublishRuntimePremisesOrPartialBounds()
    {
        var target = Prepare();
        var projection = target.ForkJoinProjection!;
        var later = projection.Children[1];
        target = target with
        {
            ForkJoinProjection = projection with
            { Children = projection.Children.SetItem(1, later with { Cost = ResourceProgram.Array(ResourceExpression.One) }) }
        };
        var binding = NativeResourceTaskRuntimeBinding.Bind(target, TestContext.CurrentContext.CancellationToken);
        Assert.That(binding.Inputs, Is.Not.Null, binding.UnknownReason);
        var cost = NativeTaskRuntimeCostVerifier.Check(target.ForkJoinProjection!, target.TotalEntry!, Layout(), binding.Inputs!,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(cost.CumulativeUpper, Is.Null);
        Assert.That(cost.RequiredRuntimeModel, Is.Null);
        Assert.That(cost.UnknownReason, Is.Not.Null.And.Not.Empty);

        var result = NativeResourceContractVerifier.Verify(target, target.EffectClaims.Single(), TestContext.CurrentContext.CancellationToken);

        AssertUnknown(result, cost.UnknownReason!);
    }

    [Test]
    public void RuntimeCompositionRejectsAnIncompatibleTraceLayoutWithoutAnyBound()
    {
        var target = Prepare();

        var check = Compose(target.ForkJoinProjection!, ResourceExpression.One, new("CoreCLR", "8.0.32", "x86", 4));

        Assert.That(check.UnknownReason, Is.EqualTo("IncompatibleResourceRuntimeLayout"));
        Assert.That(check.CumulativeUpper, Is.Null);
        Assert.That(check.RequiredRuntimeModel, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RuntimeCompositionKeepsItsExpressionConstructionBudget(bool exhausted)
    {
        var target = Prepare();
        var input = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "n", int.MaxValue));
        var terms = Enumerable.Repeat(input, exhausted ? 2048 : 1).ToArray();
        while (terms.Length > 1)
        {
            var combined = new ResourceExpression[terms.Length / 2];
            for (var index = 0; index < combined.Length; index++)
            { combined[index] = ResourceExpression.Add(terms[index * 2], terms[index * 2 + 1]); }
            terms = combined;
        }

        var check = Compose(target.ForkJoinProjection!, terms[0], Layout());

        if (exhausted)
        {
            Assert.That(terms[0].NodeCount, Is.EqualTo(ResourceExpression.MaximumNodes - 1));
            Assert.That(check.UnknownReason, Is.EqualTo("TaskRuntimeCostExpressionBudgetExceeded"));
            Assert.That(check.CumulativeUpper, Is.Null);
            Assert.That(check.RequiredRuntimeModel, Is.Null);
            return;
        }
        Assert.That(check.UnknownReason, Is.Null);
        Assert.That(check.RequiredRuntimeModel, Is.EqualTo(NativeTaskRuntimeCostVerifier.Model));
        Assert.That(check.CumulativeUpper!.Evaluate(static _ => 7, TestContext.CurrentContext.CancellationToken),
            Is.EqualTo(new System.Numerics.BigInteger(8)));
    }

    private static CompilerCallablePreparation Prepare()
    {
        var attributes = typeof(NativeTaskRuntimePremiseTests).GetMethod("Attributes", BindingFlags.NonPublic | BindingFlags.Static);
        var prepare = typeof(NativeTaskRuntimePremiseTests).GetMethod("Prepare", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(attributes, Is.Not.Null);
        Assert.That(prepare, Is.Not.Null);
        var declarations = (string)attributes!.Invoke(null, ["complete"])!;
        return (CompilerCallablePreparation)prepare!.Invoke(null, [OptimizationLevel.Release, false, declarations, 1000000L])!;
    }

    private static CompilerEffectClaimArtifact ReplaceLimit(CompilerEffectClaimArtifact claim, ResourceExpression limit)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(limit);
        var changed = claim with
        {
            Constraint = claim.Constraint with
            { ResourceUpperBound = claim.Constraint.ResourceUpperBound! with { UpperBound = ResourceExpressionCodec.Encode(limit, TestContext.CurrentContext.CancellationToken) } }
        };
        CompilerEffectClaimArtifactCodec.Seal(changed);
        return changed;
    }

    private static NativeTaskRuntimeCostCheck Compose(CompilerForkJoinProjection projection, ResourceExpression child,
        ResourceRuntimeLayoutContext layout)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(layout);
        var compose = typeof(NativeTaskRuntimeCostVerifier).GetMethod("ComposeCosts", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(compose, Is.Not.Null);
        return (NativeTaskRuntimeCostCheck)compose!.Invoke(null,
            [projection, child, ResourceExpression.One, layout, new NativeTaskRuntimeCostInputs(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                TestContext.CurrentContext.CancellationToken])!;
    }

    private static ResourceRuntimeLayoutContext Layout()
    {
        return new("CoreCLR", "8.0.32", "x64", 8);
    }

    private static void AssertUnbound(NativeTaskRuntimePremiseBindingCheck check, string reason)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(reason);
        Assert.That(check.UnknownReason, Is.EqualTo(reason));
        Assert.That(check.Inputs, Is.Null);
        Assert.That(check.Evidence, Is.Null);
    }

    private static void AssertUnknown(WorkerClaimResult result, string reason)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(reason);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo(reason));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
        Assert.That(result.ResourceBound.RuntimePremises, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.ResourceBound.Asymptotic, Is.Null);
        Assert.That(result.ResourceBound.Expected, Is.Null);
        Assert.That(result.ResourceBound.Accounting, Is.Empty);
        Assert.That(result.EffectWitness, Is.Null);
        Assert.That(result.ProofCore, Is.Empty);
        Assert.That(result.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
    }
}
