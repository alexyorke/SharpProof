using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SharpProof.Gates.Corpus;

internal static class CompilerClrTypeMatcher
{
    internal static bool Matches(ITypeSymbol symbol, Type runtime, Type choice)
    {
        System.ArgumentNullException.ThrowIfNull(choice);
        System.ArgumentNullException.ThrowIfNull(runtime);
        if (symbol is ITypeParameterSymbol)
        { return runtime == choice; }
        if (symbol is IArrayTypeSymbol array)
        { return runtime.IsArray && runtime.GetArrayRank() == array.Rank && Matches(array.ElementType, runtime.GetElementType()!, choice); }
        if (symbol is not INamedTypeSymbol named)
        { return false; }
        var definition = runtime.IsGenericType ? runtime.GetGenericTypeDefinition() : runtime;
        if (MetadataName(named) != definition.FullName)
        { return false; }
        var assembly = definition.Assembly.GetName();
        if (named.SpecialType == SpecialType.None && (named.ContainingAssembly.Identity.Name != assembly.Name ||
            named.ContainingAssembly.Identity.Version != assembly.Version ||
            !named.ContainingAssembly.Identity.PublicKeyToken.SequenceEqual(assembly.GetPublicKeyToken() ?? [])))
        { return false; }
        var arguments = ImmutableStack<INamedTypeSymbol>.Empty;
        for (var current = named; current != null; current = current.ContainingType)
        { arguments = arguments.Push(current); }
        var symbols = arguments.SelectMany(owner => owner is null ? throw new System.ArgumentNullException(nameof(owner)) : (owner.TypeArguments)).ToImmutableArray();
        var types = runtime.GetGenericArguments().ToImmutableArray();
        return symbols.Length == types.Length && symbols.Select((argument, ordinal) => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : (Matches(argument, types[ordinal], choice))).All(match => match);
    }

    internal static string MetadataName(INamedTypeSymbol type)
    {
        System.ArgumentNullException.ThrowIfNull(type);
        var names = ImmutableStack<string>.Empty;
        for (var current = type; current != null; current = current.ContainingType)
        { names = names.Push(current.MetadataName); }
        var namespaces = ImmutableStack<string>.Empty;
        for (var current = type.ContainingNamespace; !current.IsGlobalNamespace; current = current.ContainingNamespace)
        { namespaces = namespaces.Push(current.MetadataName); }
        return (namespaces.IsEmpty ? "" : string.Join(".", namespaces) + ".") + string.Join("+", names);
    }

}
