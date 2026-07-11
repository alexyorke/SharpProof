using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer.Engine.Rules;

internal static class DisposalMemberClassifier
{
    internal static IMethodSymbol? FindDisposalMethod(
        ITypeSymbol type,
        Compilation compilation,
        bool preferAsync)
    {
        return preferAsync
            ? FindDisposeAsyncMethod(type, compilation) ?? FindDisposeMethod(type, compilation)
            : FindDisposeMethod(type, compilation) ?? FindDisposeAsyncMethod(type, compilation);
    }

    internal static IEnumerable<IMethodSymbol> EnumerateRuntimeDisposalMembers(
        ITypeSymbol type,
        bool async)
    {
        var methodName = async ? "DisposeAsync" : "Dispose";
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
            foreach (var method in current
                         .GetMembers(methodName)
                         .OfType<IMethodSymbol>()
                         .Where(static method => !method.IsStatic && method.Parameters.Length == 0))
                if (seen.Add(method.OriginalDefinition))
                    yield return method;

        if (type is not INamedTypeSymbol namedType) yield break;

        foreach (var interfaceType in namedType.AllInterfaces.Prepend(namedType))
        {
            if (async ? !IsAsyncDisposable(interfaceType) : !IsDisposable(interfaceType)) continue;

            foreach (var interfaceMember in interfaceType
                         .GetMembers(methodName)
                         .OfType<IMethodSymbol>()
                         .Where(static method => !method.IsStatic && method.Parameters.Length == 0))
            {
                var implementation = namedType.FindImplementationForInterfaceMember(interfaceMember) as IMethodSymbol
                                     ?? interfaceMember;
                if (seen.Add(implementation.OriginalDefinition)) yield return implementation;
            }
        }
    }

    internal static bool IsAsyncDisposable(INamedTypeSymbol type)
    {
        return type.Arity == 0 &&
               string.Equals(type.MetadataName, "IAsyncDisposable", StringComparison.Ordinal) &&
               IsNamespace(type.ContainingNamespace, "System");
    }

    private static IMethodSymbol? FindDisposeMethod(ITypeSymbol type, Compilation compilation)
    {
        var disposable = compilation.GetTypeByMetadataName("System.IDisposable");
        var interfaceMethod = disposable?.GetMembers("Dispose").OfType<IMethodSymbol>().FirstOrDefault();
        if (disposable != null && interfaceMethod != null)
        {
            if (SymbolEqualityComparer.Default.Equals(type, disposable) ||
                type.AllInterfaces.Contains(disposable, SymbolEqualityComparer.Default))
                return type.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol ?? interfaceMethod;
        }

        return type.GetMembers("Dispose")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static method =>
                !method.IsStatic &&
                method.Parameters.Length == 0 &&
                method.ReturnsVoid);
    }

    private static IMethodSymbol? FindDisposeAsyncMethod(ITypeSymbol type, Compilation compilation)
    {
        var asyncDisposable = compilation.GetTypeByMetadataName("System.IAsyncDisposable");
        var interfaceMethod = asyncDisposable?.GetMembers("DisposeAsync").OfType<IMethodSymbol>().FirstOrDefault();
        if (asyncDisposable != null && interfaceMethod != null)
        {
            if (SymbolEqualityComparer.Default.Equals(type, asyncDisposable) ||
                type.AllInterfaces.Contains(asyncDisposable, SymbolEqualityComparer.Default))
                return type.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol ?? interfaceMethod;
        }

        return type.GetMembers("DisposeAsync")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static method => !method.IsStatic && method.Parameters.Length == 0);
    }

    private static bool IsDisposable(INamedTypeSymbol type)
    {
        return type.OriginalDefinition.SpecialType == SpecialType.System_IDisposable;
    }

    private static bool IsNamespace(INamespaceSymbol? namespaceSymbol, string expected)
    {
        if (namespaceSymbol == null || namespaceSymbol.IsGlobalNamespace) return expected.Length == 0;

        var segments = new Stack<string>();
        for (var current = namespaceSymbol; current is { IsGlobalNamespace: false }; current = current.ContainingNamespace)
            segments.Push(current.Name);

        return string.Equals(string.Join(".", segments), expected, StringComparison.Ordinal);
    }
}
