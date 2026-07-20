namespace SharpProof.Analyzer.Engine.Analysis;

internal enum TypeIdentityPolicy {
    Exact,
    ExactOrOriginalDefinition
}

internal static class TypeHierarchyEnumeration {
    internal static IEnumerable<INamedTypeSymbol> EnumerateAllNamedTypes(INamespaceSymbol root) =>
        EnumerateAllNamedTypes(root, CancellationToken.None);

    internal static IEnumerable<INamedTypeSymbol> EnumerateAllNamedTypes(
        INamespaceSymbol root,
        CancellationToken cancellationToken) {
        foreach (var member in root.GetMembers()) {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is INamespaceSymbol ns) {
                foreach (var inner in EnumerateAllNamedTypes(ns, cancellationToken)) yield return inner;
            }
            else if (member is INamedTypeSymbol type) {
                yield return type;
                foreach (var nested in EnumerateNestedTypes(type, cancellationToken)) yield return nested;
            }
        }
    }


    internal static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(
        INamedTypeSymbol type,
        CancellationToken cancellationToken) {
        foreach (var member in type.GetTypeMembers()) {
            cancellationToken.ThrowIfCancellationRequested();
            yield return member;
            foreach (var nested in EnumerateNestedTypes(member, cancellationToken)) yield return nested;
        }
    }

    internal static bool OverridesTargetMethod(IMethodSymbol method, IMethodSymbol target) => method.OverriddenMethod is { } overridden &&
               IsSameOrOverridesTargetMethod(overridden, target);

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

    internal static IEnumerable<TSymbol> EnumerateBaseTypeMembers<TSymbol>(
        ITypeSymbol type,
        string memberName)
        where TSymbol : class, ISymbol => EnumerateBaseTypes(type).SelectMany(current => current.GetMembers(memberName).OfType<TSymbol>());

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

    internal static bool IsNamespace(INamespaceSymbol? namespaceSymbol, string expected) {
        if (namespaceSymbol == null || namespaceSymbol.IsGlobalNamespace) return expected.Length == 0;

        var segments = new Stack<string>();
        for (var current = namespaceSymbol; current is { IsGlobalNamespace: false }; current = current.ContainingNamespace)
            segments.Push(current.Name);

        return string.Equals(string.Join(".", segments), expected, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tests whether <paramref name="type"/> is the named type with the given metadata name,
    /// generic arity, and containing namespace. Collapses the repeated
    /// "arity + MetadataName + IsNamespace" idiom used by the runtime-member classifiers.
    /// </summary>
    internal static bool IsTypeNamed(
        INamedTypeSymbol type,
        string containingNamespace,
        string metadataName,
        int arity) => type.Arity == arity &&
               string.Equals(type.MetadataName, metadataName, StringComparison.Ordinal) &&
               IsNamespace(type.ContainingNamespace, containingNamespace);

    /// <summary>
    /// Yields the methods in <paramref name="methods"/>, skipping any whose
    /// <see cref="ISymbol.OriginalDefinition"/> was already yielded. Centralizes the
    /// "seen set keyed on OriginalDefinition" dedup used across the member enumerators.
    /// </summary>
    internal static IEnumerable<IMethodSymbol> DistinctByOriginalDefinition(this IEnumerable<IMethodSymbol> methods) {
        var seen = new HashSet<IMethodSymbol>(SymbolEq.Default);
        foreach (var method in methods)
            if (seen.Add(method.OriginalDefinition))
                yield return method;
    }

    internal static IEnumerable<IMethodSymbol> EnumerateInterfaceMethodImplementations(
        INamedTypeSymbol type,
        string memberName,
        Func<INamedTypeSymbol, bool> interfaceMatches,
        Func<IMethodSymbol, bool> methodMatches,
        bool includeTypeSelf = true,
        bool includeUnimplementedInterfaceMember = true) {
        var seen = new HashSet<IMethodSymbol>(SymbolEq.Default);
        var interfaceTypes = includeTypeSelf ? type.AllInterfaces.Prepend(type) : type.AllInterfaces;
        foreach (var interfaceType in interfaceTypes) {
            if (!interfaceMatches(interfaceType)) continue;

            foreach (var interfaceMethod in interfaceType
                         .GetMembers(memberName)
                         .OfType<IMethodSymbol>()
                         .Where(methodMatches)) {
                var implementation = type.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol;
                if (implementation == null) {
                    if (!includeUnimplementedInterfaceMember) continue;
                    implementation = interfaceMethod;
                }

                if (seen.Add(implementation.OriginalDefinition)) yield return implementation;
            }
        }
    }

    internal static bool ExplicitlyImplements(IMethodSymbol methodSymbol, IMethodSymbol interfaceMethod) {
        foreach (var implemented in methodSymbol.ExplicitInterfaceImplementations)
            if (SymbolEq.AreEqual(implemented.OriginalDefinition,
                    interfaceMethod.OriginalDefinition))
                return true;

        return false;
    }

    internal static bool ImplementsInterface(
        INamedTypeSymbol type,
        INamedTypeSymbol? interfaceSymbol,
        bool includeInterfaceSelf = false) {
        if (interfaceSymbol == null) return false;

        if (includeInterfaceSelf &&
            SymbolEq.AreEqual(type.OriginalDefinition, interfaceSymbol.OriginalDefinition))
            return true;

        return type.AllInterfaces.Any(candidate =>
            SymbolEq.AreEqual(candidate.OriginalDefinition, interfaceSymbol.OriginalDefinition));
    }

    internal static bool DerivesFrom(
        INamedTypeSymbol type,
        INamedTypeSymbol potentialBase,
        bool includeSelf = false) {
        for (var current = includeSelf ? type : type.BaseType; current != null; current = current.BaseType)
            if (SymbolEq.AreEqual(current.OriginalDefinition, potentialBase.OriginalDefinition))
                return true;

        return false;
    }

    internal static bool HasMethodBody(IMethodSymbol methodSymbol, CancellationToken cancellationToken) {
        if (methodSymbol.DeclaringSyntaxReferences.Length == 0) return false;

        foreach (var syntaxReference in methodSymbol.DeclaringSyntaxReferences) {
            cancellationToken.ThrowIfCancellationRequested();
            var methodSyntax = syntaxReference.GetSyntax(cancellationToken);
            if (methodSyntax is MethodDeclarationSyntax methodDeclaration &&
                (methodDeclaration.Body != null || methodDeclaration.ExpressionBody != null))
                return true;
        }

        return false;
    }
}
