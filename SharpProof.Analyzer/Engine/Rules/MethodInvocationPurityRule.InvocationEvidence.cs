using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class MethodInvocationPurityRule
{
    private static string GetCatalogHitCategory(ISymbol symbol)
    {
        return PurityCatalogSemantics.GetKnownImpureCatalogHitCategory(symbol, true);
    }

    private static bool IsContractGuardInvocation(IMethodSymbol methodSymbol)
    {
        return methodSymbol.ContainingType?.OriginalDefinition.ToDisplayString() ==
               "System.Diagnostics.Contracts.Contract" &&
               methodSymbol.Name is "Requires" or "Ensures";
    }
}
