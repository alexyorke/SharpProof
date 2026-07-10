using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class MethodInvocationPurityRule
{
    private static string GetCatalogHitCategory(ISymbol symbol)
    {
        return PurityAnalysisEngine.GetKnownImpureCatalogHitCategory(symbol, true);
    }

    private static bool IsContractGuardInvocation(IMethodSymbol methodSymbol)
    {
        return methodSymbol.ContainingType?.OriginalDefinition.ToDisplayString() ==
               "System.Diagnostics.Contracts.Contract" &&
               methodSymbol.Name is "Requires" or "Ensures";
    }

    private static bool ShouldPreferSemanticImpurityEvidence(string? knownImpureMemberSource)
    {
        return knownImpureMemberSource is
            "array_mutation_semantic_rule" or
            "random_semantic_rule" or
            "string_builder_semantic_rule" or
            "threading_semantic_rule";
    }
}