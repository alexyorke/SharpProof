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
                dimension >= arrayType.Rank ||
                !TryLowerTerm(arrayExpression, context, out var arrayTerm) ||
                arrayTerm.Kind != SmtValueKind.Reference)
            {
                term = null!;
                return false;
            }

            term = new SymbolicArrayDimensionLengthTerm(arrayTerm, dimension);
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
    }
}
