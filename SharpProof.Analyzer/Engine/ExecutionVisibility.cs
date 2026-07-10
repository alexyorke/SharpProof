using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Engine;

internal static partial class ExecutionVisibility
{
    public static bool IsInStaticallyUnreachableBranch(
        SyntaxNode syntaxNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return IsInStaticallyUnreachableBranchUsingSmt(syntaxNode, semanticModel, cancellationToken, null);
    }

    public static bool IsInStaticallyUnreachableBranchUsingSmt(
        SyntaxNode syntaxNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis = null)
    {
        foreach (var ancestor in syntaxNode.Ancestors())
            if (ancestor is IfStatementSyntax ifStatement)
            {
                if (IsConditionAlwaysFalseAt(ifStatement.Condition, ifStatement, semanticModel, cancellationToken,
                        smtAnalysis) &&
                    ifStatement.Statement.Span.Contains(syntaxNode.SpanStart))
                    return true;

                if (IsConditionAlwaysTrueAt(ifStatement.Condition, ifStatement, semanticModel, cancellationToken,
                        smtAnalysis) &&
                    ifStatement.Else?.Statement.Span.Contains(syntaxNode.SpanStart) == true)
                    return true;
            }
            else if (ancestor is ConditionalExpressionSyntax conditionalExpression)
            {
                if (IsConditionAlwaysFalseAt(conditionalExpression.Condition, conditionalExpression, semanticModel,
                        cancellationToken, smtAnalysis) &&
                    conditionalExpression.WhenTrue.Span.Contains(syntaxNode.SpanStart))
                    return true;

                if (IsConditionAlwaysTrueAt(conditionalExpression.Condition, conditionalExpression, semanticModel,
                        cancellationToken, smtAnalysis) &&
                    conditionalExpression.WhenFalse.Span.Contains(syntaxNode.SpanStart))
                    return true;
            }
            else if (ancestor is ConditionalAccessExpressionSyntax conditionalAccessExpression)
            {
                if (conditionalAccessExpression.WhenNotNull.Span.Contains(syntaxNode.SpanStart) &&
                    IsReferenceKnownNullAt(
                        conditionalAccessExpression.Expression,
                        conditionalAccessExpression,
                        semanticModel,
                        cancellationToken,
                        smtAnalysis))
                    return true;
            }
            else if (ancestor is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
                    binaryExpression.Right.Span.Contains(syntaxNode.SpanStart) &&
                    IsConditionAlwaysFalseAt(binaryExpression.Left, binaryExpression, semanticModel, cancellationToken,
                        smtAnalysis))
                    return true;

                if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression) &&
                    binaryExpression.Right.Span.Contains(syntaxNode.SpanStart) &&
                    IsConditionAlwaysTrueAt(binaryExpression.Left, binaryExpression, semanticModel, cancellationToken,
                        smtAnalysis))
                    return true;

                if (binaryExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                    binaryExpression.Right.Span.Contains(syntaxNode.SpanStart))
                    if (IsReferenceKnownNonNullAt(
                            binaryExpression.Left,
                            binaryExpression,
                            semanticModel,
                            cancellationToken,
                            smtAnalysis))
                        return true;
            }
            else if (ancestor is WhileStatementSyntax whileStatement)
            {
                if (whileStatement.Statement.Span.Contains(syntaxNode.SpanStart) &&
                    IsConditionAlwaysFalseAt(whileStatement.Condition, whileStatement, semanticModel, cancellationToken,
                        smtAnalysis))
                    return true;
            }
            else if (ancestor is ForStatementSyntax forStatement)
            {
                if (forStatement.Condition != null &&
                    forStatement.Statement.Span.Contains(syntaxNode.SpanStart) &&
                    SymbolicReachabilityService.IsForInitialEntryConditionAlwaysFalse(
                        forStatement,
                        semanticModel,
                        cancellationToken,
                        smtAnalysis))
                    return true;
            }
            else if (ancestor is SwitchStatementSyntax switchStatement &&
                     IsInUnreachableSwitchStatementSection(syntaxNode, switchStatement, semanticModel,
                         cancellationToken, smtAnalysis))
            {
                return true;
            }
            else if (ancestor is SwitchExpressionSyntax switchExpression &&
                     IsInUnreachableSwitchExpressionArm(syntaxNode, switchExpression, semanticModel, cancellationToken,
                         smtAnalysis))
            {
                return true;
            }

        if (IsProgramPointUnreachableUsingSharedFacts(syntaxNode, semanticModel, cancellationToken, smtAnalysis))
            return true;
        return false;
    }

    public static bool IsEvaluationPathUnsatisfiableUsingSmt(
        SyntaxNode syntaxNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        IReadOnlyCollection<SmtFormula> basePathConditions,
        Func<ISymbol, int>? getSymbolVersion,
        SmtAnalysisService smtAnalysis)
    {
        if (basePathConditions.Count == 0) return false;

        var pathConditions = basePathConditions.ToList();
        var originalCount = pathConditions.Count;
        foreach (var ancestor in syntaxNode.Ancestors())
        {
            AddEvaluationPathFacts(
                syntaxNode,
                ancestor,
                semanticModel,
                cancellationToken,
                pathConditions,
                getSymbolVersion);

            if (pathConditions.Count > originalCount &&
                ArePathConditionsUnsatisfiableAt(pathConditions, syntaxNode, smtAnalysis))
                return true;
        }

        return false;
    }
}