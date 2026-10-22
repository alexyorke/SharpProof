namespace SharpProof.Worker;

internal sealed record NativeTaskSchedulerExceptionInstanceCheck(ResourceExpression? InstanceUpper, string? UnknownReason)
{
    internal string? RequiredRuntimeModel => InstanceUpper is null ? null : NativeTaskFaultCarrierStorageVerifier.SchedulerExceptionInstanceModel;
}

internal sealed record NativeTaskFaultCarrierCheck(ResourceExpression? BookkeepingUpper, string? UnknownReason)
{
    internal int? TaskTraceCaptureCountUpper { get; init; }
    internal int? TaskNonEmptyTraceCaptureCountUpper { get; init; }
    internal int? TaskTraceRestoreCountUpper { get; init; }
    internal int? ContextTraceCaptureCountUpper { get; init; }
    internal int? ContextTraceRestoreCountUpper { get; init; }
    internal int? TaskDispatchArrayCopiesUpper => TaskNonEmptyTraceCaptureCountUpper is { } captures && TaskTraceRestoreCountUpper is { } restores
        ? 2 * (captures + restores) : null;
    internal string? RequiredTraceCopyModel => TaskDispatchArrayCopiesUpper is null ? null : NativeTaskFaultCarrierStorageVerifier.TraceCopyModel;
    internal int? CombinedDispatchArrayCopiesUpper => TaskDispatchArrayCopiesUpper is { } taskCopies &&
        ContextTraceCaptureCountUpper is { } captures && ContextTraceRestoreCountUpper is { } restores
        ? taskCopies + 2 * (captures + restores) : null;
    internal string? RequiredCombinedTraceCopyModel => CombinedDispatchArrayCopiesUpper is null ? null : NativeTaskFaultCarrierStorageVerifier.CombinedTraceCopyModel;
}

// Conditional fixed carrier/list allocation prefixes, not a total task-byte
// contract verdict. Exception payload and trace allocation must be composed
// separately before any root cumulative byte proof can be published.
internal static class NativeTaskFaultCarrierStorageVerifier
{
    internal const string SchedulerExceptionInstanceModel = "task-join-scheduler-exception-instances-v1";
    internal const string SchedulerExceptionSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskSchedulerException.cs";

    // Explicit wrapper candidate storage in ScheduleAndStart's queue catch only.
    // Each admitted Task.Run(Action) calls InternalStartNew once, which calls
    // ScheduleAndStart(false) once. Its queue catch has one wrapper allocation.
    // Count every child even if an earlier failure prevents later scheduling.
    // Messages, inner payloads, traces, initialization and allocator/helper
    // failures remain separate components. This has no root proof authority.
    internal static NativeTaskSchedulerExceptionInstanceCheck CheckSchedulerExceptionInstances(CompilerForkJoinProjection projection,
        CompilerTotalEntryPreparation root, ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        projection = ArgumentNullGuard.NotNull(projection, nameof(projection));
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        var source = NativeForkJoinSourceCostVerifier.Check(projection, root, cancellation);
        if (!source.HasSourceCosts)
        { return new(null, source.UnknownReason); }
        var instance = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Object(ResourceTaskFaultNotificationCatalog.ExceptionFields),
            layout, cancellation);
        if (instance.Bounds is not { } bytes)
        { return new(null, instance.UnknownReason); }
        return new(ResourceExpression.Multiply(bytes.Upper, ResourceExpression.Literal(projection.Children.Length)), null);
    }

    internal const string TraceCopyModel = "task-join-task-dispatch-copy-counts-v2";
    internal const string CombinedTraceCopyModel = "task-join-context-dispatch-copy-counts-v2";
    internal static NativeTaskFaultCarrierCheck Check(CompilerForkJoinProjection projection, CompilerTotalEntryPreparation root,
        ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        projection = ArgumentNullGuard.NotNull(projection, nameof(projection));
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        var source = NativeForkJoinSourceCostVerifier.Check(projection, root, cancellation);
        if (!source.HasSourceCosts)
        { return new(null, source.UnknownReason); }
        var compatible = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Array(ResourceStorageKind.Byte,
            ResourceExpression.Zero), layout, cancellation);
        if (!compatible.Verified)
        { return new(null, compatible.UnknownReason); }

        // Ownership bounds the number of children to 2..16. All shapes below
        // have bounded constant extents and layout-supported primitive fields.
        var count = projection.Children.Length;
        var contingent = ObjectBytes(ResourceTaskFaultCarrierCatalog.ContingentFields);
        var holder = ObjectBytes(ResourceTaskFaultCarrierCatalog.HolderFields);
        var dispatch = ObjectBytes(ResourceTaskFaultCarrierCatalog.DispatchFields);
        var fixedRecord = ResourceExpression.Add(contingent, ResourceExpression.Add(holder, dispatch));
        // Do not assume scheduling and callback failures are exclusive. Budget
        // both AddException sites, including their losing carrier candidates.
        var faultsPerChild = ResourceTaskFaultCarrierCatalog.MaximumChildFaultRecords;
        var leafRecord = ResourceExpression.Add(fixedRecord, ListBytes(faultsPerChild));
        var leaf = ResourceExpression.Multiply(leafRecord, ResourceExpression.Literal(faultsPerChild));
        var total = ResourceExpression.Multiply(leaf, ResourceExpression.Literal(count));
        var joinList = ListBytes(count * faultsPerChild);
        total = ResourceExpression.Add(total, ResourceExpression.Add(fixedRecord, joinList));
        // One observed-dispatch list and at most count-1 candidate task lists,
        // including lists that lose publication races. Each candidate gets the
        // full list-growth envelope, conservatively counting unused capacity.
        total = ResourceExpression.Add(total, ResourceExpression.Multiply(joinList, ResourceExpression.Literal(count)));
        if (projection.ConfiguredAwait)
        { total = ResourceExpression.Add(total, ResourceExpression.Add(fixedRecord, ListBytes(1))); }
        // Each task-owned AddException site can capture one dispatch state.
        // WhenAll forwards existing dispatches. A configured root restores the
        // first fault once and records it once in its own async box. Queue wrappers
        // are captured before their first throw: their own trace fields are null,
        // and capture never copies the inner exception's arrays. Keep those dispatch
        // instances, but exclude their captures from possible trace-array copies.
        // Other captures/restores copy at most one raw and one resolver array.
        // Trace growth, affinity copies and other runtime/helper work remain
        // separate obligations. Context-wrapper operations are counted separately
        // below; externally induced context-change callback work is not included.
        return new(total, null)
        {
            TaskTraceCaptureCountUpper = count * faultsPerChild + (projection.ConfiguredAwait ? 1 : 0),
            TaskNonEmptyTraceCaptureCountUpper = count + (projection.ConfiguredAwait ? 1 : 0),
            TaskTraceRestoreCountUpper = projection.ConfiguredAwait ? 1 : 0,
            // Each child enters at most one RunInternal/RunFromThreadPoolDispatchLoop
            // wrapper. A configured root can resume its single await once through
            // the async box's corresponding wrapper. Each catches/rethrows once.
            ContextTraceCaptureCountUpper = count + (projection.ConfiguredAwait ? 1 : 0),
            ContextTraceRestoreCountUpper = count + (projection.ConfiguredAwait ? 1 : 0)
        };

        ResourceExpression ObjectBytes(ImmutableArray<ResourceStorageKind> fields)
        {
            RepositoryParameterGuard.RequireInitialized(fields.IsDefault, nameof(fields), "Fields must be initialized.");
            return ResourceRuntimeLayout.Bound(ResourceAllocationShape.Object(fields), layout, cancellation).Bounds!.Upper;
        }

        ResourceExpression ListBytes(int maximumCount)
        {
            var bytes = ObjectBytes(ResourceTaskFaultCarrierCatalog.ListFields);
            // Covers the holder's capacity-one start, AddRange jumps and the
            // default capacity-four lists. Further growth at least doubles.
            for (var capacity = 1; ; capacity *= 2)
            {
                cancellation.ThrowIfCancellationRequested();
                var array = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Array(ResourceStorageKind.Reference,
                    ResourceExpression.Literal(capacity)), layout, cancellation).Bounds!.Upper;
                bytes = ResourceExpression.Add(bytes, array);
                if (capacity >= maximumCount && capacity >= 4)
                { return bytes; }
            }
        }
    }
}
