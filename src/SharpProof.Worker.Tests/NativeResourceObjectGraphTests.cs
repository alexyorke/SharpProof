using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class NativeResourceContractTests
{
    [TestCase(5, 0)]
    [TestCase(6, 56)]
    public async Task ObjectGraphLifetimesRetainOwnedChildStorage(int metric, int upper)
    {
        var source = "using SharpProof.Attributes; public class Node { public int[] Child; } public static class C { " +
            $"[ResourceUpperBound((ResourceMetric){metric},{upper})] public static Node Target() {{ var root=new Node(); root.Child=new int[1]; return root; }} }}";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo(upper.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
    [TestCase("public class Node { public int[] A; public int[] B; }", "var root=new Node(); var child=new int[1]; root.A=child; root.B=child; return root;", 0, 64)]
    [TestCase("public class Node { public Node Next; public int[] A; }", "var root=new Node(); root.Next=root; root.A=new int[1]; return root;", 0, 64)]
    [TestCase("public class Node { public int[] A; }", "var root=new Node(); root.A=new int[1]; root.A=new int[3]; return root;", 32, 64)]
    public async Task ObjectGraphProofAndReplayPreserveSharingCyclesAndOverwrite(string declaration, string body, int auxiliary, int retained)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(body);
        foreach (var metric in new[] { 5, 6 })
        {
            var upper = metric == 5 ? auxiliary : retained;
            foreach (var violates in new[] { false, true })
            {
                if (violates && upper == 0)
                { continue; }
                var source = "using SharpProof.Attributes; " + declaration + " public static class C { " +
                    $"[ResourceUpperBound((ResourceMetric){metric},{(violates ? upper - 1 : upper)})] public static Node Target() {{ {body} }} }}";
                var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
                var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
                Assert.That(result.Outcome, Is.EqualTo(violates ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
                Assert.That(violates ? result.ResourceBound!.ObservedLowerBound : result.ResourceBound!.InferredUpperBound,
                    Is.EqualTo(upper.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }
    }

    [TestCase("public class Node { public int[] A; }", "var root=new Node(); root.A=input; return root;")]
    [TestCase("[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public class Node { public int[] A; }", "return new Node();")]
    [TestCase("public class Node { public System.DateTime A; }", "return new Node();")]
    public async Task ObjectGraphUnknownLayoutsAndBorrowedChildrenHaveNoPartialMetrics(string declaration, string body)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; " + declaration + " public static class C { " +
            $"[ResourceUpperBound(ResourceMetric.RetainedBytes,1000)] public static Node Target(int[] input) {{ {body} }} }}";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
    }
    [TestCase(23, "Refuted")]
    [TestCase(24, "Unknown")]
    [TestCase(32, "Proven")]
    public async Task ObjectGraphPackingIntervalsDoNotBecomeExactCosts(int limit, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var source = "using SharpProof.Attributes; public class Node { public int A; public int B; } public static class C { " +
            $"[ResourceUpperBound(ResourceMetric.RetainedBytes,{limit})] public static Node Target() => new Node(); }}";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome.ToString(), Is.EqualTo(expected), result.ResourceBound?.UnknownReason);
        if (expected == "Refuted")
        { Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo("24")); }
        else if (expected == "Proven")
        { Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo("32")); }
        else
        {
            Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
            Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        }
    }
    [Test]
    public async Task SealedObjectStorageRetainsItsOwnedArray()
    {
        var source = "using SharpProof.Attributes; public sealed class Node { public int[] Child; } public static class C { " +
            "[ResourceUpperBound(ResourceMetric.RetainedBytes,56)] public static Node Target() { var root=new Node(); root.Child=new int[1]; return root; } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo("56"));
    }
}
