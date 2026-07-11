using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;
using SharpProof.Symbolic;
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
        if (TryGetConstantReferenceNullState(expression, semanticModel, cancellationToken, out var isNull))
            return isNull;

        if (!SymbolicReachabilityService.TryCreateReferenceNullComparison(
                expression,
                semanticModel,
                cancellationToken,
                true,
                out var nullFormula))
            return false;

        return IsFormulaAlwaysTrueAt(
            nullFormula,
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
        if (TryGetConstantReferenceNullState(expression, semanticModel, cancellationToken, out var isNull))
            return !isNull;

        if (!SymbolicReachabilityService.TryCreateReferenceNullComparison(
                expression,
                semanticModel,
                cancellationToken,
                false,
                out var nonNullFormula))
            return false;

        return IsFormulaAlwaysTrueAt(
            nonNullFormula,
            site,
            semanticModel,
            cancellationToken,
            smtAnalysis);
    }

    private static bool IsFormulaAlwaysFalseAt(
        SmtFormula formula,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis)
    {
        var pathConditions =
            SymbolicReachabilityService.CollectPathConditionsAt(site, semanticModel, cancellationToken);
        return SymbolicReachabilityService.IsFormulaAlwaysFalse(
            formula,
            pathConditions,
            smtAnalysis);
    }

    private static bool IsFormulaAlwaysTrueAt(
        SmtFormula formula,
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis)
    {
        var pathConditions =
            SymbolicReachabilityService.CollectPathConditionsAt(site, semanticModel, cancellationToken);
        return SymbolicReachabilityService.IsFormulaAlwaysTrue(
            formula,
            pathConditions,
            smtAnalysis);
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
        return EvaluateKnownConditionTruthAtSite(
            expression,
            site,
            semanticModel,
            cancellationToken,
            smtAnalysis) == false;
    }

    private static bool IsConditionAlwaysTrueAt(
        ExpressionSyntax expression,
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
            smtAnalysis) == true;
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

        var truth = SymbolicReachabilityService.EvaluateKnownConditionTruth(
            expression,
            semanticModel,
            cancellationToken,
            smtAnalysis,
            SymbolicReachabilityService.CollectPathConditionsAt(site, semanticModel, cancellationToken));
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
        return SymbolicReachabilityService.EvaluateKnownConditionTruth(
            expression,
            semanticModel,
            cancellationToken,
            smtAnalysis) == true;
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
        return SymbolicReachabilityService.EvaluateKnownConditionTruth(
            expression,
            semanticModel,
            cancellationToken,
            smtAnalysis) == false;
    }


    private sealed class ConditionTruthCache
    {
        public ConcurrentDictionary<ConditionTruthCacheKey, bool?> Values { get; } = new();
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
