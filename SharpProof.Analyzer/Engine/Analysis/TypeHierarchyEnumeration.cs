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
    internal static bool IsSameOrDerivedFrom(
        ITypeSymbol candidate,
        ITypeSymbol expectedBase,
        TypeIdentityPolicy identityPolicy = TypeIdentityPolicy.Exact) {
        for (var current = candidate as INamedTypeSymbol; current != null; current = current.BaseType) {
            if (SymbolEq.AreEqual(current, expectedBase)) return true;
            if (identityPolicy == TypeIdentityPolicy.ExactOrOriginalDefinition &&
                SymbolEq.AreEqual(current.OriginalDefinition, expectedBase.OriginalDefinition))
                return true;
        }
        return false;
    }
}
