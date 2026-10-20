using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    [TestCase(false, 4)]
    [TestCase(false, 6)]
    [TestCase(true, 4)]
    [TestCase(true, 6)]
    public async Task VirtualOrArbitraryReceiverFactoryCallsRemainUnknown(bool virtualCall, int metric)
    {
        var source = "using SharpProof.Attributes; public class C { " +
            (virtualCall ? "public virtual " : "public ") +
            "System.Func<System.Func<int>> Make(int n) => () => () => n; " +
            $"[ResourceUpperBound((ResourceMetric){metric},1024)] public System.Func<System.Func<int>> Target(C other,int n) => " +
            (virtualCall ? "Make(n); }" : "other.Make(n); }");
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.Not.Null.And.Not.Empty);
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
    }

    [TestCase(false, 4)]
    [TestCase(false, 5)]
    [TestCase(false, 6)]
    [TestCase(true, 4)]
    [TestCase(true, 5)]
    [TestCase(true, 6)]
    public async Task CurrentReceiverCallsComposeReceiverIndependentFactoryLifetimes(bool explicitThis, int metric)
    {
        var limit = metric == 5 ? 0 : 96;
        var source = "using SharpProof.Attributes; public sealed class C { " +
            "System.Func<System.Func<int>> Make(int n) => () => () => n; " +
            $"[ResourceUpperBound((ResourceMetric){metric},{limit})] public System.Func<System.Func<int>> Target(int n) => " +
            (explicitThis ? "this.Make(n); }" : "Make(n); }");
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason ?? target.ResourceUnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo(limit.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Test]
    public void CompiledInstanceFactoryDoesNotRetainItsUnusedReceiver()
    {
        var receiver = new ReceiverIndependentFactory();
        var outer = receiver.Make(7);
        var frame = outer.Target;
        Assert.That(frame, Is.Not.Null);
        Assert.That(frame, Is.Not.SameAs(receiver));
        var fields = frame!.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        Assert.That(fields, Has.Length.EqualTo(2));
        Assert.That(fields.Count(static field => field is null ? throw new ArgumentNullException(nameof(field)) : field.FieldType == typeof(int)), Is.EqualTo(1));
        Assert.That(fields.Any(static field => field is null ? throw new ArgumentNullException(nameof(field)) : field.FieldType == typeof(ReceiverIndependentFactory)), Is.False);
        var inner = outer();
        Assert.That(inner.Target, Is.SameAs(frame));
        Assert.That((object)outer(), Is.SameAs(inner));
        Assert.That(inner(), Is.EqualTo(7));
    }

    private sealed class ReceiverIndependentFactory
    {
        internal Func<Func<int>> Make(int value)
        {
            GC.KeepAlive(this);
            return () => () => value;
        }
    }

    [TestCase("() => this.value")]
    [TestCase("() => () => this.value")]
    [TestCase("() => n + value")]
    public async Task InstanceReceiverCaptureRemainsUnknown(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var returnType = expression.Contains("() => ()", StringComparison.Ordinal) ? "System.Func<System.Func<int>>" : "System.Func<int>";
        var source = "using SharpProof.Attributes; public sealed class C { int value = 1; " +
            $"[ResourceUpperBound(ResourceMetric.RetainedBytes,1024)] public {returnType} Target(int n) => {expression}; }}";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    public async Task InstanceFactoryWithoutReceiverCaptureOwnsOnlyTheReturnedRootFrame(int metric)
    {
        var limit = metric == 5 ? 0 : 96;
        var source = "using SharpProof.Attributes; public sealed class C { " +
            $"[ResourceUpperBound((ResourceMetric){metric},{limit})] public System.Func<System.Func<int>> Target(int n) " +
            "=> () => () => n; }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason ?? target.ResourceUnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo(limit.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    public async Task MultipleDeferredRootDelegatesReserveSeparateCacheSlots(int metric)
    {
        var limit = metric == 5 ? 0 : 104;
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound((ResourceMetric){metric},{limit})] public static System.Func<System.Func<System.Func<int>>> Target(int n) " +
            "=> () => () => () => n; }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason ?? target.ResourceUnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo(limit.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Test]
    public void CompiledMultipleDeferredDelegatesKeepDistinctCachesOnTheSameFrame()
    {
        var outer = Create(7);
        var frame = outer.Target;
        Assert.That(frame, Is.Not.Null);
        var fields = frame!.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        Assert.That(fields, Has.Length.EqualTo(3));
        var middle = outer();
        var inner = middle();
        Assert.That(middle.Target, Is.SameAs(frame));
        Assert.That(inner.Target, Is.SameAs(frame));
        Assert.That((object)outer(), Is.SameAs(middle));
        Assert.That((object)middle(), Is.SameAs(inner));
        Assert.That(inner(), Is.EqualTo(7));

        static Func<Func<Func<int>>> Create(int value)
        {
            return () => () => () => value;
        }
    }

    [Test]
    public async Task ConstantArgumentNestedFactoryRetainsTheCachedRootFrame()
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "static System.Func<System.Func<int>> Make(int value) => () => () => value; " +
            "[ResourceUpperBound(ResourceMetric.RetainedBytes,96)] public static System.Func<System.Func<int>> Target() => Make(3); }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason ?? target.ResourceUnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo("96"));
    }

    [TestCase(false, 4)]
    [TestCase(false, 5)]
    [TestCase(false, 6)]
    [TestCase(true, 4)]
    [TestCase(true, 5)]
    [TestCase(true, 6)]
    public async Task NestedRootCapturesKeepDeferredDelegateAllocationsOutsideTheFactory(bool calledFactory, int metric)
    {
        var limit = metric == 5 ? 0 : 96;
        var source = "using SharpProof.Attributes; public static class C { " +
            "static System.Func<System.Func<int>> Make(int n) => () => () => n; " +
            $"[ResourceUpperBound((ResourceMetric){metric},{limit})] public static System.Func<System.Func<int>> Target(int n) " +
            (calledFactory ? "=> Make(n); }" : "=> () => () => n; }");
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason ?? target.ResourceUnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo(limit.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    public async Task NestedRootCapturesRetainOwnedLocalGraphsWithoutAllocatingTheDeferredDelegate(int metric)
    {
        var limit = metric == 5 ? 0 : 136;
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound((ResourceMetric){metric},{limit})] public static System.Func<System.Func<int>> Target(int n) {{ " +
            "int captured=n; var values=new int[2]; return () => () => captured+values.Length; } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason ?? target.ResourceUnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo(limit.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Test]
    public void CompiledNestedClosuresShareOneRootFrameAndCreateTheInnerDelegateOnLaterInvocation()
    {
        for (var value = -2; value <= 3; value++)
        {
            var outer = Create(value);
            var frame = outer.Target;
            Assert.That(frame, Is.Not.Null);
            var fields = frame!.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Assert.That(fields, Has.Length.EqualTo(2));
            var captured = fields.Single(field => field is null ? throw new ArgumentNullException(nameof(field)) : field.FieldType == typeof(int));
            var cache = fields.Single(field => field is null ? throw new ArgumentNullException(nameof(field)) : field.FieldType == typeof(Func<int>));
            Assert.That(captured.GetValue(frame), Is.EqualTo(value));
            Assert.That(cache.GetValue(frame), Is.Null);
            var inner = outer();
            Assert.That(cache.GetValue(frame), Is.SameAs(inner));
            Assert.That((object)outer(), Is.SameAs(inner));
            Assert.That((object)inner, Is.Not.SameAs(outer));
            Assert.That(inner.Target, Is.SameAs(frame));
            Assert.That(inner(), Is.EqualTo(value));
        }

        static Func<Func<int>> Create(int value)
        {
            return () => () => value;
        }
    }

    [TestCase("System.Func<int,System.Func<int>>", "return k => () => n+k;")]
    [TestCase("System.Func<System.Func<int>>", "return () => {int k=n; return () => k;};")]
    [TestCase("System.Func<System.Func<int>>", "return () => () => ++n;")]
    [TestCase("System.Func<System.Func<int>>", "return () => {int Read()=>n; return () => Read();};")]
    [TestCase("System.Func<System.Func<System.Threading.Tasks.Task<int>>>", "return () => async () => {await System.Threading.Tasks.Task.CompletedTask; return n;};")]
    public async Task IntermediateMutableLocalFunctionAndAsyncCaptureScopesRemainUnknown(string returnType, string body)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes,10000)] public static " + returnType + " Target(int n) { " + body + " } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
    }

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
