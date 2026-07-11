using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
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
        var pathConditions = CollectExceptionSitePathConditions(
            site,
            finallyBlock,
            semanticModel,
            cancellationToken);
        if (!SymbolicPathConditionsAreSatisfiable(pathConditions, site, smtAnalysis)) return false;

        foreach (var statement in finallyBlock.Statements)
        {
            if (StatementExitIsProven(statement, pathConditions, semanticModel, cancellationToken, smtAnalysis))
                return true;

            AddPriorStatementFacts(statement, semanticModel, cancellationToken, pathConditions);
            if (!SymbolicPathConditionsAreSatisfiable(pathConditions, statement, smtAnalysis)) return false;
        }

        return false;
    }

    private static bool StatementExitIsProven(
        StatementSyntax statement,
        IReadOnlyCollection<SmtFormula> pathConditions,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        if (StatementDefinitelyExits(statement)) return true;

        switch (statement)
        {
            case BlockSyntax block:
                return BlockExitIsProven(block, pathConditions, semanticModel, cancellationToken, smtAnalysis);
            case IfStatementSyntax ifStatement:
                return IfStatementExitIsProven(ifStatement, pathConditions, semanticModel, cancellationToken,
                    smtAnalysis);
            default:
                return false;
        }
    }

    private static bool BlockExitIsProven(
        BlockSyntax block,
        IReadOnlyCollection<SmtFormula> pathConditions,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        var blockConditions = pathConditions.ToList();
        foreach (var statement in block.Statements)
        {
            if (StatementExitIsProven(statement, blockConditions, semanticModel, cancellationToken, smtAnalysis))
                return true;

            AddPriorStatementFacts(statement, semanticModel, cancellationToken, blockConditions);
            if (!SymbolicPathConditionsAreSatisfiable(blockConditions, statement, smtAnalysis)) return false;
        }

        return false;
    }

    private static bool IfStatementExitIsProven(
        IfStatementSyntax ifStatement,
        IReadOnlyCollection<SmtFormula> pathConditions,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis)
    {
        var trueConditions = SymbolicReachabilityService.TryCollectBranchConditions(
            pathConditions,
            ifStatement.Condition,
            true,
            semanticModel,
            cancellationToken);
        if (trueConditions == null) return false;

        var trueReachable = SymbolicPathConditionsAreSatisfiable(trueConditions, ifStatement.Condition, smtAnalysis);
        var trueExits = !trueReachable ||
                        StatementExitIsProven(ifStatement.Statement, trueConditions, semanticModel, cancellationToken,
                            smtAnalysis);

        if (ifStatement.Else?.Statement is not { } elseStatement)
            return trueReachable && trueExits &&
                   SymbolicReachabilityService.PathConditionsImplyBranch(
                       pathConditions,
                       ifStatement.Condition,
                       true,
                       semanticModel,
                       cancellationToken,
                       smtAnalysis);

        var falseConditions = SymbolicReachabilityService.TryCollectBranchConditions(
            pathConditions,
            ifStatement.Condition,
            false,
            semanticModel,
            cancellationToken);
        if (falseConditions == null) return false;

        var falseReachable = SymbolicPathConditionsAreSatisfiable(falseConditions, ifStatement.Condition, smtAnalysis);
        var falseExits = !falseReachable ||
                         StatementExitIsProven(elseStatement, falseConditions, semanticModel, cancellationToken,
                             smtAnalysis);

        return trueExits && falseExits && (trueReachable || falseReachable);
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
