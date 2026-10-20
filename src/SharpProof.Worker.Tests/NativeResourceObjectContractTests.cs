using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
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
    [TestCase("public class Cell { public int[] data=new int[3]; }")]
    [TestCase("public class Cell { public int[] Data { get; } =new int[3]; }")]
    [TestCase("public class Cell { public int[] data=new int[3]; public Cell() {} }")]
    public async Task InitializerReplayProducesAConcreteMixedBytePrefix(string declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var target = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedBytes, 50)]", "var item=new Cell();") +
            declaration, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo("64"));
        Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(1));
        Assert.That(result.EffectWitness, Is.Not.Null);
    }
    [TestCase("public class Cell { public int[] data=new int[3]; public Cell() { if(data.Length==3) { var extra=new int[4]; } } }", "var item=new Cell();")]
    [TestCase("public class Cell { public int[] Data { get; } =new int[3]; }", "var item=new Cell(); if(item.Data.Length==3) { var extra=new int[4]; }")]
    public async Task InitializedFieldsReachConstructorAndCallerReads(string declaration, string body)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(body);
        var target = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedBytes, 100)]", body) + declaration,
            reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo("104"));
    }

    [TestCase(63, true)]
    [TestCase(100, false)]
    public async Task ThrowingInitializersStopLaterAllocationPrefixes(int bound, bool violates)
    {
        var source = Source($"[ResourceUpperBound(ResourceMetric.AllocatedBytes, {bound})]", "var item=new Cell();") +
            "public class Cell { public int[] first=new int[3]; public int fault=1/Zero(); public int[] later=new int[10]; static int Zero() { return 0; } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(violates ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Unknown), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo(violates ? "64" : null));
    }

    [Test]
    public async Task AThrowingArgumentCannotRunInstanceInitializers()
    {
        var source = Source("[ResourceUpperBound(ResourceMetric.AllocatedBytes, 0)]", "int zero=0; var item=new Cell(n/zero);") +
            "public class Cell { public int[] data=new int[3]; public Cell(int argument) {} }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.Null);
        Assert.That(result.EffectWitness, Is.Null);
    }
    [TestCase("public class Cell { public int[] data; }", "var item=new Cell { data=new int[3] };", 64)]
    [TestCase("public class Cell { public int[] data=new int[3]; }", "var item=new Cell { data=new int[4] };", 104)]
    [TestCase("public class Cell { public int[] data; public Cell() { data=new int[3]; } }", "var item=new Cell { data=new int[4] };", 104)]
    public async Task ObjectFieldInitializersFollowConstructorExecution(string declaration, string body, int bytes)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(body);
        var target = Read(CreateArtifact(Source($"[ResourceUpperBound(ResourceMetric.AllocatedBytes, {bytes - 1})]", body) + declaration,
            reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo(bytes.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
    [TestCase("public partial class Cell { public int[] data=new int[3]; } public partial class Cell {}", "var item=new Cell();")]
    [TestCase("public class Cell { public int[] Data { get; set; } }", "var item=new Cell { Data=new int[3] };")]
    public async Task UnsupportedInitializerReplayHasNoPartialNumericalVerdict(string declaration, string body)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(body);
        var target = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedBytes, 50)]", body) + declaration,
            reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.EffectWitness, Is.Null);
    }
    [TestCase(23, true)]
    [TestCase(24, false)]
    public async Task ObjectInitializersCannotRunAfterAThrowingConstructor(int bound, bool violates)
    {
        var source = Source($"[ResourceUpperBound(ResourceMetric.AllocatedBytes, {bound})]", "var item=new Cell(n) { data=new int[3] };") +
            "public class Cell { public int[] data; public Cell(int n) { int zero=0; int value=n/zero; } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(violates ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Unknown), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo(violates ? "24" : null));
    }
}
