using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// `new D(...)` of a class with source base classes runs, on one object, D's
// instance initializers, then the base constructor's arguments, the base
// class's initializers, its own base constructor and its body, then D's
// body. A base constructor that calls a virtual member of `this`, or a base
// class declared in metadata, keeps the construction unmodeled.
[TestFixture]
public sealed class BaseConstructorChainTests
{
    private const string Types = """
        using SharpProof.Attributes;
        public static class Log
        {
            public static long Value;
            public static long Bump() { Value = Value + 1; return Value; }
        }
        public class Root
        {
            protected readonly long root;
            private readonly long rootInit = 5;
            public Root(long v) { root = v; }
            public long RootValue => root;
            public long RootInit => rootInit;
        }
        public class Middle : Root
        {
            protected readonly long middle;
            public Middle(long v, long w) : base(v * 2) { middle = w + root; }
            public long MiddleValue => middle;
        }
        public sealed class Leaf : Middle
        {
            private readonly long leaf = 1;
            public Leaf(long v) : base(v, 3) { leaf = leaf + middle; }
            public long LeafValue => leaf;
        }
        public abstract class Shape
        {
            private readonly long sides;
            protected Shape(long sides) { this.sides = sides; }
            public long Sides => sides;
        }
        public sealed class Square : Shape
        {
            public Square() : base(4) { }
        }
        public class Seed
        {
            protected readonly long seed = 4;
            protected readonly long doubled;
            public Seed() { doubled = seed * 2; }
        }
        public sealed class Grown : Seed
        {
            private readonly long grown;
            public Grown(long v) { grown = v + doubled; }
            public long Value => grown;
        }
        public class Plain
        {
            protected readonly long p = 6;
        }
        public class Between : Plain
        {
        }
        public sealed class Implicit : Between
        {
            private readonly long q = 2;
            public long Sum => p + q;
        }
        public class Recorder
        {
            private readonly long seen;
            public Recorder(long x) { seen = Log.Value; Log.Value = x; }
            public long Seen => seen;
        }
        public sealed class Ordered : Recorder
        {
            private readonly long early = Log.Value;
            private readonly long late;
            public Ordered(long x) : base(x) { late = Log.Value; }
            public long Early => early;
            public long Late => late;
        }
        public sealed class Bumped : Recorder
        {
            private readonly long first = Log.Bump();
            public Bumped(long x) : base(x) { }
            public Bumped() : base(Log.Value * 10) { }
            public long First => first;
        }
        public class Dispatching
        {
            private readonly long size;
            public Dispatching() { size = Size(); }
            public virtual long Size() => 1;
            public long Measured => size;
        }
        public sealed class Overriding : Dispatching
        {
            public override long Size() => 2;
        }
        public class Failure : System.Exception
        {
            private readonly long code;
            public Failure(long code) : base("failed") { this.code = code; }
            public long Code => code;
        }
        public sealed class Specific : Failure
        {
            public Specific(long code) : base(code) { }
        }
        public static class Fail
        {
            public static long Argument() { throw new System.ArgumentException("argument"); }
            public static long Operation() { throw new System.InvalidOperationException("operation"); }
        }
        public class Guarded
        {
            private readonly long guard = Fail.Operation();
            protected Guarded(long x) { }
            protected Guarded() { }
        }
        public sealed class EarlyInitializer : Guarded
        {
            private readonly long first = Fail.Argument();
            public EarlyInitializer() : base(Fail.Operation()) { }
        }
        public sealed class LateBase : Guarded
        {
            public LateBase() : base() { }
        }
        public class Checked
        {
            private readonly long c;
            protected Checked(long x) { c = x; if (x < 0) { throw new System.InvalidOperationException("negative"); } }
        }
        public sealed class CheckedLeaf : Checked
        {
            public CheckedLeaf(long x) : base(x) { if (x < 10) { throw new System.ArgumentException("small"); } }
        }
        public class HiddenSlotBase
        {
            protected readonly long slot;
            protected HiddenSlotBase() { BeforeWrite = slot; slot = 3; }
            public long BeforeWrite { get; }
        }
        public sealed class HiddenSlotLeaf : HiddenSlotBase
        {
            private new readonly long slot = 8;
            public long Sum => base.slot + slot;
        }
        public class PublishedBase
        {
            public static PublishedBase Latest;
            protected PublishedBase() { Latest = this; }
        }
        public sealed class PublishedLeaf : PublishedBase
        {
            private readonly long value;
            public PublishedLeaf(long input) { value = input; }
        }
        public class AmbientBase
        {
            private static readonly int count;
            static AmbientBase() { count = System.Environment.ProcessorCount; }
            protected AmbientBase() { }
        }
        public sealed class AmbientLeaf : AmbientBase { }
        public class FinalizingBase
        {
            ~FinalizingBase() { Log.Value = 1; }
        }
        public sealed class FinalizingLeaf : FinalizingBase { }
        public class IndirectDispatch
        {
            private readonly long value;
            protected IndirectDispatch() { value = Read(); }
            private long Read() => Compute();
            protected virtual long Compute() => 1;
            public long Value => value;
        }
        public sealed class IndirectLeaf : IndirectDispatch
        {
            protected override long Compute() => 2;
        }
        """;

    // A derived class's initializers run before the base constructor's
    // arguments; those run before the base class's initializers, which run
    // before the base body; the derived body runs last. Each step throws a
    // different exception, so only the right order lets just one escape.
    [TestCase("[AllowedExceptions(typeof(System.ArgumentException))] public static object M() { return new EarlyInitializer(); }", true)]
    [TestCase("[AllowedExceptions(typeof(System.InvalidOperationException))] public static object M() { return new EarlyInitializer(); }", false)]
    [TestCase("[AllowedExceptions(typeof(System.InvalidOperationException))] public static object M() { return new LateBase(); }", true)]
    [TestCase("[AllowedExceptions(typeof(System.InvalidOperationException))] public static object M(long x) { Contract.Requires(x < 0); return new CheckedLeaf(x); }", true)]
    [TestCase("[AllowedExceptions(typeof(System.ArgumentException))] public static object M(long x) { Contract.Requires(x < 0); return new CheckedLeaf(x); }", false)]
    [TestCase("[AllowedExceptions(typeof(System.ArgumentException))] public static object M(long x) { Contract.Requires(x >= 0); return new CheckedLeaf(x); }", true)]
    public async Task ConstructionRunsInCSharpOrder(string member, bool proven)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(proven ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted), result.Reason.ToString());
    }

    // Base constructors run on the new object, so their stores are not effects.
    [TestCase("[EnforcePure] public static long M(long x) { return new Leaf(x).LeafValue; }")]
    [TestCase("[EnforcePure] public static long M() { return new Square().Sides; }")]
    [TestCase("[EnforcePure] public static long M(long x) { return new Grown(x).Value; }")]
    [TestCase("[EnforcePure] public static long M() { return new Implicit().Sum; }")]
    [TestCase("public static long M(long x) { Contract.Requires(x > -1000 && x < 1000); " +
        "Contract.Ensures(Contract.Result<long>() == 2 * x + 4); return new Leaf(x).LeafValue; }")]
    [TestCase("public static long M(long x) { Contract.Requires(x > -1000 && x < 1000); " +
        "Contract.Ensures(Contract.Result<long>() == 2 * x + 3); return new Leaf(x).MiddleValue; }")]
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == 5); return new Leaf(x).RootInit; }")]
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 4); return new Square().Sides; }")]
    [TestCase("public static long M(long x) { Contract.Requires(x > -1000 && x < 1000); " +
        "Contract.Ensures(Contract.Result<long>() == x + 8); return new Grown(x).Value; }")]
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 8); return new Implicit().Sum; }")]
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 0); return new HiddenSlotLeaf().BeforeWrite; }")]
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 11); return new HiddenSlotLeaf().Sum; }")]
    public async Task BaseChainsAreProven(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.Reason.ToString());
    }

    // Wrong arithmetic over a chain is refuted.
    [TestCase("public static long M(long x) { Contract.Requires(x > -1000 && x < 1000); " +
        "Contract.Ensures(Contract.Result<long>() == 2 * x + 5); return new Leaf(x).LeafValue; }")]
    public async Task WrongClaimsAreRefuted(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.Reason.ToString());
    }

    // A base constructor's own effects stay effects.
    [TestCase("[EnforcePure] public static long M(long x) { return new Ordered(x).Early; }")]
    [TestCase("[EnforcePure] public static long M(long x) { return new Bumped(x).First; }")]
    [TestCase("[EnforcePure] public static object M(long x) { return new PublishedLeaf(x); }")]
    [TestCase("[EnforcePure] public static object M() { return new AmbientLeaf(); }")]
    [TestCase("[EnforcePure] public static object M() { return new FinalizingLeaf(); }")]
    public async Task BaseConstructorEffectsAreNotProvenPure(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        Assert.That((await ResultAsync(member)).Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
    }

    // A virtual call in a base constructor runs the derived override; a base
    // class declared in metadata runs a body that is not modeled.
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 1); return new Overriding().Measured; }")]
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 2); return new Overriding().Measured; }")]
    [TestCase("public static long M(long x) { Contract.Ensures(Contract.Result<long>() == x); return new Specific(x).Code; }")]
    [TestCase("public static long M() { Contract.Ensures(Contract.Result<long>() == 1); return new IndirectLeaf().Value; }")]
    public async Task UnmodeledBaseConstructorsStayUnknown(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        var result = await ResultAsync(member);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown), result.Reason.ToString());
    }

    private static async Task<WorkerClaimResult> ResultAsync(string member)
    {
        System.ArgumentNullException.ThrowIfNull(member);
        using var project = new ShadowTestProject(Types + "\npublic static class C { " + member + " }\n");
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        var claim = response.Manifest.Claims.Single(static claim => claim.CallableId.Contains("C.M", StringComparison.Ordinal) &&
            claim.Kind is WorkerClaimKind.Postcondition or WorkerClaimKind.Effect);
        return response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId);
    }
}
