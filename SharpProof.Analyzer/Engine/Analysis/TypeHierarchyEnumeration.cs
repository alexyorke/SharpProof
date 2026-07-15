using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Analyzer.Engine.Analysis;

internal enum TypeIdentityPolicy
{
    Exact,
    ExactOrOriginalDefinition
}

internal static class TypeHierarchyEnumeration
{
    internal static IEnumerable<INamedTypeSymbol> EnumerateAllNamedTypes(INamespaceSymbol root)
    {
        return EnumerateAllNamedTypes(root, CancellationToken.None);
    }

    internal static IEnumerable<INamedTypeSymbol> EnumerateAllNamedTypes(
        INamespaceSymbol root,
        CancellationToken cancellationToken)
    {
        foreach (var member in root.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is INamespaceSymbol ns)
            {
                foreach (var inner in EnumerateAllNamedTypes(ns, cancellationToken)) yield return inner;
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;
                foreach (var nested in EnumerateNestedTypes(type, cancellationToken)) yield return nested;
            }
        }
    }


    internal static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(
        INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        foreach (var member in type.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return member;
            foreach (var nested in EnumerateNestedTypes(member, cancellationToken)) yield return nested;
        }
    }

    internal static bool OverridesTargetMethod(IMethodSymbol method, IMethodSymbol target)
    {
        return method.OverriddenMethod is { } overridden &&
               IsSameOrOverridesTargetMethod(overridden, target);
    }

    internal static bool IsSameOrOverridesTargetMethod(IMethodSymbol method, IMethodSymbol target)
    {
        for (var current = method; current != null; current = current.OverriddenMethod)
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, target.OriginalDefinition))
                return true;

        return false;
    }

    internal static IEnumerable<INamedTypeSymbol> EnumerateBaseTypes(
        ITypeSymbol type,
        bool includeSelf = true)
    {
        var namedType = type as INamedTypeSymbol;
        for (var current = includeSelf ? namedType : namedType?.BaseType;
             current != null;
             current = current.BaseType)
            yield return current;
    }

    internal static IEnumerable<TSymbol> EnumerateBaseTypeMembers<TSymbol>(
        ITypeSymbol type,
        string memberName)
        where TSymbol : class, ISymbol
    {
        return EnumerateBaseTypes(type).SelectMany(current => current.GetMembers(memberName).OfType<TSymbol>());
    }

    internal static bool IsSameOrDerivedFrom(
        ITypeSymbol candidate,
        ITypeSymbol expectedBase,
        TypeIdentityPolicy identityPolicy = TypeIdentityPolicy.Exact)
    {
        foreach (var current in EnumerateBaseTypes(candidate))
        {
            if (SymbolEqualityComparer.Default.Equals(current, expectedBase)) return true;
            if (identityPolicy == TypeIdentityPolicy.ExactOrOriginalDefinition &&
                SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, expectedBase.OriginalDefinition))
                return true;
        }

        return false;
    }

    internal static bool IsNamespace(INamespaceSymbol? namespaceSymbol, string expected)
    {
        if (namespaceSymbol == null || namespaceSymbol.IsGlobalNamespace) return expected.Length == 0;

        var segments = new Stack<string>();
        for (var current = namespaceSymbol; current is { IsGlobalNamespace: false }; current = current.ContainingNamespace)
            segments.Push(current.Name);

        return string.Equals(string.Join(".", segments), expected, StringComparison.Ordinal);
    }

    internal static IEnumerable<IMethodSymbol> EnumerateInterfaceMethodImplementations(
        INamedTypeSymbol type,
        string memberName,
        Func<INamedTypeSymbol, bool> interfaceMatches,
        Func<IMethodSymbol, bool> methodMatches,
        bool includeTypeSelf = true,
        bool includeUnimplementedInterfaceMember = true)
    {
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var interfaceTypes = includeTypeSelf ? type.AllInterfaces.Prepend(type) : type.AllInterfaces;
        foreach (var interfaceType in interfaceTypes)
        {
            if (!interfaceMatches(interfaceType)) continue;

            foreach (var interfaceMethod in interfaceType
                         .GetMembers(memberName)
                         .OfType<IMethodSymbol>()
                         .Where(methodMatches))
            {
                var implementation = type.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol;
                if (implementation == null)
                {
                    if (!includeUnimplementedInterfaceMember) continue;
                    implementation = interfaceMethod;
                }

                if (seen.Add(implementation.OriginalDefinition)) yield return implementation;
            }
        }
    }

    internal static bool ExplicitlyImplements(IMethodSymbol methodSymbol, IMethodSymbol interfaceMethod)
    {
        foreach (var implemented in methodSymbol.ExplicitInterfaceImplementations)
            if (SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition,
                    interfaceMethod.OriginalDefinition))
                return true;

        return false;
    }

    internal static bool ImplementsInterface(
        INamedTypeSymbol type,
        INamedTypeSymbol? interfaceSymbol,
        bool includeInterfaceSelf = false)
    {
        if (interfaceSymbol == null) return false;

        if (includeInterfaceSelf &&
            SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, interfaceSymbol.OriginalDefinition))
            return true;

        return type.AllInterfaces.Any(candidate =>
            SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, interfaceSymbol.OriginalDefinition));
    }

    internal static bool DerivesFrom(
        INamedTypeSymbol type,
        INamedTypeSymbol potentialBase,
        bool includeSelf = false)
    {
        for (var current = includeSelf ? type : type.BaseType; current != null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, potentialBase.OriginalDefinition))
                return true;

        return false;
    }

    internal static bool HasMethodBody(IMethodSymbol methodSymbol, CancellationToken cancellationToken)
    {
        if (methodSymbol.DeclaringSyntaxReferences.Length == 0) return false;

        foreach (var syntaxReference in methodSymbol.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var methodSyntax = syntaxReference.GetSyntax(cancellationToken);
            if (methodSyntax is MethodDeclarationSyntax methodDeclaration &&
                (methodDeclaration.Body != null || methodDeclaration.ExpressionBody != null))
                return true;
        }

        return false;
    }
}
