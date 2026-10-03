using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Nonvirtual source members inline on a reference receiver, which is checked
// for null after the arguments. Inside the callee, `this` is never null.
[TestFixture]
public sealed class NativeInstanceCallTests
{
    private const string Nodes = """
        public class Node<TKey, TValue> {
            private Node<TKey, TValue> _parent;
            public virtual Node<TKey, TValue> Parent { get { return this._parent; } set { this._parent = value; } }
        }
        public class RedNode<TKey, TValue> : Node<TKey, TValue> {
            public new RedNode<TKey, TValue> Parent { get { return (RedNode<TKey, TValue>)base.Parent; } }
        }
        public class Counter {
            private int _count;
            public int Twice(int value) { return value * 2; }
            public void Reset() { _count = 0; }
            public int Peek() { return _count; }
            public virtual int Dispatched(int value) { return value; }
        }
        """;

    [Test]
    public async Task GenericGettersWithDowncastsArePure()
    {
        var preparation = Prepare("""
            public class Map<TKey, TValue> {
                [ZeroAllocations, EnforcePure, DoesNotThrow]
                public RedNode<TKey, TValue> SafeParent(RedNode<TKey, TValue> node) {
                    if (node == null || node.Parent == null) return null;
                    return node.Parent;
                }
            }
            """);
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets());
        var purity = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        var exceptions = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(purity.Outcome, Is.TypeOf<ProvenOutcome>(), purity.Reason.ToString());
            // The downcast may fail, allocating its exception, but whether it
            // does is not concrete.
            foreach (var result in new[] { allocations, exceptions })
            {
                Assert.That(result.Outcome, Is.Null);
                Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
            }
        }
    }

    [TestCase("Contract.Requires(counter != null); return counter.Twice(value);", typeof(ProvenOutcome))]
    [TestCase("return counter.Twice(value);", typeof(RefutedOutcome))]
    public async Task ReceiversFaultOnlyWhenNull(string body, Type outcome)
    {
        var preparation = Prepare("public static class C { [DoesNotThrow] public static int Target(Counter counter, int value) { " +
            "Contract.Requires(value > -1000 && value < 1000); " + body + " } }");
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf(outcome), result.Reason.ToString());
        if (outcome == typeof(RefutedOutcome))
        { Assert.That(result.ExceptionWitness!.Kind, Is.EqualTo(IrExceptionKind.NullReference)); }
    }

    [Test]
    public async Task InlinedResultsReachPostconditions()
    {
        var preparation = Prepare("public static class C { public static int Target(Counter counter, int value) { " +
            "Contract.Requires(counter != null && value > -1000 && value < 1000); " +
            "Contract.Ensures(Contract.Result<int>() == value + value); return counter.Twice(value); } }", effects: false);
        var results = new List<WorkerClaimResult>();
        await TotalCallableVerifier.VerifyAsync(preparation, new WorkerBudgets(),
            check => results.Add(CallableClaimResultAssembler.FromTotal(preparation, check)), null, CancellationToken.None);
        Assert.That(results[^1].Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), results[^1].Reason.ToString());
    }

    [TestCase("counter.Reset(); return 0;", typeof(RefutedOutcome))]
    [TestCase("return counter.Peek();", typeof(ProvenOutcome))]
    public async Task CalleeFieldWritesAreNonlocal(string body, Type outcome)
    {
        var preparation = Prepare("public static class C { [EnforcePure] public static int Target(Counter counter) { " +
            "Contract.Requires(counter != null); " + body + " } }");
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.TypeOf(outcome), result.Reason.ToString());
    }

    [Test]
    public async Task VirtualMembersStayUnsupported()
    {
        var preparation = Prepare("public static class C { [DoesNotThrow] public static int Target(Counter counter) { " +
            "Contract.Requires(counter != null); return counter.Dispatched(1); } }");
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, Is.Null);
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    private static CompilerCallablePreparation Prepare(string types, bool effects = true)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("using SharpProof.Attributes; " + Nodes + types);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single(preparation => effects ? preparation.EffectClaims.Length != 0
            : preparation.Entry.CallableId.Contains("C.Target", StringComparison.Ordinal));
    }
}
