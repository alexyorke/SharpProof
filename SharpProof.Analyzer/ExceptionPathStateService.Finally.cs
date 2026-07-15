using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer;

internal static partial class ExceptionPathStateService
{
    internal static bool IsShadowedByPathSensitiveThrowingFinally(
        SyntaxNode site,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        foreach (var tryStatement in site.Ancestors().OfType<TryStatementSyntax>())
        {
            if (!tryStatement.Span.Contains(site.SpanStart) ||
                tryStatement.Finally?.Block is not { } finallyBlock ||
                finallyBlock.Span.Contains(site.SpanStart))
                continue;

            if (!tryStatement.Block.Span.Contains(site.SpanStart) &&
                !tryStatement.Catches.Any(catchClause =>
                    catchClause.Block.Span.Contains(site.SpanStart) ||
                    catchClause.Filter?.Span.Contains(site.SpanStart) == true))
                continue;

            if (FinallyBlockIsProvenToExit(site, finallyBlock, semanticModel, cancellationToken, smtAnalysis))
                return true;
        }

        return false;
    }

    private static bool FinallyBlockIsProvenToExit(
        SyntaxNode site,
        BlockSyntax finallyBlock,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        var pathState = CollectPathStateForUse(site, semanticModel, cancellationToken);
        if (!IsPathStateReachable(pathState, smtAnalysis)) return false;
        SymbolicStatementStateTransfer.AddCompletedBlockStateFacts(
            ref pathState,
            finallyBlock,
            semanticModel,
            cancellationToken);
        return !IsPathStateReachable(pathState, smtAnalysis);
    }

}
