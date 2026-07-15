using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;
using ExceptionCategories = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionCategories;
using ExceptionSources = SharpProof.Symbolic.SymbolicRuntimeExceptionFacts.ExceptionSources;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowQuery
{
    private readonly record struct ProvenRuntimeHazardSite(
        SymbolicRuntimeHazard Hazard,
        SyntaxNode Site,
        string Category,
        string Source,
        int FlowOrder,
        int InputOrder,
        bool BeforeCallees);

    private readonly record struct HazardSiteProjectionRule(
        string Source,
        int FlowOrder,
        string? Category = null,
        bool BeforeCallees = false);

    internal static ImmutableArray<SymbolicRuntimeHazard> CollectUnknownRuntimeHazardCandidates(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return QueryRuntimeHazards(methodNode, semanticModel, cancellationToken, smtAnalysis)
            .Where(static hazard =>
                hazard.Status is SymbolicRuntimeHazardStatus.Unknown or SymbolicRuntimeHazardStatus.Unsupported)
            .ToImmutableArray();
    }

    private static ImmutableArray<SymbolicRuntimeHazard> QueryRuntimeHazards(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return new SymbolicRuntimeHazardQueryService().QueryNodeRuntimeHazards(
            methodNode,
            semanticModel,
            smtAnalysis,
            cancellationToken,
            new SymbolicRuntimeHazardQueryOptions(includeUnprovenCandidates: true))
            .Hazards
            .ToImmutableArray();
    }

    private static ImmutableArray<ProvenRuntimeHazardSite> ProjectProvenRuntimeHazardSites(
        SyntaxNode methodNode,
        IEnumerable<SymbolicRuntimeHazard> hazards)
    {
        var builder = ImmutableArray.CreateBuilder<ProvenRuntimeHazardSite>();
        var inputOrder = 0;
        foreach (var hazard in hazards)
        {
            if (hazard.Status == SymbolicRuntimeHazardStatus.Proven &&
                TryProjectProvenRuntimeHazardSite(methodNode, hazard, inputOrder, out var site))
                builder.Add(site);
            inputOrder++;
        }

        return builder
            .OrderBy(static site => site.FlowOrder)
            .ThenBy(static site => site.InputOrder)
            .ToImmutableArray();
    }

    private static bool TryProjectProvenRuntimeHazardSite(
        SyntaxNode methodNode,
        SymbolicRuntimeHazard hazard,
        int inputOrder,
        out ProvenRuntimeHazardSite projected)
    {
        var site = ExceptionFlowAnalyzer.FindRuntimeHazardSiteNode(methodNode, hazard);
        var category = hazard.Category;
        string source;
        int flowOrder;
        var beforeCallees = false;

        if (GetSimpleHazardSiteProjectionRule(hazard.Kind) is { } simpleRule)
        {
            source = simpleRule.Source;
            flowOrder = simpleRule.FlowOrder;
            category = simpleRule.Category ?? category;
            beforeCallees = simpleRule.BeforeCallees;
        }
        else
        {
            switch (hazard.Kind)
            {
                case SymbolicRuntimeHazardKind.CheckedIntegralOverflow:
                    source = site is CastExpressionSyntax
                        ? ExceptionSources.CheckedConversion
                        : ExceptionSources.CheckedOperator;
                    category = ExceptionCategories.DefiniteCheckedIntegralOverflow;
                    flowOrder = 20;
                    break;
                case SymbolicRuntimeHazardKind.NullDereference when IsAnalyzerOnlySymbolicHazardCategory(category):
                    source = GetAnalyzerOnlySymbolicHazardSource(category);
                    flowOrder = 180;
                    break;
                case SymbolicRuntimeHazardKind.NullDereference when site is
                MemberAccessExpressionSyntax or ElementAccessExpressionSyntax or
                InvocationExpressionSyntax or AwaitExpressionSyntax:
                    source = site is AwaitExpressionSyntax
                        ? ExceptionSources.AwaitExpression
                        : ExceptionSources.NullReceiver;
                    category = site is AwaitExpressionSyntax
                        ? ExceptionCategories.DefiniteAwaitNull
                        : ExceptionCategories.DefiniteNullDereference;
                    flowOrder = 50;
                    break;
                case SymbolicRuntimeHazardKind.ArgumentNull
                when string.Equals(category, ExceptionCategories.DefiniteLockNull, StringComparison.Ordinal):
                    source = ExceptionSources.LockReceiver;
                    flowOrder = 60;
                    break;
                case SymbolicRuntimeHazardKind.DynamicNullBinding:
                    source = GetDynamicNullBindingHazardSource(category);
                    flowOrder = 70;
                    break;
                case SymbolicRuntimeHazardKind.IndexOutOfRange
                when string.Equals(
                    category,
                    ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange,
                    StringComparison.Ordinal):
                    source = ExceptionSources.ArrayGetValue;
                    flowOrder = 130;
                    break;
                case SymbolicRuntimeHazardKind.IndexOutOfRange
                when string.Equals(category, ExceptionCategories.DefiniteIndexOutOfRange, StringComparison.Ordinal):
                    source = ExceptionSources.ArrayIndex;
                    flowOrder = 120;
                    break;
                case SymbolicRuntimeHazardKind.ArgumentOutOfRange
                when string.Equals(category, ExceptionCategories.DefiniteRangeOutOfRange, StringComparison.Ordinal) ||
                     string.Equals(category, ExceptionCategories.DefiniteSliceOutOfRange, StringComparison.Ordinal):
                    source = site is InvocationExpressionSyntax
                        ? ExceptionSources.SpanSlice
                        : ExceptionSources.RangeSlice;
                    category = ExceptionCategories.DefiniteRangeOutOfRange;
                    flowOrder = 140;
                    break;
                case SymbolicRuntimeHazardKind.ArgumentOutOfRange
                when string.Equals(
                    category,
                    ExceptionCategories.DefiniteCountIndexOutOfRange,
                    StringComparison.Ordinal):
                    source = ExceptionSources.CountIndex;
                    flowOrder = 150;
                    break;
                case SymbolicRuntimeHazardKind.IndexOutOfRange when IsAnalyzerOnlySymbolicHazardCategory(category):
                    source = GetAnalyzerOnlySymbolicHazardSource(category);
                    flowOrder = 180;
                    break;
                default:
                    projected = default;
                    return false;
            }
        }

        projected = new ProvenRuntimeHazardSite(
            hazard,
            site,
            category,
            source,
            flowOrder,
            inputOrder,
            beforeCallees);
        return true;
    }

    private static HazardSiteProjectionRule? GetSimpleHazardSiteProjectionRule(
        SymbolicRuntimeHazardKind kind) =>
        kind switch
        {
            SymbolicRuntimeHazardKind.DirectThrow or SymbolicRuntimeHazardKind.Rethrow =>
                new(ExceptionSources.Throw, 0, BeforeCallees: true),
            SymbolicRuntimeHazardKind.DivideByZero =>
                new(ExceptionSources.BinaryOperator, 10, ExceptionCategories.DefiniteDivideByZero),
            SymbolicRuntimeHazardKind.NegativeArrayLength => new(ExceptionSources.ArrayLength, 30),
            SymbolicRuntimeHazardKind.NegativeStackAllocLength => new(ExceptionSources.StackAllocLength, 40),
            SymbolicRuntimeHazardKind.NullableValueWithoutValue => new(ExceptionSources.NullableValue, 80),
            SymbolicRuntimeHazardKind.UnboxNull => new(ExceptionSources.Cast, 90),
            SymbolicRuntimeHazardKind.InvalidCast => new(ExceptionSources.Cast, 100),
            SymbolicRuntimeHazardKind.ArrayTypeMismatch => new(ExceptionSources.ArrayStore, 110),
            SymbolicRuntimeHazardKind.SwitchExpressionNoMatch => new(ExceptionSources.SwitchExpression, 160),
            SymbolicRuntimeHazardKind.InvalidCollectionCardinality =>
                new(ExceptionSources.CollectionOperation, 170),
            _ => null
        };

    private static bool IsAnalyzerOnlySymbolicHazardCategory(string category)
    {
        return string.Equals(category, ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange,
                   StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteWithNull, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteDeconstructionNull, StringComparison.Ordinal);
    }

    private static string GetDynamicNullBindingHazardSource(string category)
    {
        if (string.Equals(category, SymbolicDynamicNullBindingFacts.MemberCategory, StringComparison.Ordinal))
            return SymbolicDynamicNullBindingFacts.MemberSource;
        if (string.Equals(category, SymbolicDynamicNullBindingFacts.IndexCategory, StringComparison.Ordinal))
            return SymbolicDynamicNullBindingFacts.IndexSource;
        return SymbolicDynamicNullBindingFacts.InvocationSource;
    }

    private static string GetAnalyzerOnlySymbolicHazardSource(string category)
    {
        if (string.Equals(category, ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange, StringComparison.Ordinal))
            return ExceptionSources.ArrayGetValue;

        if (string.Equals(category, ExceptionCategories.DefiniteWithNull, StringComparison.Ordinal))
            return ExceptionSources.WithExpression;

        if (string.Equals(category, ExceptionCategories.DefiniteDeconstructionNull, StringComparison.Ordinal))
            return ExceptionSources.DeconstructionReceiver;

        return ExceptionSources.NullReceiver;
    }

}
