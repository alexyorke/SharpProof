using Microsoft.CodeAnalysis;
using SharpProof.Analyzer.Engine.Analysis;

namespace SharpProof.Analyzer.Engine.Rules;

internal static class EnumeratorRuntimeMemberClassifier
{
    internal static bool IsLocalEnumeratorStateMutation(
        IMethodSymbol method,
        ITypeSymbol enumeratorType,
        Compilation compilation)
    {
        if (!enumeratorType.IsValueType ||
            method.IsStatic ||
            method.Parameters.Any(static parameter => parameter.RefKind != RefKind.None) ||
            method.Name is not ("MoveNext" or "Dispose" or "MoveNextAsync" or "DisposeAsync") ||
            !PurityAnalysisEngine.TryGetTrustedGeneratedPurityCoverage(
                method.OriginalDefinition,
                compilation,
                out var generated) ||
            !generated.IsImpure ||
            generated.HasUnsupportedEffects ||
            generated.Categories.IsEmpty)
            return false;

        return generated.Categories.All(static category =>
            category is "caller_visible_memory_write" or "object_state_write");
    }

    internal static IEnumerable<IMethodSymbol> EnumerateRuntimeMembers(ITypeSymbol enumeratorType)
    {
        var seen = new HashSet<IMethodSymbol>(SymbolEq.Default);

        foreach (var method in EnumerateInstanceMethods(enumeratorType, "MoveNext", 0))
            if (seen.Add(method.OriginalDefinition))
                yield return method;

        foreach (var getter in EnumerateCurrentGetters(enumeratorType))
            if (seen.Add(getter.OriginalDefinition))
                yield return getter;

        foreach (var dispose in DisposalMemberClassifier.EnumerateRuntimeDisposalMembers(enumeratorType, false))
            if (seen.Add(dispose.OriginalDefinition))
                yield return dispose;
    }

    internal static IEnumerable<IMethodSymbol> EnumerateAsyncRuntimeMembers(ITypeSymbol enumeratorType)
    {
        var seen = new HashSet<IMethodSymbol>(SymbolEq.Default);

        foreach (var method in EnumerateInstanceMethods(enumeratorType, "MoveNextAsync", 0))
            if (seen.Add(method.OriginalDefinition))
                yield return method;

        foreach (var getter in EnumerateCurrentGetters(enumeratorType))
            if (seen.Add(getter.OriginalDefinition))
                yield return getter;

        foreach (var dispose in DisposalMemberClassifier.EnumerateRuntimeDisposalMembers(enumeratorType, true))
            if (seen.Add(dispose.OriginalDefinition))
                yield return dispose;
    }

    internal static IEnumerable<IMethodSymbol> EnumerateGetEnumeratorImplementations(ITypeSymbol collectionType)
    {
        var seen = new HashSet<IMethodSymbol>(SymbolEq.Default);
        var hasPatternMethod = false;

        foreach (var getEnumerator in collectionType
                     .GetMembers("GetEnumerator")
                     .OfType<IMethodSymbol>()
                     .Where(static method => !method.IsStatic && method.Parameters.Length == 0))
            if (seen.Add(getEnumerator.OriginalDefinition))
            {
                hasPatternMethod = true;
                yield return getEnumerator;
            }

        if (hasPatternMethod) yield break;

        if (collectionType is not INamedTypeSymbol namedCollectionType) yield break;

        foreach (var implementation in TypeHierarchyEnumeration.EnumerateInterfaceMethodImplementations(
                     namedCollectionType,
                     "GetEnumerator",
                     IsEnumerableInterface,
                     static method => !method.IsStatic && method.Parameters.Length == 0))
            if (seen.Add(implementation.OriginalDefinition))
                yield return implementation;
    }

    internal static IEnumerable<IMethodSymbol> EnumerateGenericGetEnumeratorImplementations(
        ITypeSymbol collectionType)
    {
        if (collectionType is not INamedTypeSymbol namedCollectionType) yield break;

        foreach (var implementation in TypeHierarchyEnumeration.EnumerateInterfaceMethodImplementations(
                     namedCollectionType,
                     "GetEnumerator",
                     static interfaceType => interfaceType.OriginalDefinition.SpecialType ==
                                             SpecialType.System_Collections_Generic_IEnumerable_T,
                     static method => !method.IsStatic && method.Parameters.Length == 0))
            yield return implementation;
    }

    internal static IEnumerable<IMethodSymbol> EnumerateGetAsyncEnumeratorImplementations(ITypeSymbol collectionType)
    {
        var seen = new HashSet<IMethodSymbol>(SymbolEq.Default);

        foreach (var getAsyncEnumerator in collectionType
                     .GetMembers("GetAsyncEnumerator")
                     .OfType<IMethodSymbol>()
                     .Where(IsGetAsyncEnumeratorPatternMethod))
            if (seen.Add(getAsyncEnumerator.OriginalDefinition))
                yield return getAsyncEnumerator;

        if (collectionType is not INamedTypeSymbol namedCollectionType) yield break;

        foreach (var implementation in TypeHierarchyEnumeration.EnumerateInterfaceMethodImplementations(
                     namedCollectionType,
                     "GetAsyncEnumerator",
                     IsAsyncEnumerableInterface,
                     IsGetAsyncEnumeratorPatternMethod))
            if (seen.Add(implementation.OriginalDefinition))
                yield return implementation;
    }

    private static IEnumerable<IMethodSymbol> EnumerateInstanceMethods(
        ITypeSymbol type,
        string methodName,
        int parameterCount)
    {
        return TypeHierarchyEnumeration
            .EnumerateBaseTypeMembers<IMethodSymbol>(type, methodName)
            .Where(method => !method.IsStatic && method.Parameters.Length == parameterCount);
    }

    private static IEnumerable<IMethodSymbol> EnumerateCurrentGetters(ITypeSymbol type)
    {
        foreach (var property in TypeHierarchyEnumeration.EnumerateBaseTypeMembers<IPropertySymbol>(type, "Current"))
            if (property.GetMethod is { } getter)
                yield return getter;
    }

    private static bool IsGetAsyncEnumeratorPatternMethod(IMethodSymbol method)
    {
        return !method.IsStatic &&
               (method.Parameters.Length == 0 ||
                (method.Parameters.Length == 1 && method.Parameters[0].IsOptional));
    }

    private static bool IsEnumerableInterface(INamedTypeSymbol typeSymbol)
    {
        var originalDefinition = typeSymbol.OriginalDefinition;
        return originalDefinition.SpecialType == SpecialType.System_Collections_IEnumerable ||
               originalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T;
    }

    private static bool IsAsyncEnumerableInterface(INamedTypeSymbol typeSymbol)
    {
        var originalDefinition = typeSymbol.OriginalDefinition;
        return originalDefinition.Arity == 1 &&
               string.Equals(originalDefinition.MetadataName, "IAsyncEnumerable`1", StringComparison.Ordinal) &&
               TypeHierarchyEnumeration.IsNamespace(
                   originalDefinition.ContainingNamespace,
                   "System.Collections.Generic");
    }
}
