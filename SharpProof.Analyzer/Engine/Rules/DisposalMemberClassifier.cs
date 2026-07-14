using Microsoft.CodeAnalysis;
using SharpProof.Analyzer.Engine.Analysis;

namespace SharpProof.Analyzer.Engine.Rules;

internal static class DisposalMemberClassifier
{
    internal static IMethodSymbol? FindDisposalMethod(
        ITypeSymbol type,
        Compilation compilation,
        bool preferAsync)
    {
        return preferAsync
            ? FindDisposalMember(type, compilation, true) ?? FindDisposalMember(type, compilation, false)
            : FindDisposalMember(type, compilation, false) ?? FindDisposalMember(type, compilation, true);
    }

    internal static IEnumerable<IMethodSymbol> EnumerateRuntimeDisposalMembers(
        ITypeSymbol type,
        bool async)
    {
        var methodName = async ? "DisposeAsync" : "Dispose";
        var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        foreach (var current in TypeHierarchyEnumeration.EnumerateBaseTypes(type))
            foreach (var method in current
                         .GetMembers(methodName)
                         .OfType<IMethodSymbol>()
                         .Where(static method => !method.IsStatic && method.Parameters.Length == 0))
                if (seen.Add(method.OriginalDefinition))
                    yield return method;

        if (type is not INamedTypeSymbol namedType) yield break;

        foreach (var implementation in TypeHierarchyEnumeration.EnumerateInterfaceMethodImplementations(
                     namedType,
                     methodName,
                     interfaceType => async ? IsAsyncDisposable(interfaceType) : IsDisposable(interfaceType),
                     static method => !method.IsStatic && method.Parameters.Length == 0))
            if (seen.Add(implementation.OriginalDefinition))
                yield return implementation;
    }

    internal static bool IsAsyncDisposable(INamedTypeSymbol type)
    {
        return type.Arity == 0 &&
               string.Equals(type.MetadataName, "IAsyncDisposable", StringComparison.Ordinal) &&
               TypeHierarchyEnumeration.IsNamespace(type.ContainingNamespace, "System");
    }

    private static IMethodSymbol? FindDisposalMember(
        ITypeSymbol type,
        Compilation compilation,
        bool async)
    {
        var methodName = async ? "DisposeAsync" : "Dispose";
        var disposable = compilation.GetTypeByMetadataName(
            async ? "System.IAsyncDisposable" : "System.IDisposable");
        var interfaceMethod = disposable?.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault();
        if (disposable != null && interfaceMethod != null)
        {
            if (SymbolEqualityComparer.Default.Equals(type, disposable) ||
                type.AllInterfaces.Contains(disposable, SymbolEqualityComparer.Default))
                return type.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol ?? interfaceMethod;
        }

        foreach (var current in TypeHierarchyEnumeration.EnumerateBaseTypes(type))
        {
            var method = current.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(candidate =>
                    !candidate.IsStatic &&
                    candidate.Parameters.Length == 0 &&
                    (async || candidate.ReturnsVoid));
            if (method != null) return method;
        }

        return null;
    }

    private static bool IsDisposable(INamedTypeSymbol type)
    {
        return type.OriginalDefinition.SpecialType == SpecialType.System_IDisposable;
    }

}
