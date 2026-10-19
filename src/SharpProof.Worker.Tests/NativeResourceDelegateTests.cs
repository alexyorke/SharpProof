using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeResourceContractTests
{
    [TestCase("return new System.Action(Callbacks.Run);", "public static class Callbacks { public static void Run() {} }", 64)]
    [TestCase("return new System.Action(Callbacks.Run);", "public static class Callbacks { public static void Run() { var data=new int[100]; } }", 64)]
    [TestCase("return new System.Action(new Callbacks().Run);", "public class Callbacks { public void Run() {} }", 88)]
    [TestCase("return new System.Action(new Callbacks().Run);", "public class Callbacks { public int[] data=new int[3]; public void Run() {} }", 128)]
    [TestCase("System.Action last=null; for(int i=0;i<3;i++) { last=new System.Action(Callbacks.Run); } return last;", "public static class Callbacks { public static void Run() {} }", 192)]
    public async Task ExplicitDelegatesHaveTypedStorageAndPreserveReceiverEffects(string body, string declarations, int bytes)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(declarations);
        foreach (var bound in new[] { bytes - 1, bytes })
        {
            var source = "using SharpProof.Attributes; public static class C { " +
                $"[ResourceUpperBound(ResourceMetric.AllocatedBytes, {bound})] public static System.Action Target(int n, int[] values) {{ {body} }} }} " + declarations;
            var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
            var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
            Assert.That(result.Outcome, Is.EqualTo(bound == bytes ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
            Assert.That(bound == bytes ? result.ResourceBound!.InferredUpperBound : result.ResourceBound!.ObservedLowerBound,
                Is.EqualTo(bytes.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(1));
        }
    }

    [Test]
    public async Task DiscardedExplicitDelegatesDoNotAllocate()
    {
        var source = Source("[ResourceUpperBound(ResourceMetric.AllocatedBytes, 0)]", "_ = new System.Action(Callbacks.Run);") +
            "public static class Callbacks { public static void Run() {} }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo("0"));
    }

    [TestCase("return Callbacks.Run;")]
    [TestCase("return () => {};")]
    [TestCase("return () => { int value=n; };")]
    [TestCase("return new System.Action(() => {});")]
    public async Task UnmodeledDelegateConversionsRemainUnknown(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound(ResourceMetric.AllocatedBytes, 0)] public static System.Action Target(int n) {{ {body} }} }} " +
            "public static class Callbacks { public static void Run() {} }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
    }

    [Test]
    public async Task NullDelegateReceiversCannotSupplyAnAllocationWitness()
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes, 0)] public static System.Action Target() { Callbacks value=null; return new System.Action(value.Run); } } " +
            "public class Callbacks { public void Run() {} }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.Null);
        Assert.That(result.EffectWitness, Is.Null);
    }
    [Test]
    public async Task DelegateTargetsWithTypeInitializationRemainUnknown()
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes, 64)] public static System.Action Target() { return new System.Action(Callbacks.Run); } } " +
            "public static class Callbacks { static Callbacks() {} public static void Run() {} }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("DelegateTypeInitializationUnknown"));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
    }
    [Test]
    public async Task GenericDelegateTypesUseTheSameFreshStorageModel()
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes, 63)] public static System.Func<int> Target() { return new System.Func<int>(Callbacks.Run); } } " +
            "public static class Callbacks { public static int Run() { return 42; } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo("64"));
    }

    [TestCase("return new System.Action(System.GC.Collect);", "")]
    [TestCase("return new System.Action(Callbacks.Run);", "public static class Callbacks { [System.Runtime.InteropServices.DllImport(\"missing-native-library\")] public static extern void Run(); }")]
    public async Task UnreviewedDelegateBindingsHaveNoNumericalVerdict(string body, string declaration)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(declaration);
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound(ResourceMetric.AllocatedBytes, 0)] public static System.Action Target() {{ {body} }} }} " + declaration;
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
    }
}
