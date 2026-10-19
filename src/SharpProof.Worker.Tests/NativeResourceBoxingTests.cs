using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeResourceContractTests
{
    [TestCase("object value=n;")]
    [TestCase("object value=(long)n;")]
    [TestCase("object value=(byte)n;")]
    [TestCase("object value=(ushort)n;")]
    [TestCase("object value=(char)n;")]
    [TestCase("object value=true;")]
    public async Task ScalarBoxBoundsAndRefutationsUseTheirOwnedLayout(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        foreach (var bound in new[] { 23, 24 })
        {
            var target = Read(CreateArtifact(Source($"[ResourceUpperBound(ResourceMetric.AllocatedBytes, {bound})]", body), reviewedRuntime: true, x64: true));
            var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
            Assert.That(result.Outcome, Is.EqualTo(bound == 24 ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
            Assert.That(result.ResourceBound!.MetadataAssumptions, Has.Length.EqualTo(1));
            if (bound == 23)
            { Assert.That(result.ResourceBound.ObservedLowerBound, Is.EqualTo("24")); }
            else
            { Assert.That(result.ResourceBound.InferredUpperBound, Is.EqualTo("24")); }
        }
    }

    [Test]
    public async Task RepeatedBoxesComposeWithoutBecomingArrayAllocations()
    {
        var target = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedBytes, 72)] [ResourceUpperBound(ResourceMetric.ArrayAllocations, 0)]",
            "for(int i=0;i<3;i++) { object value=i; }"), reviewedRuntime: true, x64: true));
        var results = await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken);
        Assert.That(results.All(static result => result is not null && result.Outcome == WorkerClaimOutcome.Proven), Is.True);
        var bytes = results.Single(static result => result is null ? throw new ArgumentNullException(nameof(result)) : result.ResourceBound!.Metric == WorkerResourceMetric.AllocatedBytes);
        Assert.That(bytes.ResourceBound!.InferredUpperBound, Is.EqualTo("72"));
    }

    [TestCase("object value=1.0m;")]
    [TestCase("object value=1.0;")]
    public async Task UnsupportedBoxStorageHasNoPartialByteVerdict(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var target = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedBytes, 0)]", body), reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.UnknownReason, Is.Not.Null.And.Not.Empty);
    }
    [Test]
    public async Task BoxingPreservesOperandAllocationsAndEvaluationOrder()
    {
        const string source = "using SharpProof.Attributes; public static class Subject { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes, 50)] public static void Target(int n) { object value=Make(n); } " +
            "static int Make(int value) { var a=new int[3]; return value; } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo("64"));
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.EqualTo("50"));
    }

    [Test]
    public async Task AThrowingOperandDoesNotAllocateABox()
    {
        var target = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedBytes, 0)]", "int zero=0; object value=n/zero;"), reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.EffectWitness, Is.Null);
    }
}
