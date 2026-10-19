using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeResourceContractTests
{
    [TestCase("return () => n;", 88)]
    [TestCase("return new System.Func<int>(() => n);", 88)]
    [TestCase("return () => values.Length;", 88)]
    [TestCase("return () => n + values.Length;", 96)]
    public async Task RootParameterClosuresAccountForEntryFramesAndDelegates(string body, int bytes)
    {
        ArgumentNullException.ThrowIfNull(body);
        foreach (var bound in new[] { bytes - 1, bytes })
        {
            var source = "using SharpProof.Attributes; public static class C { " +
                $"[ResourceUpperBound(ResourceMetric.AllocatedBytes, {bound})] public static System.Func<int> Target(int n, int[] values) {{ {body} }} }}";
            var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
            var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
            Assert.That(result.Outcome, Is.EqualTo(bound == bytes ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
            Assert.That(bound == bytes ? result.ResourceBound!.InferredUpperBound : result.ResourceBound!.ObservedLowerBound,
                Is.EqualTo(bytes.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(1));
        }
    }

    [TestCase("int captured=n; return () => captured;")]
    [TestCase("System.Func<int> last=null; for(int i=0;i<n;i++) { last=() => n; } return last;")]
    [TestCase("if(n<0) { throw new System.Exception(); } return () => n;")]
    [TestCase("return () => 42;")]
    public async Task UnmodeledClosureScopesHaveNoPartialAllocationVerdict(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound(ResourceMetric.AllocatedBytes, 0)] public static System.Func<int> Target(int n) {{ {body} }} }}";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
    }

    [Test]
    public async Task NameofDoesNotInventAParameterCaptureFrame()
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes, 87)] public static System.Func<string> Target(int n) { return () => nameof(n); } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.Null);
    }
    [Test]
    public async Task CapturingActionsHaveTheSameRootFrameCost()
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes, 88)] public static System.Action Target(int n) { return () => { int value=n; }; } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo("88"));
    }
    [TestCase(87, 1)]
    [TestCase(88, 0)]
    [TestCase(96, 2)]
    public async Task ClosurePackingIntervalsDoNotBecomeExactCosts(int bound, int expected)
    {
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound(ResourceMetric.AllocatedBytes, {bound})] public static System.Func<int> Target(int n, int m) {{ return () => n + m; }} }}";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(expected == 1 ? WorkerClaimOutcome.Refuted : expected == 2 ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Unknown), result.ResourceBound?.UnknownReason);
        if (expected == 1)
        { Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo("88")); }
        else if (expected == 2)
        { Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo("96")); }
        else
        {
            Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
            Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        }
    }
}
