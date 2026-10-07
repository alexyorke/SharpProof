using System.Runtime.Loader;
using NUnit.Framework;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class EmptyArrayAllocationMeasurementTests
{
    [TestCase("Direct", false)]
    [TestCase("Count", false)]
    [TestCase("Capture", false)]
    [TestCase("New", false)]
    [TestCase("IntCount", false)]
    [TestCase("IntCapture", false)]
    [TestCase("Direct", true)]
    [TestCase("Count", true)]
    [TestCase("Capture", true)]
    [TestCase("New", true)]
    [TestCase("IntCount", true)]
    [TestCase("IntCapture", true)]
    public void CompiledCallsMeasureColdWarmAndPrewarmedAllocations(string mode, bool ordinaryRelease)
    {
        var body = mode switch
        {
            "Direct" => "return System.Array.Empty<Cell>().Length;",
            "Count" => "return Count();",
            "Capture" => "return Capture() == Capture() ? 1 : 0;",
            "New" => "var values = new Cell[0]; System.GC.KeepAlive(values); return values.Length;",
            "IntCount" => "return IntCount();",
            "IntCapture" => "return IntCapture() == IntCapture() ? 1 : 0;",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        var attributes = ordinaryRelease ? "" : "[System.Runtime.CompilerServices.MethodImpl(" +
            "System.Runtime.CompilerServices.MethodImplOptions.NoInlining | " +
            "System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)] ";
        var source = "public sealed class Cell { } public static class Subject { " +
            attributes + "public static int Target() { " + body + " } " +
            attributes + "public static int Control() => 0; " +
            "static int Count(params Cell[] values) => values.Length; " +
            "static Cell[] Capture(params Cell[] values) => values; " +
            "static int IntCount(params int[] values) => values.Length; " +
            "static int[] IntCapture(params int[] values) => values; }";
        using var image = new MemoryStream();
        var compilation = TestCompilation.Create("AllocationMeasurement", source);
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(Microsoft.CodeAnalysis.OptimizationLevel.Release));
        Assert.That(compilation.Emit(image).Success, Is.True);
        image.Position = 0;
        // Every test owns a fresh nominal Cell type, including on repeated runs.
        var runtime = new AssemblyLoadContext("AllocationMeasurement", isCollectible: true);
        try
        {
            var type = runtime.LoadFromStream(image).GetType("Subject")!;
            var target = type.GetMethod("Target")!.CreateDelegate<Func<int>>();
            var control = type.GetMethod("Control")!.CreateDelegate<Func<int>>();
            Assert.That(control(), Is.Zero);
            var controlMeasurement = Measure(control);
            var first = Measure(target);
            var second = Measure(target);
            // Explicitly prewarm before checking sustained steady-state behavior.
            for (var index = 0; index < 3; index++)
            { target(); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            var total = 0;
            for (var index = 0; index < 32; index++)
            { total += target(); }
            var steadyBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            var expected = mode is "Capture" or "IntCapture" ? 1 : 0;
            Assert.That(controlMeasurement, Is.EqualTo((0, 0L)));
            Assert.That(first.Result, Is.EqualTo(expected));
            Assert.That(second.Result, Is.EqualTo(expected));
            Assert.That(total, Is.EqualTo(32 * expected));
            if (mode is "Direct" or "Count" or "Capture")
            { Assert.That(first.Bytes, Is.GreaterThan(0), "Fresh nominal type must expose the first-use cache allocation"); }
            // int's cache may already have been initialized by the runtime or test
            // host. Record its first use without asserting a universal startup state.
            Assert.That(second.Bytes, mode == "New" ? Is.GreaterThan(0) : Is.Zero);
            Assert.That(steadyBytes, mode == "New" ? Is.GreaterThan(0) : Is.Zero);
            TestContext.Out.WriteLine($"mode={mode}; ordinaryRelease={ordinaryRelease}; first={first.Bytes}; second={second.Bytes}; steady32={steadyBytes}");
        }
        finally { runtime.Unload(); }
    }

    private static (int Result, long Bytes) Measure(Func<int> target)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = target();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        return (result, bytes);
    }
}