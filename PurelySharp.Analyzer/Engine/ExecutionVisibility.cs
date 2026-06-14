using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

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

                    return IsUnsatisfiableConjunction(binaryExpression, semanticModel, cancellationToken)
                        ? KnownBooleanValue.False
                        : KnownBooleanValue.Unknown;
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

                    return KnownBooleanValue.Unknown;
                }

                return EvaluateKnownComparison(binaryExpression, semanticModel, cancellationToken);
            }

            if (expression is IsPatternExpressionSyntax isPatternExpression)
            {
                return EvaluateKnownPattern(isPatternExpression, semanticModel, cancellationToken);
            }

            return KnownBooleanValue.Unknown;
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

        private static bool IsUnsatisfiableConjunction(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var constraintsBySymbol = new Dictionary<string, SymbolConstraintState>(StringComparer.Ordinal);
            foreach (var term in FlattenConjuncts(expression))
            {
                var termTruth = EvaluateKnownBooleanTerm(term, semanticModel, cancellationToken);
                if (termTruth == KnownBooleanValue.False)
                {
                    return true;
                }

                if (termTruth == KnownBooleanValue.True)
                {
                    continue;
                }

                if (TryApplyNullConstraint(term, semanticModel, cancellationToken, constraintsBySymbol))
                {
                    return true;
                }

                if (TryApplyIntegralConstraint(term, semanticModel, cancellationToken, constraintsBySymbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static KnownBooleanValue EvaluateKnownBooleanTerm(
            ExpressionSyntax term,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            term = UnwrapExpression(term);
            var constantValue = semanticModel.GetConstantValue(term, cancellationToken);
            if (constantValue.HasValue && constantValue.Value is bool booleanValue)
            {
                return booleanValue ? KnownBooleanValue.True : KnownBooleanValue.False;
            }

            if (term is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression))
            {
                return Negate(EvaluateKnownBooleanTerm(prefixUnary.Operand, semanticModel, cancellationToken));
            }

            if (term is BinaryExpressionSyntax binaryExpression &&
                !binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
                !binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
            {
                return EvaluateKnownComparison(binaryExpression, semanticModel, cancellationToken);
            }

            return KnownBooleanValue.Unknown;
        }

        private static IEnumerable<ExpressionSyntax> FlattenConjuncts(ExpressionSyntax expression)
        {
            expression = UnwrapExpression(expression);
            if (expression is BinaryExpressionSyntax binaryExpression &&
                binaryExpression.IsKind(SyntaxKind.LogicalAndExpression))
            {
                foreach (var left in FlattenConjuncts(binaryExpression.Left))
                {
                    yield return left;
                }

                foreach (var right in FlattenConjuncts(binaryExpression.Right))
                {
                    yield return right;
                }

                yield break;
            }

            yield return expression;
        }

        private static bool TryApplyNullConstraint(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            IDictionary<string, SymbolConstraintState> constraintsBySymbol)
        {
            expression = UnwrapExpression(expression);
            if (expression is BinaryExpressionSyntax binaryExpression &&
                (binaryExpression.IsKind(SyntaxKind.EqualsExpression) || binaryExpression.IsKind(SyntaxKind.NotEqualsExpression)))
            {
                if (TryGetNullComparedSymbol(binaryExpression.Left, binaryExpression.Right, semanticModel, cancellationToken, out var symbol) ||
                    TryGetNullComparedSymbol(binaryExpression.Right, binaryExpression.Left, semanticModel, cancellationToken, out symbol))
                {
                    var state = GetOrCreateState(symbol, constraintsBySymbol);
                    return ApplyNullConstraint(state, binaryExpression.IsKind(SyntaxKind.EqualsExpression));
                }
            }

            if (expression is IsPatternExpressionSyntax isPatternExpression)
            {
                if (isPatternExpression.Pattern is ConstantPatternSyntax constantPattern &&
                    constantPattern.Expression.IsKind(SyntaxKind.NullLiteralExpression) &&
                    TryGetLocalOrParameterSymbol(isPatternExpression.Expression, semanticModel, cancellationToken, out var symbol))
                {
                    var state = GetOrCreateState(symbol, constraintsBySymbol);
                    return ApplyNullConstraint(state, shouldBeNull: true);
                }

                if (isPatternExpression.Pattern is UnaryPatternSyntax unaryPattern &&
                    unaryPattern.OperatorToken.IsKind(SyntaxKind.NotKeyword) &&
                    unaryPattern.Pattern is ConstantPatternSyntax negatedConstantPattern &&
                    negatedConstantPattern.Expression.IsKind(SyntaxKind.NullLiteralExpression) &&
                    TryGetLocalOrParameterSymbol(isPatternExpression.Expression, semanticModel, cancellationToken, out symbol))
                {
                    var state = GetOrCreateState(symbol, constraintsBySymbol);
                    return ApplyNullConstraint(state, shouldBeNull: false);
                }
            }

            return false;
        }

        private static bool TryApplyIntegralConstraint(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            IDictionary<string, SymbolConstraintState> constraintsBySymbol)
        {
            expression = UnwrapExpression(expression);
            if (expression is not BinaryExpressionSyntax binaryExpression)
            {
                return false;
            }

            if (!TryGetIntegralComparison(binaryExpression, semanticModel, cancellationToken, out var symbol, out var comparisonKind, out var constant))
            {
                return false;
            }

            var state = GetOrCreateState(symbol, constraintsBySymbol);
            return ApplyIntegralConstraint(state, comparisonKind, constant);
        }

        private static bool TryGetIntegralComparison(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ISymbol symbol,
            out ComparisonKind comparisonKind,
            out long constant)
        {
            symbol = null!;
            comparisonKind = default;
            constant = default;

            if (TryGetLocalOrParameterSymbol(binaryExpression.Left, semanticModel, cancellationToken, out var leftSymbol) &&
                TryGetIntegralConstant(binaryExpression.Right, semanticModel, cancellationToken, out var rightConstant) &&
                IsIntegralSymbol(leftSymbol))
            {
                symbol = leftSymbol;
                comparisonKind = ToComparisonKind(binaryExpression.Kind());
                constant = rightConstant;
                return comparisonKind != ComparisonKind.Unknown;
            }

            if (TryGetLocalOrParameterSymbol(binaryExpression.Right, semanticModel, cancellationToken, out var rightSymbol) &&
                TryGetIntegralConstant(binaryExpression.Left, semanticModel, cancellationToken, out var leftConstant) &&
                IsIntegralSymbol(rightSymbol))
            {
                symbol = rightSymbol;
                comparisonKind = ReverseComparisonKind(ToComparisonKind(binaryExpression.Kind()));
                constant = leftConstant;
                return comparisonKind != ComparisonKind.Unknown;
            }

            return false;
        }

        private static bool TryGetNullComparedSymbol(
            ExpressionSyntax symbolExpression,
            ExpressionSyntax otherExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ISymbol symbol)
        {
            symbol = null!;
            if (!otherExpression.IsKind(SyntaxKind.NullLiteralExpression))
            {
                return false;
            }

            if (!TryGetLocalOrParameterSymbol(symbolExpression, semanticModel, cancellationToken, out symbol))
            {
                return false;
            }

            return IsReferenceSymbol(symbol);
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

        private static bool IsIntegralSymbol(ISymbol symbol)
        {
            return symbol switch
            {
                ILocalSymbol localSymbol => IsIntegralType(localSymbol.Type),
                IParameterSymbol parameterSymbol => IsIntegralType(parameterSymbol.Type),
                _ => false
            };
        }

        private static bool IsReferenceSymbol(ISymbol symbol)
        {
            return symbol switch
            {
                ILocalSymbol localSymbol => localSymbol.Type.IsReferenceType,
                IParameterSymbol parameterSymbol => parameterSymbol.Type.IsReferenceType,
                _ => false
            };
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

        private static ComparisonKind ToComparisonKind(SyntaxKind kind)
        {
            return kind switch
            {
                SyntaxKind.EqualsExpression => ComparisonKind.Equal,
                SyntaxKind.NotEqualsExpression => ComparisonKind.NotEqual,
                SyntaxKind.LessThanExpression => ComparisonKind.LessThan,
                SyntaxKind.LessThanOrEqualExpression => ComparisonKind.LessThanOrEqual,
                SyntaxKind.GreaterThanExpression => ComparisonKind.GreaterThan,
                SyntaxKind.GreaterThanOrEqualExpression => ComparisonKind.GreaterThanOrEqual,
                _ => ComparisonKind.Unknown
            };
        }

        private static ComparisonKind ReverseComparisonKind(ComparisonKind comparisonKind)
        {
            return comparisonKind switch
            {
                ComparisonKind.Equal => ComparisonKind.Equal,
                ComparisonKind.NotEqual => ComparisonKind.NotEqual,
                ComparisonKind.LessThan => ComparisonKind.GreaterThan,
                ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThanOrEqual,
                ComparisonKind.GreaterThan => ComparisonKind.LessThan,
                ComparisonKind.GreaterThanOrEqual => ComparisonKind.LessThanOrEqual,
                _ => ComparisonKind.Unknown
            };
        }

        private static bool ApplyNullConstraint(SymbolConstraintState state, bool shouldBeNull)
        {
            if (state.NullConstraint.HasValue && state.NullConstraint.Value != shouldBeNull)
            {
                return true;
            }

            state.NullConstraint = shouldBeNull;
            return false;
        }

        private static bool ApplyIntegralConstraint(SymbolConstraintState state, ComparisonKind comparisonKind, long constant)
        {
            switch (comparisonKind)
            {
                case ComparisonKind.Equal:
                    if (state.ExactValue.HasValue && state.ExactValue.Value != constant)
                    {
                        return true;
                    }

                    state.ExactValue = constant;
                    break;
                case ComparisonKind.NotEqual:
                    state.NotEqualValues.Add(constant);
                    break;
                case ComparisonKind.LessThan:
                    state.MaxInclusive = MinOrTake(state.MaxInclusive, constant - 1);
                    break;
                case ComparisonKind.LessThanOrEqual:
                    state.MaxInclusive = MinOrTake(state.MaxInclusive, constant);
                    break;
                case ComparisonKind.GreaterThan:
                    state.MinInclusive = MaxOrTake(state.MinInclusive, constant + 1);
                    break;
                case ComparisonKind.GreaterThanOrEqual:
                    state.MinInclusive = MaxOrTake(state.MinInclusive, constant);
                    break;
                default:
                    return false;
            }

            if (state.MinInclusive.HasValue && state.MaxInclusive.HasValue && state.MinInclusive.Value > state.MaxInclusive.Value)
            {
                return true;
            }

            if (state.ExactValue.HasValue)
            {
                var exactValue = state.ExactValue.Value;
                if (state.NotEqualValues.Contains(exactValue))
                {
                    return true;
                }

                if (state.MinInclusive.HasValue && exactValue < state.MinInclusive.Value)
                {
                    return true;
                }

                if (state.MaxInclusive.HasValue && exactValue > state.MaxInclusive.Value)
                {
                    return true;
                }
            }

            return false;
        }

        private static long? MaxOrTake(long? currentValue, long nextValue)
        {
            return currentValue.HasValue && currentValue.Value > nextValue ? currentValue : nextValue;
        }

        private static long? MinOrTake(long? currentValue, long nextValue)
        {
            return currentValue.HasValue && currentValue.Value < nextValue ? currentValue : nextValue;
        }

        private static SymbolConstraintState GetOrCreateState(
            ISymbol symbol,
            IDictionary<string, SymbolConstraintState> constraintsBySymbol)
        {
            var key = GetConstraintKey(symbol);
            if (!constraintsBySymbol.TryGetValue(key, out var state))
            {
                state = new SymbolConstraintState();
                constraintsBySymbol.Add(key, state);
            }

            return state;
        }

        private static string GetConstraintKey(ISymbol symbol)
        {
            var firstLocation = symbol.Locations.FirstOrDefault(location => location.IsInSource);
            if (firstLocation != null)
            {
                return symbol.Name + "#" + firstLocation.SourceSpan.Start.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return symbol.Kind.ToString() + ":" + symbol.ToDisplayString();
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

        private enum KnownBooleanValue
        {
            Unknown,
            False,
            True
        }

        private enum ComparisonKind
        {
            Unknown,
            Equal,
            NotEqual,
            LessThan,
            LessThanOrEqual,
            GreaterThan,
            GreaterThanOrEqual
        }

        private sealed class SymbolConstraintState
        {
            public long? MinInclusive { get; set; }

            public long? MaxInclusive { get; set; }

            public long? ExactValue { get; set; }

            public bool? NullConstraint { get; set; }

            public HashSet<long> NotEqualValues { get; } = new HashSet<long>();
        }
    }
}
