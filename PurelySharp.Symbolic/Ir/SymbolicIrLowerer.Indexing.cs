using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;

namespace PurelySharp.Symbolic.Ir
{
    internal static partial class SymbolicIrLowerer
    {
        private static bool TryLowerElementAccessTerm(
            ElementAccessExpressionSyntax elementAccess,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;
            if (elementAccess.ArgumentList.Arguments.Count != 1 ||
                !TryGetElementAccessValueKind(elementAccess, context, out var elementKind) ||
                !TryLowerTerm(elementAccess.Expression, context, out var receiver) ||
                receiver.Kind != SmtValueKind.Reference ||
                !TryLowerTerm(elementAccess.ArgumentList.Arguments[0].Expression, context, out var index) ||
                index.Kind != SmtValueKind.Int)
            {
                return false;
            }

            term = new SymbolicElementTerm(receiver, index, elementKind);
            return true;
        }

        private static bool TryGetElementAccessValueKind(
            ElementAccessExpressionSyntax elementAccess,
            SymbolicLoweringContext context,
            out SmtValueKind kind)
        {
            var receiverType = context.SemanticModel.GetTypeInfo(elementAccess.Expression, context.CancellationToken).Type;
            if (receiverType is IArrayTypeSymbol { Rank: 1 } arrayType &&
                TryGetValueKind(arrayType.ElementType, out kind))
            {
                return true;
            }

            kind = default;
            return false;
        }

        private static bool TryLowerArrayGetLengthInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol method,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                invocation.ArgumentList.Arguments.Count != 1 ||
                method.ContainingType?.SpecialType != SpecialType.System_Array ||
                method.Parameters.Length != 1 ||
                method.Parameters[0].Type.SpecialType != SpecialType.System_Int32)
            {
                return false;
            }

            var dimensionValue = context.SemanticModel.GetConstantValue(
                invocation.ArgumentList.Arguments[0].Expression,
                context.CancellationToken);
            if (dimensionValue is not { HasValue: true, Value: int dimension })
            {
                return false;
            }

            return TryLowerArrayDimensionLengthTerm(
                memberAccess.Expression,
                dimension,
                context,
                out term);
        }

        private static bool TryLowerArrayBoundInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol method,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                invocation.ArgumentList.Arguments.Count != 1 ||
                method.ContainingType?.SpecialType != SpecialType.System_Array ||
                method.Parameters.Length != 1 ||
                method.Parameters[0].Type.SpecialType != SpecialType.System_Int32)
            {
                return false;
            }

            var receiverType = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).ConvertedType ??
                context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
            if (receiverType is not IArrayTypeSymbol)
            {
                return false;
            }

            var dimensionValue = context.SemanticModel.GetConstantValue(
                invocation.ArgumentList.Arguments[0].Expression,
                context.CancellationToken);
            if (dimensionValue is not { HasValue: true, Value: int dimension })
            {
                return false;
            }

            if (string.Equals(method.Name, nameof(Array.GetLowerBound), StringComparison.Ordinal))
            {
                if (!TryLowerArrayDimensionLengthTerm(memberAccess.Expression, dimension, context, out _))
                {
                    return false;
                }

                term = new SymbolicIntegerConstantTerm(0);
                return true;
            }

            if (!string.Equals(method.Name, nameof(Array.GetUpperBound), StringComparison.Ordinal) ||
                !TryLowerArrayDimensionLengthTerm(memberAccess.Expression, dimension, context, out var length))
            {
                return false;
            }

            term = new SymbolicBinaryTerm(
                SymbolicBinaryTermOperator.Subtract,
                length,
                new SymbolicIntegerConstantTerm(1));
            return true;
        }

        public static bool TryLowerArrayDimensionLengthTerm(
            ExpressionSyntax arrayExpression,
            int dimension,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            arrayExpression = UnwrapExpression(arrayExpression);
            var type = context.SemanticModel.GetTypeInfo(arrayExpression, context.CancellationToken).ConvertedType ??
                context.SemanticModel.GetTypeInfo(arrayExpression, context.CancellationToken).Type;
            if (type is not IArrayTypeSymbol arrayType ||
                dimension < 0 ||
                dimension >= arrayType.Rank)
            {
                term = null!;
                return false;
            }

            if (TryLowerArrayCreationDimensionLengthTerm(arrayExpression, arrayType, dimension, context, out term))
            {
                return true;
            }

            if (!TryLowerTerm(arrayExpression, context, out var arrayTerm) ||
                arrayTerm.Kind != SmtValueKind.Reference)
            {
                term = null!;
                return false;
            }

            term = new SymbolicArrayDimensionLengthTerm(arrayTerm, dimension);
            return true;
        }

        private static bool TryLowerArrayTotalLengthTerm(
            ExpressionSyntax arrayExpression,
            IArrayTypeSymbol arrayType,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;
            arrayExpression = UnwrapExpression(arrayExpression);
            if (arrayType.Rank <= 0)
            {
                return false;
            }

            if (TryLowerArrayCreationTotalLengthTerm(arrayExpression, arrayType, context, out term))
            {
                return true;
            }

            return TryLowerTerm(arrayExpression, context, out var arrayTerm) &&
                TryCreateArrayTotalLengthReferenceTerm(arrayTerm, arrayType, out term);
        }

        internal static bool TryCreateBuiltInLengthReferenceTerm(
            ITypeSymbol? type,
            SymbolicTerm reference,
            out SymbolicTerm term)
        {
            term = null!;
            if (reference.Kind != SmtValueKind.Reference ||
                type == null)
            {
                return false;
            }

            if (type.SpecialType == SpecialType.System_String)
            {
                return TryCreateStringContentReferenceTerm(reference, out var stringContent) &&
                    CreateLengthTerm(stringContent, out term);
            }

            if (type is IArrayTypeSymbol { Rank: 1 } ||
                IsBuiltInSpanOrMemoryType(type))
            {
                return CreateLengthTerm(reference, out term);
            }

            return type is IArrayTypeSymbol { Rank: > 1 } multiDimensionalArray &&
                TryCreateArrayTotalLengthReferenceTerm(reference, multiDimensionalArray, out term);
        }

        private static bool TryCreateArrayTotalLengthReferenceTerm(
            SymbolicTerm arrayTerm,
            IArrayTypeSymbol arrayType,
            out SymbolicTerm term)
        {
            term = null!;
            if (arrayTerm.Kind != SmtValueKind.Reference ||
                arrayType.Rank <= 0)
            {
                return false;
            }

            SymbolicTerm totalLength = new SymbolicArrayDimensionLengthTerm(arrayTerm, 0);
            for (var dimension = 1; dimension < arrayType.Rank; dimension++)
            {
                totalLength = new SymbolicBinaryTerm(
                    SymbolicBinaryTermOperator.Multiply,
                    totalLength,
                    new SymbolicArrayDimensionLengthTerm(arrayTerm, dimension));
            }

            term = totalLength;
            return true;
        }

        private static bool CreateLengthTerm(SymbolicTerm value, out SymbolicTerm term)
        {
            term = new SymbolicLengthTerm(value);
            return true;
        }

        private static bool TryLowerArrayCreationTotalLengthTerm(
            ExpressionSyntax arrayExpression,
            IArrayTypeSymbol arrayType,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;
            if (arrayType.Rank <= 0 ||
                !TryLowerArrayCreationDimensionLengthTerm(arrayExpression, arrayType, 0, context, out var totalLength))
            {
                return false;
            }

            for (var dimension = 1; dimension < arrayType.Rank; dimension++)
            {
                if (!TryLowerArrayCreationDimensionLengthTerm(arrayExpression, arrayType, dimension, context, out var dimensionLength))
                {
                    return false;
                }

                totalLength = new SymbolicBinaryTerm(
                    SymbolicBinaryTermOperator.Multiply,
                    totalLength,
                    dimensionLength);
            }

            term = totalLength;
            return true;
        }

        private static bool TryLowerArrayCreationDimensionLengthTerm(
            ExpressionSyntax arrayExpression,
            IArrayTypeSymbol arrayType,
            int dimension,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;
            if (arrayExpression is not ArrayCreationExpressionSyntax arrayCreation ||
                arrayCreation.Type.RankSpecifiers.Count == 0)
            {
                return false;
            }

            var rankSpecifier = arrayCreation.Type.RankSpecifiers[0];
            if (rankSpecifier.Sizes.Count != arrayType.Rank ||
                rankSpecifier.Sizes[dimension].IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.OmittedArraySizeExpression) ||
                !TryLowerTerm(rankSpecifier.Sizes[dimension], context, out var sizeTerm) ||
                sizeTerm.Kind != SmtValueKind.Int)
            {
                return false;
            }

            term = sizeTerm;
            return true;
        }

        public static bool TryCreateArrayElementBoundsCondition(
            ExpressionSyntax arrayExpression,
            IReadOnlyList<ExpressionSyntax> indexExpressions,
            SyntaxNode source,
            string provenance,
            SymbolicLoweringContext context,
            out SymbolicCondition condition,
            out SymbolicTerm? subject)
        {
            condition = null!;
            subject = null;
            var arrayType = context.SemanticModel.GetTypeInfo(arrayExpression, context.CancellationToken).ConvertedType ??
                context.SemanticModel.GetTypeInfo(arrayExpression, context.CancellationToken).Type;
            if (arrayType is not IArrayTypeSymbol { Rank: > 0 } typedArray ||
                indexExpressions.Count != typedArray.Rank)
            {
                return false;
            }

            SymbolicCondition? combined = null;
            for (var dimension = 0; dimension < typedArray.Rank; dimension++)
            {
                if (!TryLowerTerm(indexExpressions[dimension], context, out var index) ||
                    index.Kind != SmtValueKind.Int ||
                    !TryLowerArrayDimensionLengthTerm(arrayExpression, dimension, context, out var length))
                {
                    return false;
                }

                subject ??= index;
                var dimensionInRange = new SymbolicFactCondition(SymbolicFact.Exact(
                    new SymbolicBoundsAtom(
                        index,
                        length,
                        IncludeLowerBound: true,
                        IncludeUpperBound: true),
                    source,
                    provenance));
                combined = combined == null
                    ? dimensionInRange
                    : new SymbolicBinaryCondition(
                        SymbolicConditionOperator.And,
                        combined,
                        dimensionInRange);
            }

            if (combined == null)
            {
                return false;
            }

            condition = combined;
            return true;
        }

        public static bool TryCreateSubsequenceInRangeCondition(
            ExpressionSyntax receiverExpression,
            ExpressionSyntax startExpression,
            ExpressionSyntax? lengthExpression,
            SyntaxNode source,
            string provenance,
            SymbolicLoweringContext context,
            bool oneArgumentUpperBoundIsInclusive,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (!TryLowerBuiltInLengthTerm(receiverExpression, context, out var sourceLength) ||
                !TryLowerTerm(startExpression, context, out var start) ||
                start.Kind != SmtValueKind.Int)
            {
                return false;
            }

            var startNonNegative = CreateRelationCondition(
                SymbolicRelationOperator.GreaterThanOrEqual,
                start,
                new SymbolicIntegerConstantTerm(0),
                source,
                provenance + ".start-non-negative");

            if (lengthExpression == null)
            {
                var upperBound = CreateRelationCondition(
                    oneArgumentUpperBoundIsInclusive
                        ? SymbolicRelationOperator.LessThanOrEqual
                        : SymbolicRelationOperator.LessThan,
                    start,
                    sourceLength,
                    source,
                    provenance + ".start-within-length");
                condition = new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    startNonNegative,
                    upperBound);
                return true;
            }

            if (!TryLowerTerm(lengthExpression, context, out var count) ||
                count.Kind != SmtValueKind.Int)
            {
                return false;
            }

            var countNonNegative = CreateRelationCondition(
                SymbolicRelationOperator.GreaterThanOrEqual,
                count,
                new SymbolicIntegerConstantTerm(0),
                source,
                provenance + ".count-non-negative");
            var startWithinLength = CreateRelationCondition(
                SymbolicRelationOperator.LessThanOrEqual,
                start,
                sourceLength,
                source,
                provenance + ".start-within-length");
            var remainingLength = new SymbolicBinaryTerm(
                SymbolicBinaryTermOperator.Subtract,
                sourceLength,
                start);
            var countWithinRemainingLength = CreateRelationCondition(
                SymbolicRelationOperator.LessThanOrEqual,
                count,
                remainingLength,
                source,
                provenance + ".count-within-remaining-length");
            var additionDoesNotOverflow = count is SymbolicIntegerConstantTerm { Value: 0 }
                ? new SymbolicConstantCondition(true)
                : CreateRelationCondition(
                    SymbolicRelationOperator.LessThanOrEqual,
                    start,
                    new SymbolicBinaryTerm(
                        SymbolicBinaryTermOperator.Subtract,
                        new SymbolicIntegerConstantTerm(int.MaxValue),
                        count),
                    source,
                    provenance + ".addition-does-not-overflow");

            condition = new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                startNonNegative,
                new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    countNonNegative,
                    new SymbolicBinaryCondition(
                        SymbolicConditionOperator.And,
                        startWithinLength,
                        new SymbolicBinaryCondition(
                            SymbolicConditionOperator.And,
                            countWithinRemainingLength,
                            additionDoesNotOverflow))));
            return true;
        }

        public static bool TryLowerBuiltInLengthTerm(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            expression = UnwrapExpression(expression);
            var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).ConvertedType ??
                context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
            if (type?.SpecialType == SpecialType.System_String)
            {
                if (TryLowerStringTerm(expression, context, out var stringValue))
                {
                    term = new SymbolicLengthTerm(stringValue);
                    return true;
                }

                if (TryLowerTerm(expression, context, out var reference) &&
                    TryCreateBuiltInLengthReferenceTerm(type, reference, out term))
                {
                    return true;
                }
            }

            if (TryLowerTerm(expression, context, out var receiver) &&
                TryCreateBuiltInLengthReferenceTerm(type, receiver, out term))
            {
                return true;
            }

            term = null!;
            return false;
        }
    }
}
