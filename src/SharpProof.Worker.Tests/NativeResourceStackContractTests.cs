using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeResourceContractTests
{
    [TestCase(7, 2, WorkerClaimOutcome.Proven)]
    [TestCase(8, 48, WorkerClaimOutcome.Proven)]
    [TestCase(7, 1, WorkerClaimOutcome.Unknown)]
    [TestCase(8, 47, WorkerClaimOutcome.Unknown)]
    public async Task SourceStackContractsUseDeclaredActivationStorage(int metric, int limit, WorkerClaimOutcome outcome)
    {
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound((ResourceMetric){metric},{limit})] public static void Target(int n) {{ Leaf(n); }} private static void Leaf(int value) {{ }} }}";
        var target = Read(CreateArtifact(source));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(outcome), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.CostModel, Is.EqualTo(WorkerResourceModels.StackCostModel));
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        if (outcome == WorkerClaimOutcome.Proven)
        {
            Assert.That(result.ResourceBound.InferredUpperBound, Is.EqualTo(limit.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(result.ProofCore, Does.Contain("resource-stack-model:" + WorkerResourceModels.StackCostModel));
        }
        else
        {
            Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
            Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        }
    }

    [TestCase("Target(n+1);")]
    [TestCase("System.GC.Collect();")]
    public async Task UnsupportedSourceStackCallsHaveNoPartialNumbers(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.SourceActivationDepth,100)] public static void Target(int n) { " + body + " } }";
        var target = Read(CreateArtifact(source));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
    }
    [TestCase(7, 1)]
    [TestCase(8, 24)]
    public async Task RecursiveSourceStackContractsCheckTheirRankingProgram(int metric, int scale)
    {
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound((ResourceMetric){metric},{scale},nameof(n),{scale})] public static void Target(int n) {{ if(n<=0) return; Target(n-1); }} }}";
        var target = Read(CreateArtifact(source));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.CostModel, Is.EqualTo(WorkerResourceModels.StackCostModel));
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
    }
}
