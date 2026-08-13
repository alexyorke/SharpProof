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

        return parameter.DeclaringSyntaxReferences.Any(static reference =>
            reference.GetSyntax() is ParameterSyntax
            {
                Parent.Parent: TypeDeclarationSyntax
            });
    }

    internal static bool IsPositionalRecordProperty(
        IPropertySymbol property)
    {
        return property.ContainingType.IsRecord &&
            property.GetMethod?.IsImplicitlyDeclared == true &&
            property.DeclaringSyntaxReferences.Any(static reference =>
                reference.GetSyntax() is ParameterSyntax
                {
                    Parent.Parent: RecordDeclarationSyntax
                });
    }
}
