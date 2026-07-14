namespace SharpProof.ProofCore.Smt;

internal delegate bool SmtConcreteBooleanEvaluator(
    SmtFormula formula,
    ConcreteFactContext facts,
    out bool value);

internal static class SmtBooleanReferenceFactCollector
{
    internal static bool TryCollectBooleanFacts(
        IReadOnlyList<SmtFormula> conditions,
        ConcreteFactContext facts,
        SmtConcreteBooleanEvaluator evaluateBoolean)
    {
        var iterationLimit = Math.Max(1, conditions.Count * 4);
        var changed = false;
        do
        {
            changed = false;
            foreach (var condition in conditions)
                if (!TryCollectBooleanFacts(condition, facts, evaluateBoolean, ref changed))
                    return false;

            iterationLimit--;
        } while (changed && iterationLimit > 0);

        return true;
    }

    internal static SmtConcreteFactPreparationStatus TryCollectReferenceFacts(
        IReadOnlyList<SmtFormula> conditions,
        ConcreteFactContext facts,
        SmtConcreteBooleanEvaluator evaluateBoolean)
    {
        var iterationLimit = Math.Max(1, conditions.Count * 4);
        var changed = false;
        do
        {
            changed = false;
            foreach (var condition in conditions)
            {
                var status = TryCollectReferenceFacts(condition, facts, evaluateBoolean, ref changed);
                if (status != SmtConcreteFactPreparationStatus.Ready) return status;
            }

            iterationLimit--;
        } while (changed && iterationLimit > 0);

        return SmtConcreteFactPreparationStatus.Ready;
    }

    internal static bool TryEvaluateReferenceNull(
        SmtFormula formula,
        ConcreteFactContext facts,
        SmtConcreteBooleanEvaluator evaluateBoolean,
        out bool isNull)
    {
        if (formula is SmtNullConstant)
        {
            isNull = true;
            return true;
        }

        if (facts.ReferenceNullEqualities.TryGetValue(formula, out isNull)) return true;

        if (formula is SmtConditionalFormula { Kind: SmtValueKind.Reference } conditionalFormula &&
            evaluateBoolean(conditionalFormula.Condition, facts, out var selectedBranch))
            return TryEvaluateReferenceNull(
                selectedBranch ? conditionalFormula.WhenTrue : conditionalFormula.WhenFalse,
                facts,
                evaluateBoolean,
                out isNull);

        isNull = false;
        return false;
    }

    private static bool TryCollectBooleanFacts(
        SmtFormula formula,
        ConcreteFactContext facts,
        SmtConcreteBooleanEvaluator evaluateBoolean,
        ref bool changed)
    {
        if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
            return TryCollectBooleanFacts(andFormula.Left, facts, evaluateBoolean, ref changed) &&
                   TryCollectBooleanFacts(andFormula.Right, facts, evaluateBoolean, ref changed);

        if (formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } notFormula)
            return CanCacheBooleanFact(notFormula.Operand)
                ? TryAddBooleanEquality(facts, notFormula.Operand, false, ref changed)
                : true;

        if (formula is SmtBinaryFormula
            {
                Operator: SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual
            } equalityFormula &&
            equalityFormula.Left.Kind == SmtValueKind.Bool &&
            equalityFormula.Right.Kind == SmtValueKind.Bool)
        {
            if (evaluateBoolean(equalityFormula.Left, facts, out var leftValue))
            {
                var expectedRight = equalityFormula.Operator == SmtBinaryOperator.Equal
                    ? leftValue
                    : !leftValue;
                return TryAddBooleanEquality(facts, equalityFormula.Right, expectedRight, ref changed);
            }

            if (evaluateBoolean(equalityFormula.Right, facts, out var rightValue))
            {
                var expectedLeft = equalityFormula.Operator == SmtBinaryOperator.Equal
                    ? rightValue
                    : !rightValue;
                return TryAddBooleanEquality(facts, equalityFormula.Left, expectedLeft, ref changed);
            }
        }

        if (formula.Kind == SmtValueKind.Bool &&
            CanCacheBooleanFact(formula))
            return TryAddBooleanEquality(facts, formula, true, ref changed);

        return true;
    }

    private static bool TryAddBooleanEquality(
        ConcreteFactContext facts,
        SmtFormula formula,
        bool value,
        ref bool changed)
    {
        if (formula.Kind != SmtValueKind.Bool ||
            !CanCacheBooleanFact(formula))
            return true;

        if (facts.BooleanEqualities.TryGetValue(formula, out var existing)) return existing == value;

        facts.BooleanEqualities.Add(formula, value);
        changed = true;
        return true;
    }

    private static bool CanCacheBooleanFact(SmtFormula formula)
    {
        if (formula is SmtVariable { Kind: SmtValueKind.Bool }) return true;

        if (formula is SmtRuntimeTypeTestFormula) return true;

        if (formula is not SmtBinaryFormula binaryFormula) return false;

        if (binaryFormula.Operator is SmtBinaryOperator.And or SmtBinaryOperator.Or) return false;

        if (binaryFormula.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual &&
            binaryFormula.Left.Kind == SmtValueKind.Bool &&
            binaryFormula.Right.Kind == SmtValueKind.Bool)
            return false;

        return !SmtFormulaTraversal.Contains(
            binaryFormula,
            static candidate => candidate is SmtRegexMatchFormula or
                SmtStringContainsFormula or
                SmtStringStartsWithFormula or
                SmtStringEndsWithFormula);
    }

    private static SmtConcreteFactPreparationStatus TryCollectReferenceFacts(
        SmtFormula formula,
        ConcreteFactContext facts,
        SmtConcreteBooleanEvaluator evaluateBoolean,
        ref bool changed)
    {
        if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
        {
            var leftStatus = TryCollectReferenceFacts(andFormula.Left, facts, evaluateBoolean, ref changed);
            if (leftStatus != SmtConcreteFactPreparationStatus.Ready) return leftStatus;

            return TryCollectReferenceFacts(andFormula.Right, facts, evaluateBoolean, ref changed);
        }

        if (formula is not SmtBinaryFormula
            {
                Operator: SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual
            } binaryFormula ||
            binaryFormula.Left.Kind != SmtValueKind.Reference ||
            binaryFormula.Right.Kind != SmtValueKind.Reference)
            return SmtConcreteFactPreparationStatus.Ready;

        var isEquality = binaryFormula.Operator == SmtBinaryOperator.Equal;
        if (EqualityComparer<SmtFormula>.Default.Equals(binaryFormula.Left, binaryFormula.Right))
            return isEquality
                ? SmtConcreteFactPreparationStatus.Ready
                : SmtConcreteFactPreparationStatus.Unsatisfiable;

        if (binaryFormula.Left is SmtNullConstant)
            return TryAddReferenceNullEquality(facts, binaryFormula.Right, isEquality, ref changed)
                ? SmtConcreteFactPreparationStatus.Ready
                : SmtConcreteFactPreparationStatus.Unsatisfiable;

        if (binaryFormula.Right is SmtNullConstant)
            return TryAddReferenceNullEquality(facts, binaryFormula.Left, isEquality, ref changed)
                ? SmtConcreteFactPreparationStatus.Ready
                : SmtConcreteFactPreparationStatus.Unsatisfiable;

        var leftKnown = TryEvaluateReferenceNull(binaryFormula.Left, facts, evaluateBoolean, out var leftIsNull);
        var rightKnown = TryEvaluateReferenceNull(binaryFormula.Right, facts, evaluateBoolean, out var rightIsNull);
        if (leftKnown && rightKnown && (leftIsNull || rightIsNull))
        {
            var equal = leftIsNull && rightIsNull;
            var matches = binaryFormula.Operator == SmtBinaryOperator.Equal ? equal : !equal;
            return matches
                ? SmtConcreteFactPreparationStatus.Ready
                : SmtConcreteFactPreparationStatus.Unsatisfiable;
        }

        if (!isEquality) return SmtConcreteFactPreparationStatus.Ready;

        if (leftKnown)
            return TryAddReferenceNullEquality(facts, binaryFormula.Right, leftIsNull, ref changed)
                ? SmtConcreteFactPreparationStatus.Ready
                : SmtConcreteFactPreparationStatus.Unsatisfiable;

        if (rightKnown)
            return TryAddReferenceNullEquality(facts, binaryFormula.Left, rightIsNull, ref changed)
                ? SmtConcreteFactPreparationStatus.Ready
                : SmtConcreteFactPreparationStatus.Unsatisfiable;

        return SmtConcreteFactPreparationStatus.Ready;
    }

    private static bool TryAddReferenceNullEquality(
        ConcreteFactContext facts,
        SmtFormula formula,
        bool isNull,
        ref bool changed)
    {
        if (formula.Kind != SmtValueKind.Reference) return true;

        if (formula is SmtNullConstant) return isNull;

        if (facts.ReferenceNullEqualities.TryGetValue(formula, out var existing)) return existing == isNull;

        facts.ReferenceNullEqualities.Add(formula, isNull);
        changed = true;
        return true;
    }
}

internal sealed class ConcreteFactContext
{
    internal Dictionary<SmtFormula, string> StringEqualities { get; } = new();

    internal Dictionary<SmtFormula, long> IntegerEqualities { get; } = new();

    internal Dictionary<SmtFormula, IntegerBounds> IntegerBounds { get; } = new();

    internal Dictionary<SmtFormula, bool> BooleanEqualities { get; } = new();

    internal Dictionary<SmtFormula, bool> ReferenceNullEqualities { get; } = new();
}

internal struct IntegerBounds
{
    internal long? Lower;

    internal long? Upper;

    internal bool ExcludesZero;

    internal bool IsUnsatisfiable =>
        (Lower.HasValue &&
         Upper.HasValue &&
         Lower.Value > Upper.Value) ||
        (ExcludesZero &&
         Lower.HasValue &&
         Upper.HasValue &&
         Lower.Value == 0 &&
         Upper.Value == 0);
}
