using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    internal static bool IsStrictPurityProfile => ImpurityCatalog.IsStrictPurityProfile;

    internal static bool IsKnownPureBCLMember(ISymbol symbol, Compilation? compilation)
    {
        return IsTriviallyPureObjectConstructor(symbol) ||
               ImpurityCatalog.IsKnownPureBCLMember(symbol, compilation);
    }

    private static bool IsTriviallyPureObjectConstructor(ISymbol symbol)
    {
        return symbol is IMethodSymbol methodSymbol &&
               methodSymbol.MethodKind == MethodKind.Constructor &&
               methodSymbol.Parameters.Length == 0 &&
               methodSymbol.ContainingType?.SpecialType == SpecialType.System_Object;
    }

    internal static bool IsKnownImpure(ISymbol symbol)
    {
        return ImpurityCatalog.IsKnownImpure(symbol);
    }

    internal static bool IsInImpureNamespaceOrType(ISymbol symbol)
    {
        return ImpurityCatalog.IsInImpureNamespaceOrType(symbol);
    }

    internal static bool IsInConfiguredImpureNamespaceOrType(ISymbol symbol)
    {
        return ImpurityCatalog.IsInConfiguredImpureNamespaceOrType(symbol);
    }

    internal static bool IsConfiguredKnownPureMember(ISymbol symbol)
    {
        return ImpurityCatalog.IsConfiguredKnownPureMember(symbol);
    }
}