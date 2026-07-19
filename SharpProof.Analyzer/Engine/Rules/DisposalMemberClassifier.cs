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

        var baseMembers = TypeHierarchyEnumeration
            .EnumerateBaseTypeMembers<IMethodSymbol>(type, methodName)
            .Where(static method => !method.IsStatic && method.Parameters.Length == 0);

        var interfaceMembers = type is INamedTypeSymbol namedType
            ? TypeHierarchyEnumeration.EnumerateInterfaceMethodImplementations(
                namedType,
                methodName,
                interfaceType => async ? IsAsyncDisposable(interfaceType) : IsDisposable(interfaceType),
                static method => !method.IsStatic && method.Parameters.Length == 0)
            : Enumerable.Empty<IMethodSymbol>();

        return baseMembers.Concat(interfaceMembers).DistinctByOriginalDefinition();
    }

    internal static bool IsAsyncDisposable(INamedTypeSymbol type)
    {
        return TypeHierarchyEnumeration.IsTypeNamed(type, "System", "IAsyncDisposable", 0);
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
            if (SymbolEq.AreEqual(type, disposable) ||
                type.AllInterfaces.Contains(disposable, SymbolEq.Default))
                return type.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol ?? interfaceMethod;
        }

        foreach (var candidate in TypeHierarchyEnumeration.EnumerateBaseTypeMembers<IMethodSymbol>(type, methodName))
        {
            if (!candidate.IsStatic && candidate.Parameters.Length == 0 && (async || candidate.ReturnsVoid))
                return candidate;
        }

        return null;
    }

    private static bool IsDisposable(INamedTypeSymbol type)
    {
        return type.OriginalDefinition.SpecialType == SpecialType.System_IDisposable;
    }

}
