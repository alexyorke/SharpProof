namespace SharpProof.Worker;

internal sealed record NativeTaskFaultCarrierCheck(ResourceExpression? BookkeepingUpper, string? UnknownReason);

// Conditional fixed carrier/list allocation prefixes, not a total task-byte
// contract verdict. Exception payload and trace allocation must be composed
// separately before any root cumulative byte proof can be published.
internal static class NativeTaskFaultCarrierStorageVerifier
{
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
        var leafRecord = ResourceExpression.Add(fixedRecord, ListBytes(1));
        var total = ResourceExpression.Multiply(leafRecord, ResourceExpression.Literal(count));
        var joinList = ListBytes(count);
        total = ResourceExpression.Add(total, ResourceExpression.Add(fixedRecord, joinList));
        // One observed-dispatch list and at most count-1 candidate task lists,
        // including lists that lose publication races. Each candidate gets the
        // full list-growth envelope, conservatively counting unused capacity.
        total = ResourceExpression.Add(total, ResourceExpression.Multiply(joinList, ResourceExpression.Literal(count)));
        if (projection.ConfiguredAwait)
        { total = ResourceExpression.Add(total, leafRecord); }
        return new(total, null);

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
