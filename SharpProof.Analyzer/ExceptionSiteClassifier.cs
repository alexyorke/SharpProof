using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

using static SharpProof.Analyzer.ExceptionFlowAnalyzer;

namespace SharpProof.Analyzer;

internal static partial class ExceptionSiteClassifier
{
    internal static IEnumerable<SyntaxNode> GetThrowNodes(SyntaxNode methodNode)
    {
        return GetRelevantDescendants<SyntaxNode>(methodNode)
            .Where(node => node is ThrowStatementSyntax || node is ThrowExpressionSyntax);
    }

    internal static ITypeSymbol? GetThrownExceptionType(
        SyntaxNode throwNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (throwNode is not ThrowStatementSyntax and not ThrowExpressionSyntax) return null;

        return SymbolicRuntimeExceptionFacts.GetThrownExceptionType(
            throwNode,
            semanticModel,
            cancellationToken,
            true);
    }

    internal static bool IsDefinitelyThrowNull(
        SyntaxNode throwNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        return SymbolicRuntimeExceptionFacts.TryGetThrowExpression(throwNode, out var expression) &&
               IsDefinitelyNullExpression(expression, throwNode, semanticModel, cancellationToken, smtAnalysis);
    }

    internal static bool IsShadowedByDefinitelyThrowingFinally(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var tryStatement in site.Ancestors().OfType<TryStatementSyntax>())
        {
            if (!tryStatement.Span.Contains(site.SpanStart)) continue;

            if (tryStatement.Finally == null ||
                !SymbolicControlFlowFacts.StatementDefinitelyExits(
                    tryStatement.Finally.Block,
                    semanticModel,
                    cancellationToken))
                continue;

            if (tryStatement.Finally.Block.Span.Contains(site.SpanStart)) continue;

            if (tryStatement.Block.Span.Contains(site.SpanStart) ||
                tryStatement.Catches.Any(catchClause =>
                    catchClause.Block.Span.Contains(site.SpanStart) ||
                    catchClause.Filter?.Span.Contains(site.SpanStart) == true))
                return true;
        }

        return false;
    }

}
