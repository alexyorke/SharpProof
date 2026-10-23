namespace SharpProof.Worker;

internal sealed record NativeTaskStorageCheck(ResourceExpression? PeakUpper, ResourceExpression? RetainedUpper, string? UnknownReason);

// Bounds owned storage through a successful join, under separately bound
// runtime/layout premises. This component does not publish contract verdicts.
internal static class NativeTaskStorageVerifier
{
    internal static NativeTaskStorageCheck Check(CompilerForkJoinProjection projection, CompilerTotalEntryPreparation root,
        ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        projection = ArgumentNullGuard.NotNull(projection, nameof(projection));
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        if (projection.Carriers?.Shapes is not { } shapes)
        { return Unknown("TaskCompilerCarrierEvidenceUnavailable"); }
        var compiler = NativeTaskCarrierStorageVerifier.Check(shapes, projection, root, layout, cancellation);
        if (compiler.DirectUpper is not { } direct)
        { return Unknown(compiler.UnknownReason ?? "TaskCompilerStorageUnproved"); }
        var children = NativeForkJoinSourceCostVerifier.CheckChildStorage(projection, root, layout, cancellation);
        if (children.PeakAuxiliaryUpper is not { } childPeak || children.RetainedUpper is not { } childRetained)
        { return Unknown(children.UnknownReason ?? "TaskChildStorageUnproved"); }
        if (compiler.AsyncBoxStateFields.Length > ResourceAllocationShape.MaximumFields - ResourceTaskStorageCatalog.TaskFields.Length - 1)
        { return Unknown("TaskRuntimeStorageFieldBudgetExceeded"); }
        try
        {
            // The compiler check established layout compatibility, and every
            // catalog/embedded field has an admitted primitive/reference kind.
            var task = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Object(ResourceTaskStorageCatalog.TaskFields), layout, cancellation).Bounds!.Upper;
            var join = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Object(ResourceTaskStorageCatalog.JoinFields), layout, cancellation).Bounds!.Upper;
            var runtime = ResourceExpression.Add(join, ResourceExpression.Multiply(task, ResourceExpression.Literal(projection.Children.Length)));
            if (projection.ConfiguredAwait)
            {
                // Task<VoidTaskResult> adds one empty value (bounded by a byte
                // slot). The box adds compiler state; context reuses Task state.
                ImmutableArray<ResourceStorageKind> fields = [.. ResourceTaskStorageCatalog.TaskFields, ResourceStorageKind.Byte, .. compiler.AsyncBoxStateFields];
                var box = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Object(fields), layout, cancellation).Bounds!.Upper;
                runtime = ResourceExpression.Add(runtime, box);
                runtime = ResourceExpression.Add(runtime, ResourceExpression.Literal(2 * ResourceRuntimeLayout.DelegateBytes));
            }
            var carriers = ResourceExpression.Add(direct, runtime);
            // Keeping every carrier is a safe retained upper envelope; child
            // void/private storage cannot become returned output on success.
            return new(ResourceExpression.Add(childPeak, carriers), ResourceExpression.Add(childRetained, carriers), null);
        }
        catch (ResourceExpressionLimitException)
        { return Unknown("TaskRuntimeStorageExpressionBudgetExceeded"); }
    }

    private static NativeTaskStorageCheck Unknown(string reason)
    {
        reason = ArgumentNullGuard.NotNull(reason, nameof(reason));
        return new(null, null, reason);
    }
}
