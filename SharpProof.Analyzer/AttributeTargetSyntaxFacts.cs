namespace SharpProof.Analyzer;

internal static class AttributeTargetSyntaxFacts {
    internal static bool IsGetterAliasTarget(SyntaxNode? node) {
        return node switch {
            PropertyDeclarationSyntax property =>
                property.ExpressionBody != null || HasGetter(property.AccessorList),
            IndexerDeclarationSyntax indexer =>
                indexer.ExpressionBody != null || HasGetter(indexer.AccessorList),
            _ => false
        };
    }

    private static bool HasGetter(AccessorListSyntax? accessorList) {
        return accessorList?.Accessors.Any(static accessor =>
            accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) == true;
    }
}
