using System;
using System.Globalization;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PurelySharp.Symbolic.Smt;
using SearchLib.Smt;

namespace PurelySharp.Symbolic
{
    internal static class SymbolicFactFactory
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

        public static bool TryCreateStringContentFormula(string variableName, ITypeSymbol? type, out SmtFormula formula)
        {
            if (type?.SpecialType == SpecialType.System_String)
            {
                formula = new SmtVariable(variableName + ".String", SmtValueKind.String);
                return true;
            }

            formula = null!;
            return false;
        }

        public static bool TryCreateBuiltInLengthFormula(string variableName, ITypeSymbol? type, out SmtFormula formula)
        {
            if (type?.SpecialType == SpecialType.System_String)
            {
                formula = new SmtStringLengthTerm(new SmtVariable(variableName + ".String", SmtValueKind.String));
                return true;
            }

            if (type is IArrayTypeSymbol { Rank: 1 } ||
                IsBuiltInSpanOrMemoryType(type))
            {
                return TryCreateReferenceBuiltInLengthFormula(new SmtVariable(variableName, SmtValueKind.Reference), out formula);
            }

            formula = null!;
            return false;
        }

        public static bool TryCreateBuiltInLengthFormulaForReference(
            SmtFormula receiverFormula,
            ITypeSymbol? type,
            out SmtFormula formula)
        {
            if (receiverFormula.Kind != SmtValueKind.Reference)
            {
                formula = null!;
                return false;
            }

            if (type?.SpecialType == SpecialType.System_String)
            {
                formula = new SmtStringLengthTerm(
                    new SmtVariable(GetReferenceFormulaName(receiverFormula) + ".String", SmtValueKind.String));
                return true;
            }

            if (type is IArrayTypeSymbol { Rank: 1 } ||
                IsBuiltInSpanOrMemoryType(type))
            {
                return TryCreateReferenceBuiltInLengthFormula(receiverFormula, out formula);
            }

            formula = null!;
            return false;
        }

        public static bool TryCreateArrayDimensionLengthFormula(
            string variableName,
            IArrayTypeSymbol arrayType,
            int dimension,
            out SmtFormula formula)
        {
            if (dimension < 0 ||
                dimension >= arrayType.Rank)
            {
                formula = null!;
                return false;
            }

            return TryCreateReferenceArrayDimensionLengthFormula(
                new SmtVariable(variableName, SmtValueKind.Reference),
                dimension,
                out formula);
        }

        public static bool TryCreateArrayDimensionLengthFormulaForReference(
            SmtFormula receiverFormula,
            IArrayTypeSymbol arrayType,
            int dimension,
            out SmtFormula formula)
        {
            if (dimension < 0 ||
                dimension >= arrayType.Rank)
            {
                formula = null!;
                return false;
            }

            return TryCreateReferenceArrayDimensionLengthFormula(receiverFormula, dimension, out formula);
        }

        public static bool IsBuiltInSpanOrMemoryType(ITypeSymbol? typeSymbol)
        {
            return SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(typeSymbol);
        }

        public static ITypeSymbol? GetTrackedSymbolType(ISymbol symbol)
        {
            return symbol switch
            {
                ILocalSymbol localSymbol => localSymbol.Type,
                IParameterSymbol parameterSymbol => parameterSymbol.Type,
                _ => null
            };
        }

        public static bool TryGetDirectLocalOrParameterSymbol(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ISymbol symbol)
        {
            var candidate = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol?.OriginalDefinition;
            if (candidate is ILocalSymbol or IParameterSymbol)
            {
                symbol = candidate;
                return true;
            }

            symbol = null!;
            return false;
        }

        public static bool TryCreateSymbolVariableFormula(
            string variableName,
            ITypeSymbol? type,
            Func<ITypeSymbol, bool> isIntegralType,
            Func<ITypeSymbol, bool> isReferenceLikeType,
            out SmtFormula formula)
        {
            if (type == null)
            {
                formula = null!;
                return false;
            }

            if (type.SpecialType == SpecialType.System_Boolean)
            {
                formula = new SmtVariable(variableName, SmtValueKind.Bool);
                return true;
            }

            if (isIntegralType(type))
            {
                formula = new SmtVariable(variableName, SmtValueKind.Int);
                return true;
            }

            if (isReferenceLikeType(type))
            {
                formula = new SmtVariable(variableName, SmtValueKind.Reference);
                return true;
            }

            formula = null!;
            return false;
        }

        public static bool TryGetValueKind(
            ITypeSymbol type,
            Func<ITypeSymbol, bool> isIntegralType,
            Func<ITypeSymbol, bool> isReferenceLikeType,
            out SmtValueKind kind)
        {
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                kind = SmtValueKind.Bool;
                return true;
            }

            if (isIntegralType(type))
            {
                kind = SmtValueKind.Int;
                return true;
            }

            if (isReferenceLikeType(type))
            {
                kind = SmtValueKind.Reference;
                return true;
            }

            kind = default;
            return false;
        }

        public static bool IsSupportedSmtIntegralOrEnumType(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            if (typeSymbol.SpecialType is
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64)
            {
                return true;
            }

            return typeSymbol is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlyingType } &&
                IsSupportedSmtIntegralOrEnumType(underlyingType);
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
