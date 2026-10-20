namespace SharpProof.Worker;

internal sealed record NativeTaskFaultNotificationStorageCheck(ResourceExpression? ConstructionUpper, string? UnknownReason);

// A conditional construction component, not a root task-byte verdict or a bound
// on the number of notifications. Counts/lifecycles and other fault allocations
// require independent composition before a complete runtime proof is published.
internal static class NativeTaskFaultNotificationStorageVerifier
{
    internal static NativeTaskFaultNotificationStorageCheck Check(int faults, ResourceRuntimeLayoutContext layout,
        CancellationToken cancellation)
    {
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        if (faults is < 1 or > 16)
        { return new(null, "TaskFaultNotificationCountUnsupported"); }
        var array = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Array(ResourceStorageKind.Reference,
            ResourceExpression.Literal(faults)), layout, cancellation);
        if (!array.Verified)
        { return new(null, array.UnknownReason); }
        // Validated layout and fixed supported fields make these bounded objects.
        var aggregate = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Object(ResourceTaskFaultNotificationCatalog.AggregateFields), layout, cancellation);
        var arguments = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Object(ResourceTaskFaultNotificationCatalog.EventFields), layout, cancellation);
        return new(ResourceExpression.Add(array.Bounds!.Upper,
            ResourceExpression.Add(aggregate.Bounds!.Upper, arguments.Bounds!.Upper)), null);
    }
}
