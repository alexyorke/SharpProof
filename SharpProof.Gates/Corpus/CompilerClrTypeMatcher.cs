using Microsoft.CodeAnalysis;

namespace SharpProof.Gates.Corpus;

internal static class CompilerClrTypeMatcher
{
    internal static bool Matches(ITypeSymbol symbol, Type runtime, Type choice)
    {
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
        var arguments = new Stack<INamedTypeSymbol>();
        for (var current = named; current != null; current = current.ContainingType)
        { arguments.Push(current); }
        var symbols = arguments.SelectMany(owner => owner.TypeArguments).ToArray();
        var types = runtime.GetGenericArguments();
        return symbols.Length == types.Length && symbols.Select((argument, ordinal) => Matches(argument, types[ordinal], choice)).All(match => match);
    }

    internal static string MetadataName(INamedTypeSymbol type)
    {
        var names = new Stack<string>();
        for (var current = type; current != null; current = current.ContainingType)
        { names.Push(current.MetadataName); }
        var namespaces = new Stack<string>();
        for (var current = type.ContainingNamespace; !current.IsGlobalNamespace; current = current.ContainingNamespace)
        { namespaces.Push(current.MetadataName); }
        return (namespaces.Count == 0 ? "" : string.Join(".", namespaces) + ".") + string.Join("+", names);
    }

}
