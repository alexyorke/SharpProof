using System;
using System.Globalization;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PurelySharp.Symbolic.Smt;
using SearchLib.Smt;

namespace PurelySharp.Symbolic
{
    public static class SymbolicFactFactory
    {
        public static SmtFormula CreateAssignedValueFact(SmtFormula targetFormula, SmtFormula valueFormula)
        {
            if (targetFormula.Kind == SmtValueKind.Bool &&
                valueFormula is SmtBooleanConstant booleanConstant)
            {
                return booleanConstant.Value
                    ? targetFormula
                    : new SmtUnaryFormula(SmtUnaryOperator.Not, targetFormula);
            }

            return new SmtBinaryFormula(SmtBinaryOperator.Equal, targetFormula, valueFormula);
        }

        public static bool TryCreateReferenceBackedLengthFact(
            SmtFormula targetReference,
            ExpressionSyntax valueExpression,
            ExpressionSyntax unwrappedValueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ExpressionSyntax, SemanticModel, CancellationToken, SmtFormula?> createValueLengthFormula,
            out SmtFormula fact)
        {
            fact = null!;
            var valueType = semanticModel.GetTypeInfo(unwrappedValueExpression, cancellationToken).Type;
            if (valueType is not IArrayTypeSymbol { Rank: 1 } ||
                !TryCreateReferenceBuiltInLengthFormula(targetReference, out var targetLength) ||
                createValueLengthFormula(valueExpression, semanticModel, cancellationToken) is not { } valueLength)
            {
                return false;
            }

            fact = new SmtBinaryFormula(SmtBinaryOperator.Equal, targetLength, valueLength);
            return true;
        }

        public static bool TryCreateReferenceBackedStringContentFact(
            SmtFormula targetReference,
            ExpressionSyntax valueExpression,
            ExpressionSyntax unwrappedValueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ExpressionSyntax, SemanticModel, CancellationToken, SmtFormula?> createValueStringFormula,
            out SmtFormula fact)
        {
            fact = null!;
            var valueType = semanticModel.GetTypeInfo(unwrappedValueExpression, cancellationToken).Type;
            if (valueType?.SpecialType != SpecialType.System_String ||
                !TryCreateReferenceStringContentFormula(targetReference, out var targetString) ||
                createValueStringFormula(valueExpression, semanticModel, cancellationToken) is not { } valueString)
            {
                return false;
            }

            fact = new SmtBinaryFormula(SmtBinaryOperator.Equal, targetString, valueString);
            return true;
        }

        public static bool TryCreateCollectionExpressionLengthLowerBoundFact(
            SmtFormula targetLengthFormula,
            ExpressionSyntax unwrappedValueExpression,
            out SmtFormula fact)
        {
            fact = null!;
            if (unwrappedValueExpression is not CollectionExpressionSyntax collectionExpression ||
                !TryGetCollectionExpressionFixedLowerBound(collectionExpression, out var lowerBound))
            {
                return false;
            }

            fact = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                targetLengthFormula,
                new SmtIntegerConstant(lowerBound));
            return true;
        }

        public static bool TryCreateReferenceBuiltInLengthFormula(SmtFormula receiverFormula, out SmtFormula formula)
        {
            if (receiverFormula.Kind != SmtValueKind.Reference)
            {
                formula = null!;
                return false;
            }

            formula = new SmtVariable(GetReferenceFormulaName(receiverFormula) + ".Length", SmtValueKind.Int);
            return true;
        }

        public static bool TryCreateReferenceArrayDimensionLengthFormula(
            SmtFormula receiverFormula,
            int dimension,
            out SmtFormula formula)
        {
            if (receiverFormula.Kind != SmtValueKind.Reference ||
                dimension < 0)
            {
                formula = null!;
                return false;
            }

            formula = new SmtVariable(
                GetReferenceFormulaName(receiverFormula) + ".GetLength(" + dimension.ToString(CultureInfo.InvariantCulture) + ")",
                SmtValueKind.Int);
            return true;
        }

        public static bool TryCreateReferenceStringContentFormula(SmtFormula receiverFormula, out SmtFormula formula)
        {
            if (receiverFormula.Kind != SmtValueKind.Reference)
            {
                formula = null!;
                return false;
            }

            formula = new SmtVariable(GetReferenceFormulaName(receiverFormula) + ".String", SmtValueKind.String);
            return true;
        }

        public static string GetReferenceFormulaName(SmtFormula receiverFormula)
        {
            return receiverFormula is SmtVariable variable
                ? variable.Name
                : receiverFormula.ToString() ?? string.Empty;
        }

        private static bool TryGetCollectionExpressionFixedLowerBound(
            CollectionExpressionSyntax collectionExpression,
            out int lowerBound)
        {
            lowerBound = 0;
            var hasSpread = false;
            foreach (var element in collectionExpression.Elements)
            {
                switch (element)
                {
                    case ExpressionElementSyntax:
                        lowerBound++;
                        break;
                    case SpreadElementSyntax:
                        hasSpread = true;
                        break;
                }
            }

            return hasSpread && lowerBound > 0;
        }
    }
}
