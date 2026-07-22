using SharpProof.ProofCore.Collections;

namespace SharpProof.Analyzer.Engine;

internal static partial class ExecutionVisibility {
    private static readonly ConditionalWeakTable<SemanticModel,
        BoundedConcurrentCache<ConditionTruthCacheKey, bool?>> s_conditionTruthCache = new();

    private static bool IsReferenceKnownNullStateAt(
        ExpressionSyntax expression,
        bool expectedNull,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis) {
        var constant = semanticModel.GetConstantValue(expression, cancellationToken);
        if (constant.HasValue) return (constant.Value == null) == expectedNull;

        if (!SymbolicStateFactBuilder.TryCreateReferenceNullCondition(
                expression,
                expectedNull,
                semanticModel,
                cancellationToken,
                expectedNull
                    ? "analyzer.visibility.reference-null"
                    : "analyzer.visibility.reference-non-null",
                out var nullStateCondition))
            return false;

        return HasSymbolicConditionStatusAt(
            nullStateCondition,
            SymbolicProofStatus.ProvenTrue,
            site,
            semanticModel,
            cancellationToken,
            smtAnalysis);
    }
    private static bool HasSymbolicConditionStatusAt(
        SymbolicCondition condition,
        SymbolicProofStatus expectedStatus,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis) {
        var pathState = SymbolicReachabilityService.CollectPathStateAt(site, semanticModel, cancellationToken);
        return new SymbolicProofService(smtAnalysis).ClassifyConditionTruth(pathState, condition).Status ==
               expectedStatus;
    }
    private static bool IsConditionTruthAt(
        ExpressionSyntax expression,
        bool expectedTruth,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis) =>
        EvaluateKnownConditionTruthAtSite(expression, site, semanticModel, cancellationToken, smtAnalysis) == expectedTruth;

    private static bool? EvaluateKnownConditionTruthAtSite(
        ExpressionSyntax expression,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis) {
        var key = new ConditionTruthCacheKey(expression.SpanStart, expression.Span.Length, site.SpanStart, site.Span.Length, smtAnalysis);
        var cache = s_conditionTruthCache.GetValue(semanticModel, static _ => new(512));
        if (cache.TryGetValue(key, out var cached)) return cached;

        var truth = EvaluateKnownConditionTruth(
            expression,
            SymbolicReachabilityService.CollectPathStateAt(site, semanticModel, cancellationToken),
            semanticModel,
            cancellationToken,
            smtAnalysis);
        cache.TryAdd(key, truth);
        return truth;
    }
    public static bool IsConditionAlwaysTrueUsingSmt(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis = null) =>
        IsConditionTruthUsingSmt(expression, true, semanticModel, cancellationToken, smtAnalysis);

    public static bool IsConditionAlwaysFalseUsingSmt(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis = null) =>
        IsConditionTruthUsingSmt(expression, false, semanticModel, cancellationToken, smtAnalysis);

    private static bool IsConditionTruthUsingSmt(
        ExpressionSyntax expression,
        bool expectedTruth,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis) =>
        EvaluateKnownConditionTruth(expression, new SymbolicState(), semanticModel, cancellationToken, smtAnalysis) == expectedTruth;

    private static bool? EvaluateKnownConditionTruth(
        ExpressionSyntax expression,
        SymbolicState pathState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis) {
        var constant = semanticModel.GetConstantValue(expression, cancellationToken);
        if (constant is { HasValue: true, Value: bool constantValue }) return constantValue;

        var lowering = SymbolicSemanticPipeline.LowerCondition(expression, new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is not { IsExact: true, Value: { } condition }) return null;

        return new SymbolicProofService(smtAnalysis).ClassifyConditionTruth(pathState, condition).Status
            switch {
                SymbolicProofStatus.ProvenTrue => true,
                SymbolicProofStatus.ProvenFalse => false,
                _ => null
            };
    }

    private readonly record struct ConditionTruthCacheKey(
        int ExpressionStart,
        int ExpressionLength,
        int SiteStart,
        int SiteLength,
        SmtAnalysisService? SmtAnalysis);
}
