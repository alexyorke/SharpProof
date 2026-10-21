using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace SharpProof.Specs.Test;

[TestFixture]
internal sealed class ExceptionStackTraceRuntimeTests
{
    [TestCase(0, 0)]
    [TestCase(8, 0)]
    [TestCase(32, 0)]
    [TestCase(0, 3)]
    [TestCase(8, 3)]
    [TestCase(32, 3)]
    public void CatchBoundarySeparatesOwnedTraceFramesFromAmbientCallers(int ambientDepth, int throwDepth)
    {
        Assert.That(Environment.Version, Is.EqualTo(new Version(8, 0, 32)));
        Assert.That(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            Is.EqualTo(System.Runtime.InteropServices.Architecture.X64));
        var traceField = typeof(Exception).GetField("_stackTrace", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var closed = (byte[])traceField.GetValue(CallClosedBoundary(ambientDepth, throwDepth))!;
        var escaped = (byte[])traceField.GetValue(CaptureOutsideBoundary(ambientDepth, throwDepth))!;
        Assert.That(BitConverter.ToUInt64(closed, 0), Is.EqualTo((ulong)throwDepth + 2));
        Assert.That(BitConverter.ToUInt64(escaped, 0), Is.EqualTo((ulong)(ambientDepth + throwDepth) + 3));
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static InvalidOperationException CallClosedBoundary(int ambientDepth, int throwDepth)
    {
        if (ambientDepth is < 0 or > 32)
        { throw new ArgumentOutOfRangeException(nameof(ambientDepth)); }
        var result = ambientDepth == 0 ? CaptureInsideBoundary(throwDepth) : CallClosedBoundary(ambientDepth - 1, throwDepth);
        GC.KeepAlive(ambientDepth);
        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static InvalidOperationException CaptureInsideBoundary(int throwDepth)
    {
        try
        { ThrowAtDepth(throwDepth); }
        catch (InvalidOperationException exception)
        { return exception; }
        throw new InvalidOperationException("The trace control must throw.");
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static InvalidOperationException CaptureOutsideBoundary(int ambientDepth, int throwDepth)
    {
        try
        { EscapeThroughAmbient(ambientDepth, throwDepth); }
        catch (InvalidOperationException exception)
        { return exception; }
        throw new InvalidOperationException("The trace control must throw.");
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static void EscapeThroughAmbient(int ambientDepth, int throwDepth)
    {
        if (ambientDepth is < 0 or > 32)
        { throw new ArgumentOutOfRangeException(nameof(ambientDepth)); }
        if (ambientDepth == 0)
        { ThrowAtDepth(throwDepth); }
        else
        { EscapeThroughAmbient(ambientDepth - 1, throwDepth); }
        GC.KeepAlive(ambientDepth);
    }

    [TestCase(0, 136)]
    [TestCase(1, 136)]
    [TestCase(2, 136)]
    [TestCase(3, 136)]
    [TestCase(4, 136)]
    [TestCase(5, 144)]
    [TestCase(6, 152)]
    public void OrdinarySourceExceptionInstancesFitReviewedInheritedStorage(int family, int upper)
    {
        if (family is < 0 or > 6)
        { throw new ArgumentOutOfRangeException(nameof(family)); }
        Assert.That(Environment.Version, Is.EqualTo(new Version(8, 0, 32)));
        Assert.That(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            Is.EqualTo(System.Runtime.InteropServices.Architecture.X64));
        for (var index = 0; index < 64; index++)
        { GC.KeepAlive(Create(family)); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 128; index++)
        { GC.KeepAlive(Create(family)); }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(bytes, Is.GreaterThan(0));
        Assert.That(bytes, Is.LessThanOrEqualTo(128L * upper));

        static Exception Create(int kind)
        {
            return kind switch
            {
                0 => new DivideByZeroException("preexisting-message"),
                1 => new OverflowException("preexisting-message"),
#pragma warning disable CA2201 // Measure these runtime-reserved instance layouts without throwing them.
                2 => new NullReferenceException("preexisting-message"),
                3 => new IndexOutOfRangeException("preexisting-message"),
#pragma warning restore CA2201
                4 => new InvalidCastException("preexisting-message"),
                5 => new ArgumentException("preexisting-message", nameof(family)),
                6 => new ArgumentOutOfRangeException(nameof(family), null, "preexisting-message"),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
        }
    }

    [TestCase(0)]
    [TestCase(3)]
    [TestCase(15)]
    public void PinnedTraceArraysContainTheReviewedHeaderAndElementStride(int depth)
    {
        if (depth < 0 || depth > 15)
        { throw new ArgumentOutOfRangeException(nameof(depth)); }
        Assert.That(Environment.Version, Is.EqualTo(new Version(8, 0, 32)));
        Assert.That(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            Is.EqualTo(System.Runtime.InteropServices.Architecture.X64));
        Action throwing = () =>
        {
            ThrowAtDepth(depth);
        };
        var exception = Assert.Throws<InvalidOperationException>(throwing)!;
        var traceField = typeof(Exception).GetField("_stackTrace", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var storage = (byte[])traceField.GetValue(exception)!;
        Assert.That(storage, Is.Not.Null);
        const int header = 16;
        const int stride = 32;
        Assert.That(storage.Length, Is.GreaterThanOrEqualTo(header));
        var count = BitConverter.ToUInt64(storage, 0);
        Assert.That(count, Is.GreaterThanOrEqualTo((ulong)depth + 1));
        Assert.That(count, Is.LessThanOrEqualTo((ulong)(storage.Length - header) / stride));
        for (var index = 0UL; index < count; index++)
        {
            var offset = checked(header + stride * (int)index);
            Assert.That(BitConverter.ToUInt64(storage, offset + 16) != 0, Is.True, "A recorded frame must own a method descriptor.");
            Assert.That((BitConverter.ToInt32(storage, offset + 24) & ~3) == 0, Is.True, "Only the reviewed trace flags are admitted.");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static void ThrowAtDepth(int depth)
    {
        if (depth < 0 || depth > 15)
        { throw new ArgumentOutOfRangeException(nameof(depth)); }
        if (depth == 0)
        { throw new InvalidOperationException("trace-layout-control"); }
        ThrowAtDepth(depth - 1);
        GC.KeepAlive(depth);
    }
}
