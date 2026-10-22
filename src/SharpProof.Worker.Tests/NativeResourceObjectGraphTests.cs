using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    [TestCase("public class Inner { public int Count {get;set;} } public class Node { public Inner Child=new Inner {Count=3}; }", "var root=new Node(); return new int[root.Child.Count];")]
    [TestCase("public class Node { public int Count; public Node Child; }", "var root=new Node {Child={Count=3}}; return new int[root.Count];")]
    [TestCase("public class Inner { public int this[int i] {get=>i;set{}} } public class Node { public Inner Child=new Inner {[0]=3}; }", "var root=new Node(); return new int[1];")]
    public async Task UnsupportedObjectInitializersCannotPublishPartialLifetimeBounds(string declaration, string body)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; " + declaration + " public static class C { " +
            "[ResourceUpperBound(ResourceMetric.RetainedBytes,1000)] public static int[] Target() {var earlier=new int[1];" + body + "} }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.Not.Null.And.Not.Empty);
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
    }

    [TestCase("return new System.Action(Work);", 0, 64)]
    [TestCase("return new System.Func<int>(Read);", 0, 64)]
    [TestCase("object first=new System.Action(Work); object second=new System.Action(Work); if(first==second) return null; return second;", 64, 64)]
    [TestCase("return (object)true;", 0, 24)]
    [TestCase("return (object)long.MaxValue;", 0, 24)]
    [TestCase("object first=1; object second=true; if(first==second) return null; return second;", 24, 24)]
    public async Task StaticDelegateAndScalarBoxLifetimesPreserveFreshIdentity(string body, int auxiliary, int retained)
    {
        ArgumentNullException.ThrowIfNull(body);
        foreach (var metric in new[] { 5, 6 })
        {
            var upper = metric == 5 ? auxiliary : retained;
            foreach (var violates in new[] { false, true })
            {
                if (violates && upper == 0)
                { continue; }
                var source = "using SharpProof.Attributes; public static class C { public static void Work() {} public static int Read() { throw new System.InvalidOperationException(); } " +
                    $"[ResourceUpperBound((ResourceMetric){metric},{(violates ? upper - 1 : upper)})] public static object Target() {{ {body} }} }}";
                var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
                var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
                Assert.That(result.Outcome, Is.EqualTo(violates ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
                Assert.That(violates ? result.ResourceBound!.ObservedLowerBound : result.ResourceBound!.InferredUpperBound,
                    Is.EqualTo(upper.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }
    }

    [TestCase("var receiver=new Callback(); var saved=new System.Action(receiver.Work); return saved;")]
    [TestCase("return Work;")]
    [TestCase("return () => value;")]
    [TestCase("return new System.Action(System.GC.Collect);")]
    [TestCase("return new System.Action(Initialized.Work);")]
    public async Task DelegateLifetimesWithoutCaptureAndCacheEvidenceHaveNoPartialMetrics(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; public class Callback { public void Work() {} } public static class Initialized { static Initialized() {} public static void Work() {} } public static class C { public static void Work() {} " +
            $"[ResourceUpperBound(ResourceMetric.RetainedBytes,1000)] public static System.Delegate Target(int value) {{ {body} }} }}";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
    }

    [TestCase("", "var root=new Callback(); return new System.Action(root.Work);", 0, 88)]
    [TestCase("public int[] Child;", "var root=new Callback(); root.Child=new int[1]; return new System.Action(root.Work);", 0, 120)]
    [TestCase("public Callback Next;", "var root=new Callback(); root.Next=root; return new System.Action(root.Work);", 0, 88)]
    [TestCase("public Callback Next;", "var parent=new Callback(); var root=new Callback(); parent.Next=root; return new System.Action(parent.Next.Work);", 24, 88)]
    [TestCase("", "var dead=new int[1]; var root=new Callback(); return new System.Action(root.Work);", 32, 88)]
    [TestCase("", "var root=new Callback(); return new System.Func<int>(root.Read);", 0, 88)]
    public async Task InstanceDelegateLifetimesRetainTheirOwnedTargetGraph(string fields, string body, int auxiliary, int retained)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(body);
        foreach (var metric in new[] { 5, 6 })
        {
            var upper = metric == 5 ? auxiliary : retained;
            foreach (var violates in new[] { false, true })
            {
                if (violates && upper == 0)
                { continue; }
                var source = "using SharpProof.Attributes; public class Callback { " + fields +
                    " public void Work() {} public int Read() { throw new System.InvalidOperationException(); } } public static class C { " +
                    $"[ResourceUpperBound((ResourceMetric){metric},{(violates ? upper - 1 : upper)})] public static object Target() {{ {body} }} }}";
                var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
                var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
                Assert.That(result.Outcome, Is.EqualTo(violates ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
                Assert.That(violates ? result.ResourceBound!.ObservedLowerBound : result.ResourceBound!.InferredUpperBound,
                    Is.EqualTo(upper.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }
    }

    [Test]
    public async Task BorrowedDelegateTargetsHaveNoPartialLifetimeMetrics()
    {
        const string source = "using SharpProof.Attributes; public class Callback { public void Work() {} } public static class C { " +
            "[ResourceUpperBound(ResourceMetric.RetainedBytes,1000)] public static System.Action Target(Callback input) { return new System.Action(input.Work); } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
    }

    [TestCase("public class Node { public int Count; }", "var root=new Node(); return new int[root.Count];", 24, 24)]
    [TestCase("public class Node { public int Count; public Node() { Count=2; } }", "var root=new Node(); return new int[root.Count];", 24, 32)]
    [TestCase("public class Node { public int Count; public Node() { if(Count==0) Count=2; } }", "var root=new Node(); return new int[root.Count];", 24, 32)]
    [TestCase("public class Node { public int Count=1; public Node() { if(Count==1) Count=3; } }", "var root=new Node(); return new int[root.Count];", 24, 40)]
    [TestCase("public class Node { public int Count; public Node(int count) { Count=count; } }", "var root=new Node(3); return new int[root.Count];", 24, 40)]
    [TestCase("public class Node { public int Count; }", "var root=new Node(); root.Count=3; return new int[root.Count];", 24, 40)]
    [TestCase("public class Node { public int Count=2; }", "var root=new Node(); return new int[root.Count];", 24, 32)]
    [TestCase("public class Node { public int Count; }", "var root=new Node(); var alias=root; alias.Count=3; return new int[root.Count];", 24, 40)]
    [TestCase("public class Node { public int Count; }", "var first=new Node(); var second=new Node(); second.Count=3; return new int[first.Count];", 48, 24)]
    [TestCase("public class Node { public int Count { get; } }", "var root=new Node(); return new int[root.Count];", 24, 24)]
    [TestCase("public class Node { public bool Flag; }", "var root=new Node(); if(!root.Flag) return new int[1]; return new int[3];", 24, 32)]
    [TestCase("public class Node { public bool Flag; }", "var root=new Node(); root.Flag=true; if(root.Flag==false) return new int[3]; return new int[1];", 24, 32)]
    [TestCase("public class Node { public ulong Count; }", "var root=new Node(); root.Count=ulong.MaxValue; if(root.Count>0) return new int[1]; return new int[3];", 24, 32)]
    [TestCase("public class Node { public int[] Child { get; } }", "var root=new Node(); return root.Child;", 24, 0)]
    [TestCase("public class Node { public int Count; }", "var root=new Node {Count=3}; return new int[root.Count];", 24, 40)]
    [TestCase("public class Node { public int Count=1; public Node() {Count=2;} }", "var root=new Node {Count=3}; return new int[root.Count];", 24, 40)]
    [TestCase("public class Node { public int Count=1; public Node(int value) {Count=value;} }", "var root=new Node(2) {Count=3}; return new int[root.Count];", 24, 40)]
    [TestCase("public class Node { public int Count; public static int Seed() => 3; }", "var root=new Node {Count=Node.Seed()}; return new int[root.Count];", 24, 40)]
    [TestCase("public class Node { public int Count {get;set;} }", "var root=new Node {Count=3}; return new int[root.Count];", 24, 40)]
    [TestCase("public class Node { public int this[int i] {get=>i;set{}} }", "var root=new Node {[0]=3}; return new int[1];", 24, 32)]
    [TestCase("public class Inner { public int Count; } public class Node { public Inner Child=new Inner {Count=3}; }", "var root=new Node(); return new int[root.Child.Count];", 48, 40)]
    [TestCase("public class Inner { public int Count; } public class Node { public Inner Child=new Inner(); }", "var root=new Node {Child={Count=3}}; return new int[root.Child.Count];", 48, 40)]
    public async Task CompilerFieldDefaultsAndScalarStoresDetermineFiniteMemory(string declaration, string body, int auxiliary, int retained)
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
                    $"[ResourceUpperBound((ResourceMetric){metric},{(violates ? upper - 1 : upper)})] public static int[] Target() {{ {body} }} }}";
                var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
                var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
                var unsupported = new List<string>();
                if (result.Outcome == WorkerClaimOutcome.Unknown)
                {
                    foreach (var block in target.Total!.Program.Blocks)
                    {
                        foreach (var instruction in block.Instructions)
                        {
                            if (instruction is IrCallInstruction call)
                            { unsupported.Add("call:" + target.Total.Program.Factory.GetString(target.Total.Program.Factory.GetMemberInfo(call.Member).Name)); }
                            else if (instruction is IrHavocInstruction havoc)
                            {
                                var span = target.Total.Program.Factory.GetOperationInfo(havoc.Operation).SourceSpan;
                                unsupported.Add("havoc:" + havoc.Origin + ":" + havoc.HavocKind + ":" +
                                    (span != null && span.Start >= 0 && span.Length >= 0 && span.Start <= source.Length - span.Length
                                        ? source.Substring(span.Start, span.Length) : "no-source"));
                            }
                            else if (instruction is IrAssumeInstruction or IrAssertInstruction)
                            { unsupported.Add(instruction.GetType().Name); }
                        }
                    }
                }
                Assert.That(result.Outcome, Is.EqualTo(violates ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason + " " + string.Join(",", unsupported));
                Assert.That(violates ? result.ResourceBound!.ObservedLowerBound : result.ResourceBound!.InferredUpperBound,
                    Is.EqualTo(upper.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }
    }

    [Test]
    public async Task UnknownScalarStoreCannotReuseTheFieldsDefaultZero()
    {
        var source = "using SharpProof.Attributes; public class Node { public int Count; } public static class C { " +
            "[ResourceUpperBound(ResourceMetric.RetainedBytes,1000)] public static int[] Target(int n) { var root=new Node(); root.Count=n; return new int[root.Count]; } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
    }

    [Test]
    public async Task InputConstructorCannotInventDefaultFieldEvidence()
    {
        var source = "using SharpProof.Attributes; public class Node { public int Count; public Node(int count) { Count=count; } } public static class C { " +
            "[ResourceUpperBound(ResourceMetric.RetainedBytes,1000)] public static int[] Target(int n) { var root=new Node(n); return new int[root.Count]; } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
    }

    [TestCase("return new string('x', 0);", 0, 0)]
    [TestCase("return new string('x', 3);", 0, 32)]
    [TestCase("var first=new string('x', 3); var saved=first; first=new string('y', 6); return saved;", 40, 32)]
    [TestCase("var first=new string('x', 3); var second=new string('x', 3); return second;", 32, 32)]
    [TestCase("var empty=new string('x', 0); object boxed=empty; var again=new string('y', 0); return boxed;", 0, 0)]
    [TestCase("object first=new string('x', 1); object second=new string('x', 1); if(first==second) return null; return null;", 48, 0)]
    [TestCase("var text=new string('x', 0); if(text==null) return new string('x',3); return text;", 0, 0)]
    public async Task StringStorageLifetimesUseActualIdentitiesAndCachedEmptyStorage(string body, int auxiliary, int retained)
    {
        ArgumentNullException.ThrowIfNull(body);
        foreach (var metric in new[] { 5, 6 })
        {
            var upper = metric == 5 ? auxiliary : retained;
            foreach (var violates in new[] { false, true })
            {
                if (violates && upper == 0)
                { continue; }
                var source = "using SharpProof.Attributes; public static class C { " +
                    $"[ResourceUpperBound((ResourceMetric){metric},{(violates ? upper - 1 : upper)})] public static object Target() {{ {body} }} }}";
                var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
                var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
                Assert.That(result.Outcome, Is.EqualTo(violates ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
                Assert.That(violates ? result.ResourceBound!.ObservedLowerBound : result.ResourceBound!.InferredUpperBound,
                    Is.EqualTo(upper.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }
    }

    [TestCase("return input;")]
    [TestCase("return \"borrowed literal\";")]
    [TestCase("return new string('x',-1);")]
    [TestCase("var first=new string('x',1); var second=new string('x',1); if(first==second) return first; return second;")]
    public async Task UnsupportedStringLifetimesHaveNoPartialMetrics(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound(ResourceMetric.RetainedBytes,100)] public static string Target(string input) {{ {body} }} }}";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
    }

    [TestCase("var root=new Node(); root.Child=new int[1]; return root.Child;", 24, 32)]
    [TestCase("var root=new Node(); root.Child=new int[1]; var saved=root.Child; root.Child=new int[3]; return saved;", 64, 32)]
    [TestCase("var root=new Node(); var alias=root; root.Child=new int[1]; return alias.Child;", 24, 32)]
    [TestCase("var root=new Node(); root.Child=new int[1]; root.Child=null; return root.Child;", 56, 0)]
    public async Task OwnedFieldReadsPreserveOutputIdentityAndAliasing(string body, int auxiliary, int retained)
    {
        ArgumentNullException.ThrowIfNull(body);
        foreach (var metric in new[] { 5, 6 })
        {
            var upper = metric == 5 ? auxiliary : retained;
            foreach (var violates in new[] { false, true })
            {
                if (violates && upper == 0)
                { continue; }
                var source = "using SharpProof.Attributes; public class Node { public int[] Child; } public static class C { " +
                    $"[ResourceUpperBound((ResourceMetric){metric},{(violates ? upper - 1 : upper)})] public static int[] Target() {{ {body} }} }}";
                var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
                var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
                Assert.That(result.Outcome, Is.EqualTo(violates ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
                Assert.That(violates ? result.ResourceBound!.ObservedLowerBound : result.ResourceBound!.InferredUpperBound,
                    Is.EqualTo(upper.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        }
    }

    [Test]
    public async Task FreshUnwrittenReferenceFieldReturnsItsCompilerInitializedNull()
    {
        var source = "using SharpProof.Attributes; public class Node { public int[] Child; } public static class C { " +
            "[ResourceUpperBound(ResourceMetric.RetainedBytes,100)] public static int[] Target() { var root=new Node(); return root.Child; } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo("0"));
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(1));
    }

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
    [TestCase("public class Node { public string A; }", "var root=new Node(); root.A=new string('x',3); root.A=new string('y',6); return root;", 32, 64)]
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
