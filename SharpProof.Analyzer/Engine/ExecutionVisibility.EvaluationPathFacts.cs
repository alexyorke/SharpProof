using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;
using SearchLib.Smt;

namespace SharpProof.Analyzer.Engine
{
    internal static partial class ExecutionVisibility
    {

        private static void AddEvaluationPathFacts(
            SyntaxNode syntaxNode,
            SyntaxNode ancestor,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (ancestor is IfStatementSyntax ifStatement)
            {
                if (ifStatement.Statement.Span.Contains(syntaxNode.SpanStart))
                {
                    AddBranchConditionFact(
                        ifStatement.Condition,
                        branchWhenTrue: true,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion);
                }
                else if (ifStatement.Else?.Statement.Span.Contains(syntaxNode.SpanStart) == true)
                {
                    AddBranchConditionFact(
                        ifStatement.Condition,
                        branchWhenTrue: false,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion);
                }

                return;
            }

            if (ancestor is ConditionalExpressionSyntax conditionalExpression)
            {
                if (conditionalExpression.WhenTrue.Span.Contains(syntaxNode.SpanStart))
                {
                    AddBranchConditionFact(
                        conditionalExpression.Condition,
                        branchWhenTrue: true,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion);
                }
                else if (conditionalExpression.WhenFalse.Span.Contains(syntaxNode.SpanStart))
                {
                    AddBranchConditionFact(
                        conditionalExpression.Condition,
                        branchWhenTrue: false,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion);
                }

                return;
            }

            if (ancestor is BinaryExpressionSyntax binaryExpression)
            {
                if (!binaryExpression.Right.Span.Contains(syntaxNode.SpanStart))
                {
                    return;
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression))
                {
                    AddBranchConditionFact(
                        binaryExpression.Left,
                        branchWhenTrue: true,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion);
                }
                else if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
                {
                    AddBranchConditionFact(
                        binaryExpression.Left,
                        branchWhenTrue: false,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion);
                }
                else if (binaryExpression.IsKind(SyntaxKind.CoalesceExpression))
                {
                    AddReferenceNullStateFact(
                        binaryExpression.Left,
                        equalToNull: true,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion);
                }

                return;
            }

            if (ancestor is ConditionalAccessExpressionSyntax conditionalAccessExpression &&
                conditionalAccessExpression.WhenNotNull.Span.Contains(syntaxNode.SpanStart))
            {
                AddReferenceNullStateFact(
                    conditionalAccessExpression.Expression,
                    equalToNull: false,
                    semanticModel,
                    cancellationToken,
                    pathConditions,
                    getSymbolVersion);

                return;
            }

            if (ancestor is SwitchStatementSyntax switchStatement)
            {
                var section = switchStatement.Sections.FirstOrDefault(candidate =>
                    candidate.Statements.Any(statement => statement.Span.Contains(syntaxNode.SpanStart)));
                if (section != null &&
                    !IsReachableConstantSwitchGotoTarget(section, switchStatement, semanticModel, cancellationToken) &&
                    SwitchPathConditionBuilder.TryCreateSwitchStatementSectionCondition(
                        switchStatement.Expression,
                        section,
                        semanticModel,
                        cancellationToken,
                        out var sectionCondition))
                {
                    AddArrayLengthCountAliasFact(
                        switchStatement.Expression,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion);
                    pathConditions.Add(sectionCondition);
                }

                return;
            }

            if (ancestor is SwitchExpressionSyntax switchExpression)
            {
                var arm = switchExpression.Arms.FirstOrDefault(candidate =>
                    candidate.Expression.Span.Contains(syntaxNode.SpanStart));
                if (arm != null &&
                    SwitchPathConditionBuilder.TryCreateSwitchExpressionArmCondition(
                        switchExpression.GoverningExpression,
                        arm,
                        semanticModel,
                        cancellationToken,
                        out var armCondition))
                {
                    AddArrayLengthCountAliasFact(
                        switchExpression.GoverningExpression,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion);
                    pathConditions.Add(armCondition);
                }
            }
        }

        private static void AddBranchConditionFact(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions,
            Func<ISymbol, int>? getSymbolVersion)
        {
            SymbolicReachabilityService.TryAddBranchConditionFacts(
                expression,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                pathConditions,
                getSymbolVersion,
                collectDomainFactsBeforeBranchAssumptions: true,
                addTranslatedFormulaFallback: true);
        }

        private static void AddReferenceNullStateFact(
            ExpressionSyntax expression,
            bool equalToNull,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (SymbolicReachabilityService.TryCreateReferenceNullComparison(
                    expression,
                    semanticModel,
                    cancellationToken,
                    equalToNull,
                    out var formula,
                    getSymbolVersion))
            {
                pathConditions.Add(formula);
            }
        }

        private static void AddArrayLengthCountAliasFact(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (SymbolicReachabilityService.TryCreateArrayLengthCountAliasFact(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var aliasFact,
                    getSymbolVersion))
            {
                pathConditions.Add(aliasFact);
            }
        }

    }
}
