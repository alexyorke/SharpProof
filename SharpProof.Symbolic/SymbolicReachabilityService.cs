namespace SharpProof.Symbolic;

internal static class SymbolicReachabilityService
{
    private const int StructuralPathStateCacheEntryLimit = 512;

    private static readonly ConditionalWeakTable<SemanticModel, StructuralPathStateCaches>
        s_structuralPathStateCache = new();

    internal static SymbolicState CollectPathStateAt(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SymbolicState? initialState = null,
        bool includeCurrentStatementCompletionFacts = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (initialState != null ||
            !SymbolicCfgProgramPointStateCollector.UsesDefaultAnalysisLimits(SymbolicAnalysisLimitContext.Limits))
            return BuildStructuralPathStateSnapshot(
                site,
                semanticModel,
                cancellationToken,
                initialState,
                includeCurrentStatementCompletionFacts);

        var key = new PathStateCacheKey(
            site.SpanStart, site.Span.Length, site.RawKind, includeCurrentStatementCompletionFacts);
        var methodCaches = s_structuralPathStateCache.GetOrCreateValue(semanticModel);
        var executionRoot = CSharpSyntaxFacts.GetContainingExecutionRoot(site);
        var cache = methodCaches.ByExecutionRoot.GetValue(executionRoot, static _ =>
            new BoundedConcurrentCache<PathStateCacheKey, SymbolicState>(StructuralPathStateCacheEntryLimit));
        if (!cache.TryGetValue(key, out var state))
        {
            state = BuildStructuralPathStateSnapshot(
                site,
                semanticModel,
                cancellationToken,
                null,
                includeCurrentStatementCompletionFacts);
            cache.TryAdd(key, state);
        }

        return state;
    }

    internal static SymbolicIrProofResult ClassifyStateFeasibility(
        SymbolicState state,
        SmtAnalysisService? smtAnalysis)
    {
        return new SymbolicProofService(smtAnalysis).ClassifyReachability(state);
    }


    internal static SymbolicIrProofResult ClassifyStateBranchFeasibility(
        SymbolicState state,
        SymbolicCondition branchCondition,
        SmtAnalysisService? smtAnalysis)
    {
        return new SymbolicProofService(smtAnalysis).ClassifyBranchFeasibility(state, branchCondition);
    }

    internal static SymbolicIrProofResult ClassifyStateConditionTruth(
        SymbolicState state,
        SymbolicCondition condition,
        SmtAnalysisService? smtAnalysis)
    {
        return new SymbolicProofService(smtAnalysis).ClassifyConditionTruth(state, condition);
    }

    internal static SymbolicIrProofResult ClassifyStateHazardTrigger(
        SymbolicState state,
        SymbolicFact triggerPrecondition,
        SmtAnalysisService? smtAnalysis)
    {
        return new SymbolicProofService(smtAnalysis).ClassifyHazardTrigger(state, triggerPrecondition);
    }

    internal static bool IsForInitialEntryConditionAlwaysFalse(
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis)
    {
        if (forStatement.Condition == null) return false;

        var initialEntryState = CollectForInitialEntryState(
            forStatement,
            semanticModel,
            cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerCondition(
            forStatement.Condition,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is not { IsExact: true, Value: { } initialEntryCondition }) return false;

        var proof = ClassifyStateConditionTruth(initialEntryState, initialEntryCondition, smtAnalysis);
        return proof.Info.Status is SymbolicProofStatus.ProvenFalse or SymbolicProofStatus.Unreachable;
    }

    internal static SymbolicState CollectForInitialEntryState(
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var cfgState = SymbolicCfgProgramPointStateCollector.CollectForInitialEntryState(
            forStatement,
            semanticModel,
            cancellationToken);
        return cfgState is { IsExact: true, Value: { } exactState }
            ? exactState
            : SymbolicProgramPointFacts.CollectForInitialEntryState(
                forStatement,
                semanticModel,
                cancellationToken);
    }

    internal static SymbolicCacheInfo GetStructuralPathCacheInfo(
        SyntaxNode site,
        SemanticModel semanticModel)
    {
        if (!s_structuralPathStateCache.TryGetValue(semanticModel, out var methodCaches))
            return new SymbolicCacheInfo(0, 0, 0, 0);

        var executionRoot = CSharpSyntaxFacts.GetContainingExecutionRoot(site);
        if (!methodCaches.ByExecutionRoot.TryGetValue(executionRoot, out var cache))
            return new SymbolicCacheInfo(0, 0, 0, 0);

        return new SymbolicCacheInfo(
            cache.HitCount,
            cache.MissCount,
            cache.Count,
            cache.EvictionCount);
    }

    private static SymbolicState BuildStructuralPathStateSnapshot(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SymbolicState? initialState,
        bool includeCurrentStatementCompletionFacts)
    {
        if (initialState == null &&
            !includeCurrentStatementCompletionFacts &&
            TryCollectCachedExecutionTraceState(
                site,
                semanticModel,
                cancellationToken,
                out var tracedState))
            return tracedState;

        var cfgState = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            semanticModel,
            cancellationToken,
            initialState,
            includeCurrentStatementCompletionFacts);
        if (cfgState is { IsExact: true, Value: { } exactState })
            return exactState;

        var state = SymbolicProgramPointFacts.CollectAncestorReachabilityState(
            site,
            semanticModel,
            cancellationToken);
        return SymbolicProgramPointFacts.MergeStates(
            state,
            SymbolicProgramPointFacts.CollectPriorAssignmentState(
                site,
                semanticModel,
                cancellationToken,
                includeCurrentStatementCompletionFacts,
                initialState));
    }

    private static bool TryCollectCachedExecutionTraceState(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SymbolicState state)
    {
        state = null!;
        var result = SymbolicCfgExecutionTrace.CollectCachedStateFromExecutionTrace(
            site,
            semanticModel,
            cancellationToken);
        if (result is not { IsExact: true, Value: { } exactState })
            return false;
        state = exactState;
        return true;
    }

    private sealed class StructuralPathStateCaches
    {
        internal ConditionalWeakTable<SyntaxNode, BoundedConcurrentCache<PathStateCacheKey, SymbolicState>>
            ByExecutionRoot { get; } = new();
    }

    private readonly record struct PathStateCacheKey(
        int SiteStart, int SiteLength, int SiteRawKind, bool IncludeCurrentStatementCompletionFacts);
}
