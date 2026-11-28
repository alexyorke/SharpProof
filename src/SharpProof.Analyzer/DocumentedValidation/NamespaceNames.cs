namespace SharpProof.Analyzer;

// The dotted name of a namespace, read from the names in its chain up to the
// global namespace. The repository bans ToDisplayString for analyzer symbol
// identity, and a chain of symbol names is exact for the same purpose. A missing
// namespace, such as the container of a type parameter, has no name.
internal static class NamespaceNames
{
    internal static string Of(INamespaceSymbol? space)
    {
        if (space is null || space.IsGlobalNamespace)
        {
            return string.Empty;
        }

        var parent = Of(space.ContainingNamespace);
        return parent.Length == 0 ? space.Name : parent + "." + space.Name;
    }
}
