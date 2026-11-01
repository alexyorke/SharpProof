using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    private static async Task<WorkerClaimResult> VerifyTarget(string attributes, string body)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; public static class C { " + attributes +
            " public static void Target(int n,int m) { " + body + " } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        return (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(),
            TestContext.CurrentContext.CancellationToken)).Single();
    }

    // Each declared bound holds only if the analysis misses the shape's real
    // cost, and the worker's Proven path does not check the program against IR.
    [TestCase("[ResourceUpperBound(ResourceMetric.LoopBodyEntries,0,nameof(n),2)]",
        "int i=1; for(int j=0;j<n;j++) { while(i>0) { i--; } i=m; }", "UnprovedLoopProgress")]
    [TestCase("[ResourceUpperBound(ResourceMetric.LoopBodyEntries,0,nameof(n),6)]",
        "int i=0; for(int j=0;j<n;j++) { while(i<5) { i++; } i=-m; }", "UnprovedLoopProgress")]
    [TestCase("[ResourceUpperBound(ResourceMetric.LoopBodyEntries,0,nameof(n),9)]",
        "int credit=0; for(int j=0;j<n;j++) { for(int i=0;i<4;i++) { credit++; while(credit>0) { credit--; } } credit=m; }", "UnprovedLoopProgress")]
    [TestCase("[ResourceUpperBound(ResourceMetric.ArrayAllocations,0)]",
        "for(int i=0;i<new int[1000].Length;i++) { }", "UnsupportedLoopBound")]
    [TestCase("[ResourceUpperBound(ResourceMetric.ArrayAllocations,1)]",
        "var a=new int[new int[1000].Length];", "UnsupportedAllocationSize")]
    public async Task ProgramsThatMissARealCostCannotPublishAProof(string attributes, string body, string reason)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(reason);
        var result = await VerifyTarget(attributes, body);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown), result.ResourceBound?.InferredUpperBound);
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo(reason));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
    }

    [TestCase("[ResourceUpperBound(ResourceMetric.LoopBodyEntries,0,nameof(n),4)]",
        "for(int j=0;j<n;j++) { int i=3; while(i>0) { i--; } }")]
    [TestCase("[ResourceUpperBound(ResourceMetric.LoopBodyEntries,0,nameof(n),2)]",
        "int credit=0; for(int i=0;i<n;i++) { credit++; while(credit>0) { credit--; } }")]
    [TestCase("[ResourceUpperBound(ResourceMetric.ArrayAllocations,1)]",
        "int k=new int[5].Length; while(k>0) { k--; }")]
    public async Task OrdinaryNestedAndChargedShapesStillProve(string attributes, string body)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(body);
        var result = await VerifyTarget(attributes, body);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
    }

    [Test]
    public async Task AVerifiedMethodsOwnTypeInitializerAlreadyStopsTheNativeProof()
    {
        var source = "using SharpProof.Attributes; public static class C { static C() { var hidden=new int[1000]; } " +
            "[ResourceUpperBound(ResourceMetric.ArrayAllocations,0)] public static void Target() { } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(),
            TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ResourceEntryUnavailable"));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
    }
}
