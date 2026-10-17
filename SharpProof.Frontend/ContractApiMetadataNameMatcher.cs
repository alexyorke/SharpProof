using System.Collections.Concurrent;

namespace SharpProof.Frontend;

internal sealed class ContractApiMetadataNameMatcher
{
    private readonly ConcurrentDictionary<string, MetadataNameParts> _parts = new(StringComparer.Ordinal);

    internal bool Matches(
        INamedTypeSymbol? type,
        string metadataName)
    {
        if (metadataName is null)
        {
            throw new System.ArgumentNullException(nameof(metadataName));
        }
        if (type == null || type.ContainingType != null)
        {
            return false;
        }

        var parts = _parts.GetOrAdd(
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
        string[] segments)
    {
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
            if (metadataName is null)
            {
                throw new System.ArgumentNullException(nameof(metadataName));
            }
            var separator = metadataName.LastIndexOf('.');
            // Every construction input is a fixed, namespace-qualified catalog name.
            TypeName = metadataName.Substring(separator + 1);
            NamespaceSegments = metadataName
                .Substring(0, separator)
                .Split('.');
        }

        internal string TypeName
        {
            get;
        }

        internal string[] NamespaceSegments
        {
            get;
        }
    }

}
