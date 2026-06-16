using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using PurelySharp.Analyzer.Engine.Smt;
using SearchLib.Smt;

namespace PurelySharp.Analyzer.Engine
{
    internal static class ExecutionVisibility
    {
        private static readonly TimeSpan SmtTimeout = TimeSpan.FromMilliseconds(25);

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
            foreach (var ancestor in syntaxNode.Ancestors())
            {
                if (ancestor is IfStatementSyntax ifStatement)
                {
                    if (IsConditionAlwaysFalse(ifStatement.Condition, semanticModel, cancellationToken) &&
                        ifStatement.Statement.Span.Contains(syntaxNode.SpanStart))
                    {
                        return true;
                    }

                    if (IsConditionAlwaysTrue(ifStatement.Condition, semanticModel, cancellationToken) &&
                        ifStatement.Else?.Statement.Span.Contains(syntaxNode.SpanStart) == true)
                    {
                        return true;
                    }
                }
                else if (ancestor is ConditionalExpressionSyntax conditionalExpression)
                {
                    if (IsConditionAlwaysFalse(conditionalExpression.Condition, semanticModel, cancellationToken) &&
                        conditionalExpression.WhenTrue.Span.Contains(syntaxNode.SpanStart))
                    {
                        return true;
                    }

                    if (IsConditionAlwaysTrue(conditionalExpression.Condition, semanticModel, cancellationToken) &&
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
                        IsConditionAlwaysFalse(binaryExpression.Left, semanticModel, cancellationToken))
                    {
                        return true;
                    }

                    if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression) &&
                        binaryExpression.Right.Span.Contains(syntaxNode.SpanStart) &&
                        IsConditionAlwaysTrue(binaryExpression.Left, semanticModel, cancellationToken))
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
            return EvaluateKnownBoolean(expression, semanticModel, cancellationToken) == KnownBooleanValue.True;
        }

        public static bool IsConditionAlwaysFalse(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken = default)
        {
            return EvaluateKnownBoolean(expression, semanticModel, cancellationToken) == KnownBooleanValue.False;
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
            CancellationToken cancellationToken)
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
                return Negate(EvaluateKnownBoolean(prefixUnary.Operand, semanticModel, cancellationToken));
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression))
                {
                    var left = EvaluateKnownBoolean(binaryExpression.Left, semanticModel, cancellationToken);
                    var right = EvaluateKnownBoolean(binaryExpression.Right, semanticModel, cancellationToken);
                    if (left == KnownBooleanValue.False || right == KnownBooleanValue.False)
                    {
                        return KnownBooleanValue.False;
                    }

                    if (left == KnownBooleanValue.True && right == KnownBooleanValue.True)
                    {
                        return KnownBooleanValue.True;
                    }

                    return EvaluateWithSmtFallback(expression, semanticModel, cancellationToken);
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
                {
                    var left = EvaluateKnownBoolean(binaryExpression.Left, semanticModel, cancellationToken);
                    var right = EvaluateKnownBoolean(binaryExpression.Right, semanticModel, cancellationToken);
                    if (left == KnownBooleanValue.True || right == KnownBooleanValue.True)
                    {
                        return KnownBooleanValue.True;
                    }

                    if (left == KnownBooleanValue.False && right == KnownBooleanValue.False)
                    {
                        return KnownBooleanValue.False;
                    }

                    return EvaluateWithSmtFallback(expression, semanticModel, cancellationToken);
                }

                var comparisonValue = EvaluateKnownComparison(binaryExpression, semanticModel, cancellationToken);
                return comparisonValue != KnownBooleanValue.Unknown
                    ? comparisonValue
                    : EvaluateWithSmtFallback(expression, semanticModel, cancellationToken);
            }

            if (expression is IsPatternExpressionSyntax isPatternExpression)
            {
                var patternValue = EvaluateKnownPattern(isPatternExpression, semanticModel, cancellationToken);
                return patternValue != KnownBooleanValue.Unknown
                    ? patternValue
                    : EvaluateWithSmtFallback(expression, semanticModel, cancellationToken);
            }

            return EvaluateWithSmtFallback(expression, semanticModel, cancellationToken);
        }

        private static KnownBooleanValue EvaluateKnownComparison(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (TryGetLocalOrParameterSymbol(binaryExpression.Left, semanticModel, cancellationToken, out var leftSymbol) &&
                TryGetLocalOrParameterSymbol(binaryExpression.Right, semanticModel, cancellationToken, out var rightSymbol) &&
                SymbolEqualityComparer.Default.Equals(leftSymbol, rightSymbol))
            {
                if (binaryExpression.IsKind(SyntaxKind.EqualsExpression) ||
                    binaryExpression.IsKind(SyntaxKind.LessThanOrEqualExpression) ||
                    binaryExpression.IsKind(SyntaxKind.GreaterThanOrEqualExpression))
                {
                    return KnownBooleanValue.True;
                }

                if (binaryExpression.IsKind(SyntaxKind.NotEqualsExpression) ||
                    binaryExpression.IsKind(SyntaxKind.LessThanExpression) ||
                    binaryExpression.IsKind(SyntaxKind.GreaterThanExpression))
                {
                    return KnownBooleanValue.False;
                }
            }

            if (TryGetIntegralConstant(binaryExpression.Left, semanticModel, cancellationToken, out var leftConstant) &&
                TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken, out var rightConstant))
            {
                return EvaluateIntegralComparison(binaryExpression.Kind(), leftConstant, rightConstant);
            }

            if (binaryExpression.Left.IsKind(SyntaxKind.NullLiteralExpression) &&
                binaryExpression.Right.IsKind(SyntaxKind.NullLiteralExpression))
            {
                return binaryExpression.IsKind(SyntaxKind.EqualsExpression)
                    ? KnownBooleanValue.True
                    : binaryExpression.IsKind(SyntaxKind.NotEqualsExpression)
                        ? KnownBooleanValue.False
                        : KnownBooleanValue.Unknown;
            }

            return KnownBooleanValue.Unknown;
        }

        private static KnownBooleanValue EvaluateKnownPattern(
            IsPatternExpressionSyntax isPatternExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (isPatternExpression.Pattern is ConstantPatternSyntax constantPattern &&
                constantPattern.Expression.IsKind(SyntaxKind.NullLiteralExpression) &&
                TryGetLocalOrParameterSymbol(isPatternExpression.Expression, semanticModel, cancellationToken, out _))
            {
                return KnownBooleanValue.Unknown;
            }

            if (isPatternExpression.Pattern is UnaryPatternSyntax unaryPattern &&
                unaryPattern.OperatorToken.IsKind(SyntaxKind.NotKeyword) &&
                unaryPattern.Pattern is ConstantPatternSyntax notConstantPattern &&
                notConstantPattern.Expression.IsKind(SyntaxKind.NullLiteralExpression) &&
                TryGetLocalOrParameterSymbol(isPatternExpression.Expression, semanticModel, cancellationToken, out _))
            {
                return KnownBooleanValue.Unknown;
            }

            return KnownBooleanValue.Unknown;
        }

        private static bool TryGetLocalOrParameterSymbol(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ISymbol symbol)
        {
            symbol = null!;
            expression = UnwrapExpression(expression);
            var operation = semanticModel.GetOperation(expression, cancellationToken);
            switch (operation)
            {
                case ILocalReferenceOperation localReference:
                    symbol = localReference.Local;
                    return true;
                case IParameterReferenceOperation parameterReference:
                    symbol = parameterReference.Parameter;
                    return true;
            }

            var resolved = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
            if (resolved is ILocalSymbol or IParameterSymbol)
            {
                symbol = resolved;
                return true;
            }

            return false;
        }

        private static bool TryGetIntegralConstant(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out long value)
        {
            value = default;
            expression = UnwrapExpression(expression);
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (!constantValue.HasValue || constantValue.Value == null)
            {
                return false;
            }

            switch (constantValue.Value)
            {
                case sbyte signedByte:
                    value = signedByte;
                    return true;
                case byte unsignedByte:
                    value = unsignedByte;
                    return true;
                case short signedShort:
                    value = signedShort;
                    return true;
                case ushort unsignedShort:
                    value = unsignedShort;
                    return true;
                case int signedInt:
                    value = signedInt;
                    return true;
                case uint unsignedInt:
                    value = unsignedInt;
                    return true;
                case long signedLong:
                    value = signedLong;
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsIntegralType(ITypeSymbol typeSymbol)
        {
            return typeSymbol.SpecialType is
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64;
        }

        private static KnownBooleanValue EvaluateIntegralComparison(SyntaxKind kind, long left, long right)
        {
            return kind switch
            {
                SyntaxKind.EqualsExpression => left == right ? KnownBooleanValue.True : KnownBooleanValue.False,
                SyntaxKind.NotEqualsExpression => left != right ? KnownBooleanValue.True : KnownBooleanValue.False,
                SyntaxKind.LessThanExpression => left < right ? KnownBooleanValue.True : KnownBooleanValue.False,
                SyntaxKind.LessThanOrEqualExpression => left <= right ? KnownBooleanValue.True : KnownBooleanValue.False,
                SyntaxKind.GreaterThanExpression => left > right ? KnownBooleanValue.True : KnownBooleanValue.False,
                SyntaxKind.GreaterThanOrEqualExpression => left >= right ? KnownBooleanValue.True : KnownBooleanValue.False,
                _ => KnownBooleanValue.Unknown
            };
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
            CancellationToken cancellationToken)
        {
            if (!CSharpConditionToFormula.TryTranslate(expression, semanticModel, cancellationToken, out var formula) ||
                formula == null)
            {
                return KnownBooleanValue.Unknown;
            }

            using var solver = new SmtSolver();
            var whenTrue = solver.IsSatisfiable(new[] { formula }, SmtTimeout);
            if (whenTrue == Feasibility.Unsatisfiable)
            {
                return KnownBooleanValue.False;
            }

            var whenFalse = solver.IsSatisfiable(new[] { new SmtUnaryFormula(SmtUnaryOperator.Not, formula) }, SmtTimeout);
            if (whenFalse == Feasibility.Unsatisfiable)
            {
                return KnownBooleanValue.True;
            }

            return KnownBooleanValue.Unknown;
        }

        private enum KnownBooleanValue
        {
            Unknown,
            False,
            True
        }

    }
}
