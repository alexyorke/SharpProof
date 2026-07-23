namespace SharpProof.Analyzer;
internal static class SymbolAttributeTraversal {
    internal static IEnumerable<AttributeData> GetAttributes(
        ISymbol symbol,
        bool includePropertyForGetter = false) {
        foreach (var attribute in symbol.GetAttributes()) yield return attribute;
        if (!includePropertyForGetter ||
            symbol is not IMethodSymbol { MethodKind: MethodKind.PropertyGet, AssociatedSymbol: { } property })
            yield break;
        foreach (var attribute in property.GetAttributes()) yield return attribute;
    }
}
