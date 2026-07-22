namespace SharpProof.Analyzer;

internal sealed class SharpProofAttributeIdentityPolicy {
    private const string OfficialNamespace = "SharpProof.Attributes";
    private static readonly SharpProofAttributeIdentityPolicy Instance = new();

    private SharpProofAttributeIdentityPolicy() {
    }
    internal static SharpProofAttributeIdentityPolicy Create() => Instance;

    internal bool HasAttribute(ISymbol symbol, string attributeTypeName) =>
        GetAcceptedAttributes(symbol, attributeTypeName).Any();

    internal IEnumerable<AttributeData> GetAcceptedAttributes(ISymbol symbol, string attributeTypeName) {
        foreach (var attribute in SymbolAttributeTraversal.GetAttributes(symbol, GetAssociatedAttributePolicy(attributeTypeName)))
            if (IsAccepted(attribute.AttributeClass, attributeTypeName))
                yield return attribute;
    }
    private static bool IsAccepted(INamedTypeSymbol? attributeClass, string attributeTypeName) {
        var definition = attributeClass?.OriginalDefinition;
        return definition != null &&
               string.Equals(definition.Name, attributeTypeName, StringComparison.Ordinal) &&
               string.Equals(
                   definition.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                   OfficialNamespace,
                   StringComparison.Ordinal);
    }
    private static AssociatedAttributePolicy GetAssociatedAttributePolicy(string attributeTypeName) =>
        attributeTypeName is
            "AllowedCapabilitiesAttribute" or
            "AllowedExceptionsAttribute" or
            "DoesNotThrowAttribute" or
            "EffectContractAttribute" or
            "EnforcePureAttribute" or
            "EnsuresAttribute" or
            "ExpectedComplexityAttribute" or
            "ZeroAllocationsAttribute"
            ? AssociatedAttributePolicy.PropertyForGetter
            : AssociatedAttributePolicy.None;
}
