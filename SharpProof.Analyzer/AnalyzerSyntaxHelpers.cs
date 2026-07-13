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
            LocalFunctionStatementSyntax localFunctionStatement => localFunctionStatement.Identifier.GetLocation(),
            ConstructorDeclarationSyntax constructorDeclaration => constructorDeclaration.Identifier.GetLocation(),
            AccessorDeclarationSyntax accessorDeclaration => accessorDeclaration.Keyword.GetLocation(),
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
}
