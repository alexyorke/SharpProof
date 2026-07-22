namespace SharpProof.Analyzer;

internal enum AssociatedAttributePolicy {
    None,
    PropertyForGetter,
    PropertyForAnyAccessor,
    AnyAssociatedSymbol
}
internal static class SymbolAttributeTraversal {
    internal static IEnumerable<AttributeData> GetAttributes(
        ISymbol symbol,
        AssociatedAttributePolicy associatedAttributePolicy = AssociatedAttributePolicy.None) {
        foreach (var attribute in symbol.GetAttributes()) yield return attribute;

        if (symbol is not IMethodSymbol method) yield break;

        var associatedSymbol = associatedAttributePolicy switch {
            AssociatedAttributePolicy.PropertyForGetter when method.MethodKind == MethodKind.PropertyGet =>
                method.AssociatedSymbol as IPropertySymbol,
            AssociatedAttributePolicy.PropertyForAnyAccessor => method.AssociatedSymbol as IPropertySymbol,
            AssociatedAttributePolicy.AnyAssociatedSymbol => method.AssociatedSymbol,
            _ => null
        };

        if (associatedSymbol == null) yield break;

        foreach (var attribute in associatedSymbol.GetAttributes()) yield return attribute;
    }
}
