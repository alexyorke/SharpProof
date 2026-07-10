using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static bool AnySymbolAssignedBeforeUse(
        SyntaxNode branchRoot,
        int useSpanStart,
        IReadOnlyCollection<ISymbol> symbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return AnySymbolAssignedBetween(branchRoot, branchRoot.SpanStart - 1, useSpanStart, symbols, semanticModel,
            cancellationToken);
    }

    private static bool AnyReferencedSymbolAssignedBeforeUse(
        SyntaxNode condition,
        SyntaxNode branchRoot,
        int useSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var referencedSymbols = GetReferencedLocalAndParameterSymbols(condition, semanticModel, cancellationToken);
        return referencedSymbols.Count != 0 &&
               AnySymbolAssignedBeforeUse(branchRoot, useSpanStart, referencedSymbols, semanticModel,
                   cancellationToken);
    }

    private static bool AnyReferencedSymbolAssignedBetween(
        SyntaxNode condition,
        SyntaxNode root,
        int afterSpanStart,
        int beforeSpanStart,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var referencedSymbols = GetReferencedLocalAndParameterSymbols(condition, semanticModel, cancellationToken);
        return referencedSymbols.Count != 0 &&
               AnySymbolAssignedBetween(root, afterSpanStart, beforeSpanStart, referencedSymbols, semanticModel,
                   cancellationToken);
    }

    private static bool IsSymbolAssignedBetween(
        SyntaxNode root,
        int afterSpanStart,
        int beforeSpanStart,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in
                 root.DescendantNodes(candidate => !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
        {
            if (node.SpanStart <= afterSpanStart || node.SpanStart >= beforeSpanStart) continue;

            if ((TryGetMutatedLocalOrParameterSymbol(node, semanticModel, cancellationToken, out var mutatedSymbol) &&
                 SymbolEqualityComparer.Default.Equals(mutatedSymbol, symbol)) ||
                MutatesSymbol(node, symbol, semanticModel, cancellationToken))
                return true;
        }

        return false;
    }

    private static bool AnySymbolAssignedBetween(
        SyntaxNode root,
        int afterSpanStart,
        int beforeSpanStart,
        IReadOnlyCollection<ISymbol> symbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (symbols.Count == 0) return false;

        foreach (var symbol in symbols)
            if (IsSymbolAssignedBetween(root, afterSpanStart, beforeSpanStart, symbol, semanticModel,
                    cancellationToken))
                return true;

        return false;
    }

    private static bool AnySymbolMutatedInSyntax(
        SyntaxNode root,
        IReadOnlyCollection<ISymbol> symbols,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (symbols.Count == 0) return false;

        foreach (var node in root.DescendantNodesAndSelf(candidate =>
                     !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
        {
            if (!TryGetMutatedLocalOrParameterSymbol(node, semanticModel, cancellationToken, out var mutatedSymbol))
                continue;

            foreach (var symbol in symbols)
                if (SymbolEqualityComparer.Default.Equals(mutatedSymbol, symbol))
                    return true;
        }

        return false;
    }

    private static bool MutatesSymbol(
        SyntaxNode node,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return node switch
        {
            AssignmentExpressionSyntax assignment =>
                ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken) ||
                TupleAssignmentMutatesSymbol(assignment, symbol, semanticModel, cancellationToken),
            PrefixUnaryExpressionSyntax prefixUnary
                when prefixUnary.IsKind(SyntaxKind.PreIncrementExpression) ||
                     prefixUnary.IsKind(SyntaxKind.PreDecrementExpression) =>
                ExpressionMatchesSymbol(prefixUnary.Operand, symbol, semanticModel, cancellationToken),
            PostfixUnaryExpressionSyntax postfixUnary
                when postfixUnary.IsKind(SyntaxKind.PostIncrementExpression) ||
                     postfixUnary.IsKind(SyntaxKind.PostDecrementExpression) =>
                ExpressionMatchesSymbol(postfixUnary.Operand, symbol, semanticModel, cancellationToken),
            ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None) =>
                ExpressionMatchesSymbol(argument.Expression, symbol, semanticModel, cancellationToken),
            _ => false
        };
    }

    private static bool TupleAssignmentMutatesSymbol(
        AssignmentExpressionSyntax assignment,
        ISymbol symbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (UnwrapFactExpression(assignment.Left) is not TupleExpressionSyntax leftTuple) return false;

        return leftTuple.Arguments.Any(argument =>
            ExpressionMatchesSymbol(argument.Expression, symbol, semanticModel, cancellationToken));
    }

    private static bool TryGetMutatedLocalOrParameterSymbol(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ISymbol symbol)
    {
        symbol = null!;
        var mutatedExpression = node switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left,
            PrefixUnaryExpressionSyntax prefixUnary
                when prefixUnary.IsKind(SyntaxKind.PreIncrementExpression) ||
                     prefixUnary.IsKind(SyntaxKind.PreDecrementExpression) =>
                prefixUnary.Operand,
            PostfixUnaryExpressionSyntax postfixUnary
                when postfixUnary.IsKind(SyntaxKind.PostIncrementExpression) ||
                     postfixUnary.IsKind(SyntaxKind.PostDecrementExpression) =>
                postfixUnary.Operand,
            ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None) => argument.Expression,
            _ => null
        };

        if (mutatedExpression == null) return false;

        var candidate = GetLocalOrParameterSymbol(mutatedExpression, semanticModel, cancellationToken);
        if (candidate == null) return false;

        symbol = candidate;
        return true;
    }

    private static bool StatementDefinitelyExits(StatementSyntax statement)
    {
        switch (statement)
        {
            case ReturnStatementSyntax:
            case ThrowStatementSyntax:
            case ContinueStatementSyntax:
            case BreakStatementSyntax:
                return true;
            case YieldStatementSyntax yieldStatement:
                return yieldStatement.IsKind(SyntaxKind.YieldBreakStatement);
            case BlockSyntax block:
                return block.Statements.LastOrDefault() is { } lastStatement &&
                       StatementDefinitelyExits(lastStatement);
            case IfStatementSyntax ifStatement:
                return StatementDefinitelyExits(ifStatement.Statement) &&
                       ifStatement.Else?.Statement is { } elseStatement &&
                       StatementDefinitelyExits(elseStatement);
            default:
                return false;
        }
    }
}