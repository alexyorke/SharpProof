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
