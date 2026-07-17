using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowEngine
{
    internal static ExceptionFlowResult AnalyzeMethod(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        IMethodSymbol methodSymbol,
        ExceptionSummaryCatalog exceptionSummaryCatalog,
        SmtAnalysisService smtAnalysis,
        SharpProofAttributeIdentityPolicy attributePolicy,
        HashSet<IMethodSymbol>? visitedMethods = null)
    {
        var isRoot = visitedMethods == null;
        visitedMethods ??= new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)
        {
            methodSymbol.OriginalDefinition
        };
        using var attributeScope = isRoot ? ExceptionFlowAnalyzer.UseAttributePolicy(attributePolicy) : null;
        var runtimeHazards = QueryRuntimeHazards(
            methodNode,
            semanticModel,
            cancellationToken,
            smtAnalysis);
        return new ExceptionFlowResult(
            CollectUncaughtExceptionSiteEntries(
                methodNode,
                semanticModel,
                cancellationToken,
                methodSymbol,
                exceptionSummaryCatalog,
                visitedMethods,
                smtAnalysis,
                attributePolicy,
                runtimeHazards)
            .ToImmutableArray(),
            runtimeHazards);
    }
}
