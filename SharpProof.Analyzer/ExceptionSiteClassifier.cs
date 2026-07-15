using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal static partial class ExceptionSiteClassifier
{
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
