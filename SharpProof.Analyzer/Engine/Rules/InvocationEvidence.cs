namespace SharpProof.Analyzer.Engine.Rules;

internal static class InvocationEvidence {
    internal static string GetCatalogHitCategory(ISymbol symbol) =>
        ImpurityCatalog.GetKnownImpureCatalogHitCategory(symbol, true);

    internal static bool IsContractGuardInvocation(IMethodSymbol methodSymbol) => methodSymbol.ContainingType?.OriginalDefinition.ToDisplayString() ==
               "System.Diagnostics.Contracts.Contract" &&
               methodSymbol.Name is "Requires" or "Ensures";
}
