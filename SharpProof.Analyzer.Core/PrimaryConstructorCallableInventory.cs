using System.Collections.Immutable;
namespace SharpProof.Analyzer;

internal static class PrimaryConstructorCallableInventory
{
    internal static bool TryGet(
        TypeDeclarationSyntax declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IMethodSymbol constructor)
    {
        declaration = ArgumentNullGuard.NotNull(declaration, nameof(declaration));
        semanticModel = ArgumentNullGuard.NotNull(semanticModel, nameof(semanticModel));
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
                candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.DeclaringSyntaxReferences.Any(reference =>
                    reference is null ? throw new System.ArgumentNullException(nameof(reference)) : (reference.SyntaxTree == declaration.SyntaxTree &&
                    reference.GetSyntax(cancellationToken) is
                        TypeDeclarationSyntax owner &&
                    owner.Span == declaration.Span))))
            .ToImmutableArray();
        return TrySingle(matches, out constructor);
    }

    internal static bool TryGetSynthesizedDefault(
        TypeDeclarationSyntax declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IMethodSymbol constructor)
    {
        declaration = ArgumentNullGuard.NotNull(declaration, nameof(declaration));
        semanticModel = ArgumentNullGuard.NotNull(semanticModel, nameof(semanticModel));
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
                candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (candidate.IsImplicitlyDeclared &&
                candidate.Parameters.IsEmpty))
            .ToImmutableArray();
        return TrySingle(matches, out constructor);
    }

    private static bool TrySingle(
        ImmutableArray<IMethodSymbol> matches,
        out IMethodSymbol constructor)
    {
        RepositoryParameterGuard.RequireInitialized(matches.IsDefault, nameof(matches), "Array must be initialized.");
        constructor = matches.Length == 1
            ? ContractClauseInventoryBuilder.NormalizeCallable(matches[0])
            : null!;
        return constructor != null;
    }
}
