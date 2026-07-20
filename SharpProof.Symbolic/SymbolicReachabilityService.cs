namespace SharpProof.Symbolic;

internal static class SymbolicReachabilityService {
    private const int StructuralPathStateCacheEntryLimit = 512;

    private static readonly ConditionalWeakTable<SemanticModel,
        ConditionalWeakTable<SyntaxNode, BoundedConcurrentCache<PathStateCacheKey, SymbolicState>>>
        s_structuralPathStateCache = new();

    internal static SymbolicState CollectPathStateAt(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SymbolicState? initialState = null,
        bool includeCurrentStatementCompletionFacts = false) {
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
        var cache = methodCaches.GetValue(executionRoot, static _ =>
            new BoundedConcurrentCache<PathStateCacheKey, SymbolicState>(StructuralPathStateCacheEntryLimit));
        if (!cache.TryGetValue(key, out var state)) {
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

    internal static bool IsForInitialEntryConditionAlwaysFalse(
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis) {
        if (forStatement.Condition == null) return false;

        var initialEntryState = CollectForInitialEntryState(
            forStatement,
            semanticModel,
            cancellationToken);
        var lowering = SymbolicSemanticPipeline.LowerCondition(
            forStatement.Condition,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is not { IsExact: true, Value: { } initialEntryCondition }) return false;

        var proof = new SymbolicProofService(smtAnalysis)
            .ClassifyConditionTruth(initialEntryState, initialEntryCondition);
        return proof.Status is SymbolicProofStatus.ProvenFalse or SymbolicProofStatus.Unreachable;
    }

    internal static SymbolicState CollectForInitialEntryState(
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
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

    private static SymbolicState BuildStructuralPathStateSnapshot(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SymbolicState? initialState,
        bool includeCurrentStatementCompletionFacts) {
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

    private readonly record struct PathStateCacheKey(
        int SiteStart, int SiteLength, int SiteRawKind, bool IncludeCurrentStatementCompletionFacts);
}
