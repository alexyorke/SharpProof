using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeResourceContractTests
{
    [TestCase("", "var item=new object();", 24)]
    [TestCase("public class Cell {}", "var item=new Cell();", 24)]
    [TestCase("public class Cell { public int value; }", "var item=new Cell();", 24)]
    [TestCase("public class Cell { public byte first; public int second; }", "var item=new Cell();", 32)]
    [TestCase("public class Cell { public int[] data=new int[3]; }", "var item=new Cell();", 64)]
    [TestCase("public class Cell { public int[] Data { get; } = new int[3]; }", "var item=new Cell();", 64)]
    [TestCase("public class Cell { public int value; public Cell(int n) { value=n; for(int i=0;i<n;i++) { var a=new int[2]; } } }", "var item=new Cell(3);", 120)]
    [TestCase("public class Cell { public int[] data; public Cell(int[] a) { data=a; } }", "var item=new Cell(new int[3]);", 64)]
    [TestCase("public class Cell { public int[] data; }", "var item=new Cell { data=new int[3] };", 64)]
    public async Task ObjectByteBoundsIncludeInstanceSlotsAndConstructionEffects(string declarations, string body, int upper)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(body);
        var target = Read(CreateArtifact(Source($"[ResourceUpperBound(ResourceMetric.AllocatedBytes, {upper})]", body) + declarations,
            reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo(upper.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(1));
    }

    [TestCase(23, WorkerClaimOutcome.Refuted)]
    [TestCase(24, WorkerClaimOutcome.Unknown)]
    [TestCase(32, WorkerClaimOutcome.Proven)]
    public async Task LooseObjectPackingNeverInventsAnExactByteCost(int bound, WorkerClaimOutcome expected)
    {
        var target = Read(CreateArtifact(Source($"[ResourceUpperBound(ResourceMetric.AllocatedBytes, {bound})]", "var item=new Cell();") +
            "public class Cell { public byte first; public int second; }", reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(expected), result.ResourceBound?.UnknownReason);
        if (expected == WorkerClaimOutcome.Refuted)
        { Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo("24")); }
        if (expected == WorkerClaimOutcome.Unknown)
        {
            Assert.That(result.ResourceBound!.ObservedLowerBound, Is.Null);
            Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
            Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        }
    }

    [Test]
    public async Task ConstructorArrayEventsComposeWithManagedObjectLowerCosts()
    {
        var target = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedBytes, 50)]", "var item=new Cell();") +
            "public class Cell { public Cell() { var data=new int[3]; } }", reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo("64"));
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.EqualTo("50"));
        Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(1));
    }
    [Test]
    public async Task OpaqueInitializerReplayCannotInventMixedBytePrefixes()
    {
        var target = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedBytes, 50)]", "var item=new Cell();") +
            "public class Cell { public int[] data=new int[3]; }", reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ResourceBoundComparisonUnproved"));
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.EffectWitness, Is.Null);
    }
}
