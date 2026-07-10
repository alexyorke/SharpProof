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
    internal static ImmutableArray<SymbolicRuntimeHazard> CollectUnknownRuntimeHazardCandidates(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        var result = new SymbolicRuntimeHazardQueryService().QueryNodeRuntimeHazards(
            methodNode,
            semanticModel,
            smtAnalysis,
            cancellationToken,
            new SymbolicRuntimeHazardQueryOptions(includeUnprovenCandidates: true));
        return result.Hazards
            .Where(static hazard =>
                hazard.Status is SymbolicRuntimeHazardStatus.Unknown or SymbolicRuntimeHazardStatus.Unsupported)
            .ToImmutableArray();
    }

    private static IEnumerable<SymbolicRuntimeHazard> CollectProvenNegativeStackAllocLengthHazards(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return CollectProvenRuntimeHazards(
            methodNode,
            semanticModel,
            cancellationToken,
            smtAnalysis,
            SymbolicRuntimeHazardKind.NegativeStackAllocLength);
    }

    private static IEnumerable<SymbolicRuntimeHazard> CollectProvenInvocationCheckedIntegralOverflowHazards(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return CollectProvenRuntimeHazards(
                methodNode,
                semanticModel,
                cancellationToken,
                smtAnalysis,
                SymbolicRuntimeHazardKind.CheckedIntegralOverflow)
            .Where(hazard => FindHazardSiteNode(methodNode, hazard) is InvocationExpressionSyntax);
    }

    private static IEnumerable<SymbolicRuntimeHazard> CollectProvenCountIndexOutOfRangeHazards(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return CollectProvenRuntimeHazards(
                methodNode,
                semanticModel,
                cancellationToken,
                smtAnalysis,
                SymbolicRuntimeHazardKind.ArgumentOutOfRange)
            .Where(static hazard =>
                string.Equals(
                    hazard.Category,
                    ExceptionCategories.DefiniteCountIndexOutOfRange,
                    StringComparison.Ordinal));
    }

    private static IEnumerable<SymbolicRuntimeHazard> CollectProvenSwitchExpressionNoMatchHazards(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return CollectProvenRuntimeHazards(
            methodNode,
            semanticModel,
            cancellationToken,
            smtAnalysis,
            SymbolicRuntimeHazardKind.SwitchExpressionNoMatch);
    }

    private static IEnumerable<SymbolicRuntimeHazard> CollectProvenInvalidCollectionCardinalityHazards(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return CollectProvenRuntimeHazards(
            methodNode,
            semanticModel,
            cancellationToken,
            smtAnalysis,
            SymbolicRuntimeHazardKind.InvalidCollectionCardinality);
    }

    private static IEnumerable<SymbolicRuntimeHazard> CollectProvenAnalyzerOnlySymbolicHazards(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return CollectProvenRuntimeHazards(
                methodNode,
                semanticModel,
                cancellationToken,
                smtAnalysis,
                SymbolicRuntimeHazardKind.IndexOutOfRange,
                SymbolicRuntimeHazardKind.NullDereference)
            .Where(static hazard => IsAnalyzerOnlySymbolicHazardCategory(hazard.Category));
    }

    private static IEnumerable<SymbolicRuntimeHazard> CollectProvenRuntimeHazards(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis,
        params SymbolicRuntimeHazardKind[] kinds)
    {
        var result = new SymbolicRuntimeHazardQueryService().QueryNodeRuntimeHazards(
            methodNode,
            semanticModel,
            smtAnalysis,
            cancellationToken,
            new SymbolicRuntimeHazardQueryOptions(kinds: kinds));

        return result.Hazards.Where(hazard =>
            kinds.Contains(hazard.Kind) &&
            hazard.Status == SymbolicRuntimeHazardStatus.Proven);
    }

    private static bool IsAnalyzerOnlySymbolicHazardCategory(string category)
    {
        return string.Equals(category, ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange,
                   StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteWithNull, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteDeconstructionNull, StringComparison.Ordinal);
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

    private static SyntaxNode FindHazardSiteNode(SyntaxNode methodNode, SymbolicRuntimeHazard hazard)
    {
        return methodNode.DescendantNodesAndSelf()
                   .FirstOrDefault(node => node.Span.Start == hazard.SpanStart && node.Span.End == hazard.SpanEnd)
               ?? methodNode;
    }
}
