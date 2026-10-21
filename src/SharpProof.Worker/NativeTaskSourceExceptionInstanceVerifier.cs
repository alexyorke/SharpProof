namespace SharpProof.Worker;

internal sealed record NativeTaskSourceExceptionInstanceCheck(ResourceExpression? SourceInstanceUpper, string? UnknownReason)
{
    internal string? RequiredRuntimeModel => SourceInstanceUpper is null ? null : NativeTaskSourceExceptionInstanceVerifier.Model;
}

// Fixed ordinary source-exception instances only. Messages and their initialization,
// referenced actual values, allocation/helper failures, scheduler wrappers, traces
// and dispatch copies are separate obligations. At-most-once callback invocation
// is a separate runtime premise. This has no root proof authority.
internal static class NativeTaskSourceExceptionInstanceVerifier
{
    internal const string Model = "task-join-source-exception-instances-v1";
    internal const string ArgumentSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/ArgumentException.cs";
    internal const string RangeSource = "https://github.com/dotnet/runtime/blob/b9a49e4a6d3a444a3685e318c74a7892d01873f7/src/libraries/System.Private.CoreLib/src/System/ArgumentOutOfRangeException.cs";

    internal static NativeTaskSourceExceptionInstanceCheck Check(CompilerForkJoinProjection projection,
        CompilerTotalEntryPreparation root, ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        projection = ArgumentNullGuard.NotNull(projection, nameof(projection));
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        var origins = NativeTaskIrFaultOriginVerifier.Check(projection, root, cancellation);
        if (!origins.HasOrigins)
        { return new(null, origins.UnknownReason); }
        var compatible = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Array(ResourceStorageKind.Byte,
            ResourceExpression.Zero), layout, cancellation);
        if (!compatible.Verified)
        { return new(null, compatible.UnknownReason); }
        foreach (var child in projection.Children)
        {
            foreach (var block in child.Owner.Program.Blocks)
            {
                cancellation.ThrowIfCancellationRequested();
                // A caught/retried exception could allocate repeatedly. Require
                // every explicit source throw to leave the callback immediately.
                if (block.Terminator is IrThrowInstruction thrown &&
                    child.Owner.Program.GetBlock(thrown.Target).Terminator is not IrExceptionalExitInstruction)
                { return new(null, "TaskSourceExceptionHandlingUnsupported"); }
            }
        }
        if (origins.ExceptionKinds!.IsEmpty)
        { return new(ResourceExpression.Zero, null); }
        // The seven admitted families inherit Exception's reviewed slots. Only
        // ArgumentException adds _paramName; ArgumentOutOfRange adds _actualValue.
        var extra = origins.ExceptionKinds.Contains(IrExceptionKind.ArgumentOutOfRange) ? 2 :
            origins.ExceptionKinds.Contains(IrExceptionKind.Argument) ? 1 : 0;
        var fields = ResourceTaskFaultNotificationCatalog.ExceptionFields;
        for (var index = 0; index < extra; index++)
        { fields = fields.Add(ResourceStorageKind.Reference); }
        var instance = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Object(fields), layout, cancellation).Bounds!.Upper;
        // At most one escaping ordinary source fault per callback. Taking the
        // largest family for every callback deliberately overcounts empty ones.
        return new(ResourceExpression.Multiply(instance, ResourceExpression.Literal(projection.Children.Length)), null);
    }
}
