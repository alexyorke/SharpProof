using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal static class AutoPropertyFacts
{
    internal static bool IsAccessor(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        if (method is null)
        {
            throw new ArgumentNullException(nameof(method));
        }

        return method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet &&
            method.AssociatedSymbol is IPropertySymbol property &&
            IsAutoProperty(property, cancellationToken);
    }

    internal static bool IsAutoProperty(
        IPropertySymbol property,
        CancellationToken cancellationToken)
    {
        if (property is null)
        {
            throw new ArgumentNullException(nameof(property));
        }

        return property.DeclaringSyntaxReferences.Any(reference =>
            reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (reference.GetSyntax(cancellationToken) is PropertyDeclarationSyntax
            {
                ExpressionBody: null,
                AccessorList.Accessors: var accessors
            } &&
            accessors.Count != 0 &&
            accessors.All(static accessor =>
                accessor is null ? throw new System.ArgumentNullException(nameof(accessor)) : (accessor.Body == null && accessor.ExpressionBody == null))));
    }

    internal static bool TryGetBackingField(
        IPropertySymbol property,
        out IFieldSymbol field)
    {
        if (property is null)
        {
            throw new ArgumentNullException(nameof(property));
        }

        field = property.ContainingType.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(candidate =>
                candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.AssociatedSymbol is IPropertySymbol associated &&
                AreSameProperty(associated, property)))!;
        return field != null;
    }

    private static bool AreSameProperty(
        IPropertySymbol left,
        IPropertySymbol right)
    {
        if (left is null)
        {
            throw new System.ArgumentNullException(nameof(left));
        }
        if (right is null)
        {
            throw new System.ArgumentNullException(nameof(right));
        }
        return SymbolEqualityComparer.Default.Equals(left, right) ||
            SymbolEqualityComparer.Default.Equals(
                left.OriginalDefinition,
                right.OriginalDefinition);
    }
}
