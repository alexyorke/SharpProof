using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;

namespace PurelySharp.Symbolic.Smt
{
    public static class CSharpConditionToFormula
    {
        private const int MaxSourcePredicateInlineDepth = 4;
        private const string ImplicitThisVariableName = "this";
        private static readonly ConditionalWeakTable<Compilation, ConcurrentDictionary<SourceBooleanFormulaCacheKey, SourceBooleanFormulaCacheEntry>> s_sourceBooleanFormulaCache = new();

        private readonly struct SourceBooleanFormulaCacheEntry
        {
            public SourceBooleanFormulaCacheEntry(bool success, SmtFormula? formula)
            {
                Success = success;
                Formula = formula;
            }

            public bool Success { get; }

            public SmtFormula? Formula { get; }
        }

        private readonly struct SourceBooleanFormulaCacheKey : IEquatable<SourceBooleanFormulaCacheKey>
        {
            private readonly string _kind;
            private readonly string _filePath;
            private readonly int _spanStart;
            private readonly int _spanLength;
            private readonly int _inlineDepth;

            public SourceBooleanFormulaCacheKey(
                string kind,
                string filePath,
                int spanStart,
                int spanLength,
                int inlineDepth)
            {
                _kind = kind;
                _filePath = filePath;
                _spanStart = spanStart;
                _spanLength = spanLength;
                _inlineDepth = inlineDepth;
            }

            public bool Equals(SourceBooleanFormulaCacheKey other)
            {
                return string.Equals(_kind, other._kind, StringComparison.Ordinal) &&
                    string.Equals(_filePath, other._filePath, StringComparison.Ordinal) &&
                    _spanStart == other._spanStart &&
                    _spanLength == other._spanLength &&
                    _inlineDepth == other._inlineDepth;
            }

            public override bool Equals(object? obj)
            {
                return obj is SourceBooleanFormulaCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(_kind);
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(_filePath);
                    hash = (hash * 31) + _spanStart;
                    hash = (hash * 31) + _spanLength;
                    hash = (hash * 31) + _inlineDepth;
                    return hash;
                }
            }
        }

        public static bool TryTranslate(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            expression = UnwrapExpression(expression);

            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue && constantValue.Value is bool booleanValue)
            {
                formula = new SmtBooleanConstant(booleanValue);
                return true;
            }

            if (TryTranslateValue(expression, semanticModel, cancellationToken, out var directValue, getSymbolVersion, inlineDepth) &&
                directValue is { Kind: SmtValueKind.Bool })
            {
                formula = directValue;
                return true;
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression) &&
                TryTranslate(prefixUnary.Operand, semanticModel, cancellationToken, out var operand, getSymbolVersion, inlineDepth) &&
                operand != null)
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, operand);
                return true;
            }

            if (expression is ConditionalExpressionSyntax conditionalExpression &&
                TryTranslateValue(conditionalExpression, semanticModel, cancellationToken, out var conditionalValue, getSymbolVersion, inlineDepth) &&
                conditionalValue is { Kind: SmtValueKind.Bool })
            {
                formula = conditionalValue;
                return true;
            }

            if (expression is InvocationExpressionSyntax invocationExpression)
            {
                if (TryTranslateKnownStringBooleanInvocation(invocationExpression, semanticModel, cancellationToken, out var knownStringInvocationFormula, getSymbolVersion, inlineDepth) &&
                    knownStringInvocationFormula != null)
                {
                    formula = knownStringInvocationFormula;
                    return true;
                }

                if (TryTranslateSourceBooleanInvocation(invocationExpression, semanticModel, cancellationToken, out var invocationFormula, getSymbolVersion, inlineDepth) &&
                    invocationFormula != null)
                {
                    formula = invocationFormula;
                    return true;
                }
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.IsExpression) &&
                    binaryExpression.Right is TypeSyntax &&
                    TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var typeTestValue, getSymbolVersion, inlineDepth) &&
                    typeTestValue is { Kind: SmtValueKind.Reference })
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, typeTestValue, new SmtNullConstant());
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftAnd, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightAnd, getSymbolVersion, inlineDepth) &&
                    leftAnd != null &&
                    rightAnd != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftAnd, rightAnd);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftOr, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightOr, getSymbolVersion, inlineDepth) &&
                    leftOr != null &&
                    rightOr != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftOr, rightOr);
                    return true;
                }

                if (TryTranslateUnsignedCastBoundsComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var unsignedBoundsFormula,
                        getSymbolVersion,
                        inlineDepth) &&
                    unsignedBoundsFormula != null)
                {
                    formula = unsignedBoundsFormula;
                    return true;
                }

                if (TryTranslateNullableNullComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var nullableNullComparison,
                        getSymbolVersion,
                        inlineDepth) &&
                    nullableNullComparison != null)
                {
                    formula = nullableNullComparison;
                    return true;
                }

                if (TryTranslateNullableValueComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var nullableValueComparison,
                        getSymbolVersion,
                        inlineDepth) &&
                    nullableValueComparison != null)
                {
                    formula = nullableValueComparison;
                    return true;
                }

                if (TryTranslateStringComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var stringComparison,
                        getSymbolVersion,
                        inlineDepth) &&
                    stringComparison != null)
                {
                    formula = stringComparison;
                    return true;
                }

                if (TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var leftValue, getSymbolVersion, inlineDepth) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var rightValue, getSymbolVersion, inlineDepth) &&
                    leftValue != null &&
                    rightValue != null &&
                    TryTranslateComparison(binaryExpression.Kind(), leftValue, rightValue, out var comparison))
                {
                    formula = comparison;
                    return true;
                }
            }

            if (expression is IsPatternExpressionSyntax isPatternExpression &&
                TryTranslatePatternExpression(isPatternExpression, semanticModel, cancellationToken, out var patternFormula, getSymbolVersion, inlineDepth))
            {
                formula = patternFormula;
                return true;
            }

            formula = null;
            return false;
        }

        private static bool TryTranslateNullableValueComparison(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!IsSupportedNullableValueComparison(binaryExpression.Kind()))
            {
                return false;
            }

            var leftIsNullable = TryTranslateNullableValueParts(
                binaryExpression.Left,
                semanticModel,
                cancellationToken,
                out var leftHasValue,
                out var leftNullableValue,
                getSymbolVersion,
                inlineDepth);
            var rightIsNullable = TryTranslateNullableValueParts(
                binaryExpression.Right,
                semanticModel,
                cancellationToken,
                out var rightHasValue,
                out var rightNullableValue,
                getSymbolVersion,
                inlineDepth);

            if (leftIsNullable &&
                rightIsNullable &&
                leftNullableValue != null &&
                rightNullableValue != null &&
                TryTranslateComparison(binaryExpression.Kind(), leftNullableValue, rightNullableValue, out var nullableComparison) &&
                nullableComparison != null)
            {
                formula = CreateLiftedNullableComparison(
                    binaryExpression.Kind(),
                    leftHasValue,
                    rightHasValue,
                    nullableComparison);
                return true;
            }

            if (leftIsNullable &&
                leftNullableValue != null &&
                TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var rightValue, getSymbolVersion, inlineDepth) &&
                rightValue != null &&
                TryTranslateComparison(binaryExpression.Kind(), leftNullableValue, rightValue, out var leftComparison) &&
                leftComparison != null)
            {
                formula = CreateLiftedNullableComparison(binaryExpression.Kind(), leftHasValue, leftComparison);
                return true;
            }

            if (rightIsNullable &&
                rightNullableValue != null &&
                TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var leftValue, getSymbolVersion, inlineDepth) &&
                leftValue != null &&
                TryTranslateComparison(binaryExpression.Kind(), leftValue, rightNullableValue, out var rightComparison) &&
                rightComparison != null)
            {
                formula = CreateLiftedNullableComparison(binaryExpression.Kind(), rightHasValue, rightComparison);
                return true;
            }

            return false;
        }

        private static bool IsSupportedNullableValueComparison(SyntaxKind kind)
        {
            return kind is SyntaxKind.EqualsExpression or
                SyntaxKind.NotEqualsExpression or
                SyntaxKind.LessThanExpression or
                SyntaxKind.LessThanOrEqualExpression or
                SyntaxKind.GreaterThanExpression or
                SyntaxKind.GreaterThanOrEqualExpression;
        }

        private static SmtFormula CreateLiftedNullableComparison(
            SyntaxKind kind,
            SmtFormula hasValue,
            SmtFormula comparison)
        {
            return kind == SyntaxKind.NotEqualsExpression
                ? new SmtBinaryFormula(
                    SmtBinaryOperator.Or,
                    new SmtUnaryFormula(SmtUnaryOperator.Not, hasValue),
                    comparison)
                : new SmtBinaryFormula(SmtBinaryOperator.And, hasValue, comparison);
        }

        private static SmtFormula CreateLiftedNullableComparison(
            SyntaxKind kind,
            SmtFormula leftHasValue,
            SmtFormula rightHasValue,
            SmtFormula comparison)
        {
            var bothHaveValue = new SmtBinaryFormula(SmtBinaryOperator.And, leftHasValue, rightHasValue);
            if (kind == SyntaxKind.EqualsExpression)
            {
                var neitherHasValue = new SmtBinaryFormula(
                    SmtBinaryOperator.And,
                    new SmtUnaryFormula(SmtUnaryOperator.Not, leftHasValue),
                    new SmtUnaryFormula(SmtUnaryOperator.Not, rightHasValue));
                return new SmtBinaryFormula(
                    SmtBinaryOperator.Or,
                    neitherHasValue,
                    new SmtBinaryFormula(SmtBinaryOperator.And, bothHaveValue, comparison));
            }

            if (kind == SyntaxKind.NotEqualsExpression)
            {
                return new SmtUnaryFormula(
                    SmtUnaryOperator.Not,
                    CreateLiftedNullableComparison(SyntaxKind.EqualsExpression, leftHasValue, rightHasValue, comparison));
            }

            return new SmtBinaryFormula(SmtBinaryOperator.And, bothHaveValue, comparison);
        }

        private static bool TryTranslateNullableNullComparison(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!binaryExpression.IsKind(SyntaxKind.EqualsExpression) &&
                !binaryExpression.IsKind(SyntaxKind.NotEqualsExpression))
            {
                return false;
            }

            var comparesNotNull = binaryExpression.IsKind(SyntaxKind.NotEqualsExpression);
            if (TryTranslateNullableValueParts(
                    binaryExpression.Left,
                    semanticModel,
                    cancellationToken,
                    out var leftHasValue,
                    out _,
                    getSymbolVersion,
                    inlineDepth) &&
                IsNullLikeNullableComparisonOperand(binaryExpression.Right, semanticModel, cancellationToken))
            {
                formula = comparesNotNull
                    ? leftHasValue
                    : new SmtUnaryFormula(SmtUnaryOperator.Not, leftHasValue);
                return true;
            }

            if (TryTranslateNullableValueParts(
                    binaryExpression.Right,
                    semanticModel,
                    cancellationToken,
                    out var rightHasValue,
                    out _,
                    getSymbolVersion,
                    inlineDepth) &&
                IsNullLikeNullableComparisonOperand(binaryExpression.Left, semanticModel, cancellationToken))
            {
                formula = comparesNotNull
                    ? rightHasValue
                    : new SmtUnaryFormula(SmtUnaryOperator.Not, rightHasValue);
                return true;
            }

            return false;
        }

        private static bool IsNullLikeNullableComparisonOperand(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapExpression(expression);
            if (semanticModel.GetConstantValue(expression, cancellationToken) is { HasValue: true, Value: null })
            {
                return true;
            }

            if (!expression.IsKind(SyntaxKind.DefaultLiteralExpression) &&
                expression is not DefaultExpressionSyntax)
            {
                return false;
            }

            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            return TryGetNullableUnderlyingType(typeInfo.ConvertedType ?? typeInfo.Type, out _);
        }

        private static bool TryTranslateUnsignedCastBoundsComparison(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!binaryExpression.IsKind(SyntaxKind.LessThanExpression) &&
                !binaryExpression.IsKind(SyntaxKind.GreaterThanOrEqualExpression))
            {
                return false;
            }

            if (!TryCreateUnsignedCastBoundsInRangeFormula(
                    binaryExpression.Left,
                    binaryExpression.Right,
                    semanticModel,
                    cancellationToken,
                    out var inRangeFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            formula = binaryExpression.IsKind(SyntaxKind.LessThanExpression)
                ? inRangeFormula
                : new SmtUnaryFormula(SmtUnaryOperator.Not, inRangeFormula);
            return true;
        }

        private static bool TryCreateUnsignedCastBoundsInRangeFormula(
            ExpressionSyntax leftExpression,
            ExpressionSyntax rightExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null!;
            if (!TryGetUnsignedCastOperand(leftExpression, semanticModel, cancellationToken, out var indexExpression, out var leftUnsignedType) ||
                !TryGetUnsignedCastOperand(rightExpression, semanticModel, cancellationToken, out var lengthExpression, out var rightUnsignedType) ||
                leftUnsignedType != rightUnsignedType ||
                !IsKnownNonNegativeIntegralExpression(lengthExpression, semanticModel, cancellationToken) ||
                !TryTranslateValue(indexExpression, semanticModel, cancellationToken, out var indexFormula, getSymbolVersion, inlineDepth) ||
                indexFormula is not { Kind: SmtValueKind.Int } ||
                !TryTranslateValue(lengthExpression, semanticModel, cancellationToken, out var lengthFormula, getSymbolVersion, inlineDepth) ||
                lengthFormula is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            var lowerBound = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                indexFormula,
                new SmtIntegerConstant(0));
            var upperBound = new SmtBinaryFormula(
                SmtBinaryOperator.LessThan,
                indexFormula,
                lengthFormula);
            formula = new SmtBinaryFormula(SmtBinaryOperator.And, lowerBound, upperBound);
            return true;
        }

        private static bool TryGetUnsignedCastOperand(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ExpressionSyntax operand,
            out SpecialType unsignedType)
        {
            expression = UnwrapExpression(expression);
            if (expression is not CastExpressionSyntax castExpression)
            {
                operand = null!;
                unsignedType = SpecialType.None;
                return false;
            }

            var castType = semanticModel.GetTypeInfo(castExpression.Type, cancellationToken).Type;
            if (castType?.SpecialType is not SpecialType.System_UInt32 and not SpecialType.System_UInt64)
            {
                operand = null!;
                unsignedType = SpecialType.None;
                return false;
            }

            operand = castExpression.Expression;
            unsignedType = castType.SpecialType;
            return true;
        }

        private static bool IsKnownNonNegativeIntegralExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapExpression(expression);
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue &&
                TryGetIntegralConstant(constantValue.Value!, out var integralValue))
            {
                return integralValue >= 0;
            }

            return expression is MemberAccessExpressionSyntax memberAccess &&
                IsBuiltInNonNegativeLengthAccess(memberAccess, semanticModel, cancellationToken);
        }

        private static bool TryTranslateSourceBooleanInvocation(
            InvocationExpressionSyntax invocationExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (inlineDepth >= MaxSourcePredicateInlineDepth ||
                semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation invocationOperation ||
                !CanInlineSourceBooleanPredicate(invocationOperation.TargetMethod) ||
                !TryGetReturnedBooleanFormula(
                    invocationOperation.TargetMethod,
                    semanticModel.Compilation,
                    cancellationToken,
                    inlineDepth + 1,
                    out var returnedFormula) ||
                returnedFormula is not { Kind: SmtValueKind.Bool } ||
                !TryCreateSourcePredicateSubstitutions(
                    invocationOperation,
                    semanticModel,
                    cancellationToken,
                    getSymbolVersion,
                    inlineDepth,
                    out var substitutions))
            {
                return false;
            }

            formula = SubstituteVariables(returnedFormula, substitutions);
            return true;
        }

        private static bool TryTranslateKnownStringBooleanInvocation(
            InvocationExpressionSyntax invocationExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation invocationOperation)
            {
                return false;
            }

            return TryTranslateRegexIsMatchInvocation(
                    invocationExpression,
                    invocationOperation,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth) ||
                TryTranslateStringEqualsInvocation(
                    invocationOperation,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth) ||
                TryTranslateStringPredicateInvocation(
                    invocationOperation,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth) ||
                TryTranslateStringIsNullOrEmptyInvocation(
                    invocationOperation,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth);
        }

        private static bool TryTranslateStringEqualsInvocation(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            var method = invocationOperation.TargetMethod;
            if (method.Name != "Equals" ||
                method.ReturnType.SpecialType != SpecialType.System_Boolean ||
                method.ContainingType?.SpecialType != SpecialType.System_String ||
                !HasOrdinalStringComparison(invocationOperation.Arguments, semanticModel, cancellationToken))
            {
                return false;
            }

            if (method.IsStatic)
            {
                if (invocationOperation.Arguments.Length < 3 ||
                    invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax leftExpression ||
                    invocationOperation.Arguments[1].Value.Syntax is not ExpressionSyntax rightExpression ||
                    !TryTranslateStringValue(leftExpression, semanticModel, cancellationToken, out var left, getSymbolVersion, inlineDepth) ||
                    left == null ||
                    !TryTranslateStringValue(rightExpression, semanticModel, cancellationToken, out var right, getSymbolVersion, inlineDepth) ||
                    right == null)
                {
                    return false;
                }

                formula = CreateNullSafeStringEqualityFormula(
                    leftExpression,
                    rightExpression,
                    left,
                    right,
                    semanticModel,
                    cancellationToken,
                    getSymbolVersion,
                    inlineDepth);
                return true;
            }

            if (invocationOperation.Arguments.Length < 2 ||
                invocationOperation.Instance?.Syntax is not ExpressionSyntax receiverExpression ||
                invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax argumentExpression ||
                !TryTranslateStringValue(receiverExpression, semanticModel, cancellationToken, out var receiver, getSymbolVersion, inlineDepth) ||
                receiver == null ||
                !TryTranslateStringValue(argumentExpression, semanticModel, cancellationToken, out var argument, getSymbolVersion, inlineDepth) ||
                argument == null ||
                !TryCreateStringNonNullFormula(receiverExpression, semanticModel, cancellationToken, out var receiverNonNull, getSymbolVersion, inlineDepth) ||
                receiverNonNull == null)
            {
                return false;
            }

            formula = new SmtBinaryFormula(
                SmtBinaryOperator.And,
                receiverNonNull,
                new SmtBinaryFormula(SmtBinaryOperator.Equal, receiver, argument));
            return true;
        }

        private static bool TryTranslateRegexIsMatchInvocation(
            InvocationExpressionSyntax invocationExpression,
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            var method = invocationOperation.TargetMethod;
            if (method.Name != "IsMatch" ||
                !IsRegexType(method.ContainingType))
            {
                return false;
            }

            ExpressionSyntax? inputExpression = null;
            string? pattern = null;
            if (method.IsStatic)
            {
                if (invocationOperation.Arguments.Length < 2 ||
                    !TryGetNoRegexOptions(invocationOperation.Arguments, startIndex: 2, semanticModel, cancellationToken))
                {
                    return false;
                }

                inputExpression = invocationOperation.Arguments[0].Value.Syntax as ExpressionSyntax;
                pattern = TryGetConstantString(invocationOperation.Arguments[1].Value.Syntax as ExpressionSyntax, semanticModel, cancellationToken);
            }
            else
            {
                if (invocationOperation.Arguments.Length < 1 ||
                    !TryGetNoRegexOptions(invocationOperation.Arguments, startIndex: 1, semanticModel, cancellationToken))
                {
                    return false;
                }

                inputExpression = invocationOperation.Arguments[0].Value.Syntax as ExpressionSyntax;
                pattern = TryGetRegexPatternFromReceiver(invocationExpression, semanticModel, cancellationToken);
            }

            if (inputExpression == null ||
                pattern == null ||
                !TryTranslateStringValue(inputExpression, semanticModel, cancellationToken, out var inputFormula, getSymbolVersion, inlineDepth) ||
                inputFormula == null)
            {
                return false;
            }

            formula = new SmtRegexMatchFormula(inputFormula, pattern);
            return true;
        }

        private static bool TryTranslateStringPredicateInvocation(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            var method = invocationOperation.TargetMethod;
            if (method.ReturnType.SpecialType != SpecialType.System_Boolean ||
                method.ContainingType?.SpecialType != SpecialType.System_String ||
                method.IsStatic ||
                method.Parameters.Length < 1 ||
                invocationOperation.Arguments.Length < 1 ||
                invocationOperation.Instance?.Syntax is not ExpressionSyntax receiverExpression)
            {
                return false;
            }

            if (method.Name is not "Contains" and not "StartsWith" and not "EndsWith")
            {
                return false;
            }

            var firstParameterType = method.Parameters[0].Type;
            var isCharPredicateArgument = firstParameterType.SpecialType == SpecialType.System_Char;
            if (method.Name is "StartsWith" or "EndsWith")
            {
                if (!isCharPredicateArgument &&
                    !HasOrdinalStringComparison(invocationOperation.Arguments, semanticModel, cancellationToken))
                {
                    return false;
                }
            }
            else if (invocationOperation.Arguments.Length > 1 &&
                     !HasOrdinalStringComparison(invocationOperation.Arguments, semanticModel, cancellationToken))
            {
                return false;
            }

            if (invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax searchExpression ||
                !TryTranslateStringPredicateArgument(
                    searchExpression,
                    invocationOperation.Arguments[0].Parameter?.Type,
                    semanticModel,
                    cancellationToken,
                    getSymbolVersion,
                    inlineDepth,
                    out var searchFormula) ||
                searchFormula == null ||
                !TryTranslateStringValue(receiverExpression, semanticModel, cancellationToken, out var receiverFormula, getSymbolVersion, inlineDepth) ||
                receiverFormula == null)
            {
                return false;
            }

            formula = method.Name switch
            {
                "Contains" => new SmtStringContainsFormula(receiverFormula, searchFormula),
                "StartsWith" => new SmtStringStartsWithFormula(receiverFormula, searchFormula),
                "EndsWith" => new SmtStringEndsWithFormula(receiverFormula, searchFormula),
                _ => null
            };
            return formula != null;
        }

        private static bool TryTranslateStringPredicateArgument(
            ExpressionSyntax argumentExpression,
            ITypeSymbol? parameterType,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            out SmtFormula? formula)
        {
            formula = null;
            if (parameterType?.SpecialType == SpecialType.System_String)
            {
                return TryTranslateStringValue(
                        argumentExpression,
                        semanticModel,
                        cancellationToken,
                        out formula,
                        getSymbolVersion,
                        inlineDepth) &&
                    formula != null;
            }

            if (parameterType?.SpecialType != SpecialType.System_Char)
            {
                return false;
            }

            var constantValue = semanticModel.GetConstantValue(argumentExpression, cancellationToken);
            if (constantValue is not { HasValue: true, Value: char character })
            {
                return false;
            }

            formula = new SmtStringConstant(character.ToString());
            return true;
        }

        private static bool TryTranslateStringIsNullOrEmptyInvocation(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            var method = invocationOperation.TargetMethod;
            if (!method.IsStatic ||
                method.Name != "IsNullOrEmpty" ||
                method.ContainingType?.SpecialType != SpecialType.System_String ||
                invocationOperation.Arguments.Length != 1 ||
                invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax valueExpression ||
                !TryTranslateValue(valueExpression, semanticModel, cancellationToken, out var referenceFormula, getSymbolVersion, inlineDepth) ||
                referenceFormula is not { Kind: SmtValueKind.Reference } ||
                !TryTranslateStringValue(valueExpression, semanticModel, cancellationToken, out var stringFormula, getSymbolVersion, inlineDepth) ||
                stringFormula == null)
            {
                return false;
            }

            var isNull = new SmtBinaryFormula(SmtBinaryOperator.Equal, referenceFormula, new SmtNullConstant());
            var isEmpty = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtStringLengthTerm(stringFormula),
                new SmtIntegerConstant(0));
            formula = new SmtBinaryFormula(SmtBinaryOperator.Or, isNull, isEmpty);
            return true;
        }

        private static bool IsRegexType(INamedTypeSymbol? type)
        {
            return type?.ToDisplayString() == "System.Text.RegularExpressions.Regex";
        }

        private static string? TryGetRegexPatternFromReceiver(
            InvocationExpressionSyntax invocationExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (invocationExpression.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return null;
            }

            var receiver = UnwrapExpression(memberAccess.Expression);
            if (receiver is ObjectCreationExpressionSyntax objectCreation &&
                objectCreation.ArgumentList?.Arguments.Count >= 1)
            {
                return TryGetConstantString(objectCreation.ArgumentList.Arguments[0].Expression, semanticModel, cancellationToken);
            }

            return null;
        }

        private static bool TryGetNoRegexOptions(
            ImmutableArray<IArgumentOperation> arguments,
            int startIndex,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            for (var index = startIndex; index < arguments.Length; index++)
            {
                var parameterType = arguments[index].Parameter?.Type;
                if (parameterType == null ||
                    parameterType.ToDisplayString() != "System.Text.RegularExpressions.RegexOptions")
                {
                    continue;
                }

                if (!TryGetIntegralConstantValue(arguments[index].Value.Syntax as ExpressionSyntax, semanticModel, cancellationToken, out var options) ||
                    options != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasOrdinalStringComparison(
            ImmutableArray<IArgumentOperation> arguments,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var argument in arguments)
            {
                var parameterType = argument.Parameter?.Type;
                if (parameterType == null ||
                    parameterType.ToDisplayString() != "System.StringComparison")
                {
                    continue;
                }

                return TryGetIntegralConstantValue(argument.Value.Syntax as ExpressionSyntax, semanticModel, cancellationToken, out var comparison) &&
                    comparison == (int)StringComparison.Ordinal;
            }

            return false;
        }

        private static string? TryGetConstantString(
            ExpressionSyntax? expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (expression == null)
            {
                return null;
            }

            expression = UnwrapExpression(expression);
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue is { HasValue: true, Value: string value })
            {
                return value;
            }

            return IsStringEmptyMemberAccess(expression, semanticModel, cancellationToken)
                ? string.Empty
                : null;
        }

        private static bool IsStringExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var typeInfo = semanticModel.GetTypeInfo(UnwrapExpression(expression), cancellationToken);
            return (typeInfo.ConvertedType ?? typeInfo.Type)?.SpecialType == SpecialType.System_String;
        }

        private static bool TryGetIntegralConstantValue(
            ExpressionSyntax? expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out long value)
        {
            value = default;
            if (expression == null)
            {
                return false;
            }

            var constantValue = semanticModel.GetConstantValue(UnwrapExpression(expression), cancellationToken);
            return constantValue.HasValue &&
                constantValue.Value != null &&
                TryGetIntegralConstant(constantValue.Value, out value);
        }

        private static bool CanInlineSourceBooleanPredicate(IMethodSymbol methodSymbol)
        {
            return methodSymbol is
            {
                ReturnsVoid: false,
                ReturnsByRef: false,
                ReturnsByRefReadonly: false,
                ReturnType.SpecialType: SpecialType.System_Boolean,
                DeclaringSyntaxReferences.Length: > 0
            } &&
                methodSymbol.Parameters.All(static parameter => parameter.RefKind == RefKind.None);
        }

        private static bool TryGetReturnedBooleanFormula(
            IMethodSymbol methodSymbol,
            Compilation compilation,
            CancellationToken cancellationToken,
            int inlineDepth,
            out SmtFormula? formula)
        {
            formula = null;

            var callableSyntax = methodSymbol.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(cancellationToken))
                .FirstOrDefault();
            if (callableSyntax == null)
            {
                return false;
            }

            var cache = GetSourceBooleanFormulaCache(compilation);
            var cacheKey = CreateSourceBooleanFormulaCacheKey("method", callableSyntax, inlineDepth);
            var entry = cache.GetOrAdd(
                cacheKey,
                _ =>
                {
                    var returnedSemanticModel = compilation.GetSemanticModel(callableSyntax.SyntaxTree);
                    var success = TryTranslateReturnedBooleanSyntax(
                        callableSyntax,
                        returnedSemanticModel,
                        cancellationToken,
                        inlineDepth,
                        out var cachedFormula);
                    return new SourceBooleanFormulaCacheEntry(success, cachedFormula);
                });

            formula = entry.Formula;
            return entry.Success;
        }

        private static bool TryTranslateReturnedBooleanSyntax(
            SyntaxNode callableSyntax,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int inlineDepth,
            out SmtFormula? formula)
        {
            switch (callableSyntax)
            {
                case MethodDeclarationSyntax methodDeclarationSyntax
                    when methodDeclarationSyntax.ExpressionBody?.Expression != null:
                    return TryTranslate(
                        methodDeclarationSyntax.ExpressionBody.Expression,
                        semanticModel,
                        cancellationToken,
                        out formula,
                        getSymbolVersion: null,
                        inlineDepth);
                case MethodDeclarationSyntax methodDeclarationSyntax
                    when methodDeclarationSyntax.Body != null:
                    return TryTranslateReturnedBooleanBlock(
                        methodDeclarationSyntax.Body,
                        semanticModel,
                        cancellationToken,
                        inlineDepth,
                        out formula);
                case LocalFunctionStatementSyntax localFunctionStatementSyntax
                    when localFunctionStatementSyntax.ExpressionBody?.Expression != null:
                    return TryTranslate(
                        localFunctionStatementSyntax.ExpressionBody.Expression,
                        semanticModel,
                        cancellationToken,
                        out formula,
                        getSymbolVersion: null,
                        inlineDepth);
                case LocalFunctionStatementSyntax localFunctionStatementSyntax
                    when localFunctionStatementSyntax.Body != null:
                    return TryTranslateReturnedBooleanBlock(
                        localFunctionStatementSyntax.Body,
                        semanticModel,
                        cancellationToken,
                        inlineDepth,
                        out formula);
                default:
                    formula = null;
                    return false;
            }
        }

        private static ConcurrentDictionary<SourceBooleanFormulaCacheKey, SourceBooleanFormulaCacheEntry> GetSourceBooleanFormulaCache(
            Compilation compilation)
        {
            return s_sourceBooleanFormulaCache.GetValue(
                compilation,
                static _ => new ConcurrentDictionary<SourceBooleanFormulaCacheKey, SourceBooleanFormulaCacheEntry>());
        }

        private static SourceBooleanFormulaCacheKey CreateSourceBooleanFormulaCacheKey(
            string kind,
            SyntaxNode syntax,
            int inlineDepth)
        {
            var filePath = syntax.SyntaxTree.FilePath ?? string.Empty;
            return new SourceBooleanFormulaCacheKey(kind, filePath, syntax.SpanStart, syntax.Span.Length, inlineDepth);
        }

        private static bool TryTranslateReturnedBooleanBlock(
            BlockSyntax bodySyntax,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int inlineDepth,
            out SmtFormula? formula)
        {
            formula = null;
            var substitutions = new List<SmtVariableSubstitution>();
            var localVariableNames = new HashSet<string>(StringComparer.Ordinal);
            var statementIndex = 0;
            if (!TryCollectLeadingLocalSubstitutions(
                    bodySyntax.Statements,
                    ref statementIndex,
                    semanticModel,
                    cancellationToken,
                    inlineDepth,
                    substitutions,
                    localVariableNames))
            {
                return false;
            }

            return TryTranslateReturnedBooleanStatements(
                bodySyntax.Statements,
                statementIndex,
                substitutions,
                semanticModel,
                cancellationToken,
                inlineDepth,
                out formula) &&
                formula != null &&
                !FormulaReferencesAnyVariableName(formula, localVariableNames);
        }

        private static bool TryTranslateReturnedBooleanStatements(
            SyntaxList<StatementSyntax> statements,
            int statementIndex,
            IReadOnlyList<SmtVariableSubstitution> substitutions,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int inlineDepth,
            out SmtFormula? formula)
        {
            formula = null;
            var remainingStatementCount = statements.Count - statementIndex;
            if (remainingStatementCount == 1)
            {
                if (statements[statementIndex] is ReturnStatementSyntax returnStatement &&
                    returnStatement.Expression != null)
                {
                    if (!TryTranslate(
                        returnStatement.Expression,
                        semanticModel,
                        cancellationToken,
                        out formula,
                        getSymbolVersion: null,
                        inlineDepth))
                    {
                        return false;
                    }

                    formula = SubstituteVariables(formula!, substitutions);
                    return true;
                }

                if (statements[statementIndex] is IfStatementSyntax ifStatement &&
                    ifStatement.Else?.Statement != null &&
                    TryGetSingleReturnExpression(ifStatement.Statement, out var trueReturn) &&
                    TryGetSingleReturnExpression(ifStatement.Else.Statement, out var falseReturn))
                {
                    return TryTranslateReturnedBooleanConditional(
                        ifStatement.Condition,
                        trueReturn,
                        falseReturn,
                        semanticModel,
                        cancellationToken,
                        inlineDepth,
                        substitutions,
                        out formula);
                }

                if (statements[statementIndex] is SwitchStatementSyntax switchStatement)
                {
                    return TryTranslateReturnedBooleanSwitchStatement(
                        switchStatement,
                        fallbackReturnExpression: null,
                        requireFinalDefaultSection: true,
                        substitutions,
                        semanticModel,
                        cancellationToken,
                        inlineDepth,
                        out formula);
                }
            }

            if (remainingStatementCount >= 2 &&
                TryTranslateGuardReturnChain(
                    statements,
                    statementIndex,
                    substitutions,
                    semanticModel,
                    cancellationToken,
                    inlineDepth,
                    out formula))
            {
                return true;
            }

            if (remainingStatementCount == 2 &&
                statements[statementIndex] is SwitchStatementSyntax switchWithFallback &&
                statements[statementIndex + 1] is ReturnStatementSyntax fallbackReturn &&
                fallbackReturn.Expression != null)
            {
                return TryTranslateReturnedBooleanSwitchStatement(
                    switchWithFallback,
                    fallbackReturn.Expression,
                    requireFinalDefaultSection: false,
                    substitutions,
                    semanticModel,
                    cancellationToken,
                    inlineDepth,
                    out formula);
            }

            return false;
        }

        private static bool TryTranslateReturnedBooleanSwitchStatement(
            SwitchStatementSyntax switchStatement,
            ExpressionSyntax? fallbackReturnExpression,
            bool requireFinalDefaultSection,
            IReadOnlyList<SmtVariableSubstitution> substitutions,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int inlineDepth,
            out SmtFormula? formula)
        {
            formula = null;
            if (switchStatement.Sections.Count == 0)
            {
                return false;
            }

            if (requireFinalDefaultSection)
            {
                if (switchStatement.Sections.Count < 2 ||
                    !HasDefaultLabel(switchStatement.Sections[switchStatement.Sections.Count - 1]))
                {
                    return false;
                }
            }
            else if (switchStatement.Sections.Any(HasDefaultLabel))
            {
                return false;
            }

            var sectionConditions = new List<SmtFormula>();
            var sectionValues = new List<SmtFormula>();
            foreach (var section in switchStatement.Sections)
            {
                if (!TryGetSwitchSectionReturnExpression(section, out var returnExpression) ||
                    !SwitchPathConditionBuilder.TryCreateSwitchStatementSectionCondition(
                        switchStatement.Expression,
                        section,
                        semanticModel,
                        cancellationToken,
                        out var sectionCondition) ||
                    !TryTranslate(returnExpression, semanticModel, cancellationToken, out var sectionValue, getSymbolVersion: null, inlineDepth) ||
                    sectionValue is not { Kind: SmtValueKind.Bool })
                {
                    formula = null;
                    return false;
                }

                sectionConditions.Add(sectionCondition);
                sectionValues.Add(sectionValue);
            }

            SmtFormula result;
            var startIndex = sectionValues.Count - 1;
            if (fallbackReturnExpression != null)
            {
                if (!TryTranslate(
                        fallbackReturnExpression,
                        semanticModel,
                        cancellationToken,
                        out var fallbackValue,
                        getSymbolVersion: null,
                        inlineDepth) ||
                    fallbackValue is not { Kind: SmtValueKind.Bool })
                {
                    formula = null;
                    return false;
                }

                result = fallbackValue;
            }
            else
            {
                result = sectionValues[sectionValues.Count - 1];
                startIndex--;
            }

            for (var index = startIndex; index >= 0; index--)
            {
                result = new SmtConditionalFormula(sectionConditions[index], sectionValues[index], result, SmtValueKind.Bool);
            }

            formula = SubstituteVariables(result, substitutions);
            return true;
        }

        private static bool TryGetSwitchSectionReturnExpression(
            SwitchSectionSyntax section,
            out ExpressionSyntax returnExpression)
        {
            returnExpression = null!;
            if (section.Statements.Count != 1 ||
                !TryGetSingleReturnExpression(section.Statements[0], out returnExpression))
            {
                return false;
            }

            return true;
        }

        private static bool HasDefaultLabel(SwitchSectionSyntax section)
        {
            return section.Labels.Any(static label => label is DefaultSwitchLabelSyntax);
        }

        private static bool TryTranslateGuardReturnChain(
            SyntaxList<StatementSyntax> statements,
            int statementIndex,
            IReadOnlyList<SmtVariableSubstitution> substitutions,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int inlineDepth,
            out SmtFormula? formula)
        {
            formula = null;
            if (statements[statements.Count - 1] is not ReturnStatementSyntax finalReturnStatement ||
                finalReturnStatement.Expression == null)
            {
                return false;
            }

            var guards = new List<(ExpressionSyntax Condition, ExpressionSyntax ReturnExpression)>();
            for (var index = statementIndex; index < statements.Count - 1; index++)
            {
                if (statements[index] is not IfStatementSyntax guard ||
                    guard.Else != null ||
                    !TryGetSingleReturnExpression(guard.Statement, out var guardReturn))
                {
                    return false;
                }

                guards.Add((guard.Condition, guardReturn));
            }

            if (guards.Count == 0 ||
                !TryTranslate(
                    finalReturnStatement.Expression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion: null,
                    inlineDepth) ||
                formula is not { Kind: SmtValueKind.Bool })
            {
                formula = null;
                return false;
            }

            for (var index = guards.Count - 1; index >= 0; index--)
            {
                var guard = guards[index];
                if (!TryTranslate(guard.Condition, semanticModel, cancellationToken, out var conditionFormula, getSymbolVersion: null, inlineDepth) ||
                    conditionFormula is not { Kind: SmtValueKind.Bool } ||
                    !TryTranslate(guard.ReturnExpression, semanticModel, cancellationToken, out var guardReturnFormula, getSymbolVersion: null, inlineDepth) ||
                    guardReturnFormula is not { Kind: SmtValueKind.Bool })
                {
                    formula = null;
                    return false;
                }

                formula = new SmtConditionalFormula(conditionFormula, guardReturnFormula, formula, SmtValueKind.Bool);
            }

            formula = SubstituteVariables(formula, substitutions);
            return true;
        }

        private static bool TryCollectLeadingLocalSubstitutions(
            SyntaxList<StatementSyntax> statements,
            ref int statementIndex,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int inlineDepth,
            IList<SmtVariableSubstitution> substitutions,
            ISet<string> localVariableNames)
        {
            while (statementIndex < statements.Count)
            {
                switch (statements[statementIndex])
                {
                    case LocalDeclarationStatementSyntax localDeclaration:
                        if (!TryCollectLocalDeclarationSubstitutions(
                                localDeclaration,
                                semanticModel,
                                cancellationToken,
                                inlineDepth,
                                substitutions,
                                localVariableNames))
                        {
                            return false;
                        }

                        statementIndex++;
                        continue;
                    case ExpressionStatementSyntax expressionStatement
                        when expressionStatement.Expression is AssignmentExpressionSyntax assignment &&
                            assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
                        if (!TryCollectLocalAssignmentSubstitution(
                                assignment,
                                semanticModel,
                                cancellationToken,
                                inlineDepth,
                                substitutions,
                                localVariableNames))
                        {
                            return false;
                        }

                        statementIndex++;
                        continue;
                    default:
                        return true;
                }
            }

            return true;
        }

        private static bool TryCollectLocalDeclarationSubstitutions(
            LocalDeclarationStatementSyntax localDeclaration,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int inlineDepth,
            IList<SmtVariableSubstitution> substitutions,
            ISet<string> localVariableNames)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (semanticModel.GetDeclaredSymbol(variable, cancellationToken) is not ILocalSymbol localSymbol ||
                    !TryGetValueKind(localSymbol.Type, out var localKind))
                {
                    return false;
                }

                var localVariableName = GetVariableName(localSymbol, getSymbolVersion: null);
                localVariableNames.Add(localVariableName);
                if (variable.Initializer == null)
                {
                    continue;
                }

                if (!TryTranslateValue(
                        variable.Initializer.Value,
                        semanticModel,
                        cancellationToken,
                        out var initializerFormula,
                        getSymbolVersion: null,
                        inlineDepth) ||
                    initializerFormula == null ||
                    initializerFormula.Kind != localKind)
                {
                    return false;
                }

                var replacement = SubstituteVariables(initializerFormula, substitutions.ToArray());
                SetLocalSubstitution(localVariableName, localKind, replacement, substitutions);
            }

            return true;
        }

        private static bool TryCollectLocalAssignmentSubstitution(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int inlineDepth,
            IList<SmtVariableSubstitution> substitutions,
            ISet<string> localVariableNames)
        {
            var left = UnwrapExpression(assignment.Left);
            if (semanticModel.GetSymbolInfo(left, cancellationToken).Symbol is not ILocalSymbol localSymbol ||
                !TryGetValueKind(localSymbol.Type, out var localKind))
            {
                return false;
            }

            var localVariableName = GetVariableName(localSymbol, getSymbolVersion: null);
            if (!localVariableNames.Contains(localVariableName) ||
                !TryTranslateValue(
                    assignment.Right,
                    semanticModel,
                    cancellationToken,
                    out var assignedFormula,
                    getSymbolVersion: null,
                    inlineDepth) ||
                assignedFormula == null ||
                assignedFormula.Kind != localKind)
            {
                return false;
            }

            var replacement = SubstituteVariables(assignedFormula, substitutions.ToArray());
            SetLocalSubstitution(localVariableName, localKind, replacement, substitutions);
            return true;
        }

        private static void SetLocalSubstitution(
            string localVariableName,
            SmtValueKind localKind,
            SmtFormula replacement,
            IList<SmtVariableSubstitution> substitutions)
        {
            for (var index = substitutions.Count - 1; index >= 0; index--)
            {
                if (string.Equals(substitutions[index].ExactName, localVariableName, StringComparison.Ordinal))
                {
                    substitutions.RemoveAt(index);
                }
            }

            var localVariable = new SmtVariable(localVariableName, localKind);
            substitutions.Add(new SmtVariableSubstitution(
                localVariable.Name,
                localVariable.Name + ".",
                localVariable + ".",
                replacement));
        }

        private static bool TryTranslateReturnedBooleanConditional(
            ExpressionSyntax condition,
            ExpressionSyntax whenTrue,
            ExpressionSyntax whenFalse,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            int inlineDepth,
            IReadOnlyList<SmtVariableSubstitution> substitutions,
            out SmtFormula? formula)
        {
            formula = null;
            if (!TryTranslate(condition, semanticModel, cancellationToken, out var conditionFormula, getSymbolVersion: null, inlineDepth) ||
                conditionFormula is not { Kind: SmtValueKind.Bool } ||
                !TryTranslate(whenTrue, semanticModel, cancellationToken, out var whenTrueFormula, getSymbolVersion: null, inlineDepth) ||
                whenTrueFormula is not { Kind: SmtValueKind.Bool } ||
                !TryTranslate(whenFalse, semanticModel, cancellationToken, out var whenFalseFormula, getSymbolVersion: null, inlineDepth) ||
                whenFalseFormula is not { Kind: SmtValueKind.Bool })
            {
                return false;
            }

            formula = SubstituteVariables(
                new SmtConditionalFormula(conditionFormula, whenTrueFormula, whenFalseFormula, SmtValueKind.Bool),
                substitutions);
            return true;
        }

        private static bool TryGetSingleReturnExpression(
            StatementSyntax statement,
            out ExpressionSyntax expression)
        {
            if (statement is ReturnStatementSyntax returnStatement &&
                returnStatement.Expression != null)
            {
                expression = returnStatement.Expression;
                return true;
            }

            if (statement is BlockSyntax block &&
                block.Statements.Count == 1 &&
                block.Statements[0] is ReturnStatementSyntax blockReturnStatement &&
                blockReturnStatement.Expression != null)
            {
                expression = blockReturnStatement.Expression;
                return true;
            }

            expression = null!;
            return false;
        }

        private static bool TryCreateSourcePredicateSubstitutions(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth,
            out IReadOnlyList<SmtVariableSubstitution> substitutions)
        {
            var builder = new List<SmtVariableSubstitution>(invocationOperation.Arguments.Length + 1);
            if (!invocationOperation.TargetMethod.IsStatic)
            {
                if (invocationOperation.Instance?.Syntax is not ExpressionSyntax receiverExpression ||
                    !TryTranslateValue(
                        receiverExpression,
                        semanticModel,
                        cancellationToken,
                        out var receiverFormula,
                        getSymbolVersion,
                        inlineDepth) ||
                    receiverFormula is not { Kind: SmtValueKind.Reference })
                {
                    substitutions = Array.Empty<SmtVariableSubstitution>();
                    return false;
                }

                builder.Add(CreateImplicitThisSubstitution(receiverFormula));
            }

            foreach (var argument in invocationOperation.Arguments)
            {
                var parameter = argument.Parameter;
                if (parameter == null ||
                    !TryGetValueKind(parameter.Type, out var parameterKind) ||
                    argument.Value.Syntax is not ExpressionSyntax argumentExpression)
                {
                    substitutions = Array.Empty<SmtVariableSubstitution>();
                    return false;
                }

                if (!TryTranslateValue(argumentExpression, semanticModel, cancellationToken, out var argumentFormula, getSymbolVersion, inlineDepth) ||
                    argumentFormula == null ||
                    argumentFormula.Kind != parameterKind)
                {
                    substitutions = Array.Empty<SmtVariableSubstitution>();
                    return false;
                }

                var formalVariable = new SmtVariable(GetVariableName(parameter, getSymbolVersion: null), parameterKind);
                builder.Add(new SmtVariableSubstitution(
                    formalVariable.Name,
                    formalVariable.Name + ".",
                    formalVariable + ".",
                    argumentFormula));

                if (parameter.Type.SpecialType == SpecialType.System_String &&
                    TryTranslateStringValue(argumentExpression, semanticModel, cancellationToken, out var argumentStringFormula, getSymbolVersion, inlineDepth) &&
                    argumentStringFormula != null)
                {
                    var formalStringVariable = new SmtVariable(formalVariable.Name + ".String", SmtValueKind.String);
                    builder.Add(new SmtVariableSubstitution(
                        formalStringVariable.Name,
                        formalStringVariable.Name + ".",
                        formalStringVariable + ".",
                        argumentStringFormula));
                }
            }

            substitutions = builder;
            return true;
        }

        private static SmtVariableSubstitution CreateImplicitThisSubstitution(SmtFormula receiver)
        {
            return new SmtVariableSubstitution(
                ImplicitThisVariableName,
                ImplicitThisVariableName + ".",
                new SmtVariable(ImplicitThisVariableName, SmtValueKind.Reference) + ".",
                receiver);
        }

        private static SmtFormula SubstituteVariables(
            SmtFormula formula,
            IReadOnlyList<SmtVariableSubstitution> substitutions)
        {
            switch (formula)
            {
                case SmtVariable variable:
                    return SubstituteVariable(variable, substitutions);
                case SmtUnaryFormula unary:
                    return new SmtUnaryFormula(unary.Operator, SubstituteVariables(unary.Operand, substitutions));
                case SmtBinaryFormula binary:
                    return new SmtBinaryFormula(
                        binary.Operator,
                        SubstituteVariables(binary.Left, substitutions),
                        SubstituteVariables(binary.Right, substitutions));
                case SmtIntegerUnaryTerm unary:
                    return new SmtIntegerUnaryTerm(unary.Operator, SubstituteVariables(unary.Operand, substitutions));
                case SmtIntegerBinaryTerm binary:
                    return new SmtIntegerBinaryTerm(
                        binary.Operator,
                        SubstituteVariables(binary.Left, substitutions),
                        SubstituteVariables(binary.Right, substitutions));
                case SmtStringLengthTerm stringLength:
                    return new SmtStringLengthTerm(SubstituteVariables(stringLength.Value, substitutions));
                case SmtStringConcatTerm stringConcat:
                    return new SmtStringConcatTerm(
                        SubstituteVariables(stringConcat.Left, substitutions),
                        SubstituteVariables(stringConcat.Right, substitutions));
                case SmtStringContainsFormula stringContains:
                    return new SmtStringContainsFormula(
                        SubstituteVariables(stringContains.Value, substitutions),
                        SubstituteVariables(stringContains.Search, substitutions));
                case SmtStringStartsWithFormula stringStartsWith:
                    return new SmtStringStartsWithFormula(
                        SubstituteVariables(stringStartsWith.Value, substitutions),
                        SubstituteVariables(stringStartsWith.Prefix, substitutions));
                case SmtStringEndsWithFormula stringEndsWith:
                    return new SmtStringEndsWithFormula(
                        SubstituteVariables(stringEndsWith.Value, substitutions),
                        SubstituteVariables(stringEndsWith.Suffix, substitutions));
                case SmtRegexMatchFormula regexMatch:
                    return new SmtRegexMatchFormula(
                        SubstituteVariables(regexMatch.Value, substitutions),
                        regexMatch.Pattern);
                case SmtConditionalFormula conditional:
                    return new SmtConditionalFormula(
                        SubstituteVariables(conditional.Condition, substitutions),
                        SubstituteVariables(conditional.WhenTrue, substitutions),
                        SubstituteVariables(conditional.WhenFalse, substitutions),
                        conditional.ResultKind);
                default:
                    return formula;
            }
        }

        private static SmtFormula SubstituteVariable(
            SmtVariable variable,
            IReadOnlyList<SmtVariableSubstitution> substitutions)
        {
            foreach (var substitution in substitutions)
            {
                if (string.Equals(variable.Name, substitution.ExactName, StringComparison.Ordinal))
                {
                    return substitution.Replacement;
                }
            }

            foreach (var substitution in substitutions)
            {
                if (TrySubstituteMemberVariable(variable, substitution.SimpleMemberPrefix, substitution.Replacement, out var simpleMemberReplacement) ||
                    TrySubstituteMemberVariable(variable, substitution.FormulaMemberPrefix, substitution.Replacement, out simpleMemberReplacement))
                {
                    return simpleMemberReplacement;
                }

                var renderedReceiver = substitution.FormulaMemberPrefix.TrimEnd('.');
                if (renderedReceiver.Length > 0 &&
                    variable.Name.Contains(renderedReceiver, StringComparison.Ordinal))
                {
                    return new SmtVariable(
                        variable.Name.Replace(renderedReceiver, substitution.Replacement.ToString()),
                        variable.Kind);
                }
            }

            return variable;
        }

        private static bool TrySubstituteMemberVariable(
            SmtVariable variable,
            string memberPrefix,
            SmtFormula replacement,
            out SmtFormula substituted)
        {
            if (!variable.Name.StartsWith(memberPrefix, StringComparison.Ordinal))
            {
                substituted = null!;
                return false;
            }

            var suffix = variable.Name.Substring(memberPrefix.Length - 1);
            substituted = new SmtVariable(replacement + suffix, variable.Kind);
            return true;
        }

        private static bool FormulaReferencesAnyVariableName(
            SmtFormula formula,
            ISet<string> variableNames)
        {
            if (variableNames.Count == 0)
            {
                return false;
            }

            switch (formula)
            {
                case SmtVariable variable:
                    return variableNames.Any(name =>
                        string.Equals(variable.Name, name, StringComparison.Ordinal) ||
                        variable.Name.StartsWith(name + ".", StringComparison.Ordinal));
                case SmtUnaryFormula unary:
                    return FormulaReferencesAnyVariableName(unary.Operand, variableNames);
                case SmtBinaryFormula binary:
                    return FormulaReferencesAnyVariableName(binary.Left, variableNames) ||
                        FormulaReferencesAnyVariableName(binary.Right, variableNames);
                case SmtIntegerUnaryTerm unary:
                    return FormulaReferencesAnyVariableName(unary.Operand, variableNames);
                case SmtIntegerBinaryTerm binary:
                    return FormulaReferencesAnyVariableName(binary.Left, variableNames) ||
                        FormulaReferencesAnyVariableName(binary.Right, variableNames);
                case SmtStringLengthTerm stringLength:
                    return FormulaReferencesAnyVariableName(stringLength.Value, variableNames);
                case SmtStringConcatTerm stringConcat:
                    return FormulaReferencesAnyVariableName(stringConcat.Left, variableNames) ||
                        FormulaReferencesAnyVariableName(stringConcat.Right, variableNames);
                case SmtStringContainsFormula stringContains:
                    return FormulaReferencesAnyVariableName(stringContains.Value, variableNames) ||
                        FormulaReferencesAnyVariableName(stringContains.Search, variableNames);
                case SmtStringStartsWithFormula stringStartsWith:
                    return FormulaReferencesAnyVariableName(stringStartsWith.Value, variableNames) ||
                        FormulaReferencesAnyVariableName(stringStartsWith.Prefix, variableNames);
                case SmtStringEndsWithFormula stringEndsWith:
                    return FormulaReferencesAnyVariableName(stringEndsWith.Value, variableNames) ||
                        FormulaReferencesAnyVariableName(stringEndsWith.Suffix, variableNames);
                case SmtRegexMatchFormula regexMatch:
                    return FormulaReferencesAnyVariableName(regexMatch.Value, variableNames);
                case SmtConditionalFormula conditional:
                    return FormulaReferencesAnyVariableName(conditional.Condition, variableNames) ||
                        FormulaReferencesAnyVariableName(conditional.WhenTrue, variableNames) ||
                        FormulaReferencesAnyVariableName(conditional.WhenFalse, variableNames);
                default:
                    return false;
            }
        }

        private sealed class SmtVariableSubstitution
        {
            public SmtVariableSubstitution(
                string exactName,
                string simpleMemberPrefix,
                string formulaMemberPrefix,
                SmtFormula replacement)
            {
                ExactName = exactName;
                SimpleMemberPrefix = simpleMemberPrefix;
                FormulaMemberPrefix = formulaMemberPrefix;
                Replacement = replacement;
            }

            public string ExactName { get; }

            public string SimpleMemberPrefix { get; }

            public string FormulaMemberPrefix { get; }

            public SmtFormula Replacement { get; }
        }

        public static bool TryGetKnownStringLength(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out int length)
        {
            expression = UnwrapExpression(expression);
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue is { HasValue: true, Value: string stringValue })
            {
                length = stringValue.Length;
                return true;
            }

            if (IsStringEmptyMemberAccess(expression, semanticModel, cancellationToken))
            {
                length = 0;
                return true;
            }

            length = default;
            return false;
        }

        private static bool IsStringEmptyMemberAccess(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText == "Empty" &&
                semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is IFieldSymbol
                {
                    IsStatic: true,
                    ContainingType.SpecialType: SpecialType.System_String
                };
        }

        public static bool TryTranslateStringValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            expression = UnwrapExpression(expression);
            formula = null;

            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue is { HasValue: true, Value: string stringValue })
            {
                formula = new SmtStringConstant(stringValue);
                return true;
            }

            if (IsStringEmptyMemberAccess(expression, semanticModel, cancellationToken))
            {
                formula = new SmtStringConstant(string.Empty);
                return true;
            }

            if (TryTranslateImplicitThisStringMemberValue(expression, semanticModel, cancellationToken, out formula) &&
                formula != null)
            {
                return true;
            }

            if (expression is ConditionalExpressionSyntax conditionalExpression &&
                TryTranslate(conditionalExpression.Condition, semanticModel, cancellationToken, out var conditionFormula, getSymbolVersion, inlineDepth) &&
                conditionFormula != null &&
                TryTranslateStringValue(conditionalExpression.WhenTrue, semanticModel, cancellationToken, out var whenTrue, getSymbolVersion, inlineDepth) &&
                whenTrue != null &&
                TryTranslateStringValue(conditionalExpression.WhenFalse, semanticModel, cancellationToken, out var whenFalse, getSymbolVersion, inlineDepth) &&
                whenFalse != null)
            {
                formula = new SmtConditionalFormula(conditionFormula, whenTrue, whenFalse, SmtValueKind.String);
                return true;
            }

            if (expression is BinaryExpressionSyntax coalesceExpression &&
                coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                TryTranslateValue(coalesceExpression.Left, semanticModel, cancellationToken, out var coalesceReference, getSymbolVersion, inlineDepth) &&
                coalesceReference is { Kind: SmtValueKind.Reference } &&
                TryTranslateStringValue(coalesceExpression.Left, semanticModel, cancellationToken, out var coalesceLeft, getSymbolVersion, inlineDepth) &&
                coalesceLeft != null &&
                TryTranslateStringValue(coalesceExpression.Right, semanticModel, cancellationToken, out var coalesceRight, getSymbolVersion, inlineDepth) &&
                coalesceRight != null)
            {
                formula = new SmtConditionalFormula(
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, coalesceReference, new SmtNullConstant()),
                    coalesceLeft,
                    coalesceRight,
                    SmtValueKind.String);
                return true;
            }

            if (expression is BinaryExpressionSyntax addExpression &&
                addExpression.IsKind(SyntaxKind.AddExpression) &&
                IsStringExpression(expression, semanticModel, cancellationToken) &&
                TryTranslateStringConcatOperand(addExpression.Left, semanticModel, cancellationToken, out var concatLeft, getSymbolVersion, inlineDepth) &&
                concatLeft != null &&
                TryTranslateStringConcatOperand(addExpression.Right, semanticModel, cancellationToken, out var concatRight, getSymbolVersion, inlineDepth) &&
                concatRight != null)
            {
                formula = new SmtStringConcatTerm(concatLeft, concatRight);
                return true;
            }

            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            var type = typeInfo.ConvertedType ?? typeInfo.Type;
            if (type?.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
            if (symbol is ILocalSymbol or IParameterSymbol)
            {
                formula = new SmtVariable(GetVariableName(symbol.OriginalDefinition, getSymbolVersion) + ".String", SmtValueKind.String);
                return true;
            }

            if (expression is MemberAccessExpressionSyntax memberAccess &&
                semanticModel.GetSymbolInfo(memberAccess.Name, cancellationToken).Symbol is IPropertySymbol or IFieldSymbol &&
                TryTranslateValue(memberAccess.Expression, semanticModel, cancellationToken, out var receiver, getSymbolVersion, inlineDepth) &&
                receiver != null)
            {
                formula = new SmtVariable(receiver + "." + memberAccess.Name.Identifier.ValueText + ".String", SmtValueKind.String);
                return true;
            }

            return false;
        }

        private static bool TryTranslateStringConcatOperand(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!TryTranslateStringValue(expression, semanticModel, cancellationToken, out var stringFormula, getSymbolVersion, inlineDepth) ||
                stringFormula == null)
            {
                return false;
            }

            if (TryTranslateValue(expression, semanticModel, cancellationToken, out var referenceFormula, getSymbolVersion, inlineDepth) &&
                referenceFormula is { Kind: SmtValueKind.Reference })
            {
                formula = new SmtConditionalFormula(
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, referenceFormula, new SmtNullConstant()),
                    stringFormula,
                    new SmtStringConstant(string.Empty),
                    SmtValueKind.String);
                return true;
            }

            formula = stringFormula;
            return true;
        }

        private static bool TryTranslateImplicitThisStringMemberValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula)
        {
            formula = null;
            if (expression is not IdentifierNameSyntax ||
                semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is not IPropertySymbol and not IFieldSymbol ||
                semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is not { IsStatic: false } memberSymbol ||
                !TryGetMemberType(memberSymbol, out var memberType) ||
                memberType.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            formula = new SmtVariable(
                new SmtVariable(ImplicitThisVariableName, SmtValueKind.Reference) + "." + memberSymbol.Name + ".String",
                SmtValueKind.String);
            return true;
        }

        private static bool TryTranslateStringComparison(
            BinaryExpressionSyntax binaryExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!binaryExpression.IsKind(SyntaxKind.EqualsExpression) &&
                !binaryExpression.IsKind(SyntaxKind.NotEqualsExpression))
            {
                return false;
            }

            if (!TryTranslateStringValue(binaryExpression.Left, semanticModel, cancellationToken, out var left, getSymbolVersion, inlineDepth) ||
                left == null ||
                !TryTranslateStringValue(binaryExpression.Right, semanticModel, cancellationToken, out var right, getSymbolVersion, inlineDepth) ||
                right == null)
            {
                return false;
            }

            var equality = CreateNullSafeStringEqualityFormula(
                binaryExpression.Left,
                binaryExpression.Right,
                left,
                right,
                semanticModel,
                cancellationToken,
                getSymbolVersion,
                inlineDepth);

            formula = binaryExpression.IsKind(SyntaxKind.EqualsExpression)
                ? equality
                : new SmtUnaryFormula(SmtUnaryOperator.Not, equality);
            return true;
        }

        private static SmtFormula CreateNullSafeStringEqualityFormula(
            ExpressionSyntax leftExpression,
            ExpressionSyntax rightExpression,
            SmtFormula left,
            SmtFormula right,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            var valuesEqual = new SmtBinaryFormula(SmtBinaryOperator.Equal, left, right);
            if (!TryCreateStringNonNullFormula(leftExpression, semanticModel, cancellationToken, out var leftNonNull, getSymbolVersion, inlineDepth) ||
                leftNonNull == null ||
                !TryCreateStringNonNullFormula(rightExpression, semanticModel, cancellationToken, out var rightNonNull, getSymbolVersion, inlineDepth) ||
                rightNonNull == null)
            {
                return valuesEqual;
            }

            var bothNull = new SmtBinaryFormula(
                SmtBinaryOperator.And,
                new SmtUnaryFormula(SmtUnaryOperator.Not, leftNonNull),
                new SmtUnaryFormula(SmtUnaryOperator.Not, rightNonNull));
            var bothNonNull = new SmtBinaryFormula(SmtBinaryOperator.And, leftNonNull, rightNonNull);
            return new SmtBinaryFormula(
                SmtBinaryOperator.Or,
                bothNull,
                new SmtBinaryFormula(SmtBinaryOperator.And, bothNonNull, valuesEqual));
        }

        public static bool TryCreateStringNonNullFormula(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            expression = UnwrapExpression(expression);
            formula = null;

            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue)
            {
                if (constantValue.Value is string)
                {
                    formula = new SmtBooleanConstant(true);
                    return true;
                }

                if (constantValue.Value == null)
                {
                    formula = new SmtBooleanConstant(false);
                    return true;
                }
            }

            if (IsStringEmptyMemberAccess(expression, semanticModel, cancellationToken))
            {
                formula = new SmtBooleanConstant(true);
                return true;
            }

            if (expression is BinaryExpressionSyntax addExpression &&
                addExpression.IsKind(SyntaxKind.AddExpression) &&
                IsStringExpression(expression, semanticModel, cancellationToken))
            {
                formula = new SmtBooleanConstant(true);
                return true;
            }

            if (expression is BinaryExpressionSyntax coalesceExpression &&
                coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                IsStringExpression(expression, semanticModel, cancellationToken) &&
                TryCreateStringNonNullFormula(coalesceExpression.Left, semanticModel, cancellationToken, out var coalesceLeftNonNull, getSymbolVersion, inlineDepth) &&
                coalesceLeftNonNull != null &&
                TryCreateStringNonNullFormula(coalesceExpression.Right, semanticModel, cancellationToken, out var coalesceRightNonNull, getSymbolVersion, inlineDepth) &&
                coalesceRightNonNull != null)
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.Or, coalesceLeftNonNull, coalesceRightNonNull);
                return true;
            }

            if (expression is ConditionalExpressionSyntax conditionalExpression &&
                TryTranslate(conditionalExpression.Condition, semanticModel, cancellationToken, out var conditionFormula, getSymbolVersion, inlineDepth) &&
                conditionFormula != null &&
                TryCreateStringNonNullFormula(conditionalExpression.WhenTrue, semanticModel, cancellationToken, out var whenTrueNonNull, getSymbolVersion, inlineDepth) &&
                whenTrueNonNull != null &&
                TryCreateStringNonNullFormula(conditionalExpression.WhenFalse, semanticModel, cancellationToken, out var whenFalseNonNull, getSymbolVersion, inlineDepth) &&
                whenFalseNonNull != null)
            {
                formula = new SmtConditionalFormula(conditionFormula, whenTrueNonNull, whenFalseNonNull, SmtValueKind.Bool);
                return true;
            }

            if (TryTranslateValue(expression, semanticModel, cancellationToken, out var referenceFormula, getSymbolVersion, inlineDepth) &&
                referenceFormula is { Kind: SmtValueKind.Reference })
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, referenceFormula, new SmtNullConstant());
                return true;
            }

            return false;
        }

        public static bool TryCollectBranchAssumptions(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas)
        {
            return TryCollectBranchAssumptions(expression, branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion: null);
        }

        public static bool TryCollectBranchAssumptions(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var originalCount = formulas.Count;
            TryCollectDomainFacts(expression, semanticModel, cancellationToken, formulas, getSymbolVersion);
            AddBranchAssumptions(expression, branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion);
            return formulas.Count > originalCount;
        }

        public static bool TryCollectPatternBindingFacts(
            SmtFormula matchedValue,
            ITypeSymbol? matchedValueType,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            var originalCount = formulas.Count;
            AddPatternBindingFacts(
                matchedValue,
                matchedValueType,
                pattern,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion);
            return formulas.Count > originalCount;
        }

        public static bool TryTranslateBuiltInElementAccessInRange(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            formula = null!;
            if (elementAccess.ArgumentList.Arguments.Count != 1)
            {
                return false;
            }

            var receiverType = semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).Type;
            if (receiverType is not IArrayTypeSymbol { Rank: 1 } &&
                receiverType?.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            if (!TryCreateBuiltInElementAccessLengthFormula(
                    elementAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var lengthFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                !TryCreateEffectiveBuiltInIndexFormula(
                    elementAccess.ArgumentList.Arguments[0].Expression,
                    lengthFormula,
                    semanticModel,
                    cancellationToken,
                    out var indexFormula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            var lowerBound = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                indexFormula,
                new SmtIntegerConstant(0));
            var upperBound = new SmtBinaryFormula(
                SmtBinaryOperator.LessThan,
                indexFormula,
                lengthFormula);
            formula = new SmtBinaryFormula(SmtBinaryOperator.And, lowerBound, upperBound);
            return true;
        }

        public static bool TryTranslateBuiltInLengthValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return TryCreateBuiltInElementAccessLengthFormula(
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        public static bool TryCollectDomainFacts(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            var originalCount = formulas.Count;
            expression = UnwrapExpression(expression);

            foreach (var memberAccess in expression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
            {
                if (!IsBuiltInNonNegativeLengthAccess(memberAccess, semanticModel, cancellationToken) ||
                    !TryTranslateValue(memberAccess, semanticModel, cancellationToken, out var lengthFormula, getSymbolVersion) ||
                    lengthFormula is not { Kind: SmtValueKind.Int })
                {
                    continue;
                }

                formulas.Add(new SmtBinaryFormula(
                    SmtBinaryOperator.GreaterThanOrEqual,
                    lengthFormula,
                    new SmtIntegerConstant(0)));
            }

            foreach (var invocation in expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                if (semanticModel.GetOperation(invocation, cancellationToken) is IInvocationOperation invocationOperation)
                {
                    AddKnownStringInvocationDomainFacts(invocationOperation, semanticModel, cancellationToken, formulas, getSymbolVersion);
                }
            }

            return formulas.Count > originalCount;
        }

        private static void AddKnownStringInvocationDomainFacts(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var method = invocationOperation.TargetMethod;
            if (method.Name == "IsMatch" &&
                IsRegexType(method.ContainingType) &&
                TryGetRegexInputExpression(invocationOperation, out var regexInputExpression))
            {
                AddStringNonNullDomainFact(regexInputExpression, semanticModel, cancellationToken, formulas, getSymbolVersion);
                return;
            }

            if (method.ReturnType.SpecialType != SpecialType.System_Boolean ||
                method.ContainingType?.SpecialType != SpecialType.System_String ||
                method.IsStatic ||
                method.Name is not "Contains" and not "StartsWith" and not "EndsWith")
            {
                return;
            }

            if (invocationOperation.Instance?.Syntax is ExpressionSyntax receiverExpression)
            {
                AddStringNonNullDomainFact(receiverExpression, semanticModel, cancellationToken, formulas, getSymbolVersion);
            }

            if (method.Parameters.Length >= 1 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                invocationOperation.Arguments.Length >= 1 &&
                invocationOperation.Arguments[0].Value.Syntax is ExpressionSyntax searchExpression)
            {
                AddStringNonNullDomainFact(searchExpression, semanticModel, cancellationToken, formulas, getSymbolVersion);
            }
        }

        private static bool TryGetRegexInputExpression(
            IInvocationOperation invocationOperation,
            out ExpressionSyntax inputExpression)
        {
            inputExpression = null!;
            var method = invocationOperation.TargetMethod;
            if (method.Name != "IsMatch" ||
                !IsRegexType(method.ContainingType))
            {
                return false;
            }

            if (method.IsStatic)
            {
                if (method.Parameters.Length < 1 ||
                    method.Parameters[0].Type.SpecialType != SpecialType.System_String ||
                    invocationOperation.Arguments.Length < 1 ||
                    invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax staticInputExpression)
                {
                    return false;
                }

                inputExpression = staticInputExpression;
                return true;
            }

            if (method.Parameters.Length < 1 ||
                method.Parameters[0].Type.SpecialType != SpecialType.System_String ||
                invocationOperation.Arguments.Length < 1 ||
                invocationOperation.Arguments[0].Value.Syntax is not ExpressionSyntax instanceInputExpression)
            {
                return false;
            }

            inputExpression = instanceInputExpression;
            return true;
        }

        private static void AddStringNonNullDomainFact(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (TryCreateStringNonNullFormula(expression, semanticModel, cancellationToken, out var nonNullFormula, getSymbolVersion) &&
                nonNullFormula != null)
            {
                formulas.Add(nonNullFormula);
            }
        }

        private static void AddBranchAssumptions(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            expression = UnwrapExpression(expression);

            if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression))
            {
                AddBranchAssumptions(prefixUnary.Operand, !branchWhenTrue, semanticModel, cancellationToken, formulas, getSymbolVersion);
                return;
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (branchWhenTrue && binaryExpression.IsKind(SyntaxKind.LogicalAndExpression))
                {
                    AddBranchAssumptions(binaryExpression.Left, branchWhenTrue: true, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    AddBranchAssumptions(binaryExpression.Right, branchWhenTrue: true, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    return;
                }

                if (branchWhenTrue &&
                    binaryExpression.IsKind(SyntaxKind.BitwiseAndExpression) &&
                    HasSupportedBooleanType(binaryExpression, semanticModel, cancellationToken))
                {
                    AddBranchAssumptions(binaryExpression.Left, branchWhenTrue: true, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    AddBranchAssumptions(binaryExpression.Right, branchWhenTrue: true, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    return;
                }

                if (!branchWhenTrue && binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
                {
                    AddBranchAssumptions(binaryExpression.Left, branchWhenTrue: false, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    AddBranchAssumptions(binaryExpression.Right, branchWhenTrue: false, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    return;
                }

                if (!branchWhenTrue &&
                    binaryExpression.IsKind(SyntaxKind.BitwiseOrExpression) &&
                    HasSupportedBooleanType(binaryExpression, semanticModel, cancellationToken))
                {
                    AddBranchAssumptions(binaryExpression.Left, branchWhenTrue: false, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    AddBranchAssumptions(binaryExpression.Right, branchWhenTrue: false, semanticModel, cancellationToken, formulas, getSymbolVersion);
                    return;
                }
            }

            if (branchWhenTrue)
            {
                AddPatternBindingFacts(expression, semanticModel, cancellationToken, formulas, getSymbolVersion);
            }

            if (!TryTranslate(expression, semanticModel, cancellationToken, out var formula, getSymbolVersion) ||
                formula == null)
            {
                return;
            }

            formulas.Add(branchWhenTrue
                ? formula
                : new SmtUnaryFormula(SmtUnaryOperator.Not, formula));
        }

        private static void AddPatternBindingFacts(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            expression = UnwrapExpression(expression);
            if (expression is not IsPatternExpressionSyntax isPatternExpression)
            {
                return;
            }

            if (TryAddNullablePatternBindingFacts(isPatternExpression, semanticModel, cancellationToken, formulas, getSymbolVersion))
            {
                return;
            }

            if (!TryTranslateValue(isPatternExpression.Expression, semanticModel, cancellationToken, out var matchedValue, getSymbolVersion) ||
                matchedValue == null)
            {
                return;
            }

            var valueType = semanticModel.GetTypeInfo(isPatternExpression.Expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(isPatternExpression.Expression, cancellationToken).Type;
            AddPatternBindingFacts(
                matchedValue,
                valueType,
                isPatternExpression.Pattern,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion);
        }

        private static bool TryAddNullablePatternBindingFacts(
            IsPatternExpressionSyntax isPatternExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (!TryTranslateNullableValueParts(
                    isPatternExpression.Expression,
                    semanticModel,
                    cancellationToken,
                    out _,
                    out var nullableValue,
                    getSymbolVersion,
                    inlineDepth: 0) ||
                nullableValue == null)
            {
                return false;
            }

            AddNullablePatternBindingFacts(
                nullableValue,
                isPatternExpression.Pattern,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion);
            return true;
        }

        private static void AddNullablePatternBindingFacts(
            SmtFormula nullableValue,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                AddNullablePatternBindingFacts(
                    nullableValue,
                    parenthesizedPattern.Pattern,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
                return;
            }

            if (pattern is DeclarationPatternSyntax declarationPattern)
            {
                AddDesignationBindingFact(nullableValue, declarationPattern.Designation, semanticModel, formulas, getSymbolVersion, out _);
                return;
            }

            if (pattern is BinaryPatternSyntax binaryPattern &&
                binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword))
            {
                AddNullablePatternBindingFacts(
                    nullableValue,
                    binaryPattern.Left,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
                AddNullablePatternBindingFacts(
                    nullableValue,
                    binaryPattern.Right,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
            }
        }

        private static void AddPatternBindingFacts(
            SmtFormula matchedValue,
            ITypeSymbol? matchedValueType,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                AddPatternBindingFacts(
                    matchedValue,
                    matchedValueType,
                    parenthesizedPattern.Pattern,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
                return;
            }

            switch (pattern)
            {
                case VarPatternSyntax varPattern:
                    AddDesignationBindingFact(matchedValue, varPattern.Designation, semanticModel, formulas, getSymbolVersion, out _);
                    return;
                case DeclarationPatternSyntax declarationPattern:
                    AddDesignationBindingFact(matchedValue, declarationPattern.Designation, semanticModel, formulas, getSymbolVersion, out _);
                    return;
                case RecursivePatternSyntax recursivePattern:
                    AddDesignationBindingFact(
                        matchedValue,
                        recursivePattern.Designation,
                        semanticModel,
                        formulas,
                        getSymbolVersion,
                        out var designationValue);
                    AddRecursivePropertyPatternBindingFacts(
                        matchedValue,
                        recursivePattern,
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion);
                    if (designationValue != null &&
                        !Equals(designationValue, matchedValue))
                    {
                        AddSubstitutedPatternFactsForDesignationReceiver(
                            matchedValue,
                            designationValue,
                            matchedValueType,
                            recursivePattern,
                            semanticModel,
                            cancellationToken,
                            formulas,
                            getSymbolVersion);
                        AddRecursivePropertyPatternBindingFacts(
                            designationValue,
                            recursivePattern,
                            semanticModel,
                            cancellationToken,
                            formulas,
                            getSymbolVersion);
                    }

                    return;
                case BinaryPatternSyntax binaryPattern when binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword):
                    AddPatternBindingFacts(
                        matchedValue,
                        matchedValueType,
                        binaryPattern.Left,
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion);
                    AddPatternBindingFacts(
                        matchedValue,
                        matchedValueType,
                        binaryPattern.Right,
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion);
                    return;
                case ListPatternSyntax listPattern:
                    AddListPatternBindingFacts(
                        matchedValue,
                        matchedValueType,
                        listPattern,
                        semanticModel,
                        cancellationToken,
                        formulas,
                        getSymbolVersion);
                    return;
            }
        }

        private static void AddSubstitutedPatternFactsForDesignationReceiver(
            SmtFormula matchedValue,
            SmtFormula designationValue,
            ITypeSymbol? matchedValueType,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (matchedValue is not SmtVariable matchedVariable ||
                !TryTranslatePattern(
                    matchedValue,
                    pattern,
                    semanticModel,
                    cancellationToken,
                    out var patternFormula,
                    getSymbolVersion,
                    matchedValueType,
                    inlineDepth: 0) ||
                patternFormula == null)
            {
                return;
            }

            var substitutions = new[]
            {
                new SmtVariableSubstitution(
                    matchedVariable.Name,
                    matchedVariable.Name + ".",
                    matchedVariable + ".",
                    designationValue)
            };
            formulas.Add(SubstituteVariables(patternFormula, substitutions));
        }

        private static void AddListPatternBindingFacts(
            SmtFormula matchedValue,
            ITypeSymbol? matchedValueType,
            ListPatternSyntax listPattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (!TryGetBuiltInListPatternElementType(matchedValueType, out var elementType) ||
                !TryGetValueKind(elementType, out var elementKind))
            {
                return;
            }

            for (var patternIndex = 0; patternIndex < listPattern.Patterns.Count; patternIndex++)
            {
                var subpattern = listPattern.Patterns[patternIndex];
                if (subpattern is SlicePatternSyntax)
                {
                    continue;
                }

                if (!TryGetListPatternElementPosition(listPattern, patternIndex, out var elementIndex, out var fromEnd))
                {
                    continue;
                }

                var elementValue = CreateListPatternElementFormula(matchedValue, elementIndex, fromEnd, elementKind);
                AddPatternBindingFacts(
                    elementValue,
                    elementType,
                    subpattern,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
            }
        }

        private static void AddRecursivePropertyPatternBindingFacts(
            SmtFormula matchedValue,
            RecursivePatternSyntax recursivePattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var subpatterns = recursivePattern.PropertyPatternClause?.Subpatterns;
            if (subpatterns == null)
            {
                return;
            }

            foreach (var subpattern in subpatterns.Value)
            {
                if (subpattern.NameColon?.Name == null)
                {
                    continue;
                }

                var memberSymbol = semanticModel.GetSymbolInfo(subpattern.NameColon.Name, cancellationToken).Symbol;
                if (!TryGetMemberType(memberSymbol, out var memberType) ||
                    !TryCreateMemberFormula(matchedValue, memberSymbol!.Name, memberType, out var memberValue) ||
                    memberValue == null)
                {
                    continue;
                }

                AddPatternBindingFacts(
                    memberValue,
                    memberType,
                    subpattern.Pattern,
                    semanticModel,
                    cancellationToken,
                    formulas,
                    getSymbolVersion);
            }
        }

        private static void AddDesignationBindingFact(
            SmtFormula matchedValue,
            VariableDesignationSyntax? designation,
            SemanticModel semanticModel,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion,
            out SmtFormula? localValue)
        {
            localValue = null;
            if (!TryCreateDesignationFormula(designation, semanticModel, getSymbolVersion, out var designationValue) ||
                designationValue == null ||
                !AreComparable(designationValue, matchedValue))
            {
                return;
            }

            localValue = designationValue;
            formulas.Add(new SmtBinaryFormula(SmtBinaryOperator.Equal, localValue, matchedValue));

            if (TryCreateDesignationStringFormula(designation, semanticModel, getSymbolVersion, out var designationString) &&
                TryCreateStringContentFormula(matchedValue, out var matchedString))
            {
                formulas.Add(new SmtBinaryFormula(SmtBinaryOperator.Equal, designationString, matchedString));
            }
        }

        private static bool TryCreateDesignationStringFormula(
            VariableDesignationSyntax? designation,
            SemanticModel semanticModel,
            Func<ISymbol, int>? getSymbolVersion,
            out SmtFormula formula)
        {
            formula = null!;
            if (designation is not SingleVariableDesignationSyntax singleVariableDesignation ||
                singleVariableDesignation.Identifier.ValueText == "_" ||
                semanticModel.GetDeclaredSymbol(singleVariableDesignation) is not ILocalSymbol localSymbol ||
                localSymbol.Type.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            formula = new SmtVariable(GetVariableName(localSymbol, getSymbolVersion) + ".String", SmtValueKind.String);
            return true;
        }

        private static bool TryCreateStringContentFormula(SmtFormula referenceFormula, out SmtFormula formula)
        {
            formula = null!;
            if (referenceFormula.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            var receiverName = referenceFormula is SmtVariable variable
                ? variable.Name
                : referenceFormula.ToString();
            if (string.IsNullOrEmpty(receiverName))
            {
                return false;
            }

            formula = new SmtVariable(receiverName + ".String", SmtValueKind.String);
            return true;
        }

        private static bool TryCreateDesignationFormula(
            VariableDesignationSyntax? designation,
            SemanticModel semanticModel,
            Func<ISymbol, int>? getSymbolVersion,
            out SmtFormula formula)
        {
            formula = null!;
            if (designation is not SingleVariableDesignationSyntax singleVariableDesignation ||
                singleVariableDesignation.Identifier.ValueText == "_" ||
                semanticModel.GetDeclaredSymbol(singleVariableDesignation) is not ILocalSymbol localSymbol)
            {
                return false;
            }

            return TryCreateSymbolFormula(localSymbol, getSymbolVersion, out formula);
        }

        private static bool TryTranslatePatternExpression(
            IsPatternExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (TryTranslateNullablePatternExpression(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (!TryTranslateValue(expression.Expression, semanticModel, cancellationToken, out var value, getSymbolVersion, inlineDepth) ||
                value == null)
            {
                return false;
            }

            var valueType = semanticModel.GetTypeInfo(expression.Expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(expression.Expression, cancellationToken).Type;
            return TryTranslatePattern(value, expression.Pattern, semanticModel, cancellationToken, out formula, getSymbolVersion, valueType, inlineDepth);
        }

        private static bool TryTranslateNullablePatternExpression(
            IsPatternExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!TryTranslateNullableValueParts(
                    expression.Expression,
                    semanticModel,
                    cancellationToken,
                    out var hasValueFormula,
                    out var valueFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                valueFormula == null ||
                !TryGetNullableUnderlyingType(
                    semanticModel.GetTypeInfo(expression.Expression, cancellationToken).Type,
                    out var underlyingType))
            {
                return false;
            }

            return TryTranslateNullablePattern(
                hasValueFormula,
                valueFormula,
                underlyingType,
                expression.Pattern,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        private static bool TryTranslateNullablePattern(
            SmtFormula hasValueFormula,
            SmtFormula valueFormula,
            ITypeSymbol underlyingType,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                return TryTranslateNullablePattern(
                    hasValueFormula,
                    valueFormula,
                    underlyingType,
                    parenthesizedPattern.Pattern,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth);
            }

            if (pattern is DiscardPatternSyntax or VarPatternSyntax)
            {
                formula = new SmtBooleanConstant(true);
                return true;
            }

            if (pattern is DeclarationPatternSyntax declarationPattern &&
                PatternTypeMatchesUnderlyingType(declarationPattern.Type, underlyingType, semanticModel, cancellationToken))
            {
                formula = hasValueFormula;
                return true;
            }

            if (pattern is TypePatternSyntax typePattern &&
                PatternTypeMatchesUnderlyingType(typePattern.Type, underlyingType, semanticModel, cancellationToken))
            {
                formula = hasValueFormula;
                return true;
            }

            if (pattern is ConstantPatternSyntax nullConstantPattern &&
                IsNullLikeNullableComparisonOperand(nullConstantPattern.Expression, semanticModel, cancellationToken))
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, hasValueFormula);
                return true;
            }

            if (pattern is RecursivePatternSyntax recursivePattern)
            {
                if (IsEmptyRecursivePattern(recursivePattern))
                {
                    formula = hasValueFormula;
                    return true;
                }

                if (TryTranslateRecursivePattern(
                        valueFormula,
                        recursivePattern,
                        semanticModel,
                        cancellationToken,
                        out var recursiveFormula,
                        getSymbolVersion,
                        inlineDepth) &&
                    recursiveFormula != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, hasValueFormula, recursiveFormula);
                    return true;
                }
            }

            if (pattern is ConstantPatternSyntax constantPattern &&
                TryTranslateValue(constantPattern.Expression, semanticModel, cancellationToken, out var constantValue, getSymbolVersion, inlineDepth) &&
                constantValue != null &&
                AreComparable(valueFormula, constantValue))
            {
                formula = new SmtBinaryFormula(
                    SmtBinaryOperator.And,
                    hasValueFormula,
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, valueFormula, constantValue));
                return true;
            }

            if (pattern is RelationalPatternSyntax relationalPattern &&
                valueFormula.Kind == SmtValueKind.Int &&
                TryTranslateValue(relationalPattern.Expression, semanticModel, cancellationToken, out var relationalValue, getSymbolVersion, inlineDepth) &&
                relationalValue is { Kind: SmtValueKind.Int } &&
                TryTranslateRelationalPatternComparison(relationalPattern.OperatorToken.Kind(), valueFormula, relationalValue, out var comparison))
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.And, hasValueFormula, comparison);
                return true;
            }

            if (pattern is UnaryPatternSyntax unaryPattern &&
                unaryPattern.OperatorToken.IsKind(SyntaxKind.NotKeyword) &&
                TryTranslateNullablePattern(
                    hasValueFormula,
                    valueFormula,
                    underlyingType,
                    unaryPattern.Pattern,
                    semanticModel,
                    cancellationToken,
                    out var negatedPattern,
                    getSymbolVersion,
                    inlineDepth) &&
                negatedPattern != null)
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, negatedPattern);
                return true;
            }

            if (pattern is BinaryPatternSyntax binaryPattern &&
                TryTranslateNullablePattern(
                    hasValueFormula,
                    valueFormula,
                    underlyingType,
                    binaryPattern.Left,
                    semanticModel,
                    cancellationToken,
                    out var leftPattern,
                    getSymbolVersion,
                    inlineDepth) &&
                leftPattern != null &&
                TryTranslateNullablePattern(
                    hasValueFormula,
                    valueFormula,
                    underlyingType,
                    binaryPattern.Right,
                    semanticModel,
                    cancellationToken,
                    out var rightPattern,
                    getSymbolVersion,
                    inlineDepth) &&
                rightPattern != null)
            {
                if (binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword))
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftPattern, rightPattern);
                    return true;
                }

                if (binaryPattern.OperatorToken.IsKind(SyntaxKind.OrKeyword))
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftPattern, rightPattern);
                    return true;
                }
            }

            return false;
        }

        private static bool IsEmptyRecursivePattern(RecursivePatternSyntax recursivePattern)
        {
            return recursivePattern.PropertyPatternClause is not { Subpatterns.Count: > 0 } &&
                recursivePattern.PositionalPatternClause is not { Subpatterns.Count: > 0 };
        }

        private static bool TryTranslateRelationalPatternComparison(
            SyntaxKind operatorKind,
            SmtFormula left,
            SmtFormula right,
            out SmtFormula formula)
        {
            formula = null!;
            switch (operatorKind)
            {
                case SyntaxKind.GreaterThanToken:
                    formula = new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, left, right);
                    return true;
                case SyntaxKind.GreaterThanEqualsToken:
                    formula = new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, left, right);
                    return true;
                case SyntaxKind.LessThanToken:
                    formula = new SmtBinaryFormula(SmtBinaryOperator.LessThan, left, right);
                    return true;
                case SyntaxKind.LessThanEqualsToken:
                    formula = new SmtBinaryFormula(SmtBinaryOperator.LessThanOrEqual, left, right);
                    return true;
                default:
                    return false;
            }
        }

        private static bool PatternTypeMatchesUnderlyingType(
            TypeSyntax patternTypeSyntax,
            ITypeSymbol underlyingType,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var patternType = semanticModel.GetTypeInfo(patternTypeSyntax, cancellationToken).Type;
            return patternType != null &&
                SymbolEqualityComparer.Default.Equals(patternType, underlyingType);
        }

        public static bool TryTranslatePattern(
            SmtFormula value,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            ITypeSymbol? valueType = null,
            int inlineDepth = 0)
        {
            formula = null;

            if (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                return TryTranslatePattern(value, parenthesizedPattern.Pattern, semanticModel, cancellationToken, out formula, getSymbolVersion, valueType, inlineDepth);
            }

            if (pattern is DiscardPatternSyntax or VarPatternSyntax)
            {
                formula = new SmtBooleanConstant(true);
                return true;
            }

            if (pattern is ConstantPatternSyntax constantPattern &&
                TryTranslateValue(constantPattern.Expression, semanticModel, cancellationToken, out var constantValue, getSymbolVersion, inlineDepth) &&
                constantValue != null &&
                AreComparable(value, constantValue))
            {
                formula = new SmtBinaryFormula(SmtBinaryOperator.Equal, value, constantValue);
                return true;
            }

            if (pattern is UnaryPatternSyntax unaryPattern &&
                unaryPattern.OperatorToken.IsKind(SyntaxKind.NotKeyword) &&
                TryTranslatePattern(value, unaryPattern.Pattern, semanticModel, cancellationToken, out var negatedPattern, getSymbolVersion, valueType, inlineDepth) &&
                negatedPattern != null)
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, negatedPattern);
                return true;
            }

            if (pattern is BinaryPatternSyntax binaryPattern &&
                TryTranslatePattern(value, binaryPattern.Left, semanticModel, cancellationToken, out var leftPattern, getSymbolVersion, valueType, inlineDepth) &&
                TryTranslatePattern(value, binaryPattern.Right, semanticModel, cancellationToken, out var rightPattern, getSymbolVersion, valueType, inlineDepth) &&
                leftPattern != null &&
                rightPattern != null)
            {
                if (binaryPattern.OperatorToken.IsKind(SyntaxKind.AndKeyword))
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftPattern, rightPattern);
                    return true;
                }

                if (binaryPattern.OperatorToken.IsKind(SyntaxKind.OrKeyword))
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftPattern, rightPattern);
                    return true;
                }
            }

            if (pattern is RelationalPatternSyntax relationalPattern &&
                value.Kind == SmtValueKind.Int &&
                TryTranslateValue(relationalPattern.Expression, semanticModel, cancellationToken, out var relationalValue, getSymbolVersion, inlineDepth) &&
                relationalValue is { Kind: SmtValueKind.Int })
            {
                switch (relationalPattern.OperatorToken.Kind())
                {
                    case SyntaxKind.GreaterThanToken:
                        formula = new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, value, relationalValue);
                        return true;
                    case SyntaxKind.GreaterThanEqualsToken:
                        formula = new SmtBinaryFormula(SmtBinaryOperator.GreaterThanOrEqual, value, relationalValue);
                        return true;
                    case SyntaxKind.LessThanToken:
                        formula = new SmtBinaryFormula(SmtBinaryOperator.LessThan, value, relationalValue);
                        return true;
                    case SyntaxKind.LessThanEqualsToken:
                        formula = new SmtBinaryFormula(SmtBinaryOperator.LessThanOrEqual, value, relationalValue);
                        return true;
                }
            }

            if (pattern is RecursivePatternSyntax recursivePattern)
            {
                return TryTranslateRecursivePattern(value, recursivePattern, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth);
            }

            if (pattern is ListPatternSyntax listPattern)
            {
                return TryTranslateListPattern(
                    value,
                    valueType,
                    listPattern,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth);
            }

            if (pattern is DeclarationPatternSyntax or TypePatternSyntax)
            {
                if (value.Kind != SmtValueKind.Reference)
                {
                    return false;
                }

                formula = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, value, new SmtNullConstant());
                return true;
            }

            return false;
        }

        private static bool TryTranslateRecursivePattern(
            SmtFormula value,
            RecursivePatternSyntax recursivePattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            SmtFormula? current = value.Kind == SmtValueKind.Reference
                ? new SmtBinaryFormula(SmtBinaryOperator.NotEqual, value, new SmtNullConstant())
                : null;

            var subpatterns = recursivePattern.PropertyPatternClause?.Subpatterns;
            if (subpatterns == null || subpatterns.Value.Count == 0)
            {
                formula = current;
                return formula != null;
            }

            foreach (var subpattern in subpatterns.Value)
            {
                if (!TryTranslatePropertySubpattern(value, subpattern, semanticModel, cancellationToken, out var subpatternFormula, getSymbolVersion, inlineDepth) ||
                    subpatternFormula == null)
                {
                    return false;
                }

                current = current == null
                    ? subpatternFormula
                    : new SmtBinaryFormula(SmtBinaryOperator.And, current, subpatternFormula);
            }

            formula = current;
            return formula != null;
        }

        private static bool TryTranslatePropertySubpattern(
            SmtFormula receiver,
            SubpatternSyntax subpattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (subpattern.NameColon?.Name == null)
            {
                return false;
            }

            var memberSymbol = semanticModel.GetSymbolInfo(subpattern.NameColon.Name, cancellationToken).Symbol;
            if (memberSymbol?.Name == "Length" &&
                memberSymbol.ContainingType?.SpecialType == SpecialType.System_String &&
                TryCreateStringLengthFormula(receiver, out var stringLengthFormula))
            {
                var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
                return TryTranslatePattern(stringLengthFormula, subpattern.Pattern, semanticModel, cancellationToken, out formula, getSymbolVersion, intType, inlineDepth);
            }

            if (!TryGetMemberType(memberSymbol, out var memberType) ||
                !TryCreateMemberFormula(receiver, memberSymbol!.Name, memberType, out var memberValue) ||
                memberValue == null)
            {
                return false;
            }

            return TryTranslatePattern(memberValue, subpattern.Pattern, semanticModel, cancellationToken, out formula, getSymbolVersion, memberType, inlineDepth);
        }

        private static bool TryTranslateListPattern(
            SmtFormula value,
            ITypeSymbol? valueType,
            ListPatternSyntax listPattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (value.Kind != SmtValueKind.Reference ||
                !IsSupportedBuiltInListPatternReceiver(valueType))
            {
                return false;
            }

            var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
            SmtFormula? lengthFormula;
            if (valueType?.SpecialType == SpecialType.System_String)
            {
                if (!TryCreateStringLengthFormula(value, out lengthFormula))
                {
                    return false;
                }
            }
            else if (!TryCreateMemberFormula(value, "Length", intType, out lengthFormula) ||
                     lengthFormula == null)
            {
                return false;
            }

            var hasSlice = false;
            var minimumLength = 0;
            foreach (var subpattern in listPattern.Patterns)
            {
                if (subpattern is SlicePatternSyntax slicePattern)
                {
                    if (TryGetNestedListPattern(slicePattern.Pattern, out var nestedListPattern))
                    {
                        minimumLength += GetListPatternMinimumLength(nestedListPattern);
                    }

                    hasSlice = true;
                    continue;
                }

                minimumLength++;
            }

            var nonNullFormula = new SmtBinaryFormula(
                SmtBinaryOperator.NotEqual,
                value,
                new SmtNullConstant());
            var lengthFormulaCondition = hasSlice
                ? new SmtBinaryFormula(
                    SmtBinaryOperator.GreaterThanOrEqual,
                    lengthFormula,
                    new SmtIntegerConstant(minimumLength))
                : new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    lengthFormula,
                    new SmtIntegerConstant(minimumLength));

            formula = new SmtBinaryFormula(SmtBinaryOperator.And, nonNullFormula, lengthFormulaCondition);
            AddListPatternElementConditions(
                value,
                valueType,
                listPattern,
                semanticModel,
                cancellationToken,
                ref formula,
                getSymbolVersion,
                inlineDepth);
            return true;
        }

        private static bool TryCreateStringLengthFormula(SmtFormula receiver, out SmtFormula formula)
        {
            formula = null!;
            if (receiver.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            var receiverName = receiver is SmtVariable variable
                ? variable.Name
                : receiver.ToString();
            if (string.IsNullOrEmpty(receiverName))
            {
                return false;
            }

            formula = new SmtStringLengthTerm(new SmtVariable(receiverName + ".String", SmtValueKind.String));
            return true;
        }

        private static void AddListPatternElementConditions(
            SmtFormula value,
            ITypeSymbol? valueType,
            ListPatternSyntax listPattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ref SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            if (formula == null ||
                !TryGetBuiltInListPatternElementType(valueType, out var elementType) ||
                !TryGetValueKind(elementType, out var elementKind))
            {
                return;
            }

            for (var patternIndex = 0; patternIndex < listPattern.Patterns.Count; patternIndex++)
            {
                var subpattern = listPattern.Patterns[patternIndex];
                if (subpattern is SlicePatternSyntax)
                {
                    continue;
                }

                if (!TryGetListPatternElementPosition(listPattern, patternIndex, out var elementIndex, out var fromEnd))
                {
                    continue;
                }

                var elementValue = CreateListPatternElementFormula(value, elementIndex, fromEnd, elementKind);
                if (TryTranslatePattern(
                        elementValue,
                        subpattern,
                        semanticModel,
                        cancellationToken,
                        out var elementCondition,
                        getSymbolVersion,
                        elementType,
                        inlineDepth) &&
                    elementCondition != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, formula, elementCondition);
                }
            }
        }

        private static bool TryGetListPatternElementPosition(
            ListPatternSyntax listPattern,
            int patternIndex,
            out int elementIndex,
            out bool fromEnd)
        {
            elementIndex = 0;
            fromEnd = false;

            if (listPattern.Patterns[patternIndex] is SlicePatternSyntax)
            {
                return false;
            }

            var sliceIndex = -1;
            for (var index = 0; index < listPattern.Patterns.Count; index++)
            {
                if (listPattern.Patterns[index] is SlicePatternSyntax)
                {
                    sliceIndex = index;
                    break;
                }
            }

            if (sliceIndex < 0 || patternIndex < sliceIndex)
            {
                elementIndex = patternIndex;
                return true;
            }

            elementIndex = listPattern.Patterns.Count - patternIndex;
            fromEnd = true;
            return true;
        }

        private static SmtFormula CreateListPatternElementFormula(
            SmtFormula receiver,
            int elementIndex,
            bool fromEnd,
            SmtValueKind elementKind)
        {
            var indexText = fromEnd
                ? "^" + elementIndex.ToString(CultureInfo.InvariantCulture)
                : elementIndex.ToString(CultureInfo.InvariantCulture);
            return new SmtVariable(receiver + "[" + indexText + "]", elementKind);
        }

        private static int GetListPatternMinimumLength(ListPatternSyntax listPattern)
        {
            var minimumLength = 0;
            foreach (var subpattern in listPattern.Patterns)
            {
                if (subpattern is SlicePatternSyntax slicePattern)
                {
                    if (TryGetNestedListPattern(slicePattern.Pattern, out var nestedListPattern))
                    {
                        minimumLength += GetListPatternMinimumLength(nestedListPattern);
                    }

                    continue;
                }

                minimumLength++;
            }

            return minimumLength;
        }

        private static bool TryGetNestedListPattern(PatternSyntax? pattern, out ListPatternSyntax listPattern)
        {
            while (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                pattern = parenthesizedPattern.Pattern;
            }

            if (pattern is ListPatternSyntax candidate)
            {
                listPattern = candidate;
                return true;
            }

            listPattern = null!;
            return false;
        }

        private static bool IsSupportedBuiltInListPatternReceiver(ITypeSymbol? valueType)
        {
            return valueType is IArrayTypeSymbol { Rank: 1 } ||
                valueType?.SpecialType == SpecialType.System_String;
        }

        private static bool TryGetBuiltInListPatternElementType(ITypeSymbol? valueType, out ITypeSymbol elementType)
        {
            if (valueType is IArrayTypeSymbol { Rank: 1 } arrayType)
            {
                elementType = arrayType.ElementType;
                return true;
            }

            elementType = null!;
            return false;
        }

        private static bool TryTranslateBuiltInElementAccessValue(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (elementAccess.ArgumentList.Arguments.Count != 1)
            {
                return false;
            }

            var receiverType = semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).Type;
            if (receiverType is not IArrayTypeSymbol { Rank: 1 } arrayType ||
                !TryGetValueKind(arrayType.ElementType, out var elementKind) ||
                !TryTranslateValue(
                    elementAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var receiverFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                receiverFormula is not { Kind: SmtValueKind.Reference } ||
                !TryCreateElementAccessIndexText(
                    elementAccess.ArgumentList.Arguments[0].Expression,
                    semanticModel,
                    cancellationToken,
                    out var indexText,
                    getSymbolVersion,
                    inlineDepth))
            {
                return false;
            }

            formula = new SmtVariable(receiverFormula + "[" + indexText + "]", elementKind);
            return true;
        }

        private static bool TryCreateElementAccessIndexText(
            ExpressionSyntax indexExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out string indexText,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            indexExpression = UnwrapElementAccessIndexExpression(indexExpression);
            if (indexExpression is PrefixUnaryExpressionSyntax fromEndIndex &&
                fromEndIndex.OperatorToken.IsKind(SyntaxKind.CaretToken))
            {
                if (!TryTranslateValue(
                        fromEndIndex.Operand,
                        semanticModel,
                        cancellationToken,
                        out var fromEndOffset,
                        getSymbolVersion,
                        inlineDepth) ||
                    fromEndOffset is not { Kind: SmtValueKind.Int })
                {
                    indexText = string.Empty;
                    return false;
                }

                indexText = "^" + CreateElementAccessIndexText(fromEndOffset);
                return true;
            }

            if (!TryTranslateValue(
                    indexExpression,
                    semanticModel,
                    cancellationToken,
                    out var ordinaryIndex,
                    getSymbolVersion,
                    inlineDepth) ||
                ordinaryIndex is not { Kind: SmtValueKind.Int })
            {
                indexText = string.Empty;
                return false;
            }

            indexText = CreateElementAccessIndexText(ordinaryIndex);
            return indexText.Length > 0;
        }

        private static string CreateElementAccessIndexText(SmtFormula indexFormula)
        {
            return indexFormula is SmtIntegerConstant integerConstant
                ? integerConstant.Value.ToString(CultureInfo.InvariantCulture)
                : indexFormula.ToString() ?? string.Empty;
        }

        private static bool TryCreateBuiltInElementAccessLengthFormula(
            ExpressionSyntax receiverExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula lengthFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            receiverExpression = UnwrapExpression(receiverExpression);
            if (receiverExpression is ConditionalExpressionSyntax conditionalExpression &&
                TryTranslate(
                    conditionalExpression.Condition,
                    semanticModel,
                    cancellationToken,
                    out var conditionFormula,
                    getSymbolVersion,
                    inlineDepth) &&
                conditionFormula != null &&
                TryCreateBuiltInElementAccessLengthFormula(
                    conditionalExpression.WhenTrue,
                    semanticModel,
                    cancellationToken,
                    out var whenTrueLength,
                    getSymbolVersion,
                    inlineDepth) &&
                TryCreateBuiltInElementAccessLengthFormula(
                    conditionalExpression.WhenFalse,
                    semanticModel,
                    cancellationToken,
                    out var whenFalseLength,
                    getSymbolVersion,
                    inlineDepth))
            {
                lengthFormula = new SmtConditionalFormula(conditionFormula, whenTrueLength, whenFalseLength, SmtValueKind.Int);
                return true;
            }

            if (receiverExpression is BinaryExpressionSyntax coalesceExpression &&
                coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                TryTranslateValue(
                    coalesceExpression.Left,
                    semanticModel,
                    cancellationToken,
                    out var coalesceLeft,
                    getSymbolVersion,
                    inlineDepth) &&
                coalesceLeft is { Kind: SmtValueKind.Reference } &&
                TryCreateBuiltInElementAccessLengthFormula(
                    coalesceExpression.Left,
                    semanticModel,
                    cancellationToken,
                    out var coalesceLeftLength,
                    getSymbolVersion,
                    inlineDepth) &&
                TryCreateBuiltInElementAccessLengthFormula(
                    coalesceExpression.Right,
                    semanticModel,
                    cancellationToken,
                    out var coalesceRightLength,
                    getSymbolVersion,
                    inlineDepth))
            {
                lengthFormula = new SmtConditionalFormula(
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, coalesceLeft, new SmtNullConstant()),
                    coalesceLeftLength,
                    coalesceRightLength,
                    SmtValueKind.Int);
                return true;
            }

            var receiverTypeInfo = semanticModel.GetTypeInfo(receiverExpression, cancellationToken);
            if ((receiverTypeInfo.Type is IArrayTypeSymbol { Rank: 1 } ||
                 receiverTypeInfo.ConvertedType is IArrayTypeSymbol { Rank: 1 }) &&
                TryCreateArrayLengthFormula(receiverExpression, semanticModel, cancellationToken, out lengthFormula, getSymbolVersion, inlineDepth))
            {
                return true;
            }

            if (TryGetKnownStringLength(receiverExpression, semanticModel, cancellationToken, out var knownStringLength))
            {
                lengthFormula = new SmtIntegerConstant(knownStringLength);
                return true;
            }

            if (IsStringExpression(receiverExpression, semanticModel, cancellationToken) &&
                TryTranslateStringValue(receiverExpression, semanticModel, cancellationToken, out var stringValue, getSymbolVersion, inlineDepth) &&
                stringValue != null)
            {
                lengthFormula = new SmtStringLengthTerm(stringValue);
                return true;
            }

            if (!TryTranslateValue(
                    receiverExpression,
                    semanticModel,
                    cancellationToken,
                    out var receiverFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                receiverFormula is not { Kind: SmtValueKind.Reference })
            {
                lengthFormula = null!;
                return false;
            }

            var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
            if (!TryCreateMemberFormula(receiverFormula, "Length", intType, out var candidate) ||
                candidate is not { Kind: SmtValueKind.Int })
            {
                lengthFormula = null!;
                return false;
            }

            lengthFormula = candidate;
            return true;
        }

        private static bool TryCreateArrayLengthFormula(
            ExpressionSyntax receiverExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula lengthFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            if (receiverExpression is ArrayCreationExpressionSyntax arrayCreation)
            {
                if (arrayCreation.Type.RankSpecifiers.Count == 1 &&
                    arrayCreation.Type.RankSpecifiers[0].Sizes.Count == 1 &&
                    !arrayCreation.Type.RankSpecifiers[0].Sizes[0].IsKind(SyntaxKind.OmittedArraySizeExpression) &&
                    TryTranslateValue(
                        arrayCreation.Type.RankSpecifiers[0].Sizes[0],
                        semanticModel,
                        cancellationToken,
                        out var sizeFormula,
                        getSymbolVersion,
                        inlineDepth) &&
                    sizeFormula is { Kind: SmtValueKind.Int })
                {
                    lengthFormula = sizeFormula;
                    return true;
                }

                if (arrayCreation.Initializer != null)
                {
                    lengthFormula = new SmtIntegerConstant(arrayCreation.Initializer.Expressions.Count);
                    return true;
                }
            }

            if (receiverExpression is ImplicitArrayCreationExpressionSyntax implicitArrayCreation)
            {
                lengthFormula = new SmtIntegerConstant(implicitArrayCreation.Initializer.Expressions.Count);
                return true;
            }

            if (TryCreateCollectionExpressionLengthFormula(receiverExpression, out lengthFormula))
            {
                return true;
            }

            if (IsArrayEmptyInvocation(receiverExpression, semanticModel, cancellationToken))
            {
                lengthFormula = new SmtIntegerConstant(0);
                return true;
            }

            lengthFormula = null!;
            return false;
        }

        private static bool TryCreateCollectionExpressionLengthFormula(
            ExpressionSyntax receiverExpression,
            out SmtFormula lengthFormula)
        {
            if (receiverExpression is not CollectionExpressionSyntax collectionExpression ||
                collectionExpression.Elements.Any(static element => element is not ExpressionElementSyntax))
            {
                lengthFormula = null!;
                return false;
            }

            lengthFormula = new SmtIntegerConstant(collectionExpression.Elements.Count);
            return true;
        }

        private static bool IsArrayEmptyInvocation(
            ExpressionSyntax receiverExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return receiverExpression is InvocationExpressionSyntax invocation &&
                semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol
                {
                    Name: "Empty",
                    IsStatic: true,
                    ContainingType.SpecialType: SpecialType.System_Array
                };
        }

        private static bool TryCreateEffectiveBuiltInIndexFormula(
            ExpressionSyntax indexExpression,
            SmtFormula lengthFormula,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula indexFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            indexExpression = UnwrapElementAccessIndexExpression(indexExpression);
            if (indexExpression is PrefixUnaryExpressionSyntax fromEndIndex &&
                fromEndIndex.OperatorToken.IsKind(SyntaxKind.CaretToken))
            {
                if (!TryTranslateValue(
                        fromEndIndex.Operand,
                        semanticModel,
                        cancellationToken,
                        out var fromEndOffset,
                        getSymbolVersion,
                        inlineDepth) ||
                    fromEndOffset is not { Kind: SmtValueKind.Int })
                {
                    indexFormula = null!;
                    return false;
                }

                indexFormula = new SmtIntegerBinaryTerm(
                    SmtIntegerBinaryOperator.Subtract,
                    lengthFormula,
                    fromEndOffset);
                return true;
            }

            if (!TryTranslateValue(
                    indexExpression,
                    semanticModel,
                    cancellationToken,
                    out var ordinaryIndex,
                    getSymbolVersion,
                    inlineDepth) ||
                ordinaryIndex is not { Kind: SmtValueKind.Int })
            {
                indexFormula = null!;
                return false;
            }

            indexFormula = ordinaryIndex;
            return true;
        }

        private static ExpressionSyntax UnwrapElementAccessIndexExpression(ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
            }

            return expression;
        }

        private static bool IsBuiltInNonNegativeLengthAccess(
            MemberAccessExpressionSyntax memberAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (memberAccess.Name.Identifier.ValueText != "Length")
            {
                return false;
            }

            var memberSymbol = semanticModel.GetSymbolInfo(memberAccess.Name, cancellationToken).Symbol;
            if (memberSymbol is not IPropertySymbol and not IFieldSymbol)
            {
                return false;
            }

            var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
            return receiverType is IArrayTypeSymbol ||
                receiverType?.SpecialType == SpecialType.System_String;
        }

        private static bool TryTranslateComparison(
            SyntaxKind kind,
            SmtFormula left,
            SmtFormula right,
            out SmtFormula? formula)
        {
            formula = null;
            switch (kind)
            {
                case SyntaxKind.EqualsExpression:
                    if (AreComparable(left, right))
                    {
                        formula = new SmtBinaryFormula(SmtBinaryOperator.Equal, left, right);
                        return true;
                    }

                    return false;
                case SyntaxKind.NotEqualsExpression:
                    if (AreComparable(left, right))
                    {
                        formula = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, left, right);
                        return true;
                    }

                    return false;
                case SyntaxKind.LessThanExpression:
                    return TryCreateIntegralComparison(SmtBinaryOperator.LessThan, left, right, out formula);
                case SyntaxKind.LessThanOrEqualExpression:
                    return TryCreateIntegralComparison(SmtBinaryOperator.LessThanOrEqual, left, right, out formula);
                case SyntaxKind.GreaterThanExpression:
                    return TryCreateIntegralComparison(SmtBinaryOperator.GreaterThan, left, right, out formula);
                case SyntaxKind.GreaterThanOrEqualExpression:
                    return TryCreateIntegralComparison(SmtBinaryOperator.GreaterThanOrEqual, left, right, out formula);
                default:
                    return false;
            }
        }

        private static bool TryCreateIntegralComparison(
            SmtBinaryOperator comparison,
            SmtFormula left,
            SmtFormula right,
            out SmtFormula? formula)
        {
            formula = null;
            if (left.Kind != SmtValueKind.Int || right.Kind != SmtValueKind.Int)
            {
                return false;
            }

            formula = new SmtBinaryFormula(comparison, left, right);
            return true;
        }

        private static bool AreComparable(SmtFormula left, SmtFormula right)
        {
            if (left.Kind == right.Kind)
            {
                return true;
            }

            return (left is SmtNullConstant && right.Kind == SmtValueKind.Reference) ||
                (right is SmtNullConstant && left.Kind == SmtValueKind.Reference);
        }

        public static bool TryTranslateValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth = 0)
        {
            expression = UnwrapExpression(expression);
            formula = null;

            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue)
            {
                if (constantValue.Value is bool booleanValue)
                {
                    formula = new SmtBooleanConstant(booleanValue);
                    return true;
                }

                if (constantValue.Value == null)
                {
                    formula = new SmtNullConstant();
                    return true;
                }

                if (TryGetIntegralConstant(constantValue.Value, out var integralValue))
                {
                    formula = new SmtIntegerConstant(integralValue);
                    return true;
                }
            }

            if (TryTranslateDefaultValue(expression, semanticModel, cancellationToken, out formula))
            {
                return true;
            }

            if (expression is ThisExpressionSyntax)
            {
                formula = new SmtVariable(ImplicitThisVariableName, SmtValueKind.Reference);
                return true;
            }

            if (expression is ElementAccessExpressionSyntax elementAccessExpression &&
                TryTranslateBuiltInElementAccessValue(
                    elementAccessExpression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (expression is ConditionalExpressionSyntax conditionalExpression &&
                TryTranslate(conditionalExpression.Condition, semanticModel, cancellationToken, out var conditionFormula, getSymbolVersion, inlineDepth) &&
                conditionFormula != null &&
                TryTranslateValue(conditionalExpression.WhenTrue, semanticModel, cancellationToken, out var whenTrueFormula, getSymbolVersion, inlineDepth) &&
                whenTrueFormula != null &&
                TryTranslateValue(conditionalExpression.WhenFalse, semanticModel, cancellationToken, out var whenFalseFormula, getSymbolVersion, inlineDepth) &&
                whenFalseFormula != null &&
                whenTrueFormula.Kind == whenFalseFormula.Kind)
            {
                formula = new SmtConditionalFormula(conditionFormula, whenTrueFormula, whenFalseFormula, whenTrueFormula.Kind);
                return true;
            }

            if (expression is SwitchExpressionSyntax switchExpression &&
                TryTranslateSwitchExpressionValue(switchExpression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth))
            {
                return true;
            }

            if (expression is BinaryExpressionSyntax nullableCoalesceExpression &&
                nullableCoalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                TryTranslateNullableCoalesceValue(
                    nullableCoalesceExpression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    inlineDepth))
            {
                return true;
            }

            if (expression is BinaryExpressionSyntax coalesceExpression &&
                coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                TryTranslateValue(coalesceExpression.Left, semanticModel, cancellationToken, out var coalesceLeft, getSymbolVersion, inlineDepth) &&
                coalesceLeft is { Kind: SmtValueKind.Reference } &&
                TryTranslateValue(coalesceExpression.Right, semanticModel, cancellationToken, out var coalesceRight, getSymbolVersion, inlineDepth) &&
                coalesceRight is { Kind: SmtValueKind.Reference })
            {
                formula = new SmtConditionalFormula(
                    new SmtBinaryFormula(SmtBinaryOperator.NotEqual, coalesceLeft, new SmtNullConstant()),
                    coalesceLeft,
                    coalesceRight,
                    SmtValueKind.Reference);
                return true;
            }

            if (TryTranslateBooleanTerm(expression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth))
            {
                return true;
            }

            if (TryTranslateIntegralTerm(expression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth))
            {
                return true;
            }

            var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
            if (symbol is not ILocalSymbol && symbol is not IParameterSymbol)
            {
                return TryTranslateMemberValue(expression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth);
            }

            var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            if (type == null)
            {
                return false;
            }

            if (type.SpecialType == SpecialType.System_Boolean)
            {
                formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), SmtValueKind.Bool);
                return true;
            }

            if (IsIntegralOrEnumType(type))
            {
                formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), SmtValueKind.Int);
                return true;
            }

            if (type.IsReferenceType)
            {
                formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), SmtValueKind.Reference);
                return true;
            }

            return false;
        }

        private static bool TryTranslateDefaultValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula)
        {
            formula = null;
            if (!expression.IsKind(SyntaxKind.DefaultLiteralExpression) &&
                expression is not DefaultExpressionSyntax)
            {
                return false;
            }

            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            var type = typeInfo.ConvertedType ?? typeInfo.Type;
            if (type == null)
            {
                return false;
            }

            if (type.SpecialType == SpecialType.System_Boolean)
            {
                formula = new SmtBooleanConstant(false);
                return true;
            }

            if (IsIntegralOrEnumType(type))
            {
                formula = new SmtIntegerConstant(0);
                return true;
            }

            if (type.IsReferenceType)
            {
                formula = new SmtNullConstant();
                return true;
            }

            return false;
        }

        private static bool TryTranslateSwitchExpressionValue(
            SwitchExpressionSyntax switchExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (switchExpression.Arms.Count < 2 ||
                !HasUnguardedDiscardFallback(switchExpression.Arms[switchExpression.Arms.Count - 1]))
            {
                return false;
            }

            var armConditions = new List<SmtFormula>();
            var armValues = new List<SmtFormula>();
            foreach (var arm in switchExpression.Arms)
            {
                if (!TryTranslateValue(arm.Expression, semanticModel, cancellationToken, out var armValue, getSymbolVersion, inlineDepth) ||
                    armValue == null ||
                    !SwitchPathConditionBuilder.TryCreateSwitchExpressionArmCondition(
                        switchExpression.GoverningExpression,
                        arm,
                        semanticModel,
                        cancellationToken,
                        out var armCondition,
                        getSymbolVersion))
                {
                    formula = null;
                    return false;
                }

                if (armValues.Count > 0 &&
                    armValues[0].Kind != armValue.Kind)
                {
                    formula = null;
                    return false;
                }

                armConditions.Add(armCondition);
                armValues.Add(armValue);
            }

            var result = armValues[armValues.Count - 1];
            for (var index = armValues.Count - 2; index >= 0; index--)
            {
                result = new SmtConditionalFormula(
                    armConditions[index],
                    armValues[index],
                    result,
                    result.Kind);
            }

            formula = result;
            return true;
        }

        private static bool HasUnguardedDiscardFallback(SwitchExpressionArmSyntax arm)
        {
            return arm.WhenClause == null &&
                arm.Pattern is DiscardPatternSyntax;
        }

        private static bool TryTranslateBooleanTerm(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!HasSupportedBooleanType(expression, semanticModel, cancellationToken))
            {
                return false;
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression) &&
                TryTranslate(prefixUnary.Operand, semanticModel, cancellationToken, out var operand, getSymbolVersion, inlineDepth) &&
                operand != null)
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, operand);
                return true;
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftAnd, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightAnd, getSymbolVersion, inlineDepth) &&
                    leftAnd != null &&
                    rightAnd != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftAnd, rightAnd);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.BitwiseAndExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftBitwiseAnd, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightBitwiseAnd, getSymbolVersion, inlineDepth) &&
                    leftBitwiseAnd != null &&
                    rightBitwiseAnd != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.And, leftBitwiseAnd, rightBitwiseAnd);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftOr, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightOr, getSymbolVersion, inlineDepth) &&
                    leftOr != null &&
                    rightOr != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftOr, rightOr);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.BitwiseOrExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftBitwiseOr, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightBitwiseOr, getSymbolVersion, inlineDepth) &&
                    leftBitwiseOr != null &&
                    rightBitwiseOr != null)
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.Or, leftBitwiseOr, rightBitwiseOr);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.ExclusiveOrExpression) &&
                    TryTranslate(binaryExpression.Left, semanticModel, cancellationToken, out var leftExclusiveOr, getSymbolVersion, inlineDepth) &&
                    TryTranslate(binaryExpression.Right, semanticModel, cancellationToken, out var rightExclusiveOr, getSymbolVersion, inlineDepth) &&
                    leftExclusiveOr is { Kind: SmtValueKind.Bool } &&
                    rightExclusiveOr is { Kind: SmtValueKind.Bool })
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, leftExclusiveOr, rightExclusiveOr);
                    return true;
                }

                if (TryTranslateUnsignedCastBoundsComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var unsignedBoundsFormula,
                        getSymbolVersion,
                        inlineDepth) &&
                    unsignedBoundsFormula != null)
                {
                    formula = unsignedBoundsFormula;
                    return true;
                }

                if (TryTranslateStringComparison(
                        binaryExpression,
                        semanticModel,
                        cancellationToken,
                        out var stringComparison,
                        getSymbolVersion,
                        inlineDepth) &&
                    stringComparison != null)
                {
                    formula = stringComparison;
                    return true;
                }

                if (TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var leftValue, getSymbolVersion, inlineDepth) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var rightValue, getSymbolVersion, inlineDepth) &&
                    leftValue != null &&
                    rightValue != null &&
                    TryTranslateComparison(binaryExpression.Kind(), leftValue, rightValue, out var comparison))
                {
                    formula = comparison;
                    return true;
                }
            }

            if (expression is IsPatternExpressionSyntax isPatternExpression)
            {
                return TryTranslatePatternExpression(isPatternExpression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth);
            }

            if (expression is InvocationExpressionSyntax invocationExpression)
            {
                if (TryTranslateKnownStringBooleanInvocation(invocationExpression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth))
                {
                    return formula != null;
                }

                return TryTranslateSourceBooleanInvocation(invocationExpression, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth);
            }

            return false;
        }

        private static bool TryTranslateIntegralTerm(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!HasSupportedIntegralType(expression, semanticModel, cancellationToken))
            {
                return false;
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnary)
            {
                if (prefixUnary.IsKind(SyntaxKind.UnaryPlusExpression))
                {
                    return TryTranslateValue(prefixUnary.Operand, semanticModel, cancellationToken, out formula, getSymbolVersion, inlineDepth) &&
                        formula is { Kind: SmtValueKind.Int };
                }

                if (prefixUnary.IsKind(SyntaxKind.UnaryMinusExpression) &&
                    TryTranslateValue(prefixUnary.Operand, semanticModel, cancellationToken, out var operand, getSymbolVersion, inlineDepth) &&
                    operand is { Kind: SmtValueKind.Int })
                {
                    formula = new SmtIntegerUnaryTerm(SmtIntegerUnaryOperator.Negate, operand);
                    return true;
                }
            }

            if (expression is CastExpressionSyntax castExpression &&
                IsRepresentationPreservingIntegralCast(castExpression, semanticModel, cancellationToken) &&
                TryTranslateValue(castExpression.Expression, semanticModel, cancellationToken, out var castOperand, getSymbolVersion, inlineDepth) &&
                castOperand is { Kind: SmtValueKind.Int })
            {
                formula = castOperand;
                return true;
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.AddExpression) &&
                    TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var addLeft, getSymbolVersion, inlineDepth) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var addRight, getSymbolVersion, inlineDepth) &&
                    addLeft is { Kind: SmtValueKind.Int } &&
                    addRight is { Kind: SmtValueKind.Int })
                {
                    formula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, addLeft, addRight);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.SubtractExpression) &&
                    TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var subtractLeft, getSymbolVersion, inlineDepth) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var subtractRight, getSymbolVersion, inlineDepth) &&
                    subtractLeft is { Kind: SmtValueKind.Int } &&
                    subtractRight is { Kind: SmtValueKind.Int })
                {
                    formula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, subtractLeft, subtractRight);
                    return true;
                }

                if (binaryExpression.IsKind(SyntaxKind.MultiplyExpression) &&
                    TryTranslateValue(binaryExpression.Left, semanticModel, cancellationToken, out var multiplyLeft, getSymbolVersion, inlineDepth) &&
                    TryTranslateValue(binaryExpression.Right, semanticModel, cancellationToken, out var multiplyRight, getSymbolVersion, inlineDepth) &&
                    multiplyLeft is { Kind: SmtValueKind.Int } &&
                    multiplyRight is { Kind: SmtValueKind.Int } &&
                    (multiplyLeft is SmtIntegerConstant || multiplyRight is SmtIntegerConstant))
                {
                    formula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Multiply, multiplyLeft, multiplyRight);
                    return true;
                }
            }

            return false;
        }

        private static bool TryTranslateNullableCoalesceValue(
            BinaryExpressionSyntax coalesceExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (!TryTranslateNullableValueParts(
                    coalesceExpression.Left,
                    semanticModel,
                    cancellationToken,
                    out var hasValueFormula,
                    out var nullableValueFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                nullableValueFormula == null ||
                !TryTranslateValue(
                    coalesceExpression.Right,
                    semanticModel,
                    cancellationToken,
                    out var fallbackFormula,
                    getSymbolVersion,
                    inlineDepth) ||
                fallbackFormula == null ||
                nullableValueFormula.Kind != fallbackFormula.Kind)
            {
                return false;
            }

            formula = new SmtConditionalFormula(
                hasValueFormula,
                nullableValueFormula,
                fallbackFormula,
                fallbackFormula.Kind);
            return true;
        }

        private static bool TryTranslateNullableValueParts(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula hasValueFormula,
            out SmtFormula? valueFormula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            expression = UnwrapExpression(expression);
            if (semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is { } symbol &&
                symbol is ILocalSymbol or IParameterSymbol &&
                TryGetNullableUnderlyingType(
                    semanticModel.GetTypeInfo(expression, cancellationToken).Type,
                    out var underlyingType) &&
                TryGetValueKind(underlyingType, out var nullableValueKind))
            {
                var variableName = GetVariableName(symbol.OriginalDefinition, getSymbolVersion);
                hasValueFormula = new SmtVariable(variableName + ".HasValue", SmtValueKind.Bool);
                valueFormula = new SmtVariable(variableName + ".Value", nullableValueKind);
                return true;
            }

            if (expression is ConditionalAccessExpressionSyntax conditionalAccess &&
                TryGetNullableUnderlyingType(
                    semanticModel.GetTypeInfo(conditionalAccess, cancellationToken).Type,
                    out var conditionalAccessUnderlyingType) &&
                TryTranslateValue(
                    conditionalAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var receiverFormula,
                    getSymbolVersion,
                    inlineDepth) &&
                receiverFormula is { Kind: SmtValueKind.Reference } &&
                TryCreateConditionalAccessWhenNotNullValueFormula(
                    conditionalAccess,
                    receiverFormula,
                    conditionalAccessUnderlyingType,
                    semanticModel,
                    cancellationToken,
                    out valueFormula))
            {
                hasValueFormula = new SmtBinaryFormula(
                    SmtBinaryOperator.NotEqual,
                    receiverFormula,
                    new SmtNullConstant());
                return true;
            }

            hasValueFormula = null!;
            valueFormula = null;
            return false;
        }

        private static bool TryCreateConditionalAccessWhenNotNullValueFormula(
            ConditionalAccessExpressionSyntax conditionalAccess,
            SmtFormula receiverFormula,
            ITypeSymbol expectedType,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula)
        {
            formula = null;
            if (conditionalAccess.WhenNotNull is not MemberBindingExpressionSyntax memberBinding ||
                semanticModel.GetSymbolInfo(memberBinding.Name, cancellationToken).Symbol is not { } memberSymbol ||
                !TryGetMemberType(memberSymbol, out var memberType) ||
                !SymbolEqualityComparer.Default.Equals(memberType, expectedType))
            {
                return false;
            }

            if (memberSymbol.Name == "Length" &&
                IsStringExpression(conditionalAccess.Expression, semanticModel, cancellationToken) &&
                TryTranslateStringValue(
                    conditionalAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var stringFormula) &&
                stringFormula != null)
            {
                formula = new SmtStringLengthTerm(stringFormula);
                return true;
            }

            return TryCreateMemberFormula(receiverFormula, memberSymbol.Name, memberType, out formula) &&
                formula != null;
        }

        private static bool IsRepresentationPreservingIntegralCast(
            CastExpressionSyntax castExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var sourceType = semanticModel.GetTypeInfo(castExpression.Expression, cancellationToken).Type;
            var targetType = semanticModel.GetTypeInfo(castExpression, cancellationToken).Type;
            if (sourceType == null ||
                targetType == null ||
                !IsIntegralOrEnumType(sourceType) ||
                !IsIntegralOrEnumType(targetType))
            {
                return false;
            }

            return TryGetIntegralSpecialType(sourceType, out var sourceSpecialType) &&
                TryGetIntegralSpecialType(targetType, out var targetSpecialType) &&
                IsSameOrWideningIntegralConversion(sourceSpecialType, targetSpecialType);
        }

        private static bool IsSameOrWideningIntegralConversion(
            SpecialType sourceType,
            SpecialType targetType)
        {
            if (sourceType == targetType)
            {
                return true;
            }

            return sourceType switch
            {
                SpecialType.System_SByte => targetType is
                    SpecialType.System_Int16 or
                    SpecialType.System_Int32 or
                    SpecialType.System_Int64,
                SpecialType.System_Byte => targetType is
                    SpecialType.System_Int16 or
                    SpecialType.System_UInt16 or
                    SpecialType.System_Int32 or
                    SpecialType.System_UInt32 or
                    SpecialType.System_Int64 or
                    SpecialType.System_UInt64,
                SpecialType.System_Int16 => targetType is
                    SpecialType.System_Int32 or
                    SpecialType.System_Int64,
                SpecialType.System_UInt16 => targetType is
                    SpecialType.System_Int32 or
                    SpecialType.System_UInt32 or
                    SpecialType.System_Int64 or
                    SpecialType.System_UInt64,
                SpecialType.System_Int32 => targetType == SpecialType.System_Int64,
                SpecialType.System_UInt32 => targetType is
                    SpecialType.System_Int64 or
                    SpecialType.System_UInt64,
                _ => false
            };
        }

        private static bool TryTranslateMemberValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth)
        {
            formula = null;
            if (TryTranslateImplicitThisMemberValue(expression, semanticModel, cancellationToken, out formula))
            {
                return true;
            }

            if (expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            var memberSymbol = semanticModel.GetSymbolInfo(memberAccess.Name, cancellationToken).Symbol;
            if (memberSymbol is not IPropertySymbol and not IFieldSymbol)
            {
                return false;
            }

            if (memberSymbol.Name == "Length" &&
                TryGetKnownStringLength(memberAccess.Expression, semanticModel, cancellationToken, out var stringLength))
            {
                formula = new SmtIntegerConstant(stringLength);
                return true;
            }

            if (memberSymbol.Name == "Length" &&
                IsStringExpression(memberAccess.Expression, semanticModel, cancellationToken) &&
                TryTranslateStringValue(memberAccess.Expression, semanticModel, cancellationToken, out var stringValue, getSymbolVersion, inlineDepth) &&
                stringValue != null)
            {
                formula = new SmtStringLengthTerm(stringValue);
                return true;
            }

            if (memberSymbol is IFieldSymbol { HasConstantValue: true } constantField &&
                constantField.ConstantValue != null &&
                TryGetIntegralConstant(constantField.ConstantValue, out var integralConstant))
            {
                formula = new SmtIntegerConstant(integralConstant);
                return true;
            }

            if (TryTranslateTupleElementValue(memberAccess, memberSymbol, semanticModel, cancellationToken, out formula, getSymbolVersion))
            {
                return true;
            }

            if (TryTranslateNullableMemberValue(memberAccess, memberSymbol, semanticModel, cancellationToken, out formula, getSymbolVersion))
            {
                return true;
            }

            if (!TryTranslateValue(memberAccess.Expression, semanticModel, cancellationToken, out var receiver, getSymbolVersion, inlineDepth) ||
                receiver == null)
            {
                return false;
            }

            if (memberSymbol is IPropertySymbol propertySymbol &&
                TryTranslateSourceBooleanProperty(
                    propertySymbol,
                    receiver,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    inlineDepth + 1))
            {
                return true;
            }

            var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            if (type == null)
            {
                return false;
            }

            return TryCreateMemberFormula(receiver, memberSymbol.Name, type, out formula);
        }

        private static bool TryTranslateImplicitThisMemberValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula)
        {
            formula = null;
            if (expression is not IdentifierNameSyntax ||
                semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is not IPropertySymbol and not IFieldSymbol ||
                semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is not { IsStatic: false } memberSymbol ||
                !TryGetMemberType(memberSymbol, out var memberType))
            {
                return false;
            }

            return TryCreateMemberFormula(
                new SmtVariable(ImplicitThisVariableName, SmtValueKind.Reference),
                memberSymbol.Name,
                memberType,
                out formula);
        }

        private static bool TryTranslateSourceBooleanProperty(
            IPropertySymbol propertySymbol,
            SmtFormula receiver,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            int inlineDepth)
        {
            formula = null;
            if (inlineDepth >= MaxSourcePredicateInlineDepth ||
                !CanInlineSourceBooleanProperty(propertySymbol) ||
                !TryGetSourceBooleanPropertyFormula(
                    propertySymbol,
                    semanticModel.Compilation,
                    cancellationToken,
                    inlineDepth,
                    out var propertyFormula) ||
                propertyFormula is not { Kind: SmtValueKind.Bool })
            {
                return false;
            }

            formula = SubstituteVariables(
                propertyFormula,
                new[]
                {
                    CreateImplicitThisSubstitution(receiver)
                });
            return true;
        }

        private static bool CanInlineSourceBooleanProperty(IPropertySymbol propertySymbol)
        {
            return propertySymbol is
            {
                IsStatic: false,
                IsIndexer: false,
                Type.SpecialType: SpecialType.System_Boolean,
                DeclaringSyntaxReferences.Length: > 0
            };
        }

        private static bool TryGetSourceBooleanPropertyFormula(
            IPropertySymbol propertySymbol,
            Compilation compilation,
            CancellationToken cancellationToken,
            int inlineDepth,
            out SmtFormula? formula)
        {
            formula = null;
            var propertySyntax = propertySymbol.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(cancellationToken))
                .OfType<PropertyDeclarationSyntax>()
                .FirstOrDefault();
            if (propertySyntax == null)
            {
                return false;
            }

            var cache = GetSourceBooleanFormulaCache(compilation);
            var cacheKey = CreateSourceBooleanFormulaCacheKey("property", propertySyntax, inlineDepth);
            var entry = cache.GetOrAdd(
                cacheKey,
                _ =>
                {
                    var propertySemanticModel = compilation.GetSemanticModel(propertySyntax.SyntaxTree);
                    if (propertySyntax.ExpressionBody?.Expression != null)
                    {
                        var success = TryTranslate(
                            propertySyntax.ExpressionBody.Expression,
                            propertySemanticModel,
                            cancellationToken,
                            out var cachedFormula,
                            getSymbolVersion: null,
                            inlineDepth);
                        return new SourceBooleanFormulaCacheEntry(success, cachedFormula);
                    }

                    var getter = propertySyntax.AccessorList?.Accessors
                        .FirstOrDefault(static accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
                    if (getter?.ExpressionBody?.Expression != null)
                    {
                        var success = TryTranslate(
                            getter.ExpressionBody.Expression,
                            propertySemanticModel,
                            cancellationToken,
                            out var cachedFormula,
                            getSymbolVersion: null,
                            inlineDepth);
                        return new SourceBooleanFormulaCacheEntry(success, cachedFormula);
                    }

                    if (getter?.Body != null)
                    {
                        var success = TryTranslateReturnedBooleanBlock(
                            getter.Body,
                            propertySemanticModel,
                            cancellationToken,
                            inlineDepth,
                            out var cachedFormula);
                        return new SourceBooleanFormulaCacheEntry(success, cachedFormula);
                    }

                    return new SourceBooleanFormulaCacheEntry(false, null);
                });

            formula = entry.Formula;
            return entry.Success;
        }

        private static bool TryTranslateNullableMemberValue(
            MemberAccessExpressionSyntax memberAccess,
            ISymbol memberSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion)
        {
            formula = null;
            if (memberSymbol.Name is not "HasValue" and not "Value" ||
                semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol is not { } receiverSymbol ||
                receiverSymbol is not ILocalSymbol and not IParameterSymbol ||
                !TryGetNullableUnderlyingType(
                    semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type,
                    out var underlyingType))
            {
                return false;
            }

            var variableName = GetVariableName(receiverSymbol.OriginalDefinition, getSymbolVersion);
            if (memberSymbol.Name == "HasValue")
            {
                formula = new SmtVariable(variableName + ".HasValue", SmtValueKind.Bool);
                return true;
            }

            if (!TryGetValueKind(underlyingType, out var kind))
            {
                return false;
            }

            formula = new SmtVariable(variableName + ".Value", kind);
            return true;
        }

        private static bool TryTranslateTupleElementValue(
            MemberAccessExpressionSyntax memberAccess,
            ISymbol memberSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion)
        {
            formula = null;
            if (memberSymbol is not IFieldSymbol fieldSymbol ||
                !TryGetTupleElementStorageName(fieldSymbol, out var storageName) ||
                semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol is not { } receiverSymbol ||
                receiverSymbol is not ILocalSymbol and not IParameterSymbol ||
                !TryGetValueKind(fieldSymbol.Type, out var kind))
            {
                return false;
            }

            formula = new SmtVariable(GetVariableName(receiverSymbol.OriginalDefinition, getSymbolVersion) + "." + storageName, kind);
            return true;
        }

        private static bool TryGetTupleElementStorageName(IFieldSymbol fieldSymbol, out string storageName)
        {
            var tupleField = fieldSymbol.CorrespondingTupleField ?? fieldSymbol;
            if (tupleField.Name.Length > 4 &&
                tupleField.Name.StartsWith("Item", StringComparison.Ordinal) &&
                tupleField.Name.Skip(4).All(char.IsDigit))
            {
                storageName = tupleField.Name;
                return true;
            }

            storageName = string.Empty;
            return false;
        }

        private static bool TryCreateMemberFormula(
            SmtFormula receiver,
            string memberName,
            ITypeSymbol type,
            out SmtFormula? formula)
        {
            formula = null;
            var variableName = receiver + "." + memberName;
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                formula = new SmtVariable(variableName, SmtValueKind.Bool);
                return true;
            }

            if (IsIntegralOrEnumType(type))
            {
                formula = new SmtVariable(variableName, SmtValueKind.Int);
                return true;
            }

            if (type.IsReferenceType)
            {
                formula = new SmtVariable(variableName, SmtValueKind.Reference);
                return true;
            }

            return false;
        }

        private static bool TryCreateSymbolFormula(
            ISymbol symbol,
            Func<ISymbol, int>? getSymbolVersion,
            out SmtFormula formula)
        {
            var type = symbol switch
            {
                ILocalSymbol localSymbol => localSymbol.Type,
                IParameterSymbol parameterSymbol => parameterSymbol.Type,
                _ => null
            };

            if (type == null ||
                !TryGetValueKind(type, out var kind))
            {
                formula = null!;
                return false;
            }

            formula = new SmtVariable(GetVariableName(symbol, getSymbolVersion), kind);
            return true;
        }

        private static bool TryGetValueKind(ITypeSymbol type, out SmtValueKind kind)
        {
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                kind = SmtValueKind.Bool;
                return true;
            }

            if (IsIntegralOrEnumType(type))
            {
                kind = SmtValueKind.Int;
                return true;
            }

            if (type.IsReferenceType)
            {
                kind = SmtValueKind.Reference;
                return true;
            }

            kind = default;
            return false;
        }

        private static bool TryGetNullableUnderlyingType(ITypeSymbol? type, out ITypeSymbol underlyingType)
        {
            if (type is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                namedType.TypeArguments.Length == 1)
            {
                underlyingType = namedType.TypeArguments[0];
                return true;
            }

            underlyingType = null!;
            return false;
        }

        private static bool TryGetMemberType(ISymbol? memberSymbol, out ITypeSymbol type)
        {
            switch (memberSymbol)
            {
                case IPropertySymbol propertySymbol:
                    type = propertySymbol.Type;
                    return true;
                case IFieldSymbol fieldSymbol:
                    type = fieldSymbol.Type;
                    return true;
                default:
                    type = null!;
                    return false;
            }
        }

        private static string GetVariableName(ISymbol symbol, Func<ISymbol, int>? getSymbolVersion)
        {
            var start = symbol.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0;
            var name = symbol.Name + "#" + start.ToString(CultureInfo.InvariantCulture);
            var version = getSymbolVersion?.Invoke(symbol.OriginalDefinition) ?? 0;
            return version > 0
                ? name + "@v" + version.ToString(CultureInfo.InvariantCulture)
                : name;
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
                SpecialType.System_Int64 or
                SpecialType.System_UInt64;
        }

        private static bool IsIntegralOrEnumType(ITypeSymbol typeSymbol)
        {
            return IsIntegralType(typeSymbol) ||
                typeSymbol.TypeKind == TypeKind.Enum;
        }

        private static bool TryGetIntegralSpecialType(ITypeSymbol typeSymbol, out SpecialType specialType)
        {
            if (IsIntegralType(typeSymbol))
            {
                specialType = typeSymbol.SpecialType;
                return true;
            }

            if (typeSymbol is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlyingType } &&
                IsIntegralType(underlyingType))
            {
                specialType = underlyingType.SpecialType;
                return true;
            }

            specialType = SpecialType.None;
            return false;
        }

        private static bool TryGetIntegralConstant(object value, out long integralValue)
        {
            if (value is Enum enumValue)
            {
                value = Convert.ChangeType(enumValue, enumValue.GetTypeCode(), CultureInfo.InvariantCulture);
            }

            switch (value)
            {
                case sbyte signedByte:
                    integralValue = signedByte;
                    return true;
                case byte unsignedByte:
                    integralValue = unsignedByte;
                    return true;
                case short signedShort:
                    integralValue = signedShort;
                    return true;
                case ushort unsignedShort:
                    integralValue = unsignedShort;
                    return true;
                case int signedInt:
                    integralValue = signedInt;
                    return true;
                case uint unsignedInt:
                    integralValue = unsignedInt;
                    return true;
                case long signedLong:
                    integralValue = signedLong;
                    return true;
                case ulong unsignedLong when unsignedLong <= long.MaxValue:
                    integralValue = (long)unsignedLong;
                    return true;
                default:
                    integralValue = default;
                    return false;
            }
        }

        private static bool HasSupportedIntegralType(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            return type != null && IsIntegralOrEnumType(type);
        }

        private static bool HasSupportedBooleanType(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            var type = typeInfo.ConvertedType ?? typeInfo.Type;
            return type?.SpecialType == SpecialType.System_Boolean;
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
    }
}
