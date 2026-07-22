namespace SharpProof.Symbolic;
internal static class SymbolicMethodLikeDeclaration {
    internal static bool IsSupported(SyntaxNode node, bool includeAnonymousFunctions = false, bool includeDestructors = false)
        => node switch {
            DestructorDeclarationSyntax => includeDestructors,
            BaseMethodDeclarationSyntax => true,
            AccessorDeclarationSyntax or PropertyDeclarationSyntax or IndexerDeclarationSyntax or
                LocalFunctionStatementSyntax => true,
            AnonymousFunctionExpressionSyntax => includeAnonymousFunctions,
            _ => false
        };
    internal static IMethodSymbol? GetMethodSymbol(SyntaxNode declaration, SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (declaration is AnonymousFunctionExpressionSyntax anonymousFunction)
            return semanticModel.GetOperation(anonymousFunction, cancellationToken) is IAnonymousFunctionOperation lambda
                ? lambda.Symbol
                : null;
        return semanticModel.GetDeclaredSymbol(declaration, cancellationToken) switch {
            IMethodSymbol method => method,
            IPropertySymbol property => property.GetMethod,
            _ => null
        };
    }
}
