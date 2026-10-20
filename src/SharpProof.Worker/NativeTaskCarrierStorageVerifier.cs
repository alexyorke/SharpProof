namespace SharpProof.Worker;

internal sealed record NativeTaskCarrierStorageCheck(ResourceExpression? DirectUpper,
    ImmutableArray<ResourceStorageKind> AsyncBoxStateFields, string? UnknownReason)
{
    public ImmutableArray<ResourceStorageKind> AsyncBoxStateFields { get; init; } = AsyncBoxStateFields.IsDefault
        ? throw new ArgumentException("State fields must be initialized.", nameof(AsyncBoxStateFields)) : AsyncBoxStateFields;
}

// Conditional compiler-owned carriers only. Runtime Task/async-box objects,
// execution-context effects and lifetime composition are separate obligations.
internal static class NativeTaskCarrierStorageVerifier
{
    internal const string Model = WorkerResourceModels.TaskCompilerCarrierModel;
    internal const int MaximumCallbacks = 64;

    internal static NativeTaskCarrierStorageCheck Check(CompilerTaskCarrierShapes shapes,
        CompilerForkJoinProjection projection, CompilerTotalEntryPreparation root,
        ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        shapes = ArgumentNullGuard.NotNull(shapes, nameof(shapes));
        projection = ArgumentNullGuard.NotNull(projection, nameof(projection));
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        if (!NativeForkJoinSourceCostVerifier.Check(projection, root, cancellation).HasSourceCosts ||
            shapes.RootCallableId != root.CallableId || !WorkerProtocolJson.IsSha256(shapes.ModuleSha256) ||
            string.IsNullOrWhiteSpace(shapes.CompilerIdentity) || shapes.CompilerIdentity.Length > 512 ||
            shapes.MatchedCallbacks < projection.Children.Length || shapes.MatchedCallbacks > MaximumCallbacks ||
            shapes.CallbackFrames.IsDefault || shapes.CallbackFrames.Length > shapes.MatchedCallbacks ||
            (shapes.StateMachine != null) != projection.ConfiguredAwait)
        { return Unknown("TaskCompilerCarrierOwnershipUnproved"); }
        var remaining = ResourceExpression.MaximumNodes;
        foreach (var frame in shapes.CallbackFrames)
        {
            cancellation.ThrowIfCancellationRequested();
            if (frame == null || frame.IsValueType || !Valid(frame, ref remaining, cancellation))
            { return Unknown("TaskCompilerCarrierShapeUnsupported"); }
        }
        if (shapes.StateMachine is { } machine && !Valid(machine, ref remaining, cancellation))
        { return Unknown("TaskCompilerCarrierShapeUnsupported"); }
        try
        {
            // One source params array and at most one delegate per emitted
            // callback. Shared frames/caches are deliberately overcounted.
            var array = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Array(ResourceStorageKind.Reference,
                ResourceExpression.Literal(projection.Children.Length)), layout, cancellation);
            if (array.Bounds == null)
            { return Unknown(array.UnknownReason ?? "TaskCompilerCarrierLayoutUnproved"); }
            var delegates = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Delegate(), layout, cancellation);
            if (delegates.Bounds == null)
            { return Unknown(delegates.UnknownReason ?? "TaskCompilerCarrierLayoutUnproved"); }
            var upper = ResourceExpression.Add(array.Bounds.Upper, ResourceExpression.Multiply(delegates.Bounds.Upper,
                ResourceExpression.Literal(shapes.MatchedCallbacks)));
            foreach (var frame in shapes.CallbackFrames)
            {
                cancellation.ThrowIfCancellationRequested();
                var bound = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Object(frame.Fields), layout, cancellation);
                if (bound.Bounds == null)
                { return Unknown(bound.UnknownReason ?? "TaskCompilerCarrierLayoutUnproved"); }
                upper = ResourceExpression.Add(upper, bound.Bounds.Upper);
            }
            ImmutableArray<ResourceStorageKind> stateFields = [];
            if (shapes.StateMachine is { } state)
            {
                if (state.IsValueType)
                { stateFields = state.Fields; }
                else
                {
                    var bound = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Object(state.Fields), layout, cancellation);
                    if (bound.Bounds == null)
                    { return Unknown(bound.UnknownReason ?? "TaskCompilerCarrierLayoutUnproved"); }
                    upper = ResourceExpression.Add(upper, bound.Bounds.Upper);
                    stateFields = [ResourceStorageKind.Reference];
                }
            }
            return new(upper, stateFields, null);
        }
        catch (ResourceExpressionLimitException)
        { return Unknown("TaskCompilerCarrierBudgetExceeded"); }
    }

    private static bool Valid(CompilerTaskCarrierType type, ref int remaining, CancellationToken cancellation)
    {
        type = ArgumentNullGuard.NotNull(type, nameof(type));
        if (string.IsNullOrWhiteSpace(type.MetadataName) || type.MetadataName.Length > 4096 ||
            type.Fields.IsDefault || type.Fields.Length > ResourceAllocationShape.MaximumFields)
        { return false; }
        remaining -= type.Fields.Length + 1;
        if (remaining < 0)
        { return false; }
        foreach (var field in type.Fields)
        {
            cancellation.ThrowIfCancellationRequested();
            if (field is not (ResourceStorageKind.Byte or ResourceStorageKind.Word16 or ResourceStorageKind.Word32 or
                ResourceStorageKind.Word64 or ResourceStorageKind.Reference or ResourceStorageKind.NativeInteger))
            { return false; }
        }
        return true;
    }

    private static NativeTaskCarrierStorageCheck Unknown(string reason)
    {
        reason = ArgumentNullGuard.NotNull(reason, nameof(reason));
        return new(null, [], reason);
    }
}
