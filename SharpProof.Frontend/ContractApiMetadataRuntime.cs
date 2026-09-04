namespace SharpProof.Frontend;

/// <summary>
/// Runtime projections and lookup behavior for the generated contract API catalog.
/// </summary>
internal static partial class ContractApiMetadata
{
    internal const string AttributesAssemblyMvidMetadataKey =
        "SharpProof.Attributes.MVID";
    private static readonly ImmutableHashSet<string>
        ContractMethodCandidateNameSet =
            ContractMethodCandidateNames.ToImmutableHashSet(
                StringComparer.Ordinal);

    internal static bool IsContractMethodCandidateName(string name)
    {
        return ContractMethodCandidateNameSet.Contains(name);
    }

    internal static bool TryGetMethod(
        string name,
        out ContractApiMethodDescriptor descriptor)
    {
        foreach (var candidate in Methods)
        {
            if (string.Equals(
                    candidate.Name,
                    name,
                    StringComparison.Ordinal))
            {
                descriptor = candidate;
                return true;
            }
        }

        descriptor = default;
        return false;
    }

    internal static bool TryGetAttribute(
        string metadataName,
        out ContractApiAttributeDescriptor descriptor)
    {
        foreach (var candidate in Attributes)
        {
            if (string.Equals(
                    candidate.MetadataName,
                    metadataName,
                    StringComparison.Ordinal))
            {
                descriptor = candidate;
                return true;
            }
        }

        descriptor = default;
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
