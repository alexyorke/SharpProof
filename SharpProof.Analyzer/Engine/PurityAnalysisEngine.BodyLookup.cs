using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    private static SyntaxNode? GetBodySyntaxNode(IMethodSymbol methodSymbol, CancellationToken cancellationToken)
    {
        var declaringSyntaxes = methodSymbol.DeclaringSyntaxReferences;
        foreach (var syntaxRef in declaringSyntaxes)
        {
            var syntaxNode = syntaxRef.GetSyntax(cancellationToken);


            if (syntaxNode is ArrowExpressionClauseSyntax arrowExpressionClauseSyntax &&
                (arrowExpressionClauseSyntax.Parent is PropertyDeclarationSyntax ||
                 arrowExpressionClauseSyntax.Parent is IndexerDeclarationSyntax))
                return syntaxNode;

            if (syntaxNode is MethodDeclarationSyntax ||
                syntaxNode is LocalFunctionStatementSyntax ||
                syntaxNode is AnonymousFunctionExpressionSyntax ||
                syntaxNode is AccessorDeclarationSyntax ||
                syntaxNode is ConstructorDeclarationSyntax ||
                syntaxNode is OperatorDeclarationSyntax ||
                syntaxNode is ConversionOperatorDeclarationSyntax)
                return syntaxNode;
        }

        return null;
    }
}