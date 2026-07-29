namespace SharpProof.Analyzer;

internal static class AnalyzerSyntaxHelpers
{
    internal static Location GetCallableDeclarationLocation(SyntaxNode node)
    {
        return node switch
        {
            MethodDeclarationSyntax method => method.Identifier.GetLocation(),
            PropertyDeclarationSyntax property => property.Identifier.GetLocation(),
            IndexerDeclarationSyntax indexer => indexer.ThisKeyword.GetLocation(),
            ConstructorDeclarationSyntax constructor => constructor.Identifier.GetLocation(),
            AccessorDeclarationSyntax accessor => accessor.Parent?.Parent switch
            {
                PropertyDeclarationSyntax property => property.Identifier.GetLocation(),
                IndexerDeclarationSyntax indexer => indexer.ThisKeyword.GetLocation(),
                _ => accessor.Keyword.GetLocation()
            },
            OperatorDeclarationSyntax operation => operation.OperatorToken.GetLocation(),
            ConversionOperatorDeclarationSyntax conversion =>
                conversion.ImplicitOrExplicitKeyword.GetLocation(),
            _ => node.GetLocation()
        };
    }

    internal static Location GetCallableDeclarationLocation(
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        var reference = method.DeclaringSyntaxReferences.FirstOrDefault();
        return reference == null
            ? method.Locations.FirstOrDefault() ?? Location.None
            : GetCallableDeclarationLocation(reference.GetSyntax(cancellationToken));
    }
}
