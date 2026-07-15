using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Collections;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Engine;

internal static partial class ExecutionVisibility
{
    private static readonly ConditionalWeakTable<SemanticModel, ConditionTruthCache> s_conditionTruthCache = new();

    private static bool IsReferenceKnownNullAt(
        ExpressionSyntax expression,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis)
    {
        return IsReferenceKnownNullStateAt(
            expression,
            true,
            site,
            semanticModel,
            cancellationToken,
            smtAnalysis);
    }

    private static bool IsReferenceKnownNonNullAt(
        ExpressionSyntax expression,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis)
    {
        return IsReferenceKnownNullStateAt(
            expression,
            false,
            site,
            semanticModel,
            cancellationToken,
            smtAnalysis);
    }

    private static bool IsReferenceKnownNullStateAt(
        ExpressionSyntax expression,
        bool expectedNull,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis)
    {
        if (TryGetConstantReferenceNullState(expression, semanticModel, cancellationToken, out var isNull))
            return isNull == expectedNull;

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

        return IsSymbolicConditionAlwaysTrueAt(
            nullStateCondition,
            site,
            semanticModel,
            cancellationToken,
            smtAnalysis);
    }

    private static bool IsSymbolicConditionAlwaysFalseAt(
        SymbolicCondition condition,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis)
    {
        return HasSymbolicConditionStatusAt(
            condition,
            SymbolicProofStatus.ProvenFalse,
            site,
            semanticModel,
            cancellationToken,
            smtAnalysis);
    }

    private static bool IsSymbolicConditionAlwaysTrueAt(
        SymbolicCondition condition,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis)
    {
        return HasSymbolicConditionStatusAt(
            condition,
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
        SmtAnalysisService? smtAnalysis)
    {
        var pathState = SymbolicReachabilityService.CollectPathStateAt(
            site,
            semanticModel,
            cancellationToken);
        return SymbolicReachabilityService.ClassifyStateConditionTruth(pathState, condition, smtAnalysis).Info.Status ==
               expectedStatus;
    }

    private static bool TryGetConstantReferenceNullState(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out bool isNull)
    {
        var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
        if (constantValue.HasValue)
        {
            isNull = constantValue.Value == null;
            return true;
        }

        isNull = false;
        return false;
    }

    private static bool IsConditionAlwaysFalseAt(
        ExpressionSyntax expression,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis)
    {
        return IsConditionTruthAt(
            expression,
            false,
            site,
            semanticModel,
            cancellationToken,
            smtAnalysis);
    }

    private static bool IsConditionAlwaysTrueAt(
        ExpressionSyntax expression,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis)
    {
        return IsConditionTruthAt(
            expression,
            true,
            site,
            semanticModel,
            cancellationToken,
            smtAnalysis);
    }

    private static bool IsConditionTruthAt(
        ExpressionSyntax expression,
        bool expectedTruth,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis)
    {
        return EvaluateKnownConditionTruthAtSite(
            expression,
            site,
            semanticModel,
            cancellationToken,
            smtAnalysis) == expectedTruth;
    }

    private static bool? EvaluateKnownConditionTruthAtSite(
        ExpressionSyntax expression,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis)
    {
        var key = new ConditionTruthCacheKey(
            expression.SpanStart,
            expression.Span.Length,
            site.SpanStart,
            site.Span.Length,
            smtAnalysis);
        var cache = s_conditionTruthCache.GetOrCreateValue(semanticModel);
        if (cache.Values.TryGetValue(key, out var cached)) return cached;

        var truth = EvaluateKnownConditionTruth(
            expression,
            SymbolicReachabilityService.CollectPathStateAt(site, semanticModel, cancellationToken),
            semanticModel,
            cancellationToken,
            smtAnalysis);
        cache.Values.TryAdd(key, truth);
        return truth;
    }

    public static bool IsConditionAlwaysTrue(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return IsConditionAlwaysTrueUsingSmt(expression, semanticModel, cancellationToken, null);
    }

    public static bool IsConditionAlwaysTrueUsingSmt(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis = null)
    {
        return IsConditionTruthUsingSmt(
            expression,
            true,
            semanticModel,
            cancellationToken,
            smtAnalysis);
    }

    public static bool IsConditionAlwaysFalse(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return IsConditionAlwaysFalseUsingSmt(expression, semanticModel, cancellationToken, null);
    }

    public static bool IsConditionAlwaysFalseUsingSmt(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis = null)
    {
        return IsConditionTruthUsingSmt(
            expression,
            false,
            semanticModel,
            cancellationToken,
            smtAnalysis);
    }

    private static bool IsConditionTruthUsingSmt(
        ExpressionSyntax expression,
        bool expectedTruth,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis)
    {
        return EvaluateKnownConditionTruth(
            expression,
            new SymbolicState(),
            semanticModel,
            cancellationToken,
            smtAnalysis) == expectedTruth;
    }

    private static bool? EvaluateKnownConditionTruth(
        ExpressionSyntax expression,
        SymbolicState pathState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis)
    {
        var constant = semanticModel.GetConstantValue(expression, cancellationToken);
        if (constant is { HasValue: true, Value: bool constantValue }) return constantValue;

        var lowering = SymbolicSemanticPipeline.LowerCondition(
            expression,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        if (lowering is not { IsExact: true, Value: { } condition }) return null;

        return SymbolicReachabilityService.ClassifyStateConditionTruth(pathState, condition, smtAnalysis).Info.Status
            switch
            {
                SymbolicProofStatus.ProvenTrue => true,
                SymbolicProofStatus.ProvenFalse => false,
                _ => null
            };
    }


    private sealed class ConditionTruthCache
    {
        public BoundedConcurrentCache<ConditionTruthCacheKey, bool?> Values { get; } = new(512);
    }

    private readonly struct ConditionTruthCacheKey : IEquatable<ConditionTruthCacheKey>
    {
        public ConditionTruthCacheKey(
            int expressionStart,
            int expressionLength,
            int siteStart,
            int siteLength,
            SmtAnalysisService? smtAnalysis)
        {
            ExpressionStart = expressionStart;
            ExpressionLength = expressionLength;
            SiteStart = siteStart;
            SiteLength = siteLength;
            SmtAnalysis = smtAnalysis;
        }

        public int ExpressionStart { get; }
        public int ExpressionLength { get; }
        public int SiteStart { get; }
        public int SiteLength { get; }
        public SmtAnalysisService? SmtAnalysis { get; }

        public bool Equals(ConditionTruthCacheKey other)
        {
            return ExpressionStart == other.ExpressionStart &&
                   ExpressionLength == other.ExpressionLength &&
                   SiteStart == other.SiteStart &&
                   SiteLength == other.SiteLength &&
                   ReferenceEquals(SmtAnalysis, other.SmtAnalysis);
        }

        public override bool Equals(object? obj)
        {
            return obj is ConditionTruthCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ExpressionStart;
                hash = (hash * 397) ^ ExpressionLength;
                hash = (hash * 397) ^ SiteStart;
                hash = (hash * 397) ^ SiteLength;
                hash = (hash * 397) ^ RuntimeHelpers.GetHashCode(SmtAnalysis);
                return hash;
            }
        }
    }
}
