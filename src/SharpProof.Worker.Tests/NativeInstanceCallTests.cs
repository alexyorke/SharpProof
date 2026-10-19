using System.Collections.Immutable;
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
            public int Value { get { return _count; } set { _count = value; } }
            public int this[int index] { get { return index * 2; } set { _count = value + index; } }
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
        var allocations = await NativeEffectSiteVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var purity = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var exceptions = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(purity.Outcome, Is.TypeOf<ProvenOutcome>(), purity.Reason.ToString());
            // The downcast may fail, allocating its exception, but whether it
            // does is not concrete.
            foreach (var result in ImmutableArray.Create(allocations, exceptions))
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
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(outcome);
        var preparation = Prepare("public static class C { [DoesNotThrow] public static int Target(Counter counter, int value) { " +
            "Contract.Requires(value > -1000 && value < 1000); " + body + " } }");
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
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
            check => { System.ArgumentNullException.ThrowIfNull(check); results.Add(CallableClaimResultAssembler.FromTotal(preparation, check)); }, null, TestContext.CurrentContext.CancellationToken);
        Assert.That(results[^1].Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), results[^1].Reason.ToString());
    }

    [TestCase("counter.Reset(); return 0;", typeof(RefutedOutcome))]
    [TestCase("return counter.Peek();", typeof(ProvenOutcome))]
    public async Task CalleeFieldWritesAreNonlocal(string body, Type outcome)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(outcome);
        var preparation = Prepare("public static class C { [EnforcePure] public static int Target(Counter counter) { " +
            "Contract.Requires(counter != null); " + body + " } }");
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.TypeOf(outcome), result.Reason.ToString());
    }

    // A setter takes the assigned value as its final parameter, and the
    // assignment's value is the assigned one.
    [TestCase("return counter.Value = value;", "value")]
    [TestCase("return counter[value];", "value * 2")]
    [TestCase("counter[value] = 3; return counter[value];", "value * 2")]
    public async Task SettersAndIndexersInline(string body, string result)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(result);
        var preparation = Prepare("public static class C { public static int Target(Counter counter, int value) { " +
            "Contract.Requires(counter != null && value > -1000 && value < 1000); " +
            "Contract.Ensures(Contract.Result<int>() == " + result + "); " + body + " } }", effects: false);
        var results = new List<WorkerClaimResult>();
        await TotalCallableVerifier.VerifyAsync(preparation, new WorkerBudgets(),
            check => { System.ArgumentNullException.ThrowIfNull(check); results.Add(CallableClaimResultAssembler.FromTotal(preparation, check)); }, null, TestContext.CurrentContext.CancellationToken);
        Assert.That(results[^1].Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), results[^1].Reason.ToString());
    }

    [Test]
    public async Task SetterFieldWritesAreImpure()
    {
        var preparation = Prepare("public static class C { [EnforcePure] public static void Target(Counter counter) { " +
            "Contract.Requires(counter != null); counter.Value = 1; } }");
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
    }

    [Test]
    public async Task VirtualMembersAreOpaqueCalls()
    {
        // Any override may run, so the call is opaque rather than inlined.
        var preparation = Prepare("public static class C { [DoesNotThrow] public static int Target(Counter counter) { " +
            "Contract.Requires(counter != null); return counter.Dispatched(1); } }");
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.Null);
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
    }

    [Test]
    public void LegacyGenericClassificationDoesNotSuppressConstructorArrayTransport()
    {
        var preparation = Prepare("public class ArrayNode<T> { public ArrayNode<T>[] Forwards { get; private set; } " +
            "public ArrayNode(int length) { Forwards = new ArrayNode<T>[length]; } } " +
            "public static class C { [EnforcePure] public static int Target<T>() { Contract.Ensures(Contract.Result<int>() == 2); var node = new ArrayNode<T>(2); return node.Forwards.Length; } }");
        Assert.That(preparation.FailureReason, Is.EqualTo(WorkerClaimReason.UnsupportedCallable));
        Assert.That(preparation.Total, Is.Not.Null);
    }

    [Test]
    public void GenericConstructorSetterKeepsConstructedArrayIdentityInTransport()
    {
        var preparation = Prepare("public class ArrayNode<T> { public ArrayNode<T>[] Forwards { get; private set; } " +
            "public ArrayNode(int length) { Forwards = new ArrayNode<T>[length]; } } " +
            "public static class C { [EnforcePure] public static int Target<T>(T item) where T : class { Contract.Ensures(Contract.Result<int>() == 2); var node = new ArrayNode<T>(2); return node.Forwards.Length; } }");
        Assert.That(preparation.FailureReason, Is.EqualTo(WorkerClaimReason.UnsupportedCallable));
        Assert.That(preparation.Total, Is.Not.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FieldMutationWitnessMustRetainEveryEntryRequirement(bool requireAlias)
    {
        var preparation = Prepare("public class A { } public class B { } public class Holder { public A First; public B Second; public int Value; } " +
            "public static class C { [EnforcePure] public static void Target(Holder holder) { " +
            "Contract.Requires(holder != null && holder.First != null && holder.Second != null && holder.Value == 0); " +
            (requireAlias ? "Contract.Requires(object.ReferenceEquals(holder.First, holder.Second)); " : "") + "holder.Value = 1; } }");
        var result = await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken);
        if (requireAlias)
        { Assert.That(result.Outcome, Is.TypeOf<UnknownOutcome>()); }
        else
        { Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString()); }
    }

    [TestCase("true", WorkerClaimOutcome.Proven)]
    [TestCase("false", WorkerClaimOutcome.Refuted)]
    public async Task ConstructedGenericHelperResultsKeepTheirReferenceIdentity(string expected, WorkerClaimOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var preparation = Prepare("public class Holder<T> { } public class Box<T> { public Holder<T> Echo(Holder<T> value) { return value; } } " +
            "public static class C { public static bool Target(Box<int> box, Holder<int> value) { Contract.Requires(box != null && value != null); " +
            "Contract.Ensures(Contract.Result<bool>() == " + expected + "); return object.ReferenceEquals(box.Echo(value), value); } }", effects: false);
        Assert.That(preparation.Total, Is.Not.Null, preparation.FailureReason.ToString());
        var results = new List<WorkerClaimResult>();
        await TotalCallableVerifier.VerifyAsync(preparation, new WorkerBudgets(),
            check => { ArgumentNullException.ThrowIfNull(check); results.Add(CallableClaimResultAssembler.FromTotal(preparation, check)); }, null, TestContext.CurrentContext.CancellationToken);
        Assert.That(results, Is.Not.Empty);
        Assert.That(results[^1].Outcome, Is.EqualTo(outcome), results[^1].Reason.ToString());
    }
    private static CompilerCallablePreparation Prepare(string types, bool effects = true)
    {
        System.ArgumentNullException.ThrowIfNull(types);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("using SharpProof.Attributes; " + Nodes + types);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        return preparations.Single(preparation => preparation is null ? throw new System.ArgumentNullException(nameof(preparation)) : (effects ? preparation.EffectClaims.Length != 0
            : preparation.Entry.CallableId.Contains("C.Target", StringComparison.Ordinal)));
    }
}
