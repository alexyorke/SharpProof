using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeResourceContractTests
{
    [TestCase(1, 24)]
    [TestCase(2, 32)]
    [TestCase(5, 32)]
    [TestCase(6, 40)]
    public async Task RepeatedCharacterStringsHaveLayoutBoundBytes(int count, int bytes)
    {
        foreach (var bound in new[] { bytes - 1, bytes })
        {
            var target = Read(CreateArtifact(Source($"[ResourceUpperBound(ResourceMetric.AllocatedBytes, {bound})]",
                $"var value=new string('x', {count});"), reviewedRuntime: true, x64: true));
            var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
            Assert.That(result.Outcome, Is.EqualTo(bound == bytes ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
            Assert.That(result.ResourceBound!.MetadataAssumptions, Has.Length.EqualTo(1));
            Assert.That(bound == bytes ? result.ResourceBound.InferredUpperBound : result.ResourceBound.ObservedLowerBound,
                Is.EqualTo(bytes.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    [Test]
    public async Task EmptyStringConstructionDoesNotAllocate()
    {
        var target = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedBytes, 0)]", "var value=new string('x', 0);"), reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo("0"));
    }

    [Test]
    public async Task SymbolicStringCountHasAConcreteViolatingPrefix()
    {
        var target = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedBytes, 23)]", "var value=new string('x', n);"), reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
        Assert.That(System.Numerics.BigInteger.Parse(result.ResourceBound!.ObservedLowerBound!, System.Globalization.CultureInfo.InvariantCulture), Is.GreaterThan(23));
    }

    [TestCase("var value=new string((char)n, 3);")]
    [TestCase("var value=new string('\\uD800', 3);")]
    [TestCase("var value=new string(new char[3]);")]
    public async Task UnsupportedStringConstructionReturnsUnknown(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var target = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedBytes, 0)]", body), reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
    }
    [TestCase("var first=new string('x', 1); var second=new string('x', 1); if(first==second) { var data=new int[3]; }", 88)]
    [TestCase("var value=new string(count:new int[3].Length, c:'x');", 72)]
    public async Task StringReplayPreservesValueEqualityAndArgumentEffects(string body, int bytes)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = Source($"[ResourceUpperBound(ResourceMetric.AllocatedBytes, {bytes - 1})]", body);

        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo(bytes.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Test]
    public async Task NegativeStringCountCannotSupplyAnAllocationWitness()
    {
        var target = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedBytes, 0)]", "var value=new string('x', -1);"), reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.Null);
    }
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
