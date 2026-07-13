using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Analyzer;

internal static class AnalyzerSyntaxHelpers
{
    internal static bool IsBodylessAutoPropertyGetter(MethodBodyAnalysisContext context)
    {
        return context.MethodSymbol.MethodKind == MethodKind.PropertyGet &&
               !context.MethodSymbol.IsAbstract &&
               context.MethodSymbol.ContainingType?.TypeKind != TypeKind.Interface &&
               context.Node is AccessorDeclarationSyntax accessor &&
               accessor.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GetAccessorDeclaration) &&
               accessor.Body == null &&
               accessor.ExpressionBody == null &&
               !accessor.SemicolonToken.IsMissing;
    }

    internal static Location GetCallableDeclarationLocation(SyntaxNode node)
    {
        return node switch
        {
            MethodDeclarationSyntax methodDeclaration => methodDeclaration.Identifier.GetLocation(),
            PropertyDeclarationSyntax propertyDeclaration => propertyDeclaration.Identifier.GetLocation(),
            IndexerDeclarationSyntax indexerDeclaration => indexerDeclaration.ThisKeyword.GetLocation(),
            LocalFunctionStatementSyntax localFunctionStatement => localFunctionStatement.Identifier.GetLocation(),
            ConstructorDeclarationSyntax constructorDeclaration => constructorDeclaration.Identifier.GetLocation(),
            AccessorDeclarationSyntax accessorDeclaration => accessorDeclaration.Parent?.Parent switch
            {
                PropertyDeclarationSyntax propertyDeclaration => propertyDeclaration.Identifier.GetLocation(),
                IndexerDeclarationSyntax indexerDeclaration => indexerDeclaration.ThisKeyword.GetLocation(),
                _ => accessorDeclaration.Keyword.GetLocation()
            },
            OperatorDeclarationSyntax operatorDeclaration => operatorDeclaration.OperatorToken.GetLocation(),
            ConversionOperatorDeclarationSyntax conversionOperatorDeclaration => conversionOperatorDeclaration
                .ImplicitOrExplicitKeyword.GetLocation(),
            _ => node.GetLocation()
        };
    }

    internal static Location GetCallableDeclarationLocation(
        IMethodSymbol methodSymbol,
        CancellationToken cancellationToken)
    {
        var syntaxReference = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
        return syntaxReference == null
            ? methodSymbol.Locations.First()
            : GetCallableDeclarationLocation(syntaxReference.GetSyntax(cancellationToken));
    }

    internal static bool HasResultValue(IMethodSymbol methodSymbol)
    {
        return methodSymbol.MethodKind is not (MethodKind.Constructor or MethodKind.StaticConstructor) &&
               !methodSymbol.ReturnsVoid;
    }

    internal static bool IsCompilerMarkedUnreachable(
        SyntaxNode syntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return semanticModel.GetDiagnostics(syntax.Span, cancellationToken)
            .Any(static diagnostic => diagnostic.Id == "CS0162");
    }

    internal static bool BodyEndPointIsReachable(BlockSyntax body, SemanticModel semanticModel)
    {
        var controlFlow = semanticModel.AnalyzeControlFlow(body);
        return controlFlow == null ||
               !controlFlow.Succeeded ||
               controlFlow.EndPointIsReachable;
    }

    internal static string GetFirstAttributeArgumentText(
        AttributeData attribute,
        CancellationToken cancellationToken)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is AttributeSyntax attributeSyntax)
            return attributeSyntax.ArgumentList?.Arguments.FirstOrDefault()?.ToString() ?? "<missing>";

        return "<missing>";
    }

    internal static string GetAttributeArgumentListText(
        AttributeData attribute,
        CancellationToken cancellationToken)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is AttributeSyntax attributeSyntax)
            return attributeSyntax.ArgumentList == null
                ? "<missing>"
                : string.Join(", ",
                    attributeSyntax.ArgumentList.Arguments.Select(static argument => argument.ToString()));

        return "<missing>";
    }
}
