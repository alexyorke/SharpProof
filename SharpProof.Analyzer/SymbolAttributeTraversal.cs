namespace SharpProof.Analyzer;
internal static class SymbolAttributeTraversal {
    internal static IEnumerable<AttributeData> GetAttributes(
        ISymbol symbol,
        bool includePropertyForAccessor = false) {
        foreach (var attribute in symbol.GetAttributes()) yield return attribute;
        if (!includePropertyForAccessor ||
            symbol is not IMethodSymbol {
                MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet,
                AssociatedSymbol: { } property
            })
            yield break;
        foreach (var attribute in property.GetAttributes()) yield return attribute;
    }
}
