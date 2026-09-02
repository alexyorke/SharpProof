namespace SharpProof.Frontend;

/// <summary>
/// Runtime projections and lookup behavior for the generated contract API catalog.
/// </summary>
internal static partial class ContractApiMetadata
{
    internal const string AttributesAssemblyMvidMetadataKey =
        "SharpProof.Attributes.MVID";
    internal static bool IsContractMethodCandidateName(string name)
    {
        return ContractMethodCandidateNames.Contains(
            name,
            StringComparer.Ordinal);
    }

    internal static bool TryGetMethod(
        string name,
        out ContractApiMethodDescriptor descriptor)
    {
        return TryFind(
            Methods,
            static candidate => candidate.Name,
            name,
            out descriptor);
    }

    internal static bool TryGetAttribute(
        string metadataName,
        out ContractApiAttributeDescriptor descriptor)
    {
        return TryFind(
            Attributes,
            static candidate => candidate.MetadataName,
            metadataName,
            out descriptor);
    }

    private static bool TryFind<T>(
        IEnumerable<T> candidates,
        Func<T, string> getName,
        string name,
        out T descriptor)
    {
        foreach (var candidate in candidates)
        {
            if (string.Equals(
                    getName(candidate),
                    name,
                    StringComparison.Ordinal))
            {
                descriptor = candidate;
                return true;
            }
        }

        descriptor = default!;
        return false;
    }

    internal static bool IsClosedAttributeTypeName(
        string namespaceName,
        string typeName)
    {
        return string.Equals(
                namespaceName,
                AttributesNamespace,
                StringComparison.Ordinal) &&
            Attributes.Any(attribute =>
                attribute.Category ==
                    ContractApiAttributeCategory.Closed &&
                string.Equals(
                    attribute.TypeName,
                    typeName,
                    StringComparison.Ordinal));
    }
}
