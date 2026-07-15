using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowQuery
{
    private static readonly SymbolDisplayFormat ExceptionTypeDisplayFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    internal static MethodExceptionQueryResult AnalyzeMethod(
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

    private static MethodExceptionQueryResult AnalyzeMethod(
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

        var exceptionEvidence = new ExceptionEvidenceSet();
        foreach (var siteEntry in siteEntries) exceptionEvidence.Add(siteEntry.Exception);

        return new MethodExceptionQueryResult(exceptionEvidence, siteEntries, runtimeHazards);
    }
}
