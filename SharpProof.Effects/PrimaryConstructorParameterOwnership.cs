using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal static class PrimaryConstructorParameterOwnership
{
    internal static bool IsReceiverBacked(
        IParameterSymbol parameter,
        IMethodSymbol currentMethod)
    {
        if (currentMethod.IsStatic ||
            parameter.ContainingSymbol is not IMethodSymbol
            {
                MethodKind: MethodKind.Constructor
            } constructor ||
            SymbolEqualityComparer.Default.Equals(
                constructor.OriginalDefinition,
                currentMethod.OriginalDefinition) ||
            !SymbolEqualityComparer.Default.Equals(
                constructor.ContainingType.OriginalDefinition,
                currentMethod.ContainingType.OriginalDefinition))
        {
            return false;
        }

        return HasPrimaryConstructorParameter(
            parameter.DeclaringSyntaxReferences,
            recordsOnly: false);
    }

    internal static bool IsPositionalRecordProperty(
        IPropertySymbol property)
    {
        return property.ContainingType.IsRecord &&
            property.GetMethod?.IsImplicitlyDeclared == true &&
            HasPrimaryConstructorParameter(
                property.DeclaringSyntaxReferences,
                recordsOnly: true);
    }

    private static bool HasPrimaryConstructorParameter(
        IEnumerable<SyntaxReference> references,
        bool recordsOnly)
    {
        return references.Any(reference =>
            reference.GetSyntax() is ParameterSyntax
            {
                Parent.Parent: TypeDeclarationSyntax declaration
            } && (!recordsOnly || declaration is RecordDeclarationSyntax));
    }
}
