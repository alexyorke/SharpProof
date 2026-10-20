using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    [TestCase("int n", "n")]
    [TestCase("byte n", "n")]
    [TestCase("char n", "n")]
    [TestCase("short n", "n")]
    [TestCase("long n", "unchecked((int)n)")]
    [TestCase("bool flag", "flag ? 1 : 0")]
    public async Task RootParameterClosureLifetimesRetainTheInitializedFrame(string parameters, string expression)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(expression);
        foreach (var (metric, limit, outcome) in new[]
            { (5, 0, WorkerClaimOutcome.Proven), (6, 88, WorkerClaimOutcome.Proven), (6, 87, WorkerClaimOutcome.Refuted) })
        {
            var source = "using SharpProof.Attributes; public static class C { " +
                $"[ResourceUpperBound((ResourceMetric){metric},{limit})] public static System.Func<int> Target({parameters}) => () => {expression}; }}";
            var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
            var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
            Assert.That(result.Outcome, Is.EqualTo(outcome), result.ResourceBound?.UnknownReason);
            Assert.That(outcome == WorkerClaimOutcome.Refuted ? result.ResourceBound!.ObservedLowerBound : result.ResourceBound!.InferredUpperBound,
                Is.EqualTo(metric == 5 ? "0" : "88"));
        }
    }

    [TestCase("Box input", "input.Read()", "var root=new Box(); return Make(root);", 112)]
    [TestCase("Box input", "input.Read()", "return Make(null);", 88)]
    [TestCase("Box input", "input.Child.Length", "var root=new Box(); root.Child=new int[1]; return Make(root);", 144)]
    [TestCase("Box left, Box right", "left.Read()+right.Read()", "var root=new Box(); return Make(root,root);", 120)]
    [TestCase("Box left, Box right", "left.Read()+right.Read()", "var root=new Box(); root.Child=new int[1]; return Make(root,root);", 152)]
    [TestCase("int n, Box input", "n+input.Read()", "var root=new Box(); return Make(7,root);", 120)]
    public async Task OwnedClosureCapturesComposeWithInlinedSourceFactories(string parameters, string expression, string body, int retained)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(body);
        foreach (var (metric, limit, outcome) in new[]
            { (5, 0, WorkerClaimOutcome.Proven), (6, retained, WorkerClaimOutcome.Proven), (6, retained - 1, WorkerClaimOutcome.Refuted) })
        {
            var source = "using SharpProof.Attributes; public class Box { public int[] Child; public int Read() => 42; } public static class C { " +
                $"public static System.Func<int> Make({parameters}) => () => {expression}; " +
                $"[ResourceUpperBound((ResourceMetric){metric},{limit})] public static System.Func<int> Target() {{ {body} }} }}";
            var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
            var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
            Assert.That(result.Outcome, Is.EqualTo(outcome), result.ResourceBound?.UnknownReason);
            Assert.That(outcome == WorkerClaimOutcome.Refuted ? result.ResourceBound!.ObservedLowerBound : result.ResourceBound!.InferredUpperBound,
                Is.EqualTo(metric == 5 ? "0" : retained.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    [Test]
    public async Task BorrowedClosureCaptureHasNoPartialLifetimeMetrics()
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.RetainedBytes,1000)] public static System.Func<int> Target(int[] values) => () => values.Length; }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
    }

    [TestCase(87, WorkerClaimOutcome.Refuted)]
    [TestCase(88, WorkerClaimOutcome.Unknown)]
    [TestCase(95, WorkerClaimOutcome.Unknown)]
    [TestCase(96, WorkerClaimOutcome.Proven)]
    public async Task ClosureLifetimePackingIntervalsRemainSeparate(int limit, WorkerClaimOutcome outcome)
    {
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound(ResourceMetric.RetainedBytes,{limit})] public static System.Func<int> Target(int first,int second) => () => first+second; }}";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(outcome), result.ResourceBound?.UnknownReason);
        if (outcome == WorkerClaimOutcome.Refuted)
        { Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo("88")); }
        else if (outcome == WorkerClaimOutcome.Proven)
        { Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo("96")); }
        else
        {
            Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
            Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        }
    }

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

    [TestCase("int captured=n; return () => ++captured;")]
    [TestCase("int captured=n; captured++; return () => captured;")]
    [TestCase("int captured=n; return () => { Change(ref captured); return captured; };")]
    [TestCase("System.Func<int> last=null; for(int i=0;i<n;i++) { last=() => n; } return last;")]
    [TestCase("if(n<0) { throw new System.Exception(); } return () => n;")]
    [TestCase("return () => 42;")]
    public async Task UnmodeledClosureScopesHaveNoPartialAllocationVerdict(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; public static class C { " +
            $"static void Change(ref int value) {{ value++; }} [ResourceUpperBound(ResourceMetric.AllocatedBytes, 0)] public static System.Func<int> Target(int n) {{ {body} }} }}";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
    }

    [TestCase("int captured=n; return () => captured;", 88)]
    [TestCase("int[] captured=new int[1]; return () => captured.Length;", 120)]
    [TestCase("Box captured=new Box(); return () => captured.Read();", 112)]
    [TestCase("Box captured=new Box(); Box alias=captured; return () => alias.Read();", 112)]
    [TestCase("Box captured=MakeBox(); return () => captured.Read();", 112)]
    [TestCase("return MakeClosure();", 112)]
    public async Task InitializedLocalCapturesRetainOwnedValues(string body, int retained)
    {
        ArgumentNullException.ThrowIfNull(body);
        foreach (var metric in new[] { 4, 5, 6 })
        {
            var limit = metric == 5 ? 0 : retained;
            var source = "using SharpProof.Attributes; public class Box { public int Read() => 42; } public static class C { static Box MakeBox() => new Box(); " +
                "static System.Func<int> MakeClosure() { Box captured=MakeBox(); return () => captured.Read(); } " +
                $"[ResourceUpperBound((ResourceMetric){metric},{limit})] public static System.Func<int> Target(int n) {{ {body} }} }}";
            var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
            var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
            Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo(limit.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
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
