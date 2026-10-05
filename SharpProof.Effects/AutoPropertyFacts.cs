using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal static class AutoPropertyFacts
{
    internal static bool IsAccessor(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        return method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet &&
            method.AssociatedSymbol is IPropertySymbol property &&
            IsAutoProperty(property, cancellationToken);
    }

    internal static bool IsAutoProperty(
        IPropertySymbol property,
        CancellationToken cancellationToken)
    {
        return property.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(cancellationToken) is PropertyDeclarationSyntax
            {
                ExpressionBody: null,
                AccessorList.Accessors: var accessors
            } &&
            accessors.Count != 0 &&
            accessors.All(static accessor =>
                accessor.Body == null && accessor.ExpressionBody == null));
    }

    internal static bool TryGetBackingField(
        IPropertySymbol property,
        out IFieldSymbol field)
    {
        field = property.ContainingType.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(candidate =>
                candidate.AssociatedSymbol is IPropertySymbol associated &&
                AreSameProperty(associated, property))!;
        return field != null;
    }

    private static bool AreSameProperty(
        IPropertySymbol left,
        IPropertySymbol right)
    {
        return SymbolEqualityComparer.Default.Equals(left, right) ||
            SymbolEqualityComparer.Default.Equals(
                left.OriginalDefinition,
                right.OriginalDefinition);
    }
}
