using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using static SharpProof.Analyzer.ExceptionFlowAnalyzer;

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
        var pathState = CollectExceptionSitePathState(
            site,
            finallyBlock,
            semanticModel,
            cancellationToken);
        if (!IsPathStateReachable(pathState, smtAnalysis)) return false;
        return BlockExitIsProven(finallyBlock, pathState, semanticModel, cancellationToken, smtAnalysis);
    }

    private static bool StatementExitIsProven(
        StatementSyntax statement,
        SymbolicState pathState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        if (SymbolicControlFlowFacts.StatementDefinitelyExits(statement, semanticModel, cancellationToken)) return true;

        switch (statement)
        {
            case BlockSyntax block:
                return BlockExitIsProven(block, pathState, semanticModel, cancellationToken, smtAnalysis);
            case IfStatementSyntax ifStatement:
                return IfStatementExitIsProven(ifStatement, pathState, semanticModel, cancellationToken,
                    smtAnalysis);
            default:
                return false;
        }
    }

    private static bool BlockExitIsProven(
        BlockSyntax block,
        SymbolicState pathState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        foreach (var statement in block.Statements)
        {
            var statementState = GetStatementEntryPathState(
                pathState,
                statement,
                semanticModel,
                cancellationToken);
            if (StatementExitIsProven(statement, statementState, semanticModel, cancellationToken, smtAnalysis))
                return true;

            if (!IsPathStateReachable(statementState, smtAnalysis)) return false;
        }

        return false;
    }

    private static bool IfStatementExitIsProven(
        IfStatementSyntax ifStatement,
        SymbolicState pathState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        var trueBranch = SymbolicReachabilityService.ApplyBranchFacts(
            pathState,
            ifStatement.Condition,
            true,
            semanticModel,
            cancellationToken);
        if (trueBranch is not { IsExact: true, Value: { } trueState })
            return false;

        var trueReachable = IsPathStateReachable(trueState, smtAnalysis);
        var trueExits = !trueReachable ||
                        StatementExitIsProven(ifStatement.Statement, trueState, semanticModel, cancellationToken,
                            smtAnalysis);

        if (ifStatement.Else?.Statement is not { } elseStatement)
            return trueReachable && trueExits &&
                   SymbolicSemanticPipeline.LowerCondition(
                       ifStatement.Condition,
                       new SymbolicLoweringContext(semanticModel, cancellationToken)) is
                   { IsExact: true, Value: { } condition } &&
                   SymbolicReachabilityService.ClassifyStateConditionTruth(pathState, condition, smtAnalysis)
                       .Info.Status == SymbolicProofStatus.ProvenTrue;

        var falseBranch = SymbolicReachabilityService.ApplyBranchFacts(
            pathState,
            ifStatement.Condition,
            false,
            semanticModel,
            cancellationToken);
        if (falseBranch is not { IsExact: true, Value: { } falseState })
            return false;

        var falseReachable = IsPathStateReachable(falseState, smtAnalysis);
        var falseExits = !falseReachable ||
                         StatementExitIsProven(elseStatement, falseState, semanticModel, cancellationToken,
                             smtAnalysis);

        return trueExits && falseExits && (trueReachable || falseReachable);
    }

    private static SymbolicState GetStatementEntryPathState(
        SymbolicState baseState,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return SymbolicReachabilityService.CollectPathStateAt(
            statement,
            semanticModel,
            cancellationToken,
            baseState);
    }

    private static bool AnyConditionSymbolMutatedInStatement(
        ExpressionSyntax condition,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var conditionSymbols = SymbolMutationFacts.GetReferencedLocalAndParameterSymbols(
            condition,
            semanticModel,
            cancellationToken);
        if (conditionSymbols.Count == 0) return false;

        foreach (var node in CSharpSyntaxFacts.DescendantNodesInExecution(statement))
            foreach (var symbol in conditionSymbols)
                if (SymbolMutationFacts.MutatesSymbol(node, symbol, semanticModel, cancellationToken))
                    return true;

        return false;
    }

}
