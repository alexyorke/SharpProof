using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    private static readonly string[] LifetimeExpectedBounds = ["72", "32", "40"];

    [TestCase(5, "return new int[1];", 0)]
    [TestCase(6, "return new int[1];", 32)]
    [TestCase(5, "var dead = new int[1]; return new int[3];", 32)]
    [TestCase(6, "var dead = new int[1]; return new int[3];", 40)]
    [TestCase(5, "var value = new int[1]; var alias = value; return alias;", 0)]
    [TestCase(6, "var value = new int[1]; var alias = value; return alias;", 32)]
    [TestCase(5, "return null;", 0)]
    [TestCase(6, "return null;", 0)]
    public async Task LifetimeContractsBindNativeBoundsAndModelProvenance(int metric, string body, int upper)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound((ResourceMetric){metric}, {upper})] public static int[] Target() {{ {body} }} }}";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.Metric, Is.EqualTo((WorkerResourceMetric)metric));
        Assert.That(result.ResourceBound.CostModel, Is.EqualTo(WorkerResourceModels.LifetimeCostModel));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.EqualTo(upper.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(result.ResourceBound.MetadataAssumptions.Single().RuntimeModel, Is.EqualTo(ResourceRuntimeLayout.ModelId));
        Assert.That(result.Assumptions.Count(static assumption => assumption is not null && assumption.Used), Is.EqualTo(1));
        Assert.That(result.ProofCore, Does.Contain("resource-memory-model:" + ResourceMemoryTrace.ModelId));
        Assert.That(target.Compilation.ResourceTargetPlatform, Is.EqualTo("X64"));
        Assert.That(target.Compilation.ResourceMetadataReferences, Has.Length.EqualTo(1));
    }

    [TestCase(5, "return input;")]
    [TestCase(6, "return input;")]
    [TestCase(5, "if(n>0) return new int[1]; return new int[3];")]
    [TestCase(6, "if(n>0) return new int[1]; return new int[3];")]
    [TestCase(5, "return new int[n];")]
    [TestCase(6, "return new int[n];")]
    public async Task LifetimeUnknownCasesHaveNoPartialNumbersOrUsedAssumptions(int metric, string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound((ResourceMetric){metric}, 1000)] public static int[] Target(int n, int[] input) {{ {body} }} }}";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.UnknownReason, Is.Not.Null.And.Not.Empty);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
    }

    [TestCase(5)]
    [TestCase(6)]
    public async Task LifetimeContractsRejectUnboundLayoutEvidence(int metric)
    {
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound((ResourceMetric){metric}, 1000)] public static int[] Target() => new int[1]; }}";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        target.Compilation.ResourceTargetPlatform = "AnyCpu";
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ResourceLayoutAssumptionsNotBound"));
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
    }

    [TestCase(5)]
    [TestCase(6)]
    public async Task LifetimeViolationsRequireCompletedConcreteReplay(int metric)
    {
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound((ResourceMetric){metric}, 0)] public static int[] Target() {{ var dead = new int[1]; return new int[3]; }} }}";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(result.ResourceBound!.UnknownReason, Is.Null);
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.EqualTo(metric == 5 ? "32" : "40"));
        Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(1));
        Assert.That(result.EffectWitness!.Detail, Does.Contain("completed execution"));
    }
    [TestCase(5, 0)]
    [TestCase(6, 32)]
    public async Task LifetimeProofDoesNotRequireACumulativeProgram(int metric, int upper)
    {
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound((ResourceMetric){metric}, {upper})] public static int[] Target() {{ var size=1; return new int[size]; }} }}";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        Assert.That(target.ResourceProgram, Is.Null);
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo(upper.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task LifetimeContractsComposeWithCumulativeBytesWithoutMixingModels()
    {
        var source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes,72)] " +
            "[ResourceUpperBound(ResourceMetric.PeakAuxiliaryBytes,32)] " +
            "[ResourceUpperBound(ResourceMetric.RetainedBytes,40)] " +
            "public static int[] Target() { var dead=new int[1]; return new int[3]; } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var results = await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken);
        Assert.That(results, Has.Length.EqualTo(3));
        foreach (var result in results)
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
            Assert.That(result.ResourceBound!.CostModel, Is.EqualTo(result.ResourceBound.Metric == WorkerResourceMetric.AllocatedBytes
                ? WorkerResourceModels.AllocationCostModel : WorkerResourceModels.LifetimeCostModel));
            Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(1));
        }
        Assert.That(results.Select(static result => result is null ? throw new ArgumentNullException(nameof(result)) : result.ResourceBound!.InferredUpperBound),
            Is.EquivalentTo(LifetimeExpectedBounds));
    }

    [TestCase(5)]
    [TestCase(6)]
    public void LifetimeProtocolRejectsAConflictingCostModel(int metric)
    {
        var constraint = new WorkerResourceConstraint
        {
            Metric = (WorkerResourceMetric)metric,
            CostModel = WorkerResourceModels.LifetimeCostModel,
            UpperBound = ResourceExpressionCodec.Encode(ResourceExpression.Zero, TestContext.CurrentContext.CancellationToken)
        };
        Assert.That(WorkerProtocolMetadata.HasValidResourceConstraint(WorkerEffectContractKind.ResourceUpperBound, constraint), Is.True);
        constraint.CostModel = WorkerResourceModels.AllocationCostModel;
        Assert.That(WorkerProtocolMetadata.HasValidResourceConstraint(WorkerEffectContractKind.ResourceUpperBound, constraint), Is.False);
        constraint.CostModel = WorkerResourceModels.CostModel;
        Assert.That(WorkerProtocolMetadata.HasValidResourceConstraint(WorkerEffectContractKind.ResourceUpperBound, constraint), Is.False);
    }
    [TestCase("Contract.Requires(false);", false)]
    [TestCase("Contract.Requires(n < 0 && n > 0);", false)]
    [TestCase("Contract.Requires(n == 37);", true)]
    public async Task LifetimeReplayValidatesRequiresAndSolverSuppliedEntries(string requires, bool violation)
    {
        ArgumentNullException.ThrowIfNull(requires);
        var source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.RetainedBytes,0)] public static int[] Target(int n) { " +
            requires + " return new int[3]; } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        if (violation)
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
            Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo("40"));
            Assert.That(result.Model.Any(static value => value is not null && value.Variable == "parameter:0" && value.Value == "37"), Is.True);
        }
        else
        {
            Assert.That(result.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(result.ResourceBound!.ObservedLowerBound, Is.Null);
            Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        }
    }
}
