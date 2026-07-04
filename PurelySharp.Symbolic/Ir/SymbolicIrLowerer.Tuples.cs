using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PurelySharp.Symbolic.Ir
{
    internal static partial class SymbolicIrLowerer
    {
        private static bool TryLowerTupleEqualityCondition(
            BinaryExpressionSyntax binaryExpression,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (!TryLowerTupleElementTerms(binaryExpression.Left, context, out var leftElements) ||
                !TryLowerTupleElementTerms(binaryExpression.Right, context, out var rightElements) ||
                leftElements.Length == 0 ||
                leftElements.Length != rightElements.Length)
            {
                return false;
            }

            SymbolicCondition? equality = null;
            for (var index = 0; index < leftElements.Length; index++)
            {
                if (!CanCompareTerms(leftElements[index], rightElements[index], SymbolicRelationOperator.Equal))
                {
                    return false;
                }

                var elementEquality = CreateRelationCondition(
                    SymbolicRelationOperator.Equal,
                    leftElements[index],
                    rightElements[index],
                    binaryExpression,
                    "ir.tuple.equality.element");
                equality = equality == null
                    ? elementEquality
                    : new SymbolicBinaryCondition(SymbolicConditionOperator.And, equality, elementEquality);
            }

            condition = binaryExpression.IsKind(SyntaxKind.EqualsExpression)
                ? equality!
                : new SymbolicNotCondition(equality!);
            return true;
        }

        private static bool TryLowerTupleElementMemberTerm(
            MemberAccessExpressionSyntax memberAccess,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;
            if (!TryGetStableVariableSymbol(memberAccess.Expression, context, out var tupleSymbol) ||
                context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is not IFieldSymbol field ||
                !TryGetTupleElementStorageName(field, out var storageName) ||
                !TryGetValueKind(field.Type, out var kind))
            {
                return false;
            }

            term = new SymbolicVariableTerm(context.GetVariableName(tupleSymbol) + "." + storageName, kind);
            return true;
        }

        private static bool TryLowerTupleElementTerms(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out ImmutableArray<SymbolicTerm> terms)
        {
            terms = ImmutableArray<SymbolicTerm>.Empty;
            if (!TryGetStableVariableSymbol(expression, context, out var symbol) ||
                context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type is not INamedTypeSymbol { IsTupleType: true } tupleType ||
                tupleType.TupleElements.Length == 0)
            {
                return false;
            }

            var builder = ImmutableArray.CreateBuilder<SymbolicTerm>(tupleType.TupleElements.Length);
            foreach (var element in tupleType.TupleElements)
            {
                var field = element.CorrespondingTupleField ?? element;
                if (!TryGetTupleElementStorageName(field, out var storageName) ||
                    !TryGetValueKind(field.Type, out var kind))
                {
                    return false;
                }

                builder.Add(new SymbolicVariableTerm(context.GetVariableName(symbol) + "." + storageName, kind));
            }

            terms = builder.ToImmutable();
            return true;
        }

        private static bool TryGetTupleElementStorageName(IFieldSymbol field, out string storageName)
        {
            var storageField = field.CorrespondingTupleField ?? field;
            storageName = storageField.Name;
            return storageName.StartsWith("Item", StringComparison.Ordinal);
        }
    }
}
