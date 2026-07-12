using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

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
        if (initialState != null)
            return BuildStructuralPathStateSnapshot(
                site,
                semanticModel,
                cancellationToken,
                initialState,
                includeCurrentStatementCompletionFacts);

        var key = new PathStateCacheKey(
            site.SpanStart,
            site.Span.Length,
            site.RawKind,
            includeCurrentStatementCompletionFacts);
        var methodCaches = s_structuralPathStateCache.GetOrCreateValue(semanticModel);
        var executionRoot = CSharpSyntaxFacts.GetContainingExecutionRoot(site);
        var cache = methodCaches.ByExecutionRoot.GetValue(
            executionRoot,
            static _ => new StructuralPathStateCache(StructuralPathStateCacheEntryLimit));
        if (!cache.Values.TryGetValue(key, out var state))
        {
            state = BuildStructuralPathStateSnapshot(
                site,
                semanticModel,
                cancellationToken,
                null,
                includeCurrentStatementCompletionFacts);
            cache.Values.TryAdd(key, state);
        }

        return state;
    }

    internal static SymbolicState MergePathStates(SymbolicState left, SymbolicState right)
    {
        return SymbolicProgramPointFacts.MergeStates(left, right);
    }

    internal static SymbolicIrProofResult ClassifyStateFeasibility(
        SymbolicState state,
        SmtAnalysisService? smtAnalysis)
    {
        return new SymbolicProofService(smtAnalysis).ClassifyReachability(state);
    }

    internal static SymbolicIrProofResult ClassifyStateImplication(
        SymbolicState state,
        SymbolicFact fact,
        SmtAnalysisService? smtAnalysis)
    {
        return new SymbolicProofService(smtAnalysis).ClassifyImplication(state, fact);
    }

    internal static SymbolicIrProofResult ClassifyStateImplication(
        SymbolicState state,
        SymbolicCondition condition,
        SmtAnalysisService? smtAnalysis)
    {
        return new SymbolicProofService(smtAnalysis).ClassifyImplication(state, condition);
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

    internal static SymbolicLoweringResult<SymbolicState> ApplyBranchFacts(
        SymbolicState state,
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        Func<ISymbol, int>? getSymbolVersion = null)
    {
        var lowering = SymbolicSemanticPipeline.LowerBranchFacts(
            condition,
            branchWhenTrue,
            new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion));
        if (lowering is not { IsExact: true, Value: { } branchFacts })
            return lowering;

        var branchState = state;
        foreach (var fact in branchFacts.Facts) branchState = branchState.AddFact(fact);
        foreach (var pathCondition in branchFacts.PathConditions)
            branchState = branchState.AddPathCondition(pathCondition);

        return SymbolicLoweringResult<SymbolicState>.Exact(branchState, lowering.Provenance[0]);
    }

    internal static bool IsForInitialEntryConditionAlwaysFalse(
        ForStatementSyntax forStatement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis)
    {
        if (forStatement.Condition == null) return false;

        var initialEntryState = SymbolicProgramPointFacts.CollectForInitialEntryState(
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
            cache.Values.HitCount,
            cache.Values.MissCount,
            cache.Values.Count,
            cache.Values.EvictionCount);
    }

    private static SymbolicState BuildStructuralPathStateSnapshot(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SymbolicState? initialState,
        bool includeCurrentStatementCompletionFacts)
    {
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

    private sealed class StructuralPathStateCaches
    {
        internal ConditionalWeakTable<SyntaxNode, StructuralPathStateCache> ByExecutionRoot { get; } = new();
    }

    private sealed class StructuralPathStateCache
    {
        internal StructuralPathStateCache(int capacity)
        {
            Values = new BoundedConcurrentCache<PathStateCacheKey, SymbolicState>(capacity);
        }

        internal BoundedConcurrentCache<PathStateCacheKey, SymbolicState> Values { get; }
    }

    private readonly struct PathStateCacheKey : IEquatable<PathStateCacheKey>
    {
        internal PathStateCacheKey(
            int siteStart,
            int siteLength,
            int siteRawKind,
            bool includeCurrentStatementCompletionFacts)
        {
            SiteStart = siteStart;
            SiteLength = siteLength;
            SiteRawKind = siteRawKind;
            IncludeCurrentStatementCompletionFacts = includeCurrentStatementCompletionFacts;
        }

        private int SiteStart { get; }
        private int SiteLength { get; }
        private int SiteRawKind { get; }
        private bool IncludeCurrentStatementCompletionFacts { get; }

        public bool Equals(PathStateCacheKey other)
        {
            return SiteStart == other.SiteStart &&
                   SiteLength == other.SiteLength &&
                   SiteRawKind == other.SiteRawKind &&
                   IncludeCurrentStatementCompletionFacts == other.IncludeCurrentStatementCompletionFacts;
        }

        public override bool Equals(object? obj)
        {
            return obj is PathStateCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = SiteStart;
                hash = (hash * 397) ^ SiteLength;
                hash = (hash * 397) ^ SiteRawKind;
                hash = (hash * 397) ^ (IncludeCurrentStatementCompletionFacts ? 1 : 0);
                return hash;
            }
        }
    }
}
