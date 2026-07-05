using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Ir
{
    internal static partial class SymbolicIrLowerer
    {
        private static bool TryLowerNullableGetValueOrDefaultInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol method,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                invocation.ArgumentList.Arguments.Count is not 0 and not 1 ||
                method.Parameters.Length != invocation.ArgumentList.Arguments.Count ||
                method.ContainingType?.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T ||
                !TryLowerNullableHasValueTerm(memberAccess.Expression, context, out var hasValueTerm) ||
                !TryLowerNullableValueTerm(memberAccess.Expression, context, out var valueTerm))
            {
                return false;
            }

            SymbolicTerm fallbackTerm;
            if (invocation.ArgumentList.Arguments.Count == 0)
            {
                if (!TryCreateDefaultTerm(method.ReturnType, out fallbackTerm))
                {
                    return false;
                }
            }
            else if (!TryLowerTerm(invocation.ArgumentList.Arguments[0].Expression, context, out fallbackTerm) ||
                fallbackTerm.Kind != valueTerm.Kind)
            {
                return false;
            }

            term = new SymbolicConditionalTerm(
                CreateFactCondition(
                    new SymbolicTruthAtom(hasValueTerm),
                    invocation,
                    "ir.known-api.nullable.get-value-or-default.has-value"),
                valueTerm,
                fallbackTerm);
            return true;
        }

        public static bool TryLowerNullableHasValueTerm(
            ExpressionSyntax nullableExpression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            var originalExpression = nullableExpression;
            nullableExpression = UnwrapExpression(nullableExpression);
            var typeInfo = context.SemanticModel.GetTypeInfo(originalExpression, context.CancellationToken);
            var expressionType = typeInfo.ConvertedType ?? typeInfo.Type;
            if (!SymbolicTypeFacts.TryGetNullableUnderlyingType(
                    expressionType,
                    out var underlyingType))
            {
                term = null!;
                return false;
            }

            if (TryGetStableVariableSymbol(nullableExpression, context, out var symbol))
            {
                term = new SymbolicNullableHasValueTerm(context.GetVariableName(symbol));
                return true;
            }

            if (TryLowerNullLikeNullableHasValueTerm(nullableExpression, context, out term))
            {
                return true;
            }

            if (nullableExpression is BinaryExpressionSyntax coalesceExpression &&
                coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                TryLowerNullableCoalesceHasValueTerm(coalesceExpression, context, out term))
            {
                return true;
            }

            if (nullableExpression is ConditionalExpressionSyntax conditionalExpression &&
                TryLowerNullableConditionalHasValueTerm(conditionalExpression, context, out term))
            {
                return true;
            }

            if (nullableExpression is ConditionalAccessExpressionSyntax conditionalAccess &&
                TryLowerNullableConditionalAccessHasValueTerm(conditionalAccess, context, out term))
            {
                return true;
            }

            var valueExpression = nullableExpression is CastExpressionSyntax castExpression
                ? castExpression.Expression
                : nullableExpression;
            if (valueExpression != nullableExpression ||
                !SymbolicTypeFacts.TryGetNullableUnderlyingType(typeInfo.Type, out _))
            {
                var valueTypeInfo = context.SemanticModel.GetTypeInfo(valueExpression, context.CancellationToken);
                if (SymbolEqualityComparer.Default.Equals(valueTypeInfo.ConvertedType, underlyingType) ||
                    SymbolEqualityComparer.Default.Equals(valueTypeInfo.Type, underlyingType))
                {
                    term = new SymbolicBooleanConstantTerm(true);
                    return true;
                }
            }

            term = null!;
            return false;
        }

        private static bool TryLowerNullLikeNullableHasValueTerm(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            var constant = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
            if (constant is { HasValue: true, Value: null } ||
                expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
                expression is DefaultExpressionSyntax)
            {
                term = new SymbolicBooleanConstantTerm(false);
                return true;
            }

            term = null!;
            return false;
        }

        private static bool TryLowerNullableCoalesceHasValueTerm(
            BinaryExpressionSyntax coalesceExpression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            if (!TryLowerNullableHasValueTerm(coalesceExpression.Left, context, out var leftHasValue) ||
                !TryLowerNullableHasValueTerm(coalesceExpression.Right, context, out var rightHasValue))
            {
                term = null!;
                return false;
            }

            term = new SymbolicConditionalTerm(
                CreateFactCondition(
                    new SymbolicTruthAtom(leftHasValue),
                    coalesceExpression.Left,
                    "ir.nullable.coalesce.left-has-value"),
                new SymbolicBooleanConstantTerm(true),
                rightHasValue);
            return true;
        }

        private static bool TryLowerNullableConditionalHasValueTerm(
            ConditionalExpressionSyntax conditionalExpression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            if (!TryLowerCondition(conditionalExpression.Condition, context, out var condition) ||
                !TryLowerNullableHasValueTerm(conditionalExpression.WhenTrue, context, out var whenTrueHasValue) ||
                !TryLowerNullableHasValueTerm(conditionalExpression.WhenFalse, context, out var whenFalseHasValue))
            {
                term = null!;
                return false;
            }

            term = new SymbolicConditionalTerm(condition, whenTrueHasValue, whenFalseHasValue);
            return true;
        }

        private static bool TryLowerNullableConditionalAccessHasValueTerm(
            ConditionalAccessExpressionSyntax conditionalAccess,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            if (!TryLowerTerm(conditionalAccess.Expression, context, out var receiver) ||
                receiver.Kind != SmtValueKind.Reference)
            {
                term = null!;
                return false;
            }

            term = new SymbolicConditionalTerm(
                CreateReferenceNullCondition(
                    receiver,
                    equalToNull: false,
                    conditionalAccess.Expression,
                    "ir.nullable.conditional-access.receiver-not-null"),
                new SymbolicBooleanConstantTerm(true),
                new SymbolicBooleanConstantTerm(false));
            return true;
        }

        private static bool TryLowerNullLikeNullableValueTerm(
            ExpressionSyntax expression,
            ITypeSymbol underlyingType,
            out SymbolicTerm term)
        {
            if (expression.IsKind(SyntaxKind.DefaultLiteralExpression) ||
                expression is DefaultExpressionSyntax ||
                expression.IsKind(SyntaxKind.NullLiteralExpression))
            {
                return TryCreateDefaultTerm(underlyingType, out term);
            }

            term = null!;
            return false;
        }

        private static bool TryLowerNullableCoalesceNullableValueTerm(
            BinaryExpressionSyntax coalesceExpression,
            SmtValueKind expectedKind,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            if (!TryLowerNullableHasValueTerm(coalesceExpression.Left, context, out var leftHasValue) ||
                !TryLowerNullableValueTerm(coalesceExpression.Left, context, out var leftValue) ||
                !TryLowerNullableValueTerm(coalesceExpression.Right, context, out var rightValue) ||
                leftValue.Kind != expectedKind ||
                rightValue.Kind != expectedKind)
            {
                term = null!;
                return false;
            }

            term = new SymbolicConditionalTerm(
                CreateFactCondition(
                    new SymbolicTruthAtom(leftHasValue),
                    coalesceExpression.Left,
                    "ir.nullable.coalesce.left-has-value"),
                leftValue,
                rightValue);
            return true;
        }

        private static bool TryLowerNullableConditionalValueTerm(
            ConditionalExpressionSyntax conditionalExpression,
            SmtValueKind expectedKind,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            if (!TryLowerCondition(conditionalExpression.Condition, context, out var condition) ||
                !TryLowerNullableValueTerm(conditionalExpression.WhenTrue, context, out var whenTrueValue) ||
                !TryLowerNullableValueTerm(conditionalExpression.WhenFalse, context, out var whenFalseValue) ||
                whenTrueValue.Kind != expectedKind ||
                whenFalseValue.Kind != expectedKind)
            {
                term = null!;
                return false;
            }

            term = new SymbolicConditionalTerm(condition, whenTrueValue, whenFalseValue);
            return true;
        }

        private static bool TryLowerNullableConditionalAccessValueTerm(
            ConditionalAccessExpressionSyntax conditionalAccess,
            SmtValueKind expectedKind,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;
            if (!TryLowerTerm(conditionalAccess.Expression, context, out var receiver) ||
                receiver.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            return conditionalAccess.WhenNotNull switch
            {
                ElementBindingExpressionSyntax elementBinding => TryLowerConditionalAccessElementBindingValueTerm(
                    conditionalAccess,
                    elementBinding,
                    receiver,
                    expectedKind,
                    context,
                    out term),
                MemberBindingExpressionSyntax memberBinding => TryLowerConditionalAccessMemberBindingValueTerm(
                    conditionalAccess,
                    memberBinding,
                    receiver,
                    expectedKind,
                    context,
                    out term),
                _ => false,
            };
        }

        private static bool TryLowerConditionalAccessElementBindingValueTerm(
            ConditionalAccessExpressionSyntax conditionalAccess,
            ElementBindingExpressionSyntax elementBinding,
            SymbolicTerm receiver,
            SmtValueKind expectedKind,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            var receiverType = context.SemanticModel.GetTypeInfo(conditionalAccess.Expression, context.CancellationToken).Type;
            if (elementBinding.ArgumentList.Arguments.Count != 1 ||
                receiverType is not IArrayTypeSymbol { Rank: 1 } arrayType ||
                !TryGetValueKind(arrayType.ElementType, out var elementKind) ||
                elementKind != expectedKind ||
                !TryLowerTerm(elementBinding.ArgumentList.Arguments[0].Expression, context, out var index) ||
                index.Kind != SmtValueKind.Int)
            {
                term = null!;
                return false;
            }

            term = new SymbolicElementTerm(receiver, index, elementKind);
            return true;
        }

        private static bool TryLowerConditionalAccessMemberBindingValueTerm(
            ConditionalAccessExpressionSyntax conditionalAccess,
            MemberBindingExpressionSyntax memberBinding,
            SymbolicTerm receiver,
            SmtValueKind expectedKind,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            if (context.SemanticModel.GetSymbolInfo(memberBinding.Name, context.CancellationToken).Symbol is not { } memberSymbol ||
                !TryGetSymbolType(memberSymbol, out var memberType) ||
                !TryGetValueKind(memberType, out var memberKind) ||
                memberKind != expectedKind)
            {
                term = null!;
                return false;
            }

            var receiverType = context.SemanticModel.GetTypeInfo(conditionalAccess.Expression, context.CancellationToken).Type;
            if (string.Equals(memberSymbol.Name, nameof(string.Length), StringComparison.Ordinal))
            {
                if (receiverType?.SpecialType == SpecialType.System_String &&
                    TryLowerStringTerm(conditionalAccess.Expression, context, out var stringValue))
                {
                    term = new SymbolicLengthTerm(stringValue);
                    return true;
                }

                if (receiverType is IArrayTypeSymbol { Rank: 1 } ||
                    SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(receiverType))
                {
                    term = new SymbolicLengthTerm(receiver);
                    return true;
                }

                if (receiverType is IArrayTypeSymbol { Rank: > 1 } multiDimensionalArray &&
                    TryLowerArrayTotalLengthTerm(conditionalAccess.Expression, multiDimensionalArray, context, out term))
                {
                    return true;
                }
            }

            term = new SymbolicMemberTerm(receiver, memberSymbol.Name, memberKind);
            return true;
        }

        public static bool TryLowerNullableValueTerm(
            ExpressionSyntax nullableExpression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            var originalExpression = nullableExpression;
            nullableExpression = UnwrapExpression(nullableExpression);
            var typeInfo = context.SemanticModel.GetTypeInfo(originalExpression, context.CancellationToken);
            var expressionType = typeInfo.ConvertedType ?? typeInfo.Type;
            if (!SymbolicTypeFacts.TryGetNullableUnderlyingType(expressionType, out var underlyingType) ||
                !TryGetValueKind(underlyingType, out var valueKind))
            {
                term = null!;
                return false;
            }

            if (TryGetStableVariableSymbol(nullableExpression, context, out var symbol))
            {
                term = new SymbolicNullableValueTerm(context.GetVariableName(symbol), valueKind);
                return true;
            }

            if (TryLowerNullLikeNullableValueTerm(nullableExpression, underlyingType, out term))
            {
                return true;
            }

            if (nullableExpression is BinaryExpressionSyntax coalesceExpression &&
                coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                TryLowerNullableCoalesceNullableValueTerm(coalesceExpression, valueKind, context, out term))
            {
                return true;
            }

            if (nullableExpression is ConditionalExpressionSyntax conditionalExpression &&
                TryLowerNullableConditionalValueTerm(conditionalExpression, valueKind, context, out term))
            {
                return true;
            }

            if (nullableExpression is ConditionalAccessExpressionSyntax conditionalAccess &&
                TryLowerNullableConditionalAccessValueTerm(conditionalAccess, valueKind, context, out term))
            {
                return true;
            }

            var valueExpression = nullableExpression is CastExpressionSyntax castExpression
                ? castExpression.Expression
                : nullableExpression;
            if ((valueExpression != nullableExpression ||
                 !SymbolicTypeFacts.TryGetNullableUnderlyingType(typeInfo.Type, out _)) &&
                TryLowerTerm(valueExpression, context, out var valueTerm) &&
                valueTerm.Kind == valueKind)
            {
                term = valueTerm;
                return true;
            }

            term = null!;
            return false;
        }

        public static bool TryLowerNullableCoalesceValueTerm(
            BinaryExpressionSyntax coalesceExpression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            var typeInfo = context.SemanticModel.GetTypeInfo(coalesceExpression, context.CancellationToken);
            var resultType = typeInfo.ConvertedType ?? typeInfo.Type;
            if (resultType == null ||
                !TryGetValueKind(resultType, out var resultKind) ||
                !TryLowerNullableHasValueTerm(coalesceExpression.Left, context, out var leftHasValue) ||
                !TryLowerNullableValueTerm(coalesceExpression.Left, context, out var leftValue) ||
                !TryLowerTerm(coalesceExpression.Right, context, out var fallbackValue) ||
                leftValue.Kind != resultKind ||
                fallbackValue.Kind != resultKind)
            {
                term = null!;
                return false;
            }

            term = new SymbolicConditionalTerm(
                CreateFactCondition(
                    new SymbolicTruthAtom(leftHasValue),
                    coalesceExpression.Left,
                    "ir.nullable.coalesce.left-has-value"),
                leftValue,
                fallbackValue);
            return true;
        }

        private static bool TryCreateDefaultTerm(ITypeSymbol type, out SymbolicTerm term)
        {
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                term = new SymbolicBooleanConstantTerm(false);
                return true;
            }

            if (TryGetValueKind(type, out var kind) &&
                kind == SmtValueKind.Int)
            {
                term = new SymbolicIntegerConstantTerm(0);
                return true;
            }

            term = null!;
            return false;
        }
    }
}
