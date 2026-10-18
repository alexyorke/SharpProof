namespace SharpProof.Frontend;

internal sealed class ContractApiMetadataNameMatcher
{
    private ImmutableDictionary<string, MetadataNameParts> _parts = ImmutableDictionary.Create<string, MetadataNameParts>(StringComparer.Ordinal);

    internal bool Matches(
        INamedTypeSymbol? type,
        string metadataName)
    {
        metadataName = RepositoryParameterGuard.NotNull(metadataName, nameof(metadataName));
        if (type == null || type.ContainingType != null)
        {
            return false;
        }

        var parts = ImmutableInterlocked.GetOrAdd(ref _parts,
            metadataName,
            static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (new MetadataNameParts(value)));
        return string.Equals(
            type.MetadataName,
            parts.TypeName,
            StringComparison.Ordinal) &&
            NamespaceMatches(
                type.ContainingNamespace,
                parts.NamespaceSegments);
    }

    private static bool NamespaceMatches(
        INamespaceSymbol @namespace,
        ImmutableArray<string> segments)
    {
        RepositoryParameterGuard.RequireInitialized(segments.IsDefault, nameof(segments), "Array must be initialized.");
        for (var index = segments.Length - 1; index >= 0; index--)
        {
            if (@namespace.IsGlobalNamespace ||
                !string.Equals(
                    @namespace.Name,
                    segments[index],
                    StringComparison.Ordinal))
            {
                return false;
            }

            @namespace = @namespace.ContainingNamespace;
        }

        return @namespace.IsGlobalNamespace;
    }

    private sealed class MetadataNameParts
    {
        internal MetadataNameParts(string metadataName)
        {
            metadataName = RepositoryParameterGuard.NotNull(metadataName, nameof(metadataName));
            var separator = metadataName.LastIndexOf('.');
            // Every construction input is a fixed, namespace-qualified catalog name.
            TypeName = metadataName.Substring(separator + 1);
            NamespaceSegments = metadataName
                .Substring(0, separator)
                .Split('.').ToImmutableArray();
        }

        internal string TypeName
        {
            get;
        }

        internal ImmutableArray<string> NamespaceSegments
        {
            get;
        }
    }

}
