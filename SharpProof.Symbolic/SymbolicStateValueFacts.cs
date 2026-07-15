using Microsoft.CodeAnalysis;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Symbolic;

internal static class SymbolicStateValueFacts
{
    internal const string ImplicitThisVariableName = "this";

    internal static SymbolicState RemoveReferences(SymbolicState state, ISymbol symbol)
    {
        var symbolName = SymbolicFactFactory.GetSmtVariableName(symbol.OriginalDefinition);
        return RemoveReferences(state, symbolName);
    }

    internal static SymbolicState RemoveReferences(SymbolicState state, string symbolName)
    {
        return SymbolicIrReferenceScanner.RemoveVariableReferences(state, symbolName);
    }

    internal static bool TryGetCurrentValue(
        SymbolicState state,
        ISymbol symbol,
        out SymbolicTerm valueTerm)
    {
        valueTerm = null!;
        var symbolName = SymbolicFactFactory.GetSmtVariableName(symbol.OriginalDefinition);
        for (var index = state.PathConditions.Length - 1; index >= 0; index--)
            if (TryGetEqualityValue(state.PathConditions[index], symbolName, out valueTerm))
                return true;

        for (var index = state.Facts.Length - 1; index >= 0; index--)
            if (TryGetEqualityValue(state.Facts[index], symbolName, out valueTerm))
                return true;

        return false;
    }

    internal static bool IsKnownNonNullReference(SymbolicState state, ISymbol symbol)
    {
        return TryGetKnownReferenceNullState(state, symbol, out var isNull) && !isNull;
    }

    internal static bool IsKnownNullReference(SymbolicState state, ISymbol symbol)
    {
        return TryGetKnownReferenceNullState(state, symbol, out var isNull) && isNull;
    }

    internal static bool IsKnownNullReference(SymbolicState state, SymbolicTerm reference)
    {
        return reference is SymbolicVariableTerm { ValueKind: SmtValueKind.Reference } variable &&
               TryGetKnownBooleanState(state, variable.Name, TryGetReferenceNullFactState, out var isNull) &&
               isNull;
    }

    internal static bool IsKnownNullableHasValue(SymbolicState state, ISymbol symbol)
    {
        return TryGetKnownNullableHasValueState(state, symbol, out var hasValue) && hasValue;
    }

    internal static bool IsKnownNullableNoValue(SymbolicState state, ISymbol symbol)
    {
        return TryGetKnownNullableHasValueState(state, symbol, out var hasValue) && !hasValue;
    }

    private static bool TryGetKnownReferenceNullState(
        SymbolicState state,
        ISymbol symbol,
        out bool isNull)
    {
        return TryGetKnownBooleanState(state, symbol, TryGetReferenceNullFactState, out isNull);
    }

    private static bool TryGetReferenceNullFactState(
        SymbolicFact fact,
        string symbolName,
        out bool isNull)
    {
        isNull = false;
        if (fact.Atom is not SymbolicRelationAtom relation) return false;

        if (relation.Left is SymbolicVariableTerm { ValueKind: SmtValueKind.Reference } leftVariable &&
            string.Equals(leftVariable.Name, symbolName, StringComparison.Ordinal) &&
            relation.Right is SymbolicNullTerm &&
            relation.Operator is SymbolicRelationOperator.Equal or SymbolicRelationOperator.NotEqual)
        {
            isNull = (relation.Operator == SymbolicRelationOperator.Equal) == fact.Polarity;
            return true;
        }

        if (relation.Right is SymbolicVariableTerm { ValueKind: SmtValueKind.Reference } rightVariable &&
            string.Equals(rightVariable.Name, symbolName, StringComparison.Ordinal) &&
            relation.Left is SymbolicNullTerm &&
            relation.Operator is SymbolicRelationOperator.Equal or SymbolicRelationOperator.NotEqual)
        {
            isNull = (relation.Operator == SymbolicRelationOperator.Equal) == fact.Polarity;
            return true;
        }

        return false;
    }

    private static bool TryGetKnownNullableHasValueState(
        SymbolicState state,
        ISymbol symbol,
        out bool hasValue)
    {
        return TryGetKnownBooleanState(state, symbol, TryGetNullableHasValueFactState, out hasValue);
    }

    private static bool TryGetKnownBooleanState(
        SymbolicState state,
        ISymbol symbol,
        TryGetBooleanFactState tryGetFactState,
        out bool value)
    {
        var symbolName = SymbolicFactFactory.GetSmtVariableName(symbol.OriginalDefinition);
        return TryGetKnownBooleanState(state, symbolName, tryGetFactState, out value);
    }

    private static bool TryGetKnownBooleanState(
        SymbolicState state,
        string symbolName,
        TryGetBooleanFactState tryGetFactState,
        out bool value)
    {
        value = false;
        for (var index = state.PathConditions.Length - 1; index >= 0; index--)
            if (TryGetKnownBooleanState(state.PathConditions[index], symbolName, tryGetFactState, out value))
                return true;

        for (var index = state.Facts.Length - 1; index >= 0; index--)
            if (tryGetFactState(state.Facts[index], symbolName, out value))
                return true;

        return false;
    }

    private static bool TryGetKnownBooleanState(
        SymbolicCondition condition,
        string symbolName,
        TryGetBooleanFactState tryGetFactState,
        out bool value)
    {
        switch (condition)
        {
            case SymbolicFactCondition factCondition:
                return tryGetFactState(factCondition.Fact, symbolName, out value);
            case SymbolicNotCondition notCondition
                when TryGetKnownBooleanState(
                    notCondition.Operand,
                    symbolName,
                    tryGetFactState,
                    out value):
                value = !value;
                return true;
            case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.And } andCondition:
                if (TryGetKnownBooleanState(
                        andCondition.Left,
                        symbolName,
                        tryGetFactState,
                        out value))
                    return true;

                return TryGetKnownBooleanState(
                    andCondition.Right,
                    symbolName,
                    tryGetFactState,
                    out value);
            case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or } orCondition
                when TryGetKnownBooleanState(
                         orCondition.Left,
                         symbolName,
                         tryGetFactState,
                         out var leftValue) &&
                     TryGetKnownBooleanState(
                         orCondition.Right,
                         symbolName,
                         tryGetFactState,
                         out var rightValue) &&
                     leftValue == rightValue:
                value = leftValue;
                return true;
            default:
                value = false;
                return false;
        }
    }

    private static bool TryGetNullableHasValueFactState(
        SymbolicFact fact,
        string symbolName,
        out bool hasValue)
    {
        hasValue = false;
        switch (fact.Atom)
        {
            case SymbolicTruthAtom { Condition: SymbolicNullableHasValueTerm nullableHasValue }
                when string.Equals(nullableHasValue.NullableName, symbolName, StringComparison.Ordinal):
                hasValue = fact.Polarity;
                return true;
            case SymbolicRelationAtom
            {
                Operator: SymbolicRelationOperator.Equal,
                Left: SymbolicNullableHasValueTerm leftNullableHasValue,
                Right: SymbolicBooleanConstantTerm rightBoolean
            } when string.Equals(leftNullableHasValue.NullableName, symbolName, StringComparison.Ordinal):
                hasValue = rightBoolean.Value == fact.Polarity;
                return true;
            case SymbolicRelationAtom
            {
                Operator: SymbolicRelationOperator.Equal,
                Left: SymbolicBooleanConstantTerm leftBoolean,
                Right: SymbolicNullableHasValueTerm rightNullableHasValue
            } when string.Equals(rightNullableHasValue.NullableName, symbolName, StringComparison.Ordinal):
                hasValue = leftBoolean.Value == fact.Polarity;
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetEqualityValue(
        SymbolicCondition condition,
        string symbolName,
        out SymbolicTerm valueTerm)
    {
        valueTerm = null!;
        return condition is SymbolicFactCondition factCondition &&
               TryGetEqualityValue(factCondition.Fact, symbolName, out valueTerm);
    }

    private static bool TryGetEqualityValue(
        SymbolicFact fact,
        string symbolName,
        out SymbolicTerm valueTerm)
    {
        valueTerm = null!;
        if (!fact.Polarity ||
            fact.Atom is not SymbolicRelationAtom
            {
                Operator: SymbolicRelationOperator.Equal,
                Left: var left,
                Right: var right
            })
            return false;

        if (left is SymbolicVariableTerm { ValueKind: SmtValueKind.Int } leftVariable &&
            string.Equals(leftVariable.Name, symbolName, StringComparison.Ordinal) &&
            right.Kind == SmtValueKind.Int)
        {
            valueTerm = right;
            return true;
        }

        if (right is SymbolicVariableTerm { ValueKind: SmtValueKind.Int } rightVariable &&
            string.Equals(rightVariable.Name, symbolName, StringComparison.Ordinal) &&
            left.Kind == SmtValueKind.Int)
        {
            valueTerm = left;
            return true;
        }

        return false;
    }

    private delegate bool TryGetBooleanFactState(SymbolicFact fact, string symbolName, out bool value);
}
