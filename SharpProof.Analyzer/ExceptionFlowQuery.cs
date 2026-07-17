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
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        var visitedMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default)
        {
            methodSymbol.OriginalDefinition
        };

        using (ExceptionFlowAnalyzer.UseAttributePolicy(attributePolicy))
        {
            return AnalyzeMethod(
                methodNode,
                semanticModel,
                cancellationToken,
                methodSymbol,
                exceptionSummaryCatalog,
                visitedMethods,
                smtAnalysis,
                attributePolicy);
        }
    }

    private static ExceptionFlowResult AnalyzeMethod(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        IMethodSymbol methodSymbol,
        ExceptionSummaryCatalog exceptionSummaryCatalog,
        HashSet<IMethodSymbol> visitedMethods,
        SmtAnalysisService smtAnalysis,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        var runtimeHazards = QueryRuntimeHazards(
            methodNode,
            semanticModel,
            cancellationToken,
            smtAnalysis);
        var siteEntries = CollectUncaughtExceptionSiteEntries(
                methodNode,
                semanticModel,
                cancellationToken,
                methodSymbol,
                exceptionSummaryCatalog,
                visitedMethods,
                smtAnalysis,
                attributePolicy,
                runtimeHazards)
            .ToImmutableArray();

        return new ExceptionFlowResult(siteEntries, runtimeHazards);
    }
}
