using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Numerics;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Specs.Test;

[TestFixture]
internal sealed class ExceptionStackTraceRuntimeTests
{
    [TestCase(0, 0, 30, 0L)]
    [TestCase(1, 0, 30, 264L)]
    [TestCase(2, 3, 30, 1320L)]
    [TestCase(10, 4, 30, 4816L)]
    [TestCase(1, 2, 1024, 24648L)]
    public void ResolverPrefixBoundsPreserveScratchCapacityAndCountCopies(int frames, int copies, int capacity, long expected)
    {
        var result = ResourceTaskFaultCarrierCatalog.BoundResolverTraceBuffers(frames, copies, capacity,
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.UnknownReason, Is.Null);
        Assert.That(result.CumulativeUpper!.Constant, Is.EqualTo(new BigInteger(expected)));
        Assert.That(result.RequiredRuntimeModel, Is.EqualTo(ResourceTaskFaultCarrierCatalog.ResolverTraceBufferModel));
    }

    [TestCase(-1, 0, 30)]
    [TestCase(0, -1, 30)]
    [TestCase(0, 0, -1)]
    public void InvalidResolverBudgetsNeverPublishABound(int frames, int copies, int capacity)
    {
        var result = ResourceTaskFaultCarrierCatalog.BoundResolverTraceBuffers(frames, copies, capacity,
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.CumulativeUpper, Is.Null);
        Assert.That(result.RequiredRuntimeModel, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("InvalidResolverTracePrefixBudget"));
    }

    [Test]
    public void ResolverBudgetsRequirePinnedLayoutAndPropagateCancellation()
    {
        var layout = new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "arm64", 8);
        var result = ResourceTaskFaultCarrierCatalog.BoundResolverTraceBuffers(1, 1, 30, layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.CumulativeUpper, Is.Null);
        Assert.That(result.RequiredRuntimeModel, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("IncompatibleResourceRuntimeLayout"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action check = () => { _ = ResourceTaskFaultCarrierCatalog.BoundResolverTraceBuffers(1, 1, 30, layout, cancellation.Token); };
        Assert.Throws<OperationCanceledException>(check);
    }

    [Test]
    public void ResolverPrefixArithmeticDoesNotOverflowInt64()
    {
        var result = ResourceTaskFaultCarrierCatalog.BoundResolverTraceBuffers(int.MaxValue, int.MaxValue, int.MaxValue,
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        var frames = new BigInteger(int.MaxValue);
        Assert.That(result.CumulativeUpper!.Constant, Is.EqualTo(2 * frames * (24 + 32 * frames)));
        Assert.That(result.CumulativeUpper.Constant, Is.GreaterThan(new BigInteger(long.MaxValue)));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(8)]
    public void DynamicThrowAndDispatchAllocationsFitClosedBufferBudgets(int restores)
    {
        Assert.That(Environment.Version, Is.EqualTo(new Version(8, 0, 32)));
        Assert.That(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            Is.EqualTo(System.Runtime.InteropServices.Architecture.X64));
        var method = new DynamicMethod("ThrowResolverTraceControl", typeof(void), [typeof(InvalidOperationException)],
            typeof(ExceptionStackTraceRuntimeTests).Module, true);
        var generator = method.GetILGenerator();
        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(OpCodes.Throw);
        var thrower = method.CreateDelegate<Action<InvalidOperationException>>();
        for (var index = 0; index < 64; index++)
        {
            var warm = new InvalidOperationException("resolver-trace-control");
            CaptureDynamicTrace(thrower, warm);
            ConsumeRestoredDispatch(ExceptionDispatchInfo.Capture(warm));
        }
        var traceField = typeof(Exception).GetField("_stackTrace", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var resolverField = typeof(Exception).GetField("_dynamicMethods", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var exception = new InvalidOperationException("resolver-trace-control");
        var before = GC.GetAllocatedBytesForCurrentThread();
        CaptureDynamicTrace(thrower, exception);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var resolvers = (object[])resolverField.GetValue(exception)!;
        Assert.That(resolvers.Length, Is.EqualTo(30), "The fresh runtime scratch seed is part of this control's premise.");
        for (var index = 0; index < restores; index++)
        {
            var dispatch = ExceptionDispatchInfo.Capture(exception);
            before = GC.GetAllocatedBytesForCurrentThread();
            ConsumeRestoredDispatch(dispatch);
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        var trace = (byte[])traceField.GetValue(exception)!;
        // The trace only extends in this control. Its final count bounds all
        // appended frames. Although capture preparation is outside measured
        // intervals, budget both capture and restore copies for the whole prefix.
        var frames = checked((int)BitConverter.ToUInt64(trace, 0));
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var layout = new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "x64", 8);
        var raw = ResourceTaskFaultCarrierCatalog.BoundRawTraceBuffers(frames, 2 * restores, layout, cancellation);
        var resolver = ResourceTaskFaultCarrierCatalog.BoundResolverTraceBuffers(frames, 2 * restores, 30, layout, cancellation);
        Assert.That(((object[])resolverField.GetValue(exception)!).Length, Is.LessThanOrEqualTo(Math.Max(30, 4 * frames)));
        Assert.That(new BigInteger(allocated), Is.LessThanOrEqualTo(raw.CumulativeUpper!.Constant!.Value + resolver.CumulativeUpper!.Constant!.Value));
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static void CaptureDynamicTrace(Action<InvalidOperationException> thrower, InvalidOperationException exception)
    {
        ArgumentNullException.ThrowIfNull(thrower);
        ArgumentNullException.ThrowIfNull(exception);
        try
        { thrower(exception); }
        catch (InvalidOperationException)
        { }
    }

    [TestCase(0, 0, 0L)]
    [TestCase(1, 0, 240L)]
    [TestCase(2, 3, 1288L)]
    [TestCase(10, 4, 16704L)]
    public void RawTracePrefixBoundsCountGrowthAffinityAndCopies(int frames, int copies, long expected)
    {
        var result = ResourceTaskFaultCarrierCatalog.BoundRawTraceBuffers(frames, copies,
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.UnknownReason, Is.Null);
        Assert.That(result.CumulativeUpper!.Constant, Is.EqualTo(new BigInteger(expected)));
        Assert.That(result.RequiredRuntimeModel, Is.EqualTo(ResourceTaskFaultCarrierCatalog.RawTraceBufferModel));
    }

    [TestCase(-1, 0)]
    [TestCase(0, -1)]
    public void InvalidRawTraceBudgetsDoNotPublishPartialBounds(int frames, int copies)
    {
        var result = ResourceTaskFaultCarrierCatalog.BoundRawTraceBuffers(frames, copies,
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.CumulativeUpper, Is.Null);
        Assert.That(result.RequiredRuntimeModel, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("InvalidRawTracePrefixBudget"));
    }

    [Test]
    public void RawTraceBudgetsRequireThePinnedLayoutAndHonorCancellation()
    {
        var layout = new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "arm64", 8);
        var result = ResourceTaskFaultCarrierCatalog.BoundRawTraceBuffers(1, 1, layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.CumulativeUpper, Is.Null);
        Assert.That(result.RequiredRuntimeModel, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("IncompatibleResourceRuntimeLayout"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action check = () => { _ = ResourceTaskFaultCarrierCatalog.BoundRawTraceBuffers(1, 1, layout, cancellation.Token); };
        Assert.Throws<OperationCanceledException>(check);
    }

    [Test]
    public void RawTracePrefixArithmeticDoesNotOverflowInt64()
    {
        var result = ResourceTaskFaultCarrierCatalog.BoundRawTraceBuffers(int.MaxValue, int.MaxValue,
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        var frames = new BigInteger(int.MaxValue);
        Assert.That(result.CumulativeUpper!.Constant, Is.EqualTo(3 * frames * (56 + 64 * frames)));
        Assert.That(result.CumulativeUpper.Constant, Is.GreaterThan(new BigInteger(long.MaxValue)));
    }

    [TestCase(0)]
    [TestCase(3)]
    [TestCase(15)]
    public void FreshClosedTraceAllocationsStayWithinTheRawBufferPrefixBound(int depth)
    {
        Assert.That(Environment.Version, Is.EqualTo(new Version(8, 0, 32)));
        Assert.That(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            Is.EqualTo(System.Runtime.InteropServices.Architecture.X64));
        var traceField = typeof(Exception).GetField("_stackTrace", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var resolverField = typeof(Exception).GetField("_dynamicMethods", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var index = 0; index < 64; index++)
        { CaptureRawTrace(depth, new InvalidOperationException("raw-trace-control")); }
        for (var index = 0; index < 32; index++)
        {
            var exception = new InvalidOperationException("raw-trace-control");
            var before = GC.GetAllocatedBytesForCurrentThread();
            CaptureRawTrace(depth, exception);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            // Reflection and inspection occur outside the measured interval.
            var trace = (byte[])traceField.GetValue(exception)!;
            Assert.That(resolverField.GetValue(exception), Is.Null);
            var frames = checked((int)BitConverter.ToUInt64(trace, 0));
            var result = ResourceTaskFaultCarrierCatalog.BoundRawTraceBuffers(frames, 0,
                new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
            Assert.That(allocated, Is.GreaterThan(0));
            Assert.That(new BigInteger(allocated), Is.LessThanOrEqualTo(result.CumulativeUpper!.Constant!.Value));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static void CaptureRawTrace(int depth, InvalidOperationException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        { _ = ThrowRawAtDepth(depth, exception); }
        catch (InvalidOperationException)
        { }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static int ThrowRawAtDepth(int depth, InvalidOperationException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (depth is < 0 or > 15)
        { throw new ArgumentOutOfRangeException(nameof(depth)); }
        if (depth == 0)
        { throw exception; }
        return 1 + ThrowRawAtDepth(depth - 1, exception);
    }

    [Test]
    public async Task CrossThreadDispatchUsesTheClosedRawTraceBudget()
    {
        Assert.That(Environment.Version, Is.EqualTo(new Version(8, 0, 32)));
        Assert.That(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            Is.EqualTo(System.Runtime.InteropServices.Architecture.X64));
        var traceField = typeof(Exception).GetField("_stackTrace", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var resolverField = typeof(Exception).GetField("_dynamicMethods", BindingFlags.Instance | BindingFlags.NonPublic)!;
        // Prepare runtime/JIT paths before creating the fresh closed collection.
        for (var index = 0; index < 64; index++)
        {
            var warm = new InvalidOperationException("raw-trace-control");
            CaptureRawTrace(0, warm);
            ConsumeRestoredDispatch(ExceptionDispatchInfo.Capture(warm));
        }
        var exception = new InvalidOperationException("raw-trace-control");
        CaptureRawTrace(3, exception);
        var original = (byte[])traceField.GetValue(exception)!;
        var initialFrames = checked((int)BitConverter.ToUInt64(original, 0));
        var originalOwner = BitConverter.ToUInt64(original, 8);
        var dispatch = ExceptionDispatchInfo.Capture(exception);
        var completion = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            ConsumeRestoredDispatch(dispatch);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            completion.SetResult(allocated);
        })
        { IsBackground = true };
        thread.Start();
        var bytes = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.CurrentContext.CancellationToken);
        thread.Join();
        var trace = (byte[])traceField.GetValue(exception)!;
        var frames = initialFrames + checked((int)BitConverter.ToUInt64(trace, 0));
        Assert.That(resolverField.GetValue(exception), Is.Null);
        Assert.That(BitConverter.ToUInt64(trace, 8), Is.Not.EqualTo(originalOwner));
        // One restore clone and an affinity copy both copy the original capacity.
        Assert.That(bytes, Is.GreaterThanOrEqualTo(2 * ((24L + original.Length + 7) & ~7L)));
        var result = ResourceTaskFaultCarrierCatalog.BoundRawTraceBuffers(frames, 2,
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        Assert.That(new BigInteger(bytes), Is.LessThanOrEqualTo(result.CumulativeUpper!.Constant!.Value));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task ContextWrappersRestoreFlowStateAndPreserveFaultIdentity(bool threadPool, bool faulting)
    {
        Assert.That(Environment.Version, Is.EqualTo(new Version(8, 0, 32)));
        var slot = new AsyncLocal<int>();
        var exception = new InvalidOperationException("context-copy-control");
        var observed = 0;
        ContextCallback callback = _ =>
        {
            observed = slot.Value;
            slot.Value = 11;
            if (faulting)
            { throw exception; }
        };
        slot.Value = 7;
        var context = ExecutionContext.Capture()!;
        slot.Value = 9;
        InvalidOperationException? error = null;
        int restored;
        if (threadPool)
        {
            var completion = new TaskCompletionSource<(InvalidOperationException? Error, int Restored)>(TaskCreationOptions.RunContinuationsAsynchronously);
            var method = typeof(ExecutionContext).GetMethod("RunFromThreadPoolDispatchLoop", BindingFlags.Static | BindingFlags.NonPublic)!;
            var run = method.CreateDelegate<Action<Thread, ExecutionContext, ContextCallback, object>>();
            ThreadPool.UnsafeQueueUserWorkItem(_ =>
            {
                InvalidOperationException? failure = null;
                try
                { run(Thread.CurrentThread, context, callback, new object()); }
                catch (InvalidOperationException caught)
                { failure = caught; }
                completion.SetResult((failure, slot.Value));
            }, null);
            (error, restored) = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.CurrentContext.CancellationToken);
        }
        else
        {
            try
            { ExecutionContext.Run(context, callback, null); }
            catch (InvalidOperationException caught)
            { error = caught; }
            restored = slot.Value;
        }
        Assert.That(observed, Is.EqualTo(7));
        Assert.That(restored, Is.EqualTo(threadPool ? 0 : 9));
        Assert.That(slot.Value, Is.EqualTo(9));
        Assert.That(error, faulting ? Is.SameAs(exception) : Is.Null);
    }

    [TestCase(0, false)]
    [TestCase(3, false)]
    [TestCase(15, false)]
    [TestCase(0, true)]
    [TestCase(3, true)]
    [TestCase(15, true)]
    public void DispatchCaptureAndRestoreCopyOnlyTheirReviewedTracePayloads(int depth, bool restoring)
    {
        Assert.That(Environment.Version, Is.EqualTo(new Version(8, 0, 32)));
        Assert.That(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            Is.EqualTo(System.Runtime.InteropServices.Architecture.X64));
        var exception = CaptureInsideBoundary(depth);
        var trace = (byte[])typeof(Exception).GetField("_stackTrace", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(exception)!;
        var resolvers = (object[]?)typeof(Exception).GetField("_dynamicMethods", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(exception);
        var payload = ArrayBytes(trace.Length, 1) + (resolvers is null ? 0 : ArrayBytes(resolvers.Length, 8));
        var dispatch = ExceptionDispatchInfo.Capture(exception);
        var restore = CreateDispatchRestorer();
        for (var index = 0; index < 64; index++)
        {
            if (restoring)
            { restore(dispatch); ConsumeRestoredDispatch(dispatch); }
            else
            { GC.KeepAlive(ExceptionDispatchInfo.Capture(exception)); }
        }
        var bytes = 0L;
        for (var index = 0; index < 128; index++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            if (restoring)
            { restore(dispatch); }
            else
            { GC.KeepAlive(ExceptionDispatchInfo.Capture(exception)); }
            bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            // Restore marks a pending foreign raise. Consume it outside the
            // measured interval; this throw has separate trace-growth costs.
            if (restoring)
            { ConsumeRestoredDispatch(dispatch); }
        }
        Assert.That(bytes, Is.GreaterThan(0));
        // Capture also constructs the fixed dispatch object; restore does not.
        Assert.That(bytes, Is.LessThanOrEqualTo(128 * (payload + (restoring ? 0 : 64))));

        static long ArrayBytes(int length, int elementBytes)
        { return (24L + (long)length * elementBytes + 7) & ~7L; }
    }

    private static Action<ExceptionDispatchInfo> CreateDispatchRestorer()
    {
        var state = typeof(ExceptionDispatchInfo).GetField("_dispatchState", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var target = typeof(Exception).GetMethod("RestoreDispatchState", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var method = new DynamicMethod("RestoreExceptionDispatch", typeof(void), [typeof(ExceptionDispatchInfo)],
            typeof(ExceptionStackTraceRuntimeTests).Module, true);
        var generator = method.GetILGenerator();
        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(OpCodes.Callvirt, typeof(ExceptionDispatchInfo).GetProperty(nameof(ExceptionDispatchInfo.SourceException))!.GetMethod!);
        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(OpCodes.Ldflda, state);
        generator.Emit(OpCodes.Call, target);
        generator.Emit(OpCodes.Ret);
        return method.CreateDelegate<Action<ExceptionDispatchInfo>>();
    }

    private static void ConsumeRestoredDispatch(ExceptionDispatchInfo dispatch)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        try
        { dispatch.Throw(); }
        catch (InvalidOperationException)
        { }
    }

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
