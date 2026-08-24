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
                candidate.Parameters.Length == parameters.Value.Count &&
                candidate.Parameters.Select(static parameter => parameter.Name)
                    .SequenceEqual(
                        parameters.Value.Select(static parameter =>
                            parameter.Identifier.ValueText),
                        StringComparer.Ordinal))
            .ToArray();
        var primary = matches.FirstOrDefault(static candidate =>
            candidate.IsImplicitlyDeclared);
        if (primary == null && matches.Length == 1)
        {
            primary = matches[0];
        }
        if (primary == null)
        {
            return false;
        }

        constructor = ContractClauseInventoryBuilder.NormalizeCallable(
            primary);
        return true;
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
}
