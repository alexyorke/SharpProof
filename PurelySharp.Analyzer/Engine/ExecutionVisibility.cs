using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Purity;
using PurelySharp.Analyzer.Engine.Smt;
using SearchLib.Smt;

namespace PurelySharp.Analyzer.Engine
{
    internal static class ExecutionVisibility
    {
        public static IEnumerable<IOperation> VisibleDescendants(IOperation rootOperation)
        {
            foreach (var operation in rootOperation.DescendantsAndSelf())
            {
                if (!IsNestedFunctionDescendant(operation, rootOperation))
                {
                    yield return operation;
                }
            }
        }

        public static bool IsNestedCallableBoundary(SyntaxNode node)
        {
            return node is MethodDeclarationSyntax or
                ConstructorDeclarationSyntax or
                OperatorDeclarationSyntax or
                AccessorDeclarationSyntax or
                LocalFunctionStatementSyntax or
                ParenthesizedLambdaExpressionSyntax or
                SimpleLambdaExpressionSyntax or
                AnonymousMethodExpressionSyntax;
        }

        public static bool IsInStaticallyUnreachableBranch(
            SyntaxNode syntaxNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken = default)
        {
            return IsInStaticallyUnreachableBranchUsingSmt(syntaxNode, semanticModel, cancellationToken, smtAnalysis: null);
        }

        public static bool IsInStaticallyUnreachableBranchUsingSmt(
            SyntaxNode syntaxNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis = null)
        {
            foreach (var ancestor in syntaxNode.Ancestors())
            {
                if (ancestor is IfStatementSyntax ifStatement)
                {
                    if (IsConditionAlwaysFalseUsingSmt(ifStatement.Condition, semanticModel, cancellationToken, smtAnalysis) &&
                        ifStatement.Statement.Span.Contains(syntaxNode.SpanStart))
                    {
                        return true;
                    }

                    if (IsConditionAlwaysTrueUsingSmt(ifStatement.Condition, semanticModel, cancellationToken, smtAnalysis) &&
                        ifStatement.Else?.Statement.Span.Contains(syntaxNode.SpanStart) == true)
                    {
                        return true;
                    }
                }
                else if (ancestor is ConditionalExpressionSyntax conditionalExpression)
                {
                    if (IsConditionAlwaysFalseUsingSmt(conditionalExpression.Condition, semanticModel, cancellationToken, smtAnalysis) &&
                        conditionalExpression.WhenTrue.Span.Contains(syntaxNode.SpanStart))
                    {
                        return true;
                    }

                    if (IsConditionAlwaysTrueUsingSmt(conditionalExpression.Condition, semanticModel, cancellationToken, smtAnalysis) &&
                        conditionalExpression.WhenFalse.Span.Contains(syntaxNode.SpanStart))
                    {
                        return true;
                    }
                }
                else if (ancestor is ConditionalAccessExpressionSyntax conditionalAccessExpression)
                {
                    var receiverValue = semanticModel.GetConstantValue(conditionalAccessExpression.Expression, cancellationToken);
                    if (receiverValue.HasValue &&
                        receiverValue.Value == null &&
                        conditionalAccessExpression.WhenNotNull.Span.Contains(syntaxNode.SpanStart))
                    {
                        return true;
                    }
                }
                else if (ancestor is BinaryExpressionSyntax binaryExpression)
                {
                    if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
                        binaryExpression.Right.Span.Contains(syntaxNode.SpanStart) &&
                        IsConditionAlwaysFalseUsingSmt(binaryExpression.Left, semanticModel, cancellationToken, smtAnalysis))
                    {
                        return true;
                    }

                    if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression) &&
                        binaryExpression.Right.Span.Contains(syntaxNode.SpanStart) &&
                        IsConditionAlwaysTrueUsingSmt(binaryExpression.Left, semanticModel, cancellationToken, smtAnalysis))
                    {
                        return true;
                    }

                    if (binaryExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                        binaryExpression.Right.Span.Contains(syntaxNode.SpanStart))
                    {
                        var leftValue = semanticModel.GetConstantValue(binaryExpression.Left, cancellationToken);
                        if (leftValue.HasValue && leftValue.Value != null)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public static bool IsConditionAlwaysTrue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken = default)
        {
            return IsConditionAlwaysTrueUsingSmt(expression, semanticModel, cancellationToken, smtAnalysis: null);
        }

        public static bool IsConditionAlwaysTrueUsingSmt(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis = null)
        {
            return EvaluateKnownBoolean(expression, semanticModel, cancellationToken, smtAnalysis) == KnownBooleanValue.True;
        }

        public static bool IsConditionAlwaysFalse(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken = default)
        {
            return IsConditionAlwaysFalseUsingSmt(expression, semanticModel, cancellationToken, smtAnalysis: null);
        }

        public static bool IsConditionAlwaysFalseUsingSmt(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis = null)
        {
            return EvaluateKnownBoolean(expression, semanticModel, cancellationToken, smtAnalysis) == KnownBooleanValue.False;
        }

        private static bool IsNestedFunctionDescendant(IOperation operation, IOperation rootOperation)
        {
            if (ReferenceEquals(operation, rootOperation))
            {
                return false;
            }

            for (var parent = operation.Parent; parent != null && !ReferenceEquals(parent, rootOperation); parent = parent.Parent)
            {
                if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
                {
                    return true;
                }
            }

            return false;
        }

        private static KnownBooleanValue EvaluateKnownBoolean(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            expression = UnwrapExpression(expression);
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue && constantValue.Value is bool booleanValue)
            {
                return booleanValue ? KnownBooleanValue.True : KnownBooleanValue.False;
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression))
            {
                return Negate(EvaluateKnownBoolean(prefixUnary.Operand, semanticModel, cancellationToken, smtAnalysis));
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression))
                {
                    var left = EvaluateKnownBoolean(binaryExpression.Left, semanticModel, cancellationToken, smtAnalysis);
                    var right = EvaluateKnownBoolean(binaryExpression.Right, semanticModel, cancellationToken, smtAnalysis);
                    if (left == KnownBooleanValue.False || right == KnownBooleanValue.False)
                    {
                        return KnownBooleanValue.False;
                    }

                    if (left == KnownBooleanValue.True && right == KnownBooleanValue.True)
                    {
                        return KnownBooleanValue.True;
                    }

                    return EvaluateWithSmtFallback(expression, semanticModel, cancellationToken, smtAnalysis);
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
                {
                    var left = EvaluateKnownBoolean(binaryExpression.Left, semanticModel, cancellationToken, smtAnalysis);
                    var right = EvaluateKnownBoolean(binaryExpression.Right, semanticModel, cancellationToken, smtAnalysis);
                    if (left == KnownBooleanValue.True || right == KnownBooleanValue.True)
                    {
                        return KnownBooleanValue.True;
                    }

                    if (left == KnownBooleanValue.False && right == KnownBooleanValue.False)
                    {
                        return KnownBooleanValue.False;
                    }

                    return EvaluateWithSmtFallback(expression, semanticModel, cancellationToken, smtAnalysis);
                }

                return EvaluateWithSmtFallback(expression, semanticModel, cancellationToken, smtAnalysis);
            }

            if (expression is IsPatternExpressionSyntax isPatternExpression)
            {
                return EvaluateWithSmtFallback(isPatternExpression, semanticModel, cancellationToken, smtAnalysis);
            }

            return EvaluateWithSmtFallback(expression, semanticModel, cancellationToken, smtAnalysis);
        }

        private static ExpressionSyntax UnwrapExpression(ExpressionSyntax expression)
        {
            while (true)
            {
                if (expression is ParenthesizedExpressionSyntax parenthesizedExpression)
                {
                    expression = parenthesizedExpression.Expression;
                    continue;
                }

                if (expression is PostfixUnaryExpressionSyntax postfixUnary &&
                    postfixUnary.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                {
                    expression = postfixUnary.Operand;
                    continue;
                }

                return expression;
            }
        }

        private static KnownBooleanValue Negate(KnownBooleanValue value)
        {
            return value switch
            {
                KnownBooleanValue.True => KnownBooleanValue.False,
                KnownBooleanValue.False => KnownBooleanValue.True,
                _ => KnownBooleanValue.Unknown
            };
        }

        private static KnownBooleanValue EvaluateWithSmtFallback(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            if (!CSharpConditionToFormula.TryTranslate(expression, semanticModel, cancellationToken, out var formula) ||
                formula == null)
            {
                return KnownBooleanValue.Unknown;
            }

            if (IsBranchConditionUnreachable(formula, smtAnalysis))
            {
                return KnownBooleanValue.False;
            }

            if (IsBranchConditionUnreachable(new SmtUnaryFormula(SmtUnaryOperator.Not, formula), smtAnalysis))
            {
                return KnownBooleanValue.True;
            }

            return KnownBooleanValue.Unknown;
        }

        private static bool IsBranchConditionUnreachable(SmtFormula formula, SmtAnalysisService? smtAnalysis)
        {
            var query = new PurityProofQuery(
                Array.Empty<SmtFormula>(),
                new PurityHazard(PurityHazardKind.BranchReachability, formula));

            var proofResult = (smtAnalysis ?? new SmtAnalysisService(SmtAnalysisOptions.Default)).Classify(query);
            return proofResult.Outcome == PurityProofOutcome.ProvablyPure;
        }

        private enum KnownBooleanValue
        {
            Unknown,
            False,
            True
        }

    }
}
