using System.Collections.Immutable;
using System.Numerics;
using SharpProof.Ir;

namespace SharpProof.Specs;

internal enum ResourcePrimitiveHCallKind { DivideByZero, Overflow, NullReference, RangeCheck, NewArrayWorker }

internal sealed record ResourcePrimitiveHCallFrameCheck(int? DirectFramesUpper, string? UnknownReason)
{
    internal string? RequiredRuntimeModel => DirectFramesUpper is null ? null : ResourceTaskFaultCarrierCatalog.PrimitiveHCallFrameModel;
}

internal sealed record ResourceNativeOomMessageCheck(ResourceExpression? CumulativeUpper, string? UnknownReason)
{
    internal string? RequiredRuntimeModel => CumulativeUpper is null ? null : ResourceTaskFaultCarrierCatalog.NativeOomMessageModel;
}

internal sealed record ResourceBestOomInstanceCheck(ResourceExpression? CumulativeUpper, string? UnknownReason)
{
    internal string? RequiredRuntimeModel => CumulativeUpper is null ? null : ResourceTaskFaultCarrierCatalog.BestOomInstanceModel;
}
internal sealed record ResourceRawTraceBufferCheck(ResourceExpression? CumulativeUpper, string? UnknownReason)
{
    internal string? RequiredRuntimeModel => CumulativeUpper is null ? null : ResourceTaskFaultCarrierCatalog.RawTraceBufferModel;
}

internal sealed record ResourceResolverTraceBufferCheck(ResourceExpression? CumulativeUpper, string? UnknownReason)
{
    internal string? RequiredRuntimeModel => CumulativeUpper is null ? null : ResourceTaskFaultCarrierCatalog.ResolverTraceBufferModel;
}

internal sealed record ResourceTraceBufferCheck(ResourceExpression? CumulativeUpper, string? UnknownReason)
{
    internal string? RequiredRuntimeModel => CumulativeUpper is null ? null : ResourceTaskFaultCarrierCatalog.TraceBufferModel;
}

// Fixed .NET 8.0.32 fault bookkeeping and separately scoped trace components.
// None of these components establishes a root allocation verdict.
internal static class ResourceTaskFaultCarrierCatalog
{
    internal const string Model = "task-join-fixed-fault-carriers-v2";
    internal const string RawTraceBufferModel = "closed-raw-exception-trace-buffers-v1";
    internal const string ResolverTraceBufferModel = "closed-exception-resolver-buffers-v1";
    internal const string TraceBufferModel = "closed-exception-trace-buffer-composition-v1";
    internal const string PrimitiveHCallFrameModel = "coreclr8-direct-primitive-hcall-trace-frames-v1";
    internal const string HCallIdentitySource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/fcall.h";
    internal const string HCallMappingSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/ecall.cpp";
    internal const string HCallFrameSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/frames.cpp";
    internal const string PrimitiveHelperSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/jithelpers.cpp";

    // The direct explicit frame of the reviewed HCIMPL entry only. HCIMPL
    // clears __me; HelperMethodFrame receives that null FCall identity, and
    // MapTargetBackToMethod(null) returns null. ProcessExplicitFrame appends
    // only non-null MethodDesc frames. This does not bound managed calls made
    // by a helper, all implementations selected by an opcode, or unwind visits.
    internal static ResourcePrimitiveHCallFrameCheck BoundDirectPrimitiveHCallFrames(ResourcePrimitiveHCallKind helper,
        ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        if (helper is not (ResourcePrimitiveHCallKind.DivideByZero or ResourcePrimitiveHCallKind.Overflow or
            ResourcePrimitiveHCallKind.NullReference or ResourcePrimitiveHCallKind.RangeCheck or ResourcePrimitiveHCallKind.NewArrayWorker))
        { return new(null, "UnsupportedPrimitiveHCallEntry"); }
        var compatible = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Array(ResourceStorageKind.Byte,
            ResourceExpression.Zero), layout, cancellation);
        if (!compatible.Verified)
        { return new(null, compatible.UnknownReason); }
        // JIT_ThrowDivZero, JIT_Overflow, JIT_ThrowNullRef, JIT_RngChkFail,
        // and the JIT_NewArr1 worker are HCIMPL entries in the pinned source.
        return new(0, null);
    }
    internal const string NativeOomMessageModel = "coreclr8-linux-native-oom-message-storage-v1";
    internal const string NativeOomMessageSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/comutilnative.cpp";

    // Returned managed-string storage only. The pinned Unix resource table and
    // native fallback both contain at most 61 UTF-16 characters for this ID.
    // Exactly one QCall setter is selected; it attempts one managed string.
    // Lookup/culture callbacks, native buffers, helper failures, initialization
    // and other exception messages remain separate costs. The caller must bind
    // Linux and independently bound all constructor attempts before composition.
    internal static ResourceNativeOomMessageCheck BoundNativeOomConstructorMessages(ResourceExpression constructorAttempts,
        string operatingSystem, ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        constructorAttempts = ArgumentNullGuard.NotNull(constructorAttempts, nameof(constructorAttempts));
        operatingSystem = ArgumentNullGuard.NotNull(operatingSystem, nameof(operatingSystem));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        if (operatingSystem != "Linux")
        { return new(null, "UnsupportedNativeOomMessagePlatform"); }
        var message = ResourceRuntimeLayout.Bound(ResourceAllocationShape.String(ResourceExpression.Literal(61)), layout, cancellation);
        if (message.Bounds is not { } bytes)
        { return new(null, message.UnknownReason); }
        try
        { return new(ResourceExpression.Multiply(bytes.Upper, constructorAttempts), null); }
        catch (ResourceExpressionLimitException)
        { return new(null, "NativeOomMessageBudgetExceeded"); }
    }

    internal const string BestOomInstanceModel = "coreclr8-best-oom-instance-storage-v1";
    internal const string BestOomInstanceSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/clrex.cpp";

    // Instance storage only for GetBestOutOfMemoryException conversions. The
    // caller must independently bound every conversion attempt. GetBestException
    // attempts AllocateObject once, sets two scalar fields, and falls back to
    // the preallocated exception on failure; it does not call a managed ctor.
    // Trace buffers, notifications, initialization and dimension-error message
    // exceptions remain separate components. This has no root proof authority.
    internal static ResourceBestOomInstanceCheck BoundBestOomInstances(ResourceExpression conversions,
        ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        conversions = ArgumentNullGuard.NotNull(conversions, nameof(conversions));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        var instance = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Object(ResourceTaskFaultNotificationCatalog.ExceptionFields),
            layout, cancellation);
        if (instance.Bounds is not { } bytes)
        { return new(null, instance.UnknownReason); }
        try
        { return new(ResourceExpression.Multiply(bytes.Upper, conversions), null); }
        catch (ResourceExpressionLimitException)
        { return new(null, "BestOomInstanceBudgetExceeded"); }
    }
    internal const string FreshSourceTrackerModel = "closed-source-fault-tracker-seed-v1";
    // Both ExceptionTracker constructors call StackTraceInfo.Init (counter zero).
    // AllocateStackTrace seeds 30 in retail builds, 2 in debug builds, or zero
    // on failure. Continuing saves are accounted by the resolver prefix bound.
    internal const int FreshTrackerResolverCapacityUpper = 30;
    internal const string TrackerEntrySource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/exceptionhandling.h";
    internal const string TrackerDispatchSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/exceptionhandling.cpp";
    internal const string RawTraceSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/object.cpp";
    internal const string SaveTraceSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/vm/excep.cpp";

    // Symbolic composition of the two closed-buffer models below. Every input
    // is a nonnegative mathematical upper bound, with its ownership and prefix
    // scope established separately by the caller. In particular, source depth
    // cannot be supplied as appendedFrames without a CLR/helper-frame bridge.
    // This includes arrays only, not their referents or other fault storage.
    internal static ResourceTraceBufferCheck BoundTraceBuffers(ResourceExpression appendedFrames,
        ResourceExpression rawCopies, ResourceExpression resolverCopies, ResourceExpression initialResolverCapacity,
        ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        appendedFrames = ArgumentNullGuard.NotNull(appendedFrames, nameof(appendedFrames));
        rawCopies = ArgumentNullGuard.NotNull(rawCopies, nameof(rawCopies));
        resolverCopies = ArgumentNullGuard.NotNull(resolverCopies, nameof(resolverCopies));
        initialResolverCapacity = ArgumentNullGuard.NotNull(initialResolverCapacity, nameof(initialResolverCapacity));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        var compatible = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Array(ResourceStorageKind.Reference,
            ResourceExpression.Zero), layout, cancellation);
        if (!compatible.Verified)
        { return new(null, compatible.UnknownReason); }
        try
        {
            var raw = ResourceExpression.Multiply(
                ResourceExpression.Add(ResourceExpression.Multiply(ResourceExpression.Literal(2), appendedFrames), rawCopies),
                ResourceExpression.Add(ResourceExpression.Literal(56), ResourceExpression.Multiply(ResourceExpression.Literal(64), appendedFrames)));
            var resolver = ResourceExpression.Multiply(ResourceExpression.Add(appendedFrames, resolverCopies),
                ResourceExpression.Add(ResourceExpression.Literal(24), ResourceExpression.Multiply(ResourceExpression.Literal(8),
                    ResourceExpression.Max(initialResolverCapacity, ResourceExpression.Multiply(ResourceExpression.Literal(4), appendedFrames)))));
            return new(ResourceExpression.Add(raw, resolver), null);
        }
        catch (ResourceExpressionLimitException)
        { return new(null, "TraceBufferCompositionBudgetExceeded"); }
    }

    // A closed collection starts without raw traces and copies only traces from
    // that collection. Inputs must bound ALL attempted appended frames (including
    // failed saves/retries) and raw-array capture/restore copies over its prefix.
    // This does not derive either input from source activations. Resolver arrays,
    // native scratch memory, exception/dispatch instances and helper faults are
    // separate components; this result has no root proof authority.
    internal static ResourceRawTraceBufferCheck BoundRawTraceBuffers(int appendedFrames, int rawCopies,
        ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        if (appendedFrames < 0 || rawCopies < 0)
        { return new(null, "InvalidRawTracePrefixBudget"); }
        var compatible = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Array(ResourceStorageKind.Byte,
            ResourceExpression.Zero), layout, cancellation);
        if (!compatible.Verified)
        { return new(null, compatible.UnknownReason); }
        // x64 StackTraceElement is 32 bytes; its embedded ArrayHeader is 16.
        // Grow allocates only below the required capacity, so doubling remains
        // <= 2 * (16 + 32 * totalFrames). Affinity copies preserve that capacity.
        // Each nonempty SaveStackTrace attempts at most one Append, which can
        // allocate one affinity copy and one growth array. The number of such
        // saves is <= totalFrames, even when an earlier save failed or reset.
        // Both 24 + this capacity and every smaller byte[] align below this bound.
        var frames = new BigInteger(appendedFrames);
        var maximumArrayBytes = 56 + 64 * frames;
        var maximumArrays = 2 * frames + rawCopies;
        return new(ResourceExpression.Literal(maximumArrays * maximumArrayBytes), null);
    }
    // The same closed-collection premise as raw traces, plus an independently
    // proved entry bound on every participating StackTraceInfo's
    // m_cDynamicMethodItems value. ClearStackTrace preserves that counter;
    // it cannot be inferred from the surviving trace size. Referenced resolver
    // and loader objects are not included.
    internal static ResourceResolverTraceBufferCheck BoundResolverTraceBuffers(int appendedFrames, int resolverCopies,
        int initialResolverCapacity, ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        if (appendedFrames < 0 || resolverCopies < 0 || initialResolverCapacity < 0)
        { return new(null, "InvalidResolverTracePrefixBudget"); }
        var compatible = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Array(ResourceStorageKind.Reference,
            ResourceExpression.Zero), layout, cancellation);
        if (!compatible.Verified)
        { return new(null, compatible.UnknownReason); }
        // A replacement saves at most twice its batch's dynamic count, or the
        // existing scratch capacity. An extension recounts the whole stored
        // trace (<= F) and adds the new dynamic batch once more (<= F), then can
        // double that sum. Restoring an old resolver array preserves capacity.
        // Thus no capacity exceeds max(entryCapacity, 4 * totalFrames).
        // Each nonempty save allocates at most one resolver array, even when
        // abandoned; capture/restore each contributes at most one extra copy.
        var frames = new BigInteger(appendedFrames);
        var maximumElements = BigInteger.Max(initialResolverCapacity, 4 * frames);
        var maximumArrayBytes = 24 + 8 * maximumElements;
        return new(ResourceExpression.Literal((frames + resolverCopies) * maximumArrayBytes), null);
    }

    // One queue attempt and one at-most-once callback can each record a fault.
    internal const int MaximumChildFaultRecords = 2;
    internal const int MaximumJoinedDispatches = 16 * MaximumChildFaultRecords;
    internal const string TaskSource = ResourceTaskCatalog.EvidenceSource;
    internal const string HolderSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskExceptionHolder.cs";
    internal const string DispatchSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Runtime/ExceptionServices/ExceptionDispatchInfo.cs";
    internal const string StateSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/coreclr/System.Private.CoreLib/src/System/Exception.CoreCLR.cs";
    internal const string ListSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/List.cs";

    // CancellationToken embeds one source reference. The remaining contingent
    // fields comprise six references and two Int32 values.
    internal static ImmutableArray<ResourceStorageKind> ContingentFields { get; } =
        [ResourceStorageKind.Reference, ResourceStorageKind.Reference, ResourceStorageKind.Reference,
         ResourceStorageKind.Reference, ResourceStorageKind.Reference, ResourceStorageKind.Word32,
         ResourceStorageKind.Word32, ResourceStorageKind.Reference, ResourceStorageKind.Reference];
    internal static ImmutableArray<ResourceStorageKind> HolderFields { get; } =
        [ResourceStorageKind.Reference, ResourceStorageKind.Reference, ResourceStorageKind.Reference, ResourceStorageKind.Byte];
    // Exception reference plus embedded DispatchState: four references and UIntPtr.
    // Referenced payloads are deliberately not part of this fixed-instance model.
    internal static ImmutableArray<ResourceStorageKind> DispatchFields { get; } =
        [ResourceStorageKind.Reference, ResourceStorageKind.Reference, ResourceStorageKind.Reference,
         ResourceStorageKind.Reference, ResourceStorageKind.NativeInteger, ResourceStorageKind.Reference];
    internal static ImmutableArray<ResourceStorageKind> ListFields { get; } =
        [ResourceStorageKind.Reference, ResourceStorageKind.Word32, ResourceStorageKind.Word32];
}
