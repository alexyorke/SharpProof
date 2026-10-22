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
    [TestCase(0, false)]
    [TestCase(1_000_000, false)]
    [TestCase(0, true)]
    [TestCase(1_000_000, true)]
    public void OneCallFreeSourceInvocationAppearsOnceInTheCapturedTrace(int iterations, bool forwarded)
    {
        Assert.That(Environment.Version, Is.EqualTo(new Version(8, 0, 32)));
        Assert.That(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            Is.EqualTo(System.Runtime.InteropServices.Architecture.X64));
        Exception? fault = null;
        try
        { _ = forwarded ? DirectSourceForwarderProbe(iterations, 0) : DirectSourceFrameProbe(iterations, 0); }
        catch (DivideByZeroException captured)
        { fault = captured; }
        Assert.That(fault, Is.Not.Null);
        var trace = new System.Diagnostics.StackTrace(fault!, false);
        var count = trace.GetFrames().Count(static frame => frame is null ? throw new ArgumentNullException(nameof(frame)) :
            frame.GetMethod()?.Name == nameof(DirectSourceFrameProbe));
        Assert.That(count, Is.EqualTo(1));
        var wrappers = trace.GetFrames().Count(static frame => frame is null ? throw new ArgumentNullException(nameof(frame)) :
            frame.GetMethod()?.Name == nameof(DirectSourceForwarderProbe));
        Assert.That(wrappers, Is.LessThanOrEqualTo(forwarded ? 1 : 0));
        Assert.That(count + wrappers, Is.LessThanOrEqualTo(forwarded ? 2 : 1));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int DirectSourceForwarderProbe(
        int /* SPINT001: all values valid - forwarded unchanged to the arithmetic probe. */ iterations,
        int /* SPINT001: all values valid - forwarded unchanged to the arithmetic probe. */ divisor)
    {
        return DirectSourceFrameProbe(iterations, divisor);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int DirectSourceFrameProbe(
        int /* SPINT001: all values valid - nonpositive counts skip the loop. */ iterations,
        int /* SPINT001: all values valid - zero deliberately raises the primitive fault. */ divisor)
    {
        var value = 1;
        for (var index = 0; index < iterations; index++)
        { value = unchecked((value * 31) ^ index); }
        return value / divisor;
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(16)]
    public void DimensionFaultStringsIncludeDiscardedConstructorAndReplacementMessages(int materializations)
    {
        var check = ResourceTaskFaultCarrierCatalog.BoundNativeDimensionFaultStrings(ResourceExpression.Literal(materializations), "Linux",
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        Assert.That(check.UnknownReason, Is.Null);
        Assert.That(check.CumulativeUpper!.Constant, Is.EqualTo(new BigInteger(256) * materializations));
        Assert.That(check.RequiredRuntimeModel, Is.EqualTo(ResourceTaskFaultCarrierCatalog.NativeDimensionMessageModel));
    }

    [TestCase("Windows")]
    [TestCase("macOS")]
    public void DimensionFaultStringsPropagateUnsupportedConstructorPlatforms(string operatingSystem)
    {
        ArgumentNullException.ThrowIfNull(operatingSystem);
        var check = ResourceTaskFaultCarrierCatalog.BoundNativeDimensionFaultStrings(ResourceExpression.Zero, operatingSystem,
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        Assert.That(check.UnknownReason, Is.EqualTo("UnsupportedNativeOomMessagePlatform"));
        Assert.That(check.CumulativeUpper, Is.Null);
        Assert.That(check.RequiredRuntimeModel, Is.Null);
    }

    [Test]
    public void DimensionFaultStringCompositionPreservesCountsWithoutPartialResultsOnFailure()
    {
        var layout = new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "x64", 8);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var count = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "materializations", int.MaxValue));
        var check = ResourceTaskFaultCarrierCatalog.BoundNativeDimensionFaultStrings(count, "Linux", layout, cancellation);
        Assert.That(check.CumulativeUpper!.Evaluate(static input => input is null ? throw new ArgumentNullException(nameof(input)) : 7,
            cancellation), Is.EqualTo(new BigInteger(1792)));
        for (var depth = count.Depth; depth < ResourceExpression.MaximumDepth - 1; depth++)
        { count = ResourceExpression.Add(count, ResourceExpression.One); }
        check = ResourceTaskFaultCarrierCatalog.BoundNativeDimensionFaultStrings(count, "Linux", layout, cancellation);
        Assert.That(check.UnknownReason, Is.EqualTo("NativeDimensionMessageBudgetExceeded"));
        Assert.That(check.CumulativeUpper, Is.Null);
        Assert.That(check.RequiredRuntimeModel, Is.Null);
        count = ResourceExpression.Add(count, ResourceExpression.One);
        check = ResourceTaskFaultCarrierCatalog.BoundNativeDimensionFaultStrings(count, "Linux", layout, cancellation);
        Assert.That(check.UnknownReason, Is.EqualTo("NativeOomMessageBudgetExceeded"));
        Assert.That(check.CumulativeUpper, Is.Null);
        Assert.That(check.RequiredRuntimeModel, Is.Null);
        check = ResourceTaskFaultCarrierCatalog.BoundNativeDimensionFaultStrings(ResourceExpression.Zero, "Linux",
            new("CoreCLR", "8.0.32", "arm64", 8), cancellation);
        Assert.That(check.UnknownReason, Is.EqualTo("IncompatibleResourceRuntimeLayout"));
        Assert.That(check.CumulativeUpper, Is.Null);
        Assert.That(check.RequiredRuntimeModel, Is.Null);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Action operation = () => { _ = ResourceTaskFaultCarrierCatalog.BoundNativeDimensionFaultStrings(ResourceExpression.One, "Linux", layout, canceled.Token); };
        Assert.Throws<OperationCanceledException>(operation);
    }

    [Test]
    public void DimensionFaultAllocationsFitMessageInstanceAndObservedTraceComponents()
    {
        Assert.That(Environment.Version, Is.EqualTo(new Version(8, 0, 32)));
        Assert.That(OperatingSystem.IsLinux(), Is.True);
        Assert.That(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            Is.EqualTo(System.Runtime.InteropServices.Architecture.X64));
        Assert.That(Array.MaxLength, Is.LessThan(int.MaxValue));
        var layout = new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "x64", 8);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var messages = ResourceTaskFaultCarrierCatalog.BoundNativeDimensionFaultStrings(ResourceExpression.One, "Linux", layout, cancellation);
        var instance = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Object(ResourceTaskFaultNotificationCatalog.ExceptionFields), layout, cancellation);
        var traceField = typeof(Exception).GetField("_stackTrace", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var warmup = 0; warmup < 64; warmup++)
        { GC.KeepAlive(CaptureDimensionFault()); }
        for (var iteration = 0; iteration < 32; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var fault = CaptureDimensionFault();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            var trace = (byte[])traceField.GetValue(fault)!;
            var frames = checked((int)BitConverter.ToUInt64(trace, 0));
            var raw = ResourceTaskFaultCarrierCatalog.BoundRawTraceBuffers(frames, 0, layout, cancellation);
            var resolver = ResourceTaskFaultCarrierCatalog.BoundResolverTraceBuffers(frames, 0, 30, layout, cancellation);
            var bound = messages.CumulativeUpper!.Constant!.Value + instance.Bounds!.Upper.Constant!.Value +
                raw.CumulativeUpper!.Constant!.Value + resolver.CumulativeUpper!.Constant!.Value;
            Assert.That(fault.Message.Length, Is.EqualTo(42));
            Assert.That(new BigInteger(allocated), Is.GreaterThan(0).And.LessThanOrEqualTo(bound));
            GC.KeepAlive(fault);
        }
    }

    private static OutOfMemoryException CaptureDimensionFault()
    {
        try
        { GC.KeepAlive(CreateOversizedByteArray(int.MaxValue)); }
        catch (OutOfMemoryException fault)
        { return fault; }
        throw new InvalidOperationException("Expected the checked dimension limit to reject the array.");
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static byte[] CreateOversizedByteArray(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, Array.MaxLength);
        return new byte[length];
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(16)]
    public void NativeOomMessageStorageBoundsOneReturnedStringPerConstructor(int attempts)
    {
        var check = ResourceTaskFaultCarrierCatalog.BoundNativeOomConstructorMessages(ResourceExpression.Literal(attempts), "Linux",
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        Assert.That(check.UnknownReason, Is.Null);
        Assert.That(check.CumulativeUpper!.Constant, Is.EqualTo(new BigInteger(144) * attempts));
        Assert.That(check.RequiredRuntimeModel, Is.EqualTo(ResourceTaskFaultCarrierCatalog.NativeOomMessageModel));
    }

    [TestCase("Windows")]
    [TestCase("macOS")]
    public void NativeOomMessagesCannotBorrowTheUnixTableForOtherPlatforms(string operatingSystem)
    {
        ArgumentNullException.ThrowIfNull(operatingSystem);
        var check = ResourceTaskFaultCarrierCatalog.BoundNativeOomConstructorMessages(ResourceExpression.Zero, operatingSystem,
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        Assert.That(check.UnknownReason, Is.EqualTo("UnsupportedNativeOomMessagePlatform"));
        Assert.That(check.CumulativeUpper, Is.Null);
        Assert.That(check.RequiredRuntimeModel, Is.Null);
    }

    [Test]
    public void NativeOomMessageCompositionPreservesSymbolicCountsAndRejectsBudgetOrLayoutFailure()
    {
        var layout = new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "x64", 8);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var attempts = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "attempts", int.MaxValue));
        var check = ResourceTaskFaultCarrierCatalog.BoundNativeOomConstructorMessages(attempts, "Linux", layout, cancellation);
        Assert.That(check.CumulativeUpper!.Evaluate(static input => input is null ? throw new ArgumentNullException(nameof(input)) : 3,
            cancellation), Is.EqualTo(new BigInteger(432)));
        for (var depth = attempts.Depth; depth < ResourceExpression.MaximumDepth; depth++)
        { attempts = ResourceExpression.Add(attempts, ResourceExpression.One); }
        check = ResourceTaskFaultCarrierCatalog.BoundNativeOomConstructorMessages(attempts, "Linux", layout, cancellation);
        Assert.That(check.UnknownReason, Is.EqualTo("NativeOomMessageBudgetExceeded"));
        Assert.That(check.CumulativeUpper, Is.Null);
        Assert.That(check.RequiredRuntimeModel, Is.Null);
        check = ResourceTaskFaultCarrierCatalog.BoundNativeOomConstructorMessages(ResourceExpression.Zero, "Linux",
            new("CoreCLR", "8.0.32", "arm64", 8), cancellation);
        Assert.That(check.UnknownReason, Is.EqualTo("IncompatibleResourceRuntimeLayout"));
        Assert.That(check.CumulativeUpper, Is.Null);
        Assert.That(check.RequiredRuntimeModel, Is.Null);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Action operation = () => { _ = ResourceTaskFaultCarrierCatalog.BoundNativeOomConstructorMessages(ResourceExpression.One, "Linux", layout, canceled.Token); };
        Assert.Throws<OperationCanceledException>(operation);
    }

#pragma warning disable CA2201 // Measure the native constructor message allocation without throwing the exception.
    [Test]
    public void WarmOomConstructorsFitTheIndependentInstanceAndNativeMessageComponents()
    {
        Assert.That(Environment.Version, Is.EqualTo(new Version(8, 0, 32)));
        Assert.That(OperatingSystem.IsLinux(), Is.True);
        Assert.That(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            Is.EqualTo(System.Runtime.InteropServices.Architecture.X64));
        var layout = new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "x64", 8);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var instance = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Object(ResourceTaskFaultNotificationCatalog.ExceptionFields), layout, cancellation);
        var message = ResourceTaskFaultCarrierCatalog.BoundNativeOomConstructorMessages(ResourceExpression.One, "Linux", layout, cancellation);
        var bound = instance.Bounds!.Upper.Constant!.Value + message.CumulativeUpper!.Constant!.Value;
        for (var warmup = 0; warmup < 64; warmup++)
        { GC.KeepAlive(new OutOfMemoryException()); }
        string? previous = null;
        for (var iteration = 0; iteration < 128; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var exception = new OutOfMemoryException();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(new BigInteger(allocated), Is.GreaterThan(0).And.LessThanOrEqualTo(bound));
            Assert.That(exception.Message.Length, Is.LessThanOrEqualTo(61));
            Assert.That(ReferenceEquals(previous, exception.Message), Is.False);
            previous = exception.Message;
            GC.KeepAlive(exception);
        }
    }

#pragma warning restore CA2201

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(16)]
    public void BestOomMaterializationBoundsOneInstancePerConversion(int conversions)
    {
        var check = ResourceTaskFaultCarrierCatalog.BoundBestOomInstances(ResourceExpression.Literal(conversions),
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        Assert.That(check.UnknownReason, Is.Null);
        Assert.That(check.CumulativeUpper!.Constant, Is.EqualTo(new BigInteger(136) * conversions));
        Assert.That(check.RequiredRuntimeModel, Is.EqualTo(ResourceTaskFaultCarrierCatalog.BestOomInstanceModel));
    }

    [Test]
    public void BestOomMaterializationPreservesSymbolicConversionCounts()
    {
        var conversions = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "conversions", int.MaxValue));
        var check = ResourceTaskFaultCarrierCatalog.BoundBestOomInstances(conversions,
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        Assert.That(check.UnknownReason, Is.Null);
        Assert.That(check.CumulativeUpper!.Evaluate(static input => input is null ? throw new ArgumentNullException(nameof(input)) : 7,
            TestContext.CurrentContext.CancellationToken), Is.EqualTo(new BigInteger(952)));
    }

    [Test]
    public void BestOomMaterializationNeverPublishesPartialBoundsOnLayoutOrBudgetFailure()
    {
        var layout = new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "x64", 8);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var conversions = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "conversions", int.MaxValue));
        for (var depth = conversions.Depth; depth < ResourceExpression.MaximumDepth; depth++)
        { conversions = ResourceExpression.Add(conversions, ResourceExpression.One); }
        var check = ResourceTaskFaultCarrierCatalog.BoundBestOomInstances(conversions, layout, cancellation);
        Assert.That(check.UnknownReason, Is.EqualTo("BestOomInstanceBudgetExceeded"));
        Assert.That(check.CumulativeUpper, Is.Null);
        Assert.That(check.RequiredRuntimeModel, Is.Null);
        check = ResourceTaskFaultCarrierCatalog.BoundBestOomInstances(ResourceExpression.Zero,
            new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "arm64", 8), cancellation);
        Assert.That(check.UnknownReason, Is.EqualTo("IncompatibleResourceRuntimeLayout"));
        Assert.That(check.CumulativeUpper, Is.Null);
        Assert.That(check.RequiredRuntimeModel, Is.Null);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Action operation = () => { _ = ResourceTaskFaultCarrierCatalog.BoundBestOomInstances(ResourceExpression.One, layout, canceled.Token); };
        Assert.Throws<OperationCanceledException>(operation);
    }

    [Test]
    public void UninitializedOomInstanceAllocationFitsTheReviewedShapeWithoutMessageConstruction()
    {
        Assert.That(Environment.Version, Is.EqualTo(new Version(8, 0, 32)));
        Assert.That(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            Is.EqualTo(System.Runtime.InteropServices.Architecture.X64));
        Assert.That(typeof(OutOfMemoryException).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly), Is.Empty);
        Assert.That(typeof(SystemException).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly), Is.Empty);
        for (var warmup = 0; warmup < 64; warmup++)
        { GC.KeepAlive(RuntimeHelpers.GetUninitializedObject(typeof(OutOfMemoryException))); }
        var message = typeof(Exception).GetField("_message", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var iteration = 0; iteration < 128; iteration++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var instance = RuntimeHelpers.GetUninitializedObject(typeof(OutOfMemoryException));
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.GreaterThan(0).And.LessThanOrEqualTo(136));
            Assert.That(message.GetValue(instance), Is.Null);
            GC.KeepAlive(instance);
        }
    }
    [TestCase(ResourcePrimitiveHCallKind.DivideByZero)]
    [TestCase(ResourcePrimitiveHCallKind.Overflow)]
    [TestCase(ResourcePrimitiveHCallKind.NullReference)]
    [TestCase(ResourcePrimitiveHCallKind.RangeCheck)]
    [TestCase(ResourcePrimitiveHCallKind.NewArrayWorker)]
    public void ReviewedPrimitiveHCallsHaveNoDirectTraceFrame(ResourcePrimitiveHCallKind helper)
    {
        var result = ResourceTaskFaultCarrierCatalog.BoundDirectPrimitiveHCallFrames(helper,
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.UnknownReason, Is.Null);
        Assert.That(result.DirectFramesUpper, Is.Zero);
        Assert.That(result.RequiredRuntimeModel, Is.EqualTo(ResourceTaskFaultCarrierCatalog.PrimitiveHCallFrameModel));
    }

    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    public void UnreviewedHelperEntriesNeverPublishAFrameBound(int helper)
    {
        var result = ResourceTaskFaultCarrierCatalog.BoundDirectPrimitiveHCallFrames((ResourcePrimitiveHCallKind)helper,
            new("CoreCLR", "8.0.32", "x64", 8), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.UnknownReason, Is.EqualTo("UnsupportedPrimitiveHCallEntry"));
        Assert.That(result.DirectFramesUpper, Is.Null);
        Assert.That(result.RequiredRuntimeModel, Is.Null);
    }

    [Test]
    public void PrimitiveHelperFramesRequirePinnedLayoutAndPropagateCancellation()
    {
        var layout = new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "arm64", 8);
        var result = ResourceTaskFaultCarrierCatalog.BoundDirectPrimitiveHCallFrames(ResourcePrimitiveHCallKind.Overflow,
            layout, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.UnknownReason, Is.EqualTo("IncompatibleResourceRuntimeLayout"));
        Assert.That(result.DirectFramesUpper, Is.Null);
        Assert.That(result.RequiredRuntimeModel, Is.Null);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Action check = () => { _ = ResourceTaskFaultCarrierCatalog.BoundDirectPrimitiveHCallFrames(ResourcePrimitiveHCallKind.Overflow, layout, canceled.Token); };
        Assert.Throws<OperationCanceledException>(check);
    }

    [TestCase(ResourcePrimitiveHCallKind.DivideByZero, 0, typeof(DivideByZeroException))]
    [TestCase(ResourcePrimitiveHCallKind.Overflow, int.MaxValue, typeof(OverflowException))]
    [TestCase(ResourcePrimitiveHCallKind.NullReference, 0, typeof(NullReferenceException))]
    [TestCase(ResourcePrimitiveHCallKind.RangeCheck, 1, typeof(IndexOutOfRangeException))]
    [TestCase(ResourcePrimitiveHCallKind.NewArrayWorker, -1, typeof(OverflowException))]
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public void PrimitiveFaultRawTracesFitTheIndependentSourceAndCatchFrames(ResourcePrimitiveHCallKind helper, int input, Type expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        Assert.That(Environment.Version, Is.EqualTo(new Version(8, 0, 32)));
        Assert.That(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            Is.EqualTo(System.Runtime.InteropServices.Architecture.X64));
        Exception? fault = null;
        try
        { _ = PrimitiveFault(helper, input); }
        catch (Exception exception) when (exception is DivideByZeroException or OverflowException or NullReferenceException or IndexOutOfRangeException)
        { fault = exception; }
        Assert.That(fault, Is.TypeOf(expected));
        var trace = (byte[])typeof(Exception).GetField("_stackTrace", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fault)!;
        // Inspect the raw VM trace, including hidden frames, rather than the
        // formatted StackTrace API. Only the source and this catch are active.
        var frames = BitConverter.ToUInt64(trace, 0);
        Assert.That(frames, Is.InRange(1UL, 2UL));
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static int PrimitiveFault(ResourcePrimitiveHCallKind helper,
        /* SPINT001: all values valid - Invalid operands intentionally exercise primitive fault paths. */ int input)
    {
        switch (helper)
        {
            case ResourcePrimitiveHCallKind.DivideByZero:
                return 17 / input;
            case ResourcePrimitiveHCallKind.Overflow:
                return checked(input + 1);
            case ResourcePrimitiveHCallKind.NullReference:
                int[]? missing = null;
                return missing!.Length;
            case ResourcePrimitiveHCallKind.RangeCheck:
                return (new int[1])[input];
            case ResourcePrimitiveHCallKind.NewArrayWorker:
                return new int[input].Length;
            default:
                throw new ArgumentOutOfRangeException(nameof(helper));
        }
    }

    [TestCase(0, 0, 0, 0)]
    [TestCase(1, 0, 2, 30)]
    [TestCase(10, 4, 1, 1024)]
    [TestCase(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue)]
    public void SymbolicBufferCompositionPreservesIndependentPrefixInputs(int frames, int rawCopies, int resolverCopies, int capacity)
    {
        var inputs = Enumerable.Range(0, 4).Select(ordinal =>
            ResourceExpression.InputSize(new ResourceInput(ordinal, ResourceInputKind.NonNegativeScalar, "prefix" + ordinal, int.MaxValue))).ToArray();
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var result = ResourceTaskFaultCarrierCatalog.BoundTraceBuffers(inputs[0], inputs[1], inputs[2], inputs[3],
            new("CoreCLR", "8.0.32", "x64", 8), cancellation);
        BigInteger[] values = [frames, rawCopies, resolverCopies, capacity];
        var f = new BigInteger(frames);
        var expected = (2 * f + rawCopies) * (56 + 64 * f) +
            (f + resolverCopies) * (24 + 8 * BigInteger.Max(capacity, 4 * f));
        Assert.That(result.UnknownReason, Is.Null);
        Assert.That(result.RequiredRuntimeModel, Is.EqualTo(ResourceTaskFaultCarrierCatalog.TraceBufferModel));
        Assert.That(result.CumulativeUpper!.Evaluate(input =>
            input is null ? throw new ArgumentNullException(nameof(input)) : values[input.Ordinal], cancellation), Is.EqualTo(expected));
    }

    [Test]
    public void BufferCompositionNeverPublishesPartialBoundsOnBudgetOrLayoutFailure()
    {
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var layout = new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "x64", 8);
        var frames = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "frames", int.MaxValue));
        for (var depth = frames.Depth; depth < ResourceExpression.MaximumDepth; depth++)
        { frames = ResourceExpression.Add(frames, ResourceExpression.One); }
        var result = ResourceTaskFaultCarrierCatalog.BoundTraceBuffers(frames, ResourceExpression.Zero, ResourceExpression.Zero,
            ResourceExpression.Zero, layout, cancellation);
        Assert.That(result.CumulativeUpper, Is.Null);
        Assert.That(result.RequiredRuntimeModel, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("TraceBufferCompositionBudgetExceeded"));
        result = ResourceTaskFaultCarrierCatalog.BoundTraceBuffers(ResourceExpression.One, ResourceExpression.One,
            ResourceExpression.One, ResourceExpression.One, new("CoreCLR", "8.0.32", "arm64", 8), cancellation);
        Assert.That(result.CumulativeUpper, Is.Null);
        Assert.That(result.RequiredRuntimeModel, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("IncompatibleResourceRuntimeLayout"));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Action check = () =>
        {
            _ = ResourceTaskFaultCarrierCatalog.BoundTraceBuffers(ResourceExpression.One, ResourceExpression.One,
            ResourceExpression.One, ResourceExpression.One, layout, canceled.Token);
        };
        Assert.Throws<OperationCanceledException>(check);
    }

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
        var combined = ResourceTaskFaultCarrierCatalog.BoundTraceBuffers(ResourceExpression.Literal(frames),
            ResourceExpression.Literal(2 * restores), ResourceExpression.Literal(2 * restores), ResourceExpression.Literal(30), layout, cancellation);
        Assert.That(combined.UnknownReason, Is.Null);
        Assert.That(combined.CumulativeUpper!.Constant, Is.EqualTo(raw.CumulativeUpper!.Constant + resolver.CumulativeUpper!.Constant));
        Assert.That(((object[])resolverField.GetValue(exception)!).Length, Is.LessThanOrEqualTo(Math.Max(30, 4 * frames)));
        Assert.That(new BigInteger(allocated), Is.LessThanOrEqualTo(combined.CumulativeUpper.Constant!.Value));
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

    [Test]
    public void NestedFreshFaultDoesNotInheritTheAmbientResolverCounter()
    {
        Assert.That(Environment.Version, Is.EqualTo(new Version(8, 0, 32)));
        Assert.That(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            Is.EqualTo(System.Runtime.InteropServices.Architecture.X64));
        var method = new DynamicMethod("FreshResolverSeedControl", typeof(void), [typeof(InvalidOperationException)],
            typeof(ExceptionStackTraceRuntimeTests).Module, true);
        var generator = method.GetILGenerator();
        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(OpCodes.Throw);
        var freshThrower = method.CreateDelegate<Action<InvalidOperationException>>();
        var keepAlive = typeof(GC).GetMethod(nameof(GC.KeepAlive), [typeof(object)])!;
        for (var index = 0; index < 40; index++)
        {
            var next = new DynamicMethod("AmbientResolverSeedControl", typeof(void), [typeof(InvalidOperationException)],
                typeof(ExceptionStackTraceRuntimeTests).Module, true);
            generator = next.GetILGenerator();
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Call, method);
            // Work after the call prevents tail calls without boxing or allocating.
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Call, keepAlive);
            generator.Emit(OpCodes.Ret);
            method = next;
        }
        var ambientThrower = method.CreateDelegate<Action<InvalidOperationException>>();
        var ambient = new InvalidOperationException("ambient-resolver-control");
        CaptureDynamicTrace(ambientThrower, ambient);
        var resolverField = typeof(Exception).GetField("_dynamicMethods", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That(((object[])resolverField.GetValue(ambient)!).Length,
            Is.GreaterThan(ResourceTaskFaultCarrierCatalog.FreshTrackerResolverCapacityUpper));
        var dispatch = ExceptionDispatchInfo.Capture(ambient);
        var fresh = new InvalidOperationException("fresh-resolver-control");
        long allocated = 0;
        try
        { dispatch.Throw(); }
        catch (InvalidOperationException)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            CaptureDynamicTrace(freshThrower, fresh);
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        var seed = ResourceTaskFaultCarrierCatalog.FreshTrackerResolverCapacityUpper;
        Assert.That(seed, Is.EqualTo(30));
        Assert.That(((object[])resolverField.GetValue(fresh)!).Length, Is.EqualTo(seed));
        var traceField = typeof(Exception).GetField("_stackTrace", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var trace = (byte[])traceField.GetValue(fresh)!;
        var frames = checked((int)BitConverter.ToUInt64(trace, 0));
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var layout = new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "x64", 8);
        var raw = ResourceTaskFaultCarrierCatalog.BoundRawTraceBuffers(frames, 0, layout, cancellation);
        var resolver = ResourceTaskFaultCarrierCatalog.BoundResolverTraceBuffers(frames, 0, seed, layout, cancellation);
        Assert.That(allocated, Is.GreaterThan(0));
        Assert.That(new BigInteger(allocated), Is.LessThanOrEqualTo(raw.CumulativeUpper!.Constant!.Value + resolver.CumulativeUpper!.Constant!.Value));
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

    [TestCase(0, 136, false)]
    [TestCase(1, 136, false)]
    [TestCase(2, 136, false)]
    [TestCase(3, 136, false)]
    [TestCase(4, 136, false)]
    [TestCase(5, 144, false)]
    [TestCase(6, 152, false)]
    [TestCase(0, 136, true)]
    [TestCase(1, 136, true)]
    [TestCase(2, 136, true)]
    [TestCase(3, 136, true)]
    [TestCase(4, 136, true)]
    [TestCase(5, 144, true)]
    [TestCase(6, 152, true)]
    public void OrdinarySourceExceptionInstancesFitReviewedInheritedStorage(int family, int upper, bool defaultMessage)
    {
        if (family is < 0 or > 6)
        { throw new ArgumentOutOfRangeException(nameof(family)); }
        Assert.That(Environment.Version, Is.EqualTo(new Version(8, 0, 32)));
        Assert.That(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            Is.EqualTo(System.Runtime.InteropServices.Architecture.X64));
        for (var index = 0; index < 64; index++)
        { GC.KeepAlive(Create(family, defaultMessage)); }
        if (defaultMessage)
        {
            Assert.That(ReferenceEquals(Create(family, true).Message, Create(family, true).Message), Is.True,
                "These measurements require warmed shared default messages.");
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 128; index++)
        { GC.KeepAlive(Create(family, defaultMessage)); }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(bytes, Is.GreaterThan(0));
        Assert.That(bytes, Is.LessThanOrEqualTo(128L * upper));

        static Exception Create(int kind, bool useDefaultMessage)
        {
            if (useDefaultMessage)
            {
                return kind switch
                {
                    0 => new DivideByZeroException(),
                    1 => new OverflowException(),
#pragma warning disable CA2201 // Measure these runtime-reserved instance layouts without throwing them.
                    2 => new NullReferenceException(),
                    3 => new IndexOutOfRangeException(),
#pragma warning restore CA2201
                    4 => new InvalidCastException(),
#pragma warning disable CA2208 // Parameterless constructors are the default-message paths under measurement.
                    5 => new ArgumentException(),
                    6 => new ArgumentOutOfRangeException(),
#pragma warning restore CA2208
                    _ => throw new ArgumentOutOfRangeException(nameof(kind))
                };
            }
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
