using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
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
                !tryStatement.Catches.Any(catchClause => catchClause.Block.Span.Contains(site.SpanStart)))
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

        foreach (var statement in finallyBlock.Statements)
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

    private static bool StatementExitIsProven(
        StatementSyntax statement,
        SymbolicState pathState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        if (StatementDefinitelyExits(statement)) return true;

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
        if (!SymbolicReachabilityService.TryCollectBranchState(
            pathState,
            ifStatement.Condition,
            true,
            semanticModel,
            cancellationToken,
            out var trueState))
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

        if (!SymbolicReachabilityService.TryCollectBranchState(
            pathState,
            ifStatement.Condition,
            false,
            semanticModel,
            cancellationToken,
            out var falseState))
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
        return SymbolicReachabilityService.MergePathStates(
            baseState,
            SymbolicReachabilityService.CollectPathStateAt(
                statement,
                semanticModel,
                cancellationToken));
    }

    private static IEnumerable<(BlockSyntax Block, StatementSyntax ContainingStatement)> EnumerateContainingBlocks(
        SyntaxNode useNode)
    {
        for (var current = useNode; current != null; current = current.Parent)
            if (current is StatementSyntax statement &&
                statement.Parent is BlockSyntax block)
                yield return (block, statement);
    }

    private static bool AnyConditionSymbolMutatedInStatement(
        ExpressionSyntax condition,
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var conditionSymbols = GetReferencedLocalAndParameterSymbols(condition, semanticModel, cancellationToken);
        if (conditionSymbols.Count == 0) return false;

        foreach (var node in statement.DescendantNodesAndSelf(candidate =>
                     !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
            foreach (var symbol in conditionSymbols)
                if (MutatesSymbol(node, symbol, semanticModel, cancellationToken))
                    return true;

        return false;
    }

    private static IReadOnlyList<ISymbol> GetReferencedLocalAndParameterSymbols(
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var symbols = new List<ISymbol>();
        foreach (var node in root.DescendantNodesAndSelf(candidate =>
                     !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
        {
            if (node is not ExpressionSyntax expression) continue;

            var symbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
            if (symbol != null &&
                symbols.All(existing => !SymbolEqualityComparer.Default.Equals(existing, symbol)))
                symbols.Add(symbol);
        }

        return symbols;
    }
}
