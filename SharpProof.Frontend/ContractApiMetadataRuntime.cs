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
    private static readonly ImmutableDictionary<string, ContractApiAttributeDescriptor>
        AttributeByMetadataName =
            Attributes.ToImmutableDictionary(
                static attribute => attribute.MetadataName,
                StringComparer.Ordinal);
    private static readonly ImmutableHashSet<string>
        ClosedAttributeTypeNameSet =
            Attributes
                .Where(static attribute =>
                    attribute.Category == ContractApiAttributeCategory.Closed)
                .Select(static attribute => attribute.TypeName)
                .ToImmutableHashSet(StringComparer.Ordinal);

    internal static bool IsContractMethodCandidateName(string name)
    {
        return ContractMethodCandidateNameSet.Contains(name);
    }

    internal static bool IsAttribute(
        AttributeData attribute,
        INamedTypeSymbol? expected)
    {
        return expected != null &&
            SymbolEqualityComparer.Default.Equals(
                attribute.AttributeClass?.OriginalDefinition,
                expected.OriginalDefinition);
    }

    internal static bool TryGetAttribute(
        string metadataName,
        out ContractApiAttributeDescriptor descriptor)
    {
        if (metadataName is null)
        {
            descriptor = default;
            return false;
        }

        return AttributeByMetadataName.TryGetValue(
            metadataName,
            out descriptor);
    }

    internal static bool IsClosedAttributeTypeName(
        string namespaceName,
        string typeName)
    {
        return string.Equals(
                namespaceName,
                AttributesNamespace,
                StringComparison.Ordinal) &&
            typeName is not null &&
            ClosedAttributeTypeNameSet.Contains(typeName);
    }
}
