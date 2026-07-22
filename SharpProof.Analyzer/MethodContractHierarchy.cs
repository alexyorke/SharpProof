namespace SharpProof.Analyzer;

internal static class MethodContractHierarchy {
    internal static IEnumerable<IMethodSymbol> EnumerateSources(IMethodSymbol method, CancellationToken cancellationToken) {
        if (method == null) throw new ArgumentNullException(nameof(method));

        var seen = new HashSet<IMethodSymbol>(SymbolEq.Default);
        for (var current = method; current != null; current = current.OverriddenMethod) {
            cancellationToken.ThrowIfCancellationRequested();
            if (seen.Add(current)) yield return current;
        }
        foreach (var implemented in method.ExplicitInterfaceImplementations)
            if (seen.Add(implemented))
                yield return implemented;

        var containingType = method.ContainingType;
        if (containingType == null) yield break;

        foreach (var interfaceType in containingType.AllInterfaces) {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var interfaceMember in interfaceType.GetMembers()) {
                cancellationToken.ThrowIfCancellationRequested();
                switch (interfaceMember) {
                    case IMethodSymbol interfaceMethod
                        when Implements(containingType, method, interfaceMethod) && seen.Add(interfaceMethod):
                        yield return interfaceMethod;
                        break;

                    case IPropertySymbol interfaceProperty
                        when containingType.FindImplementationForInterfaceMember(interfaceProperty) is
                            IPropertySymbol implementationProperty: {
                            var interfaceAccessor = SelectMatchingAccessor(method, implementationProperty, interfaceProperty);
                            if (interfaceAccessor != null && seen.Add(interfaceAccessor)) yield return interfaceAccessor;
                            break;
                        }
                }
            }
        }
    }
    private static bool Implements(INamedTypeSymbol containingType, IMethodSymbol method, IMethodSymbol interfaceMethod)
        => containingType.FindImplementationForInterfaceMember(interfaceMethod) is IMethodSymbol implementation &&
               TypeHierarchyEnumeration.IsSameOrOverridesTargetMethod(method, implementation);
    private static IMethodSymbol? SelectMatchingAccessor(
        IMethodSymbol method,
        IPropertySymbol implementation,
        IPropertySymbol interfaceProperty) {
        if (implementation.GetMethod != null &&
            TypeHierarchyEnumeration.IsSameOrOverridesTargetMethod(method, implementation.GetMethod))
            return interfaceProperty.GetMethod;

        if (implementation.SetMethod != null &&
            TypeHierarchyEnumeration.IsSameOrOverridesTargetMethod(method, implementation.SetMethod))
            return interfaceProperty.SetMethod;

        return null;
    }
}
