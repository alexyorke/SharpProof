namespace SharpProof.Analyzer.Engine.Analysis;

internal enum TypeIdentityPolicy {
    Exact,
    ExactOrOriginalDefinition
}

internal static class TypeHierarchyEnumeration {
    internal static bool IsSameOrOverridesTargetMethod(IMethodSymbol method, IMethodSymbol target) {
        for (var current = method; current != null; current = current.OverriddenMethod)
            if (SymbolEq.AreEqual(current.OriginalDefinition, target.OriginalDefinition))
                return true;

        return false;
    }

    internal static IEnumerable<INamedTypeSymbol> EnumerateBaseTypes(
        ITypeSymbol type,
        bool includeSelf = true) {
        var namedType = type as INamedTypeSymbol;
        for (var current = includeSelf ? namedType : namedType?.BaseType;
             current != null;
             current = current.BaseType)
            yield return current;
    }

    internal static bool IsSameOrDerivedFrom(
        ITypeSymbol candidate,
        ITypeSymbol expectedBase,
        TypeIdentityPolicy identityPolicy = TypeIdentityPolicy.Exact) {
        foreach (var current in EnumerateBaseTypes(candidate)) {
            if (SymbolEq.AreEqual(current, expectedBase)) return true;
            if (identityPolicy == TypeIdentityPolicy.ExactOrOriginalDefinition &&
                SymbolEq.AreEqual(current.OriginalDefinition, expectedBase.OriginalDefinition))
                return true;
        }

        return false;
    }

}
