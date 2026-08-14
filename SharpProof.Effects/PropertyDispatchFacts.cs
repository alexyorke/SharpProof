using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal static class PropertyDispatchFacts
{
    internal static bool IsUncertain(
        IPropertyReferenceOperation property,
        IMethodSymbol accessor)
    {
        return !IsStaticallyBound(property) &&
               IsSymbolDispatchUncertain(accessor);
    }

    private static bool IsStaticallyBound(
        IPropertyReferenceOperation property)
    {
        return property.Instance?.Syntax is BaseExpressionSyntax ||
               property.Instance?.Type?.IsSealed == true;
    }

    private static bool IsSymbolDispatchUncertain(
        IMethodSymbol accessor)
    {
        return !accessor.IsStatic &&
               (accessor.IsVirtual ||
                accessor.IsAbstract ||
                accessor.IsOverride ||
                accessor.ContainingType?.TypeKind == TypeKind.Interface) &&
               accessor.ContainingType?.IsSealed != true &&
               !accessor.IsSealed;
    }
}
