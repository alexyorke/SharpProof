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

            var symbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
            if (symbol != null &&
                symbols.All(existing => !SymbolEqualityComparer.Default.Equals(existing, symbol)))
                symbols.Add(symbol);
        }

        return symbols;
    }

    private static ISymbol? GetLocalOrParameterSymbol(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return SymbolicFactFactory.TryGetDirectLocalOrParameterSymbol(
            UnwrapFactExpression(expression),
            semanticModel,
            cancellationToken,
            out var symbol)
            ? symbol
            : null;
    }

    private static ExpressionSyntax UnwrapFactExpression(ExpressionSyntax expression)
    {
        return CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
    }

    private static bool ExpressionMatchesSymbol(
        ExpressionSyntax expression,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var expressionSymbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
        return expressionSymbol != null && SymbolEqualityComparer.Default.Equals(expressionSymbol, symbol);
    }

    private static bool ExpressionReferencesSymbol(
        SyntaxNode root,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in CSharpSyntaxFacts.DescendantNodesInExecution(root))
            if (node is ExpressionSyntax expression &&
                ExpressionMatchesSymbol(expression, symbol, semanticModel, cancellationToken))
                return true;

        return false;
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
