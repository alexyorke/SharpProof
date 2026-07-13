using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static IReadOnlyList<ISymbol> CollectLocalAndParameterSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbols = new List<ISymbol>();
        foreach (var node in CSharpSyntaxFacts.DescendantNodesInExecution(root))
        {
            if (node is not ExpressionSyntax expression) continue;

            if (SymbolMutationFacts.TryGetLocalOrParameterSymbol(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var symbol) &&
                symbols.All(existing => !SymbolEqualityComparer.Default.Equals(existing, symbol)))
                symbols.Add(symbol);
        }

        return symbols;
    }

    private static ExpressionSyntax UnwrapFactExpression(ExpressionSyntax expression)
    {
        return CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
    }

    private static bool IsDefaultExpressionSyntax(ExpressionSyntax expression)
    {
        return expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
               expression is DefaultExpressionSyntax;
    }

    private static ITypeSymbol? GetExpressionType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        return typeInfo.ConvertedType ?? typeInfo.Type;
    }
}
