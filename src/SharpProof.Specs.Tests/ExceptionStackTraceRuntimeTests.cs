using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace SharpProof.Specs.Test;

[TestFixture]
internal sealed class ExceptionStackTraceRuntimeTests
{
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
