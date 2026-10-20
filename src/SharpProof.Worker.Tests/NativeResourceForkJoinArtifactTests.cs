using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    [Test]
    public async Task CompilerArtifactCarriesTaskChildrenWithoutInventingRootVerdicts()
    {
        var artifact = CreateArtifact("using SharpProof.Attributes; using System.Threading.Tasks; public static class C { [ResourceUpperBound(ResourceMetric.LoopBodyEntries,100)] public static Task Target(int n) => Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3))); static void Child(int count) {for(int i=0;i<count;i++){}} }");
        Assert.That(artifact.Callables.Single().ForkJoinProjection, Is.Not.Null.And.Not.Empty);
        var target = Read(artifact);
        Assert.That(target.Total, Is.Null);
        Assert.That(target.TotalEntry, Is.Not.Null);
        Assert.That(target.ForkJoinProjection, Is.Not.Null);
        var source = NativeForkJoinSourceCostVerifier.Check(target.ForkJoinProjection!, target.TotalEntry!, TestContext.CurrentContext.CancellationToken);
        Assert.That(source.HasSourceCosts, Is.True, source.UnknownReason);
        Assert.That(source.Upper!.LoopEntries.ToDisplayString(), Is.EqualTo("(max(0, n) + 3)"));
        Assert.That(target.Compilation.ResourceMetadataReferences, Has.Some.Matches<CompilerReferenceSnapshot>(reference => reference is null ? throw new ArgumentNullException(nameof(reference)) :
            reference.Identity == target.ForkJoinProjection!.FrameworkAssemblyIdentity));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
    }

    [TestCase("LoopBodyEntries", false, false)]
    [TestCase("LoopBodyEntries", true, false)]
    [TestCase("AllocatedBytes", false, true)]
    [TestCase("AllocatedBytes", true, true)]
    [TestCase("PeakAuxiliaryBytes", false, true)]
    [TestCase("PeakAuxiliaryBytes", true, true)]
    [TestCase("RetainedBytes", false, true)]
    [TestCase("RetainedBytes", true, true)]
    public async Task CollectorAttachesCarriersOnlyForSelectedStorageMetrics(string metric, bool configured, bool expected)
    {
        ArgumentNullException.ThrowIfNull(metric);
        var source = "using SharpProof.Attributes; using System.Threading.Tasks; public static class C { " +
            "[ResourceUpperBound(ResourceMetric." + metric + ",100)] public static " + (configured ? "async Task" : "Task") +
            " Target(int n) => " + (configured ? "await " : "") + "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3)))" +
            (configured ? ".ConfigureAwait(false)" : "") + "; static void Child(int n) {for(int i=0;i<n;i++){}} }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        Assert.That(target.ForkJoinProjection, Is.Not.Null);
        var capture = target.ForkJoinProjection!.Carriers;
        Assert.That(capture != null, Is.EqualTo(expected));
        if (expected)
        {
            Assert.That(capture!.UnknownReason, Is.Null);
            Assert.That(capture.Shapes, Is.Not.Null);
            var native = NativeTaskCarrierStorageVerifier.Check(capture.Shapes!, target.ForkJoinProjection, target.TotalEntry!,
                new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
            Assert.That(native.UnknownReason, Is.Null);
            Assert.That(native.DirectUpper, Is.Not.Null);
            var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(),
                TestContext.CurrentContext.CancellationToken)).Single();
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ForkJoinResourceMetricUnsupported"));
        }
    }

    [Test]
    public async Task UnavailableCarrierEmissionPreservesTheRootSourceProjection()
    {
        var source = "using SharpProof.Attributes; using System.Threading.Tasks; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,3,\"n\",1)] [ResourceUpperBound(ResourceMetric.PeakAuxiliaryBytes,100)] " +
            "public static Task Target(int n) => Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3))); " +
            "static void Child(int n) {for(int i=0;i<n;i++){}} } /*" + new string('x', 1024 * 1024) + "*/";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        Assert.That(target.ForkJoinProjection, Is.Not.Null);
        Assert.That(target.ForkJoinProjection!.Carriers!.Shapes, Is.Null);
        Assert.That(target.ForkJoinProjection.Carriers.UnknownReason, Is.EqualTo("TaskCarrierEmissionBudgetExceeded"));
        var sourceCosts = NativeForkJoinSourceCostVerifier.Check(target.ForkJoinProjection, target.TotalEntry!, TestContext.CurrentContext.CancellationToken);
        Assert.That(sourceCosts.HasSourceCosts, Is.True, sourceCosts.UnknownReason);
        var results = await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(),
            TestContext.CurrentContext.CancellationToken);
        var loop = results.Single(static result => result is null ? throw new ArgumentNullException(nameof(result)) :
            result.ResourceBound?.Metric == WorkerResourceMetric.LoopBodyEntries);
        var storage = results.Single(static result => result is null ? throw new ArgumentNullException(nameof(result)) :
            result.ResourceBound?.Metric == WorkerResourceMetric.PeakAuxiliaryBytes);
        Assert.That(loop.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), loop.ResourceBound?.UnknownReason);
        Assert.That(storage.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    [TestCase("identity")]
    [TestCase("entry")]
    [TestCase("ordinary-body")]
    public void CompilerArtifactRejectsUnownedTaskProjectionData(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var artifact = CreateArtifact("using SharpProof.Attributes; using System.Threading.Tasks; public static class C { [ResourceUpperBound(ResourceMetric.LoopBodyEntries,100)] public static Task Target(int n) => Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3))); static void Child(int count) {for(int i=0;i<count;i++){}} }");
        var callable = artifact.Callables.Single();
        if (mutation == "entry")
        { callable.TotalEntry = null; }
        else if (mutation == "ordinary-body")
        {
            var ordinary = CreateArtifact("using SharpProof.Attributes; public static class C { [ResourceUpperBound(ResourceMetric.LoopBodyEntries,100)] public static void Target(int n) {for(int i=0;i<n;i++){}} }");
            ordinary.Callables.Single().ForkJoinProjection = callable.ForkJoinProjection;
            artifact = ordinary;
        }
        else
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(callable.ForkJoinProjection!)!.AsObject();
            json["root"] = "other";
            callable.ForkJoinProjection = System.Text.Encoding.UTF8.GetBytes(json.ToJsonString());
        }
        Action read = () => { _ = Read(artifact); };
        Rejects(read);
    }
}
