namespace SharpProof.Analyzer;

internal static class PrimaryConstructorCallableInventory
{
    internal static bool TryGet(
        TypeDeclarationSyntax declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IMethodSymbol constructor)
    {
        constructor = null!;
        var parameters = declaration.ParameterList?.Parameters;
        if (parameters == null ||
            semanticModel.GetDeclaredSymbol(
                declaration,
                cancellationToken) is not INamedTypeSymbol type)
        {
            return false;
        }

        var matches = type.InstanceConstructors
            .Where(candidate =>
                candidate.MethodKind == MethodKind.Constructor &&
                candidate.DeclaringSyntaxReferences.Any(reference =>
                    reference.SyntaxTree == declaration.SyntaxTree &&
                    reference.GetSyntax(cancellationToken) is
                        TypeDeclarationSyntax owner &&
                    owner.Span == declaration.Span))
            .ToArray();
        return TrySingle(matches, out constructor);
    }

    internal static bool TryGetSynthesizedDefault(
        TypeDeclarationSyntax declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IMethodSymbol constructor)
    {
        constructor = null!;
        if (declaration.ParameterList != null ||
            semanticModel.GetDeclaredSymbol(
                declaration,
                cancellationToken) is not INamedTypeSymbol
                {
                    TypeKind: TypeKind.Class
                } type)
        {
            return false;
        }

        var matches = type.InstanceConstructors
            .Where(static candidate =>
                candidate.IsImplicitlyDeclared &&
                candidate.Parameters.IsEmpty)
            .ToArray();
        return TrySingle(matches, out constructor);
    }

    internal static bool IsDeclaration(
        IMethodSymbol method,
        SyntaxNode? declaration,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken)
    {
        return declaration is TypeDeclarationSyntax type &&
            semanticModel != null &&
            TryGet(type, semanticModel, cancellationToken, out var constructor) &&
            SymbolEqualityComparer.Default.Equals(
                constructor,
                ContractClauseInventoryBuilder.NormalizeCallable(method));
    }

    private static bool TrySingle(
        IMethodSymbol[] matches,
        out IMethodSymbol constructor)
    {
        constructor = matches.Length == 1
            ? ContractClauseInventoryBuilder.NormalizeCallable(matches[0])
            : null!;
        return constructor != null;
    }
}
