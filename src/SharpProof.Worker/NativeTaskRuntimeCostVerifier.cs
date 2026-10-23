namespace SharpProof.Worker;

internal sealed record NativeTaskRuntimeCostInputs(long RawTraceArrayAttemptsPerFaultEpisode, long RawTraceArrayPayloadBytes,
    long ResolverTraceArrayAttemptsPerFaultEpisode, long ResolverTraceArraySlots, long FaultConstructionBytesPerFaultEpisode,
    long FreshResolverReferentBytesPerFaultEpisode, long SchedulingBytesPerChildStart, long JoinBytesPerAttempt,
    long AwaitBytesPerConfiguredAwait, long InitializationBytesPerInvocation, long TotalFaultEpisodesPerInvocation);

internal sealed record NativeTaskRuntimeCostCheck(ResourceExpression? CumulativeUpper, string? UnknownReason)
{
    internal string? RequiredRuntimeModel => CumulativeUpper is null ? null : NativeTaskRuntimeCostVerifier.Model;
}

// Conditional arithmetic only. Callers must independently bind and report every
// runtime quantity, deployment profile and execution-prefix scope. Fault budgets
// cover all nested construction/replacement work, retries, imported state and
// existing-exception processing. They are not derived from source fault sites.
// Runtime budgets include their fixed Task/fault carriers and notifications;
// adding the private fixed-component bounds again would charge them twice.
internal static class NativeTaskRuntimeCostVerifier
{
    internal const string Model = "task-join-declared-runtime-allocation-prefixes-v1";

    internal static NativeTaskRuntimeCostCheck Check(CompilerForkJoinProjection projection, CompilerTotalEntryPreparation root,
        ResourceRuntimeLayoutContext layout, NativeTaskRuntimeCostInputs inputs, CancellationToken cancellation)
    {
        projection = ArgumentNullGuard.NotNull(projection, nameof(projection));
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        inputs = ArgumentNullGuard.NotNull(inputs, nameof(inputs));
        cancellation.ThrowIfCancellationRequested();
        if (inputs.RawTraceArrayAttemptsPerFaultEpisode < 0 || inputs.RawTraceArrayPayloadBytes < 0 ||
            inputs.ResolverTraceArrayAttemptsPerFaultEpisode < 0 || inputs.ResolverTraceArraySlots < 0 ||
            inputs.FaultConstructionBytesPerFaultEpisode < 0 || inputs.FreshResolverReferentBytesPerFaultEpisode < 0 ||
            inputs.SchedulingBytesPerChildStart < 0 || inputs.JoinBytesPerAttempt < 0 ||
            inputs.AwaitBytesPerConfiguredAwait < 0 || inputs.InitializationBytesPerInvocation < 0 ||
            inputs.TotalFaultEpisodesPerInvocation < 0)
        { return Unknown("TaskRuntimeCostInputsInvalid"); }
        if (projection.Carriers?.Shapes is not { } shapes)
        { return Unknown("TaskCompilerCarrierEvidenceUnavailable"); }
        var compiler = NativeTaskCarrierStorageVerifier.Check(shapes, projection, root, layout, cancellation);
        if (compiler.DirectUpper is not { } direct)
        { return Unknown(compiler.UnknownReason ?? "TaskCompilerStorageUnproved"); }
        // The returned variant is the owned factory expression. It has no
        // authored async catch to borrow. Its runtime factory/helper failures
        // remain inside the declared episode and per-operation budgets.
        if (projection.ConfiguredAwait && NativeTaskCarrierStorageVerifier.CheckExceptionBoundary(shapes, projection,
            root, layout, cancellation) is { } boundary)
        { return Unknown(boundary); }
        var emitted = NativeTaskEmittedFaultSiteVerifier.Check(projection, root, layout, cancellation);
        if (!emitted.HasSites)
        { return Unknown(emitted.UnknownReason ?? "TaskEmittedFaultBodyEvidenceUnavailable"); }
        var children = NativeForkJoinSourceCostVerifier.CheckChildStorage(projection, root, layout, cancellation);
        if (children.CumulativeUpper is not { } child)
        { return Unknown(children.UnknownReason ?? "TaskChildStorageUnproved"); }
        try
        {
            var raw = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Array(ResourceStorageKind.Byte,
                ResourceExpression.Literal(inputs.RawTraceArrayPayloadBytes)), layout, cancellation);
            var resolver = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Array(ResourceStorageKind.Reference,
                ResourceExpression.Literal(inputs.ResolverTraceArraySlots)), layout, cancellation);
            if (raw.Bounds is not { } rawBytes || resolver.Bounds is not { } resolverBytes)
            { return Unknown(raw.UnknownReason ?? resolver.UnknownReason ?? "TaskRuntimeTraceLayoutUnproved"); }
            var episode = ResourceExpression.Add(
                ResourceExpression.Multiply(ResourceExpression.Literal(inputs.RawTraceArrayAttemptsPerFaultEpisode), rawBytes.Upper),
                ResourceExpression.Multiply(ResourceExpression.Literal(inputs.ResolverTraceArrayAttemptsPerFaultEpisode), resolverBytes.Upper));
            episode = ResourceExpression.Add(episode, ResourceExpression.Literal(inputs.FaultConstructionBytesPerFaultEpisode));
            episode = ResourceExpression.Add(episode, ResourceExpression.Literal(inputs.FreshResolverReferentBytesPerFaultEpisode));
            var runtime = ResourceExpression.Multiply(ResourceExpression.Literal(projection.Children.Length),
                ResourceExpression.Literal(inputs.SchedulingBytesPerChildStart));
            // The owned expression attempts one join, even when earlier failure
            // prevents reaching it. A configured root attempts its sole await.
            runtime = ResourceExpression.Add(runtime, ResourceExpression.Literal(inputs.JoinBytesPerAttempt));
            if (projection.ConfiguredAwait)
            { runtime = ResourceExpression.Add(runtime, ResourceExpression.Literal(inputs.AwaitBytesPerConfiguredAwait)); }
            runtime = ResourceExpression.Add(runtime, ResourceExpression.Multiply(
                ResourceExpression.Literal(inputs.TotalFaultEpisodesPerInvocation), episode));
            runtime = ResourceExpression.Add(runtime, ResourceExpression.Literal(inputs.InitializationBytesPerInvocation));
            return new(ResourceExpression.Add(child, ResourceExpression.Add(direct, runtime)), null);
        }
        catch (ResourceExpressionLimitException)
        { return Unknown("TaskRuntimeCostExpressionBudgetExceeded"); }
    }

    private static NativeTaskRuntimeCostCheck Unknown(string reason)
    {
        reason = ArgumentNullGuard.NotNull(reason, nameof(reason));
        return new(null, reason);
    }
}
