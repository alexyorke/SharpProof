using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class ResourceRuntimeLayoutTests
{
    private static readonly ResourceRuntimeLayoutContext Context = new("CoreCLR", "8.0.32", "x64", 8);
    private static readonly object SharedReference = new();
    private static object? _escape;

    private static ResourceAllocationFootprint Bound(ResourceAllocationShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var result = ResourceRuntimeLayout.Bound(shape, Context, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Verified, Is.True, result.UnknownReason);
        return result.Bounds!;
    }

    [TestCase(1, 0, 24)]
    [TestCase(1, 1, 32)]
    [TestCase(1, 8, 32)]
    [TestCase(1, 9, 40)]
    [TestCase(2, 1, 32)]
    [TestCase(3, 2, 32)]
    [TestCase(4, 2, 40)]
    [TestCase(5, 2, 40)]
    [TestCase(6, 2, 40)]
    public void ArrayStorageIncludesHeaderElementWidthAndAlignment(int storage, int length, int expected)
    {
        var result = Bound(ResourceAllocationShape.Array((ResourceStorageKind)storage, ResourceExpression.Literal(length)));
        Assert.That(result.IsExact, Is.True);
        Assert.That(result.Lower.Constant, Is.EqualTo(new BigInteger(expected)));
        Assert.That(result.Upper.Constant, Is.EqualTo(new BigInteger(expected)));
        Assert.That(result.Objects, Is.EqualTo(1));
    }

    [TestCase(0, 24)]
    [TestCase(1, 24)]
    [TestCase(2, 32)]
    [TestCase(5, 32)]
    [TestCase(6, 40)]
    public void FreshStringStorageIncludesLengthAndTerminator(int length, int expected)
    {
        var result = Bound(ResourceAllocationShape.String(ResourceExpression.Literal(length)));
        Assert.That(result.IsExact, Is.True);
        Assert.That(result.Upper.Constant, Is.EqualTo(new BigInteger(expected)));
    }

    [Test]
    public void ObjectPackingProducesAnIntervalInsteadOfAnInventedExactSize()
    {
        var empty = Bound(ResourceAllocationShape.Object([]));
        Assert.That(empty.Lower.Constant, Is.EqualTo(new BigInteger(24)));
        var packed = Bound(ResourceAllocationShape.Object([ResourceStorageKind.Word32, ResourceStorageKind.Word32]));
        Assert.That(packed.Lower.Constant, Is.EqualTo(new BigInteger(24)));
        Assert.That(packed.Upper.Constant, Is.EqualTo(new BigInteger(32)));
        Assert.That(packed.IsExact, Is.False);
        var mixed = Bound(ResourceAllocationShape.Object([ResourceStorageKind.Reference, ResourceStorageKind.Word64, ResourceStorageKind.Byte]));
        Assert.That(mixed.Lower.Constant, Is.EqualTo(new BigInteger(40)));
        Assert.That(mixed.Upper.Constant, Is.EqualTo(new BigInteger(40)));
        Assert.That(mixed.IsExact, Is.True);
    }

    [Test]
    public void BoxesDelegatesAndClosureBundlesHaveSeparateOwnedStorage()
    {
        foreach (var storage in new[] { ResourceStorageKind.Byte, ResourceStorageKind.Word16, ResourceStorageKind.Word32,
            ResourceStorageKind.Word64, ResourceStorageKind.NativeInteger })
        { Assert.That(Bound(ResourceAllocationShape.Box(storage)).Upper.Constant, Is.EqualTo(new BigInteger(24))); }
        Assert.That(Bound(ResourceAllocationShape.Delegate()).Upper.Constant, Is.EqualTo(new BigInteger(64)));
        var closure = Bound(ResourceAllocationShape.Closure([ResourceStorageKind.Word32]));
        Assert.That(closure.Lower.Constant, Is.EqualTo(new BigInteger(88)));
        Assert.That(closure.Upper.Constant, Is.EqualTo(new BigInteger(88)));
        Assert.That(closure.Objects, Is.EqualTo(2));
        var loose = Bound(ResourceAllocationShape.Closure([ResourceStorageKind.Word32, ResourceStorageKind.Byte]));
        Assert.That(loose.Lower.Constant, Is.EqualTo(new BigInteger(88)));
        Assert.That(loose.Upper.Constant, Is.EqualTo(new BigInteger(96)));
        Assert.That(loose.IsExact, Is.False);
    }

    [TestCase("Mono", "8.0.32", "x64", 8)]
    [TestCase("CoreCLR", "9.0.0", "x64", 8)]
    [TestCase("CoreCLR", "8.0.32", "arm64", 8)]
    [TestCase("CoreCLR", "8.0.32", "x86", 4)]
    [TestCase("CoreCLR", "8.0.32", "x64", 4)]
    public void UnsupportedRuntimeContextsReturnUnknownWithoutPartialBytes(string runtime, string version, string architecture, int nativeWidth)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(architecture);
        var result = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Object([]),
            new(runtime, version, architecture, nativeWidth), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Verified, Is.False);
        Assert.That(result.Bounds, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("IncompatibleResourceRuntimeLayout"));
        var unsupported = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Unsupported, Context, TestContext.CurrentContext.CancellationToken);
        Assert.That(unsupported.Bounds, Is.Null);
        Assert.That(unsupported.UnknownReason, Is.EqualTo("UnsupportedManagedAllocationShape"));
    }

    [Test]
    public void SymbolicStorageUsesNaturalArithmeticAndPreservesInputIdentity()
    {
        var count = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "n", long.MaxValue));
        var array = Bound(ResourceAllocationShape.Array(ResourceStorageKind.Word64, count));
        Assert.That(array.Upper.Maximum, Is.EqualTo(new BigInteger(long.MaxValue) * 8 + 24));
        for (int length = 0; length < 100; length++)
        {
            long independentlyAligned = ((24L + 8L * length + 7L) / 8L) * 8L;
            Assert.That(array.Upper.Evaluate(_ => length, TestContext.CurrentContext.CancellationToken), Is.EqualTo(new BigInteger(independentlyAligned)));
        }
    }

    [Test]
    public void ShapeAndExpressionBudgetsRejectUnboundedLayoutWork()
    {
        Action absent = () => ResourceAllocationShape.Object(default);
        Action fields = () => ResourceAllocationShape.Closure(Enumerable.Repeat(ResourceStorageKind.Byte, ResourceAllocationShape.MaximumFields + 1).ToImmutableArray());
        Action undefined = () => ResourceAllocationShape.Array((ResourceStorageKind)999, ResourceExpression.Zero);
        Action referenceBox = () => ResourceAllocationShape.Box(ResourceStorageKind.Reference);
        Assert.Throws<ArgumentException>(absent);
        Assert.Throws<ResourceExpressionLimitException>(fields);
        Assert.Throws<ArgumentOutOfRangeException>(undefined);
        Assert.Throws<ArgumentException>(referenceBox);
        var count = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.ArrayLength, "n", 100));
        for (int level = 0; level < 11; level++)
        { count = ResourceExpression.Add(count, count); }
        var result = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Array(ResourceStorageKind.Word64, count), Context, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Bounds, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("ResourceLayoutBudgetExceeded"));
        Action cancel = () => ResourceRuntimeLayout.Bound(ResourceAllocationShape.Unsupported, Context, new CancellationToken(true));
        Assert.Throws<OperationCanceledException>(cancel);
    }

    [Test]
    public void MalformedContextsAndIntervalsCannotBecomeLayoutEvidence()
    {
        Action context = () => _ = new ResourceRuntimeLayoutContext("CoreCLR", "bad\nversion", "x64", 8);
        Action pointer = () => _ = new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "x64", 0);
        Action reversed = () => _ = new ResourceAllocationFootprint(ResourceExpression.One, ResourceExpression.Zero, 1, TestContext.CurrentContext.CancellationToken);
        Action count = () => _ = new ResourceAllocationFootprint(ResourceExpression.One, ResourceExpression.One, 0, TestContext.CurrentContext.CancellationToken);
        Action absent = () => _ = new ResourceLayoutCheck(null, null);
        Action conflicting = () => _ = new ResourceLayoutCheck(Bound(ResourceAllocationShape.Object([])), "Unknown");
        Assert.Throws<ArgumentException>(context);
        Assert.Throws<ArgumentOutOfRangeException>(pointer);
        Assert.Throws<ArgumentException>(reversed);
        Assert.Throws<ArgumentOutOfRangeException>(count);
        Assert.Throws<ArgumentException>(absent);
        Assert.Throws<ArgumentException>(conflicting);
    }
    [Test]
    public void LayoutIdentityBindsTheActualReviewedDeclarations()
    {
        var declaration = ResourceRuntimeLayout.ModelId + "\n" + ResourceRuntimeLayout.EvidenceSource + "\n" +
            ResourceRuntimeLayout.RuntimeFamily + "\n" + ResourceRuntimeLayout.RuntimeVersion + "\n" + ResourceRuntimeLayout.TargetArchitecture + "\n" +
            FormattableString.Invariant($"pointer={ResourceRuntimeLayout.PointerBytes};alignment={ResourceRuntimeLayout.Alignment};object={ResourceRuntimeLayout.ObjectHeader};minimum={ResourceRuntimeLayout.MinimumObject};array={ResourceRuntimeLayout.ArrayHeader};string={ResourceRuntimeLayout.StringHeader};delegate={ResourceRuntimeLayout.DelegateBytes}\n") +
            "Byte:1\nWord16:2\nWord32:4\nWord64:8\nReference:8\nNativeInteger:8\n" +
            "object:aligned-packed-lower,aligned-pointer-slot-upper;closure:frame+delegate;fresh-instances-only\n";
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(declaration)));
        Assert.That(ResourceRuntimeLayout.ContentSha256.ToUpperInvariant(), Is.EqualTo(expected));
        Assert.That(ResourceRuntimeLayout.ContentSha256.All(static value => value is >= '0' and <= '9' or >= 'a' and <= 'f'), Is.True);
    }
    [Test]
    public void CanonicalRuntimeAllocationsIndependentlyFitReviewedIntervals()
    {
        Assert.That(Environment.Version, Is.EqualTo(new Version(8, 0, 32)));
        Assert.That(RuntimeInformation.ProcessArchitecture, Is.EqualTo(Architecture.X64));
        Assert.That(Type.GetType("Mono.Runtime"), Is.Null);
        CheckMeasured(ResourceAllocationShape.Array(ResourceStorageKind.Byte, ResourceExpression.Literal(9)), static () => new byte[9]);
        CheckMeasured(ResourceAllocationShape.Array(ResourceStorageKind.Word16, ResourceExpression.Literal(7)), static () => new short[7]);
        CheckMeasured(ResourceAllocationShape.Array(ResourceStorageKind.Word32, ResourceExpression.Literal(5)), static () => new int[5]);
        CheckMeasured(ResourceAllocationShape.Array(ResourceStorageKind.Word64, ResourceExpression.Literal(3)), static () => new long[3]);
        CheckMeasured(ResourceAllocationShape.Array(ResourceStorageKind.Reference, ResourceExpression.Literal(3)), static () => new object[3]);
        CheckMeasured(ResourceAllocationShape.Array(ResourceStorageKind.Byte, ResourceExpression.Literal(9)), static () => new bool[9]);
        CheckMeasured(ResourceAllocationShape.String(ResourceExpression.Literal(6)), static () => new string('x', 6));
        CheckMeasured(ResourceAllocationShape.Object([]), static () => new object());
        CheckMeasured(ResourceAllocationShape.Box(ResourceStorageKind.Word32), static () => 42);
        CheckMeasured(ResourceAllocationShape.Object([ResourceStorageKind.Word32, ResourceStorageKind.Word32]), static () => new SmallFields(42, 43));
        CheckMeasured(ResourceAllocationShape.Object([ResourceStorageKind.Reference, ResourceStorageKind.Word64, ResourceStorageKind.Byte]),
            static () => new MixedFields(SharedReference, 42, 1));
        CheckMeasured(ResourceAllocationShape.Delegate(), CreateDelegate);
        CheckMeasured(ResourceAllocationShape.Closure([ResourceStorageKind.Word32]), CreateClosure);
        Assert.That(Measure(static () => new InitializedFieldCell()), Is.EqualTo(64));
        Assert.That(Measure(static () => new InitializedPropertyCell()), Is.EqualTo(64));
        Assert.That(Measure(static () => new InitializedFieldCell { Data = new int[4] }), Is.EqualTo(104));
        Assert.That(Measure(static () => new string('x', 0)), Is.Zero);
        Assert.That(Bound(ResourceAllocationShape.String(ResourceExpression.Zero)).Upper.Constant, Is.GreaterThan(BigInteger.Zero));
        Assert.That(ResourceRuntimeLayout.ContentSha256, Has.Length.EqualTo(64));
        Assert.That(ResourceRuntimeLayout.EvidenceSource, Does.Contain(ResourceRuntimeLayout.SourceCommit));
    }

    private static void CheckMeasured(ResourceAllocationShape shape, Func<object> allocate)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(allocate);
        var interval = Bound(shape);
        long observed = Measure(allocate);
        Assert.That(new BigInteger(observed), Is.GreaterThanOrEqualTo(interval.Lower.Constant!.Value));
        Assert.That(new BigInteger(observed), Is.LessThanOrEqualTo(interval.Upper.Constant!.Value));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long Measure(Func<object> allocate)
    {
        ArgumentNullException.ThrowIfNull(allocate);
        for (int warm = 0; warm < 16; warm++)
        { Volatile.Write(ref _escape, allocate()); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        var instance = allocate();
        Volatile.Write(ref _escape, instance);
        long after = GC.GetAllocatedBytesForCurrentThread();
        GC.KeepAlive(instance);
        GC.KeepAlive(Volatile.Read(ref _escape));
        return after - before;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object CreateDelegate()
    { return new Func<int>(Value); }
    private static int Value()
    { return 42; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object CreateClosure()
    {
        int captured = Environment.TickCount;
        Func<int> closure = () => captured;
        return closure;
    }

    private sealed class InitializedFieldCell
    {
        internal int[] Data = new int[3];

        [MethodImpl(MethodImplOptions.NoInlining)]
        public InitializedFieldCell() { }
    }

    private sealed class InitializedPropertyCell
    {
        internal int[] Data { get; } = new int[3];

        [MethodImpl(MethodImplOptions.NoInlining)]
        public InitializedPropertyCell() { }
    }
    private sealed class SmallFields(int first, int second)
    {
        internal int First { get; } = first;
        internal int Second { get; } = second;
    }

    private sealed class MixedFields(object reference, long number, byte flag)
    {
        internal object Reference { get; } = reference ?? throw new ArgumentNullException(nameof(reference));
        internal long Number { get; } = number;
        internal byte Flag { get; } = flag;
    }
}
