namespace SharpProof.Worker;

internal sealed record NativeTaskFaultNotificationStorageCheck(ResourceExpression? ConstructionUpper, string? UnknownReason);
internal sealed record NativeTaskFaultNotificationPrefixCheck(ResourceExpression? PrefixUpper, string? UnknownReason)
{
    internal string? RequiredRuntimeModel => PrefixUpper is null ? null : ResourceTaskFaultNotificationCatalog.LifecycleModel;
}

// Conditional construction and closed-lifecycle components, not root task-byte
// verdicts. Runtime premises and other fault allocations require independent
// binding/composition before a complete runtime proof is published.
internal static class NativeTaskFaultNotificationStorageVerifier
{
    // This conditional component requires the separately declared closed-holder
    // lifecycle premise. Checking source ownership does not bind that premise
    // or establish a complete root runtime-byte bound.
    internal static NativeTaskFaultNotificationPrefixCheck CheckJoinedPrefixes(CompilerForkJoinProjection projection,
        CompilerTotalEntryPreparation root, ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        projection = ArgumentNullGuard.NotNull(projection, nameof(projection));
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        var source = NativeForkJoinSourceCostVerifier.Check(projection, root, cancellation);
        if (!source.HasSourceCosts)
        { return new(null, source.UnknownReason); }
        var records = ResourceTaskFaultCarrierCatalog.MaximumChildFaultRecords;
        var child = Check(records, layout, cancellation);
        if (child.ConstructionUpper is not { } childBytes)
        { return new(null, child.UnknownReason); }
        var count = projection.Children.Length;
        // A holder starts registered. Only a later AddFaultException can
        // re-register it through MarkAsUnhandled after it was observed.
        // Two recording sites therefore allow at most two constructions per
        // child. Losing holder candidates never receive a fault list.
        var prefix = ResourceExpression.Multiply(childBytes, ResourceExpression.Literal(count * records));
        // Source ownership admits 2..16 children. Layout was checked above,
        // so 2*count dispatches and the single-dispatch async box are supported.
        var joined = Check(count * records, layout, cancellation).ConstructionUpper!;
        prefix = ResourceExpression.Add(prefix, joined);
        if (projection.ConfiguredAwait)
        { prefix = ResourceExpression.Add(prefix, Check(1, layout, cancellation).ConstructionUpper!); }
        return new(prefix, null);
    }

    internal static NativeTaskFaultNotificationStorageCheck Check(int faults, ResourceRuntimeLayoutContext layout,
        CancellationToken cancellation)
    {
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        if (faults is < 1 or > ResourceTaskFaultCarrierCatalog.MaximumJoinedDispatches)
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
