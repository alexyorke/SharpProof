namespace SharpProof.Symbolic;
internal static class SymbolicReachabilityService {
    private const int StructuralPathStateCacheEntryLimit = 512;
    private sealed class StructuralPathStateCaches {
        internal ConditionalWeakTable<SemanticModel,
            ConditionalWeakTable<SyntaxNode,
                BoundedConcurrentCache<PathStateCacheKey, SymbolicState>>> Models { get; } = new();
    }
    private static readonly StructuralPathStateCaches s_defaultStructuralPathStateCaches = new();
    private static readonly ConditionalWeakTable<SmtAnalysisService, StructuralPathStateCaches>
        s_serviceStructuralPathStateCaches = new();
    internal static SymbolicState CollectPathStateAt(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis = null,
        SymbolicState? initialState = null,
        bool includeCurrentStatementCompletionFacts = false) {
        cancellationToken.ThrowIfCancellationRequested();
        if (initialState != null)
            return BuildStructuralPathStateSnapshot(
                site,
                semanticModel,
                cancellationToken,
                smtAnalysis,
                initialState,
                includeCurrentStatementCompletionFacts);
        var key = new PathStateCacheKey(site.SpanStart, site.Span.Length, site.RawKind, includeCurrentStatementCompletionFacts);
        var structuralCaches = smtAnalysis == null
            ? s_defaultStructuralPathStateCaches
            : s_serviceStructuralPathStateCaches.GetOrCreateValue(smtAnalysis);
        var methodCaches = structuralCaches.Models.GetOrCreateValue(semanticModel);
        var executionRoot = CSharpSyntaxFacts.GetContainingExecutionRoot(site);
        var cache = methodCaches.GetValue(executionRoot, static _ =>
            new BoundedConcurrentCache<PathStateCacheKey, SymbolicState>(StructuralPathStateCacheEntryLimit));
        if (!cache.TryGetValue(key, out var state)) {
            state = BuildStructuralPathStateSnapshot(
                site,
                semanticModel,
                cancellationToken,
                smtAnalysis,
                null,
                includeCurrentStatementCompletionFacts);
            cache.TryAdd(key, state);
        }
        return state;
    }
    internal static SymbolicState CollectForInitialEntryState(
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis = null) {
        var cfgState = CompilerProgramPointAnalysis.Collect(
            forStatement,
            semanticModel,
            cancellationToken,
            forInitialEntry: true,
            smtAnalysis: smtAnalysis);
        return cfgState is { IsExact: true, Value: { } exactState }
            ? exactState
            : UnsupportedState(cfgState);
    }
    private static SymbolicState BuildStructuralPathStateSnapshot(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis,
        SymbolicState? initialState,
        bool includeCurrentStatementCompletionFacts) {
        if (!includeCurrentStatementCompletionFacts)
            site = GetNextExecutableSite(site);
        var cfgState = CompilerProgramPointAnalysis.Collect(
            site,
            semanticModel,
            cancellationToken,
            initialState,
            includeCurrentStatementCompletionFacts,
            smtAnalysis: smtAnalysis);
        if (cfgState is { IsExact: true, Value: { } exactState })
            return exactState;
        return UnsupportedState(cfgState);
    }
    private static SyntaxNode GetNextExecutableSite(SyntaxNode site) {
        while (site is LocalFunctionStatementSyntax localFunction &&
               localFunction.Parent is BlockSyntax block) {
            var index = block.Statements.IndexOf(localFunction);
            if (index < 0 || index + 1 >= block.Statements.Count) break;
            site = block.Statements[index + 1];
        }
        return site;
    }
    private static SymbolicState UnsupportedState(SymbolicLoweringResult<SymbolicState> result) => new(
        isExact: result.IsExact,
        unknownReason: result.UnknownReason == SymbolicUnknownReason.None
            ? SymbolicUnknownReason.UnsupportedIrEncoding
            : result.UnknownReason,
        provenance: result.Provenance);
    readonly record struct PathStateCacheKey(int SiteStart, int SiteLength, int SiteRawKind, bool IncludeCurrentStatementCompletionFacts);
}
