using System.Numerics;
using System.Text.RegularExpressions;
using ConcreteFactContext = SharpProof.ProofCore.Smt.SmtSyntacticClassifier.SyntacticFactSet;

namespace SharpProof.ProofCore.Smt;

internal enum SmtConcreteFactPreparationStatus
{
    Ready,
    Unsatisfiable,
    Unknown
}

internal sealed class SmtConcreteFactPreprocessor
{
    private readonly SmtRegexValidator _regexValidator = new();

    internal int RegexValidationCacheCount => _regexValidator.CacheCount;

    internal SmtConcreteFactPreparationStatus Prepare(
        SmtFormula[] conditions,
        out SmtFormula[] preparedConditions)
    {
        if (!SmtFormulaNormalizer.TryNormalizeInitial(
                conditions,
                out var normalizedConditions,
                out var changed))
        {
            preparedConditions = Array.Empty<SmtFormula>();
            return SmtConcreteFactPreparationStatus.Unsatisfiable;
        }

        var factConditions = normalizedConditions.SelectMany(SmtFormulaTraversal.EnumerateConjuncts).ToArray();
        var facts = SmtSyntacticClassifier.SyntacticFactSet.Create(factConditions, out var hasContradiction);
        if (hasContradiction)
        {
            preparedConditions = Array.Empty<SmtFormula>();
            return ValidateContradictoryConditions(normalizedConditions, facts);
        }

        var conditionalStatus = SmtConditionalFactSimplifier.Simplify(
            normalizedConditions,
            facts,
            ref changed);
        if (conditionalStatus != SmtConcreteFactPreparationStatus.Ready)
        {
            preparedConditions = Array.Empty<SmtFormula>();
            return conditionalStatus;
        }

        factConditions = normalizedConditions.SelectMany(SmtFormulaTraversal.EnumerateConjuncts).ToArray();
        facts = SmtSyntacticClassifier.SyntacticFactSet.Create(factConditions, out hasContradiction);
        if (hasContradiction)
        {
            preparedConditions = Array.Empty<SmtFormula>();
            return ValidateContradictoryConditions(normalizedConditions, facts);
        }

        foreach (var condition in normalizedConditions)
        {
            var integerStatus = ValidateIntegerTermSafety(condition, facts);
            if (integerStatus != SmtConcreteFactPreparationStatus.Ready)
            {
                preparedConditions = Array.Empty<SmtFormula>();
                return integerStatus;
            }
        }

        var stringLengthEqualities = new Dictionary<SmtFormula, long>();
        foreach (var condition in normalizedConditions)
            if (!TryCollectStringLengthEqualities(condition, stringLengthEqualities))
            {
                preparedConditions = Array.Empty<SmtFormula>();
                return SmtConcreteFactPreparationStatus.Unsatisfiable;
            }

        foreach (var condition in normalizedConditions)
        {
            var status = TryInferStringEqualitiesFromLengthConstrainedPredicates(
                condition,
                stringLengthEqualities,
                facts);
            if (status != SmtConcreteFactPreparationStatus.Ready)
            {
                preparedConditions = Array.Empty<SmtFormula>();
                return status;
            }
        }

        var stringShapeStatus = TryApplyStringShapeFacts(
            normalizedConditions,
            stringLengthEqualities,
            facts);
        if (stringShapeStatus != SmtConcreteFactPreparationStatus.Ready)
        {
            preparedConditions = Array.Empty<SmtFormula>();
            return stringShapeStatus;
        }

        var builder = new List<SmtFormula>(normalizedConditions.Count);
        foreach (var condition in normalizedConditions)
        {
            var status = SimplifyConcreteFacts(
                condition,
                facts,
                out var preparedCondition,
                out var conditionChanged);
            if (status != SmtConcreteFactPreparationStatus.Ready)
            {
                preparedConditions = Array.Empty<SmtFormula>();
                return status;
            }

            changed |= conditionChanged;
            if (!SmtFormulaNormalizer.TryClassifyCondition(preparedCondition, out var shouldKeep))
            {
                preparedConditions = Array.Empty<SmtFormula>();
                return SmtConcreteFactPreparationStatus.Unsatisfiable;
            }

            if (!shouldKeep)
            {
                changed = true;
                continue;
            }

            builder.Add(preparedCondition);
        }

        preparedConditions = changed ? builder.ToArray() : conditions;
        return SmtConcreteFactPreparationStatus.Ready;
    }

    private static SmtConcreteFactPreparationStatus ValidateContradictoryConditions(
        IEnumerable<SmtFormula> conditions,
        ConcreteFactContext facts)
    {
        var safeConditions = new List<SmtFormula>();
        var unsafeStatus = SmtConcreteFactPreparationStatus.Ready;
        foreach (var condition in conditions)
        {
            var status = ValidateIntegerTermSafety(condition, facts);
            if (status == SmtConcreteFactPreparationStatus.Ready)
                safeConditions.Add(condition);
            else if (status == SmtConcreteFactPreparationStatus.Unsatisfiable)
                return status;
            else
                unsafeStatus = status;
        }

        var safeFacts = safeConditions.SelectMany(SmtFormulaTraversal.EnumerateConjuncts);
        SmtSyntacticClassifier.SyntacticFactSet.Create(safeFacts, out var safeContradiction);
        return safeContradiction || unsafeStatus == SmtConcreteFactPreparationStatus.Ready
            ? SmtConcreteFactPreparationStatus.Unsatisfiable
            : unsafeStatus;
    }

    private static SmtConcreteFactPreparationStatus ValidateIntegerTermSafety(
        SmtFormula formula,
        ConcreteFactContext facts)
    {
        switch (formula)
        {
            case SmtUnaryFormula unaryFormula:
                return ValidateIntegerTermSafety(unaryFormula.Operand, facts);
            case SmtBinaryFormula binaryFormula:
                var leftStatus = ValidateIntegerTermSafety(binaryFormula.Left, facts);
                if (leftStatus != SmtConcreteFactPreparationStatus.Ready) return leftStatus;

                return ValidateIntegerTermSafety(binaryFormula.Right, facts);
            case SmtIntegerUnaryTerm integerUnaryTerm:
                return ValidateIntegerTermSafety(integerUnaryTerm.Operand, facts);
            case SmtIntegerBinaryTerm integerBinaryTerm:
                var integerLeftStatus = ValidateIntegerTermSafety(integerBinaryTerm.Left, facts);
                if (integerLeftStatus != SmtConcreteFactPreparationStatus.Ready) return integerLeftStatus;

                var integerRightStatus = ValidateIntegerTermSafety(integerBinaryTerm.Right, facts);
                if (integerRightStatus != SmtConcreteFactPreparationStatus.Ready) return integerRightStatus;

                if (integerBinaryTerm.Operator is not (SmtIntegerBinaryOperator.Divide
                    or SmtIntegerBinaryOperator.Remainder)) return SmtConcreteFactPreparationStatus.Ready;

                if (TryEvaluateInteger(integerBinaryTerm.Right, facts, out var denominator))
                    return denominator == 0
                        ? SmtConcreteFactPreparationStatus.Unknown
                        : SmtConcreteFactPreparationStatus.Ready;

                if (TryIntegerIntervalExcludesZero(integerBinaryTerm.Right, facts))
                    return SmtConcreteFactPreparationStatus.Ready;

                // Z3 assigns a totalized value to division and remainder by zero,
                // while C# throws. Only encode the operation when the path facts
                // prove that the divisor cannot be zero.
                return SmtConcreteFactPreparationStatus.Unknown;
            case SmtOpaqueIntegerBinaryTerm opaqueIntegerTerm:
                var opaqueLeftStatus = ValidateIntegerTermSafety(opaqueIntegerTerm.Left, facts);
                if (opaqueLeftStatus != SmtConcreteFactPreparationStatus.Ready) return opaqueLeftStatus;

                return ValidateIntegerTermSafety(opaqueIntegerTerm.Right, facts);
            case SmtStringLengthTerm stringLengthTerm:
                return ValidateIntegerTermSafety(stringLengthTerm.Value, facts);
            case SmtStringConcatTerm stringConcatTerm:
                var concatLeftStatus = ValidateIntegerTermSafety(stringConcatTerm.Left, facts);
                if (concatLeftStatus != SmtConcreteFactPreparationStatus.Ready) return concatLeftStatus;

                return ValidateIntegerTermSafety(stringConcatTerm.Right, facts);
            case SmtStringContainsFormula stringContainsFormula:
                var containsValueStatus = ValidateIntegerTermSafety(stringContainsFormula.Value, facts);
                if (containsValueStatus != SmtConcreteFactPreparationStatus.Ready) return containsValueStatus;

                return ValidateIntegerTermSafety(stringContainsFormula.Search, facts);
            case SmtStringStartsWithFormula stringStartsWithFormula:
                var startsWithValueStatus = ValidateIntegerTermSafety(stringStartsWithFormula.Value, facts);
                if (startsWithValueStatus != SmtConcreteFactPreparationStatus.Ready) return startsWithValueStatus;

                return ValidateIntegerTermSafety(stringStartsWithFormula.Prefix, facts);
            case SmtStringEndsWithFormula stringEndsWithFormula:
                var endsWithValueStatus = ValidateIntegerTermSafety(stringEndsWithFormula.Value, facts);
                if (endsWithValueStatus != SmtConcreteFactPreparationStatus.Ready) return endsWithValueStatus;

                return ValidateIntegerTermSafety(stringEndsWithFormula.Suffix, facts);
            case SmtRegexMatchFormula regexMatchFormula:
                return ValidateIntegerTermSafety(regexMatchFormula.Value, facts);
            case SmtRuntimeTypeTestFormula runtimeTypeTest:
                return ValidateIntegerTermSafety(runtimeTypeTest.Value, facts);
            case SmtConditionalFormula conditionalFormula:
                var conditionStatus = ValidateIntegerTermSafety(conditionalFormula.Condition, facts);
                if (conditionStatus != SmtConcreteFactPreparationStatus.Ready) return conditionStatus;

                if (TryEvaluateConcreteBoolean(conditionalFormula.Condition, facts, out var selectedBranch))
                    return ValidateIntegerTermSafety(
                        selectedBranch ? conditionalFormula.WhenTrue : conditionalFormula.WhenFalse,
                        facts);

                var trueStatus = ValidateIntegerTermSafety(conditionalFormula.WhenTrue, facts);
                if (trueStatus != SmtConcreteFactPreparationStatus.Ready) return trueStatus;

                return ValidateIntegerTermSafety(conditionalFormula.WhenFalse, facts);
            default:
                return SmtConcreteFactPreparationStatus.Ready;
        }
    }

    private static bool TryIntegerIntervalExcludesZero(SmtFormula formula, ConcreteFactContext facts)
    {
        if (TryGetIntegerInterval(formula, facts, out var lower, out var upper))
            return (lower.HasValue && lower.Value > 0) ||
                   (upper.HasValue && upper.Value < 0);

        return facts.IntegerIntervals.TryGetValue(formula, out var interval) &&
               interval.Excludes(0);
    }

    private static bool TryGetIntegerInterval(
        SmtFormula formula,
        ConcreteFactContext facts,
        out long? lower,
        out long? upper)
    {
        lower = null;
        upper = null;

        if (TryEvaluateInteger(formula, facts, out var concrete))
        {
            lower = concrete;
            upper = concrete;
            return true;
        }

        var foundInterval = false;
        if (facts.IntegerIntervals.TryGetValue(formula, out var interval))
        {
            lower = interval.LowerBound;
            upper = interval.UpperBound;
            foundInterval = lower.HasValue || upper.HasValue;
        }

        long? structuralLower = null;
        long? structuralUpper = null;
        var foundStructuralInterval = false;
        switch (formula)
        {
            case SmtStringLengthTerm stringLengthTerm:
                foundStructuralInterval = TryGetStringLengthInterval(
                    stringLengthTerm.Value,
                    facts,
                    out structuralLower,
                    out structuralUpper);
                break;
            case SmtIntegerUnaryTerm { Operator: SmtIntegerUnaryOperator.Negate } unaryTerm:
                if (!TryGetIntegerInterval(unaryTerm.Operand, facts, out var operandLower, out var operandUpper)) break;

                if (operandUpper.HasValue)
                {
                    if (!SmtIntegerArithmetic.TryNegate(operandUpper.Value, out var negatedUpper)) break;

                    structuralLower = negatedUpper;
                }

                if (operandLower.HasValue)
                {
                    if (!SmtIntegerArithmetic.TryNegate(operandLower.Value, out var negatedLower)) break;

                    structuralUpper = negatedLower;
                }

                foundStructuralInterval = true;
                break;
            case SmtIntegerBinaryTerm binaryTerm:
                foundStructuralInterval = TryGetIntegerBinaryInterval(
                    binaryTerm,
                    facts,
                    out structuralLower,
                    out structuralUpper);
                break;
        }

        if (foundStructuralInterval)
        {
            if (structuralLower.HasValue && (!lower.HasValue || structuralLower.Value > lower.Value))
                lower = structuralLower.Value;

            if (structuralUpper.HasValue && (!upper.HasValue || structuralUpper.Value < upper.Value))
                upper = structuralUpper.Value;

            foundInterval = foundInterval || structuralLower.HasValue || structuralUpper.HasValue;
        }

        return foundInterval;
    }

    private static bool TryGetStringLengthInterval(
        SmtFormula value,
        ConcreteFactContext facts,
        out long? lower,
        out long? upper)
    {
        lower = 0;
        upper = null;

        if (TryGetConcreteString(value, facts, out var concrete))
        {
            lower = concrete.Length;
            upper = concrete.Length;
            return true;
        }

        if (value is SmtStringConcatTerm concat)
        {
            if (!TryGetStringLengthInterval(concat.Left, facts, out var leftLower, out var leftUpper) ||
                !TryGetStringLengthInterval(concat.Right, facts, out var rightLower, out var rightUpper))
                return false;

            return TryCombineBounds(leftLower, rightLower, SmtIntegerArithmetic.TryAdd, out lower) &&
                   TryCombineBounds(leftUpper, rightUpper, SmtIntegerArithmetic.TryAdd, out upper);
        }

        return value.Kind == SmtValueKind.String;
    }

    private static bool TryGetIntegerBinaryInterval(
        SmtIntegerBinaryTerm term,
        ConcreteFactContext facts,
        out long? lower,
        out long? upper)
    {
        lower = null;
        upper = null;
        if (!TryGetIntegerInterval(term.Left, facts, out var leftLower, out var leftUpper) ||
            !TryGetIntegerInterval(term.Right, facts, out var rightLower, out var rightUpper))
            return false;

        switch (term.Operator)
        {
            case SmtIntegerBinaryOperator.Add:
                return TryCombineBounds(leftLower, rightLower, SmtIntegerArithmetic.TryAdd, out lower) &&
                       TryCombineBounds(leftUpper, rightUpper, SmtIntegerArithmetic.TryAdd, out upper);
            case SmtIntegerBinaryOperator.Subtract:
                return TryCombineBounds(leftLower, rightUpper, SmtIntegerArithmetic.TrySubtract, out lower) &&
                       TryCombineBounds(leftUpper, rightLower, SmtIntegerArithmetic.TrySubtract, out upper);
            case SmtIntegerBinaryOperator.Multiply:
                if (TryEvaluateInteger(term.Left, facts, out var leftConstant))
                    return TryScaleBounds(rightLower, rightUpper, leftConstant, out lower, out upper);

                if (TryEvaluateInteger(term.Right, facts, out var rightConstant))
                    return TryScaleBounds(leftLower, leftUpper, rightConstant, out lower, out upper);

                return false;
            case SmtIntegerBinaryOperator.Remainder:
                if (!HasNonNegativeDividendAndPositiveDivisor(leftLower, rightLower)) return false;

                lower = 0;
                if (rightUpper.HasValue &&
                    SmtIntegerArithmetic.TryAdd(rightUpper.Value, -1, out var remainderUpper))
                    upper = remainderUpper;

                return true;
            default:
                return false;
        }
    }

    private static bool HasNonNegativeDividendAndPositiveDivisor(long? dividendLower, long? divisorLower)
    {
        return dividendLower.HasValue &&
               dividendLower.Value >= 0 &&
               divisorLower.HasValue &&
               divisorLower.Value > 0;
    }

    private static bool TryCombineBounds(
        long? left,
        long? right,
        CheckedLongBinaryOperation operation,
        out long? value)
    {
        value = null;
        if (!left.HasValue || !right.HasValue) return true;

        if (!operation(left.Value, right.Value, out var combined)) return false;

        value = combined;
        return true;
    }

    private static bool TryScaleBounds(
        long? lower,
        long? upper,
        long multiplier,
        out long? scaledLower,
        out long? scaledUpper)
    {
        scaledUpper = null;
        if (multiplier == 0)
        {
            scaledLower = 0;
            scaledUpper = 0;
            return true;
        }

        if (multiplier > 0)
            return TryScaleBound(lower, multiplier, out scaledLower) &&
                   TryScaleBound(upper, multiplier, out scaledUpper);

        return TryScaleBound(upper, multiplier, out scaledLower) &&
               TryScaleBound(lower, multiplier, out scaledUpper);
    }

    private static bool TryScaleBound(long? bound, long multiplier, out long? scaled)
    {
        scaled = null;
        if (!bound.HasValue) return true;

        if (!SmtIntegerArithmetic.TryMultiply(bound.Value, multiplier, out var scaledValue)) return false;

        scaled = scaledValue;
        return true;
    }

    private static bool TryEvaluateConcreteBoolean(
        SmtFormula formula,
        ConcreteFactContext facts,
        out bool value)
    {
        switch (formula)
        {
            case SmtBooleanConstant booleanConstant:
                value = booleanConstant.Value;
                return true;
            case SmtUnaryFormula { Operator: SmtUnaryOperator.Not } unaryFormula
                when TryEvaluateConcreteBoolean(unaryFormula.Operand, facts, out var operand):
                value = !operand;
                return true;
            case SmtBinaryFormula binaryFormula:
                return TryEvaluateConcreteBinaryBoolean(binaryFormula, facts, out value);
            case SmtStringContainsFormula or SmtStringStartsWithFormula or SmtStringEndsWithFormula:
                if (TryGetPositiveStringPredicateFact(formula, out var predicate) &&
                    TryGetConcreteString(predicate.Value, facts, out var concreteValue) &&
                    TryGetConcreteString(predicate.Argument, facts, out var concreteArgument))
                {
                    value = EvaluateStringPredicate(predicate.Kind, concreteValue, concreteArgument);
                    return true;
                }

                break;
            case SmtConditionalFormula { Kind: SmtValueKind.Bool } conditionalFormula:
                if (TryEvaluateConcreteBoolean(conditionalFormula.Condition, facts, out var selectedBranch))
                    return TryEvaluateConcreteBoolean(
                        selectedBranch ? conditionalFormula.WhenTrue : conditionalFormula.WhenFalse,
                        facts,
                        out value);

                break;
        }

        if (CanCacheBooleanFact(formula) && facts.BooleanEqualities.TryGetValue(formula, out value)) return true;
        value = false;
        return false;
    }

    private static bool CanCacheBooleanFact(SmtFormula formula)
    {
        if (formula is SmtVariable { Kind: SmtValueKind.Bool } or SmtRuntimeTypeTestFormula) return true;
        if (formula is not SmtBinaryFormula binary ||
            binary.Operator is SmtBinaryOperator.And or SmtBinaryOperator.Or)
            return false;
        if (binary.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual &&
            binary.Left.Kind == SmtValueKind.Bool && binary.Right.Kind == SmtValueKind.Bool)
            return false;

        return !SmtFormulaTraversal.Contains(
            binary,
            static candidate => candidate is SmtRegexMatchFormula or SmtStringContainsFormula or
                SmtStringStartsWithFormula or SmtStringEndsWithFormula);
    }

    private static bool ShouldPreserveSourceFact(SmtFormula formula)
    {
        if (formula is not SmtBinaryFormula binaryFormula ||
            !SmtComparisonOperatorFacts.IsComparison(binaryFormula.Operator))
            return false;

        if (IsLiteral(binaryFormula.Left) && IsLiteral(binaryFormula.Right)) return false;

        return binaryFormula.Left.Kind is SmtValueKind.Int or SmtValueKind.String or SmtValueKind.Reference ||
               binaryFormula.Right.Kind is SmtValueKind.Int or SmtValueKind.String or SmtValueKind.Reference;
    }

    private static bool IsLiteral(SmtFormula formula)
    {
        return formula is SmtBooleanConstant or
            SmtIntegerConstant or
            SmtStringConstant or
            SmtNullConstant;
    }

    private static bool TryEvaluateConcreteBinaryBoolean(
        SmtBinaryFormula formula,
        ConcreteFactContext facts,
        out bool value)
    {
        if (formula.Operator == SmtBinaryOperator.And)
        {
            if (TryEvaluateConcreteBoolean(formula.Left, facts, out var left))
            {
                if (!left)
                {
                    value = false;
                    return true;
                }

                if (TryEvaluateConcreteBoolean(formula.Right, facts, out var right))
                {
                    value = right;
                    return true;
                }
            }

            value = false;
            return false;
        }

        if (formula.Operator == SmtBinaryOperator.Or)
        {
            if (TryEvaluateConcreteBoolean(formula.Left, facts, out var left))
            {
                if (left)
                {
                    value = true;
                    return true;
                }

                if (TryEvaluateConcreteBoolean(formula.Right, facts, out var right))
                {
                    value = right;
                    return true;
                }
            }

            value = false;
            return false;
        }

        if (TryEvaluateStringLengthComparison(formula, facts, out value)) return true;

        if (formula.Left.Kind == SmtValueKind.Int &&
            formula.Right.Kind == SmtValueKind.Int &&
            TryEvaluateIntegerIntervalComparison(formula, facts, out value))
            return true;

        if (formula.Left.Kind == SmtValueKind.Int &&
            formula.Right.Kind == SmtValueKind.Int &&
            TryEvaluateInteger(formula.Left, facts, out var leftInteger) &&
            TryEvaluateInteger(formula.Right, facts, out var rightInteger))
            return SmtIntegerComparisonFacts.TryEvaluate(
                formula.Operator,
                leftInteger,
                rightInteger,
                out value);

        if (formula.Left.Kind == SmtValueKind.String &&
            formula.Right.Kind == SmtValueKind.String &&
            TryGetConcreteString(formula.Left, facts, out var leftString) &&
            TryGetConcreteString(formula.Right, facts, out var rightString))
        {
            value = CompareEquality(formula.Operator, string.Equals(leftString, rightString, StringComparison.Ordinal));
            return formula.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual;
        }

        if (formula.Left.Kind == SmtValueKind.Reference &&
            formula.Right.Kind == SmtValueKind.Reference &&
            formula.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual)
        {
            if (facts.TryGetKnownReferenceNullState(formula.Left, out var leftIsNull) &&
                facts.TryGetKnownReferenceNullState(formula.Right, out var rightIsNull) &&
                (leftIsNull || rightIsNull))
            {
                value = CompareEquality(formula.Operator, leftIsNull && rightIsNull);
                return true;
            }

            if (EqualityComparer<SmtFormula>.Default.Equals(formula.Left, formula.Right))
            {
                value = formula.Operator == SmtBinaryOperator.Equal;
                return true;
            }
        }

        if (formula.Left.Kind == SmtValueKind.Bool &&
            formula.Right.Kind == SmtValueKind.Bool &&
            TryEvaluateConcreteBoolean(formula.Left, facts, out var leftBoolean) &&
            TryEvaluateConcreteBoolean(formula.Right, facts, out var rightBoolean))
        {
            value = CompareEquality(formula.Operator, leftBoolean == rightBoolean);
            return formula.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual;
        }

        if (formula.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual &&
            ((formula.Left is SmtNullConstant && formula.Right is SmtNullConstant) ||
             EqualityComparer<SmtFormula>.Default.Equals(formula.Left, formula.Right)))
        {
            value = formula.Operator == SmtBinaryOperator.Equal;
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryEvaluateStringLengthComparison(
        SmtBinaryFormula formula,
        ConcreteFactContext facts,
        out bool value)
    {
        if (!TryNormalizeStringLengthComparison(formula, out var stringValue, out var op, out var constant))
        {
            value = false;
            return false;
        }

        if (TryGetConcreteString(stringValue, facts, out var concreteString))
            return SmtIntegerComparisonFacts.TryEvaluate(op, concreteString.Length, constant, out value);

        bool? result = op switch
        {
            SmtBinaryOperator.Equal when constant < 0 => false,
            SmtBinaryOperator.NotEqual when constant < 0 => true,
            SmtBinaryOperator.LessThan when constant <= 0 => false,
            SmtBinaryOperator.LessThanOrEqual when constant < 0 => false,
            SmtBinaryOperator.GreaterThan when constant < 0 => true,
            SmtBinaryOperator.GreaterThanOrEqual when constant <= 0 => true,
            _ => null
        };
        value = result.GetValueOrDefault();
        return result.HasValue;
    }

    private static bool TryEvaluateIntegerIntervalComparison(
        SmtBinaryFormula formula,
        ConcreteFactContext facts,
        out bool value)
    {
        value = false;
        if (!SmtComparisonOperatorFacts.IsComparison(formula.Operator)) return false;

        if (TryEvaluateRemainderRangeComparison(formula, facts, out value)) return true;

        if (!TryGetIntegerInterval(formula.Left, facts, out var leftLower, out var leftUpper) ||
            !TryGetIntegerInterval(formula.Right, facts, out var rightLower, out var rightUpper))
            return false;

        return SmtIntegerComparisonFacts.TryEvaluateIntervals(
            formula.Operator,
            leftLower,
            leftUpper,
            rightLower,
            rightUpper,
            out value);
    }

    private static bool TryEvaluateRemainderRangeComparison(
        SmtBinaryFormula formula,
        ConcreteFactContext facts,
        out bool value)
    {
        if (formula.Left is SmtIntegerBinaryTerm leftRemainder &&
            TryEvaluateRemainderComparison(leftRemainder, formula.Operator, formula.Right, facts, out value))
            return true;

        if (formula.Right is SmtIntegerBinaryTerm rightRemainder &&
            TryEvaluateRemainderComparison(
                rightRemainder,
                SmtComparisonOperatorFacts.Reverse(formula.Operator),
                formula.Left,
                facts,
                out value))
            return true;

        value = false;
        return false;
    }

    private static bool TryEvaluateRemainderComparison(
        SmtIntegerBinaryTerm remainder,
        SmtBinaryOperator op,
        SmtFormula other,
        ConcreteFactContext facts,
        out bool value)
    {
        value = false;
        if (remainder.Operator != SmtIntegerBinaryOperator.Remainder ||
            !TryGetIntegerInterval(remainder.Left, facts, out var dividendLower, out _) ||
            !TryGetIntegerInterval(remainder.Right, facts, out var divisorLower, out _) ||
            !HasNonNegativeDividendAndPositiveDivisor(dividendLower, divisorLower) ||
            !EqualityComparer<SmtFormula>.Default.Equals(other, remainder.Right))
            return false;

        switch (op)
        {
            case SmtBinaryOperator.LessThan:
            case SmtBinaryOperator.LessThanOrEqual:
            case SmtBinaryOperator.NotEqual:
                value = true;
                return true;
            case SmtBinaryOperator.Equal:
            case SmtBinaryOperator.GreaterThan:
            case SmtBinaryOperator.GreaterThanOrEqual:
                value = false;
                return true;
            default:
                return false;
        }
    }

    private static bool TryNormalizeStringLengthComparison(
        SmtBinaryFormula formula,
        out SmtFormula stringValue,
        out SmtBinaryOperator op,
        out long constant)
    {
        if (formula.Left is SmtStringLengthTerm leftLength &&
            formula.Right is SmtIntegerConstant rightConstant)
        {
            stringValue = leftLength.Value;
            op = formula.Operator;
            constant = rightConstant.Value;
            return SmtComparisonOperatorFacts.IsComparison(op);
        }

        if (formula.Left is SmtIntegerConstant leftConstant &&
            formula.Right is SmtStringLengthTerm rightLength)
        {
            stringValue = rightLength.Value;
            op = SmtComparisonOperatorFacts.Reverse(formula.Operator);
            constant = leftConstant.Value;
            return SmtComparisonOperatorFacts.IsComparison(op);
        }

        stringValue = null!;
        op = default;
        constant = default;
        return false;
    }

    private static bool TryEvaluateInteger(
        SmtFormula formula,
        ConcreteFactContext facts,
        out long value)
    {
        if (formula is SmtIntegerConstant integerConstant)
        {
            value = integerConstant.Value;
            return true;
        }

        if (facts.IntegerIntervals.TryGetValue(formula, out var interval) && interval.ExactValue.HasValue)
        {
            value = interval.ExactValue.Value;
            return true;
        }

        switch (formula)
        {
            case SmtIntegerUnaryTerm { Operator: SmtIntegerUnaryOperator.Negate } unaryTerm
                when TryEvaluateInteger(unaryTerm.Operand, facts, out var operand):
                return SmtIntegerArithmetic.TryNegate(operand, out value);
            case SmtIntegerBinaryTerm binaryTerm:
                return TryEvaluateIntegerBinary(binaryTerm, facts, out value);
            case SmtConditionalFormula { Kind: SmtValueKind.Int } conditionalFormula
                when TryEvaluateConcreteBoolean(conditionalFormula.Condition, facts, out var selectedBranch):
                return TryEvaluateInteger(
                    selectedBranch ? conditionalFormula.WhenTrue : conditionalFormula.WhenFalse,
                    facts,
                    out value);
            default:
                value = default;
                return false;
        }
    }

    private static bool TryEvaluateIntegerBinary(
        SmtIntegerBinaryTerm term,
        ConcreteFactContext facts,
        out long value)
    {
        value = default;
        if (!TryEvaluateInteger(term.Left, facts, out var left) ||
            !TryEvaluateInteger(term.Right, facts, out var right))
            return false;

        return SmtIntegerArithmetic.TryEvaluateBinary(term.Operator, left, right, out value);
    }

    private static bool CompareEquality(SmtBinaryOperator op, bool equality)
    {
        return op switch
        {
            SmtBinaryOperator.Equal => equality,
            SmtBinaryOperator.NotEqual => !equality,
            _ => false
        };
    }

    private static bool TryAddStringEquality(
        ConcreteFactContext facts,
        SmtFormula formula,
        string value,
        ref bool changed)
    {
        if (facts.StringEqualities.TryGetValue(formula, out var existing))
            return string.Equals(existing, value, StringComparison.Ordinal);

        facts.StringEqualities.Add(formula, value);
        changed = true;
        return true;
    }

    private static bool TryAddStringEquality(
        ConcreteFactContext facts,
        SmtFormula formula,
        string value)
    {
        var changed = false;
        return TryAddStringEquality(facts, formula, value, ref changed);
    }

    private static bool TryCollectStringLengthEqualities(
        SmtFormula formula,
        Dictionary<SmtFormula, long> stringLengthEqualities)
    {
        if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
            return TryCollectStringLengthEqualities(andFormula.Left, stringLengthEqualities) &&
                   TryCollectStringLengthEqualities(andFormula.Right, stringLengthEqualities);

        if (!TryGetStringLengthEquality(formula, out var value, out var length)) return true;

        if (length < 0) return false;

        if (stringLengthEqualities.TryGetValue(value, out var existing)) return existing == length;

        stringLengthEqualities.Add(value, length);
        return true;
    }

    private static bool TryGetStringLengthEquality(
        SmtFormula formula,
        out SmtFormula value,
        out long length)
    {
        value = null!;
        length = default;
        if (formula is not SmtBinaryFormula { Operator: SmtBinaryOperator.Equal } equalFormula) return false;

        if (equalFormula.Left is SmtStringLengthTerm leftLength &&
            equalFormula.Right is SmtIntegerConstant rightConstant)
        {
            value = leftLength.Value;
            length = rightConstant.Value;
            return true;
        }

        if (equalFormula.Left is SmtIntegerConstant leftConstant &&
            equalFormula.Right is SmtStringLengthTerm rightLength)
        {
            value = rightLength.Value;
            length = leftConstant.Value;
            return true;
        }

        return false;
    }

    private static SmtConcreteFactPreparationStatus TryInferStringEqualitiesFromLengthConstrainedPredicates(
        SmtFormula formula,
        IReadOnlyDictionary<SmtFormula, long> stringLengthEqualities,
        ConcreteFactContext facts)
    {
        if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
        {
            var leftStatus = TryInferStringEqualitiesFromLengthConstrainedPredicates(
                andFormula.Left,
                stringLengthEqualities,
                facts);
            if (leftStatus != SmtConcreteFactPreparationStatus.Ready) return leftStatus;

            return TryInferStringEqualitiesFromLengthConstrainedPredicates(
                andFormula.Right,
                stringLengthEqualities,
                facts);
        }

        if (!TryGetPositiveStringPredicateFact(formula, out var predicate) ||
            !stringLengthEqualities.TryGetValue(predicate.Value, out var knownLength) ||
            !TryGetConcreteString(predicate.Argument, facts, out var concreteArgument))
            return SmtConcreteFactPreparationStatus.Ready;

        if (knownLength < concreteArgument.Length) return SmtConcreteFactPreparationStatus.Unsatisfiable;

        if (knownLength == concreteArgument.Length)
            if (!TryAddStringEquality(facts, predicate.Value, concreteArgument))
                return SmtConcreteFactPreparationStatus.Unsatisfiable;

        return SmtConcreteFactPreparationStatus.Ready;
    }

    private static SmtConcreteFactPreparationStatus TryApplyStringShapeFacts(
        IReadOnlyList<SmtFormula> conditions,
        IReadOnlyDictionary<SmtFormula, long> stringLengthEqualities,
        ConcreteFactContext facts)
    {
        var shapeFacts = new Dictionary<SmtFormula, StringShapeFact>();
        foreach (var condition in conditions)
        {
            var status = TryCollectStringShapeFacts(condition, facts, shapeFacts);
            if (status != SmtConcreteFactPreparationStatus.Ready) return status;
        }

        foreach (var entry in shapeFacts)
        {
            var value = entry.Key;
            var shape = entry.Value;
            long? exactLength = null;
            if (stringLengthEqualities.TryGetValue(value, out var knownLength))
                exactLength = knownLength;
            else if (TryGetConcreteString(value, facts, out var concreteValue)) exactLength = concreteValue.Length;

            if (exactLength.HasValue)
            {
                if (shape.MinLength > exactLength.Value) return SmtConcreteFactPreparationStatus.Unsatisfiable;

                if (!TryApplyExactLengthStringShape(value, exactLength.Value, shape, facts))
                    return SmtConcreteFactPreparationStatus.Unsatisfiable;
            }
        }

        return SmtConcreteFactPreparationStatus.Ready;
    }

    private static SmtConcreteFactPreparationStatus TryCollectStringShapeFacts(
        SmtFormula formula,
        ConcreteFactContext facts,
        Dictionary<SmtFormula, StringShapeFact> shapeFacts)
    {
        if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
        {
            var leftStatus = TryCollectStringShapeFacts(andFormula.Left, facts, shapeFacts);
            if (leftStatus != SmtConcreteFactPreparationStatus.Ready) return leftStatus;

            return TryCollectStringShapeFacts(andFormula.Right, facts, shapeFacts);
        }

        if (!TryGetPositiveStringPredicateFact(formula, out var predicate) ||
            !TryGetConcreteString(predicate.Argument, facts, out var argument))
            return SmtConcreteFactPreparationStatus.Ready;

        var shape = shapeFacts.TryGetValue(predicate.Value, out var existing)
            ? existing
            : default;

        var status = predicate.Kind switch
        {
            StringPredicateKind.Contains => shape.AddContains(argument),
            StringPredicateKind.StartsWith => shape.AddPrefix(argument),
            StringPredicateKind.EndsWith => shape.AddSuffix(argument),
            _ => SmtConcreteFactPreparationStatus.Ready
        };

        if (status != SmtConcreteFactPreparationStatus.Ready) return status;

        shapeFacts[predicate.Value] = shape;
        return SmtConcreteFactPreparationStatus.Ready;
    }

    private static bool TryApplyExactLengthStringShape(
        SmtFormula value,
        long exactLength,
        StringShapeFact shape,
        ConcreteFactContext facts)
    {
        if (exactLength > int.MaxValue) return true;

        var length = (int)exactLength;
        var prefix = shape.Prefix;
        var suffix = shape.Suffix;
        if (prefix is not null &&
            prefix.Length != 0 &&
            prefix.Length == length)
            return TryAddStringEquality(facts, value, prefix);

        if (suffix is not null &&
            suffix.Length != 0 &&
            suffix.Length == length)
            return TryAddStringEquality(facts, value, suffix);

        if (prefix is not null &&
            suffix is not null &&
            prefix.Length != 0 &&
            suffix.Length != 0 &&
            prefix.Length + suffix.Length >= length)
        {
            var characters = new char?[length];
            if (!TryOverlayString(characters, 0, prefix) ||
                !TryOverlayString(characters, length - suffix.Length, suffix))
                return false;

            if (characters.All(static c => c.HasValue))
                return TryAddStringEquality(
                    facts,
                    value,
                    new string(characters.Select(static c => c!.Value).ToArray()));
        }

        return true;
    }

    private static bool TryOverlayString(char?[] target, int start, string value)
    {
        if (start < 0 ||
            start + value.Length > target.Length)
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            var index = start + i;
            if (target[index].HasValue && target[index]!.Value != value[i]) return false;

            target[index] = value[i];
        }

        return true;
    }

    private SmtConcreteFactPreparationStatus SimplifyConcreteFacts(
        SmtFormula formula,
        ConcreteFactContext facts,
        out SmtFormula preparedFormula,
        out bool changed)
    {
        preparedFormula = formula;
        changed = false;

        if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
        {
            var leftStatus = SimplifyConcreteFacts(
                andFormula.Left,
                facts,
                out var left,
                out var leftChanged);
            if (leftStatus != SmtConcreteFactPreparationStatus.Ready) return leftStatus;

            var rightStatus = SimplifyConcreteFacts(
                andFormula.Right,
                facts,
                out var right,
                out var rightChanged);
            if (rightStatus != SmtConcreteFactPreparationStatus.Ready) return rightStatus;

            changed = leftChanged || rightChanged;
            if (left is SmtBooleanConstant { Value: false } ||
                right is SmtBooleanConstant { Value: false })
            {
                preparedFormula = new SmtBooleanConstant(false);
                changed = true;
                return SmtConcreteFactPreparationStatus.Ready;
            }

            if (left is SmtBooleanConstant { Value: true })
            {
                preparedFormula = right;
                changed = true;
                return SmtConcreteFactPreparationStatus.Ready;
            }

            if (right is SmtBooleanConstant { Value: true })
            {
                preparedFormula = left;
                changed = true;
                return SmtConcreteFactPreparationStatus.Ready;
            }

            if (changed) preparedFormula = new SmtBinaryFormula(SmtBinaryOperator.And, left, right);

            return SmtConcreteFactPreparationStatus.Ready;
        }

        if (TryEvaluateConcreteBoolean(formula, facts, out var concreteBoolean))
        {
            if (concreteBoolean && ShouldPreserveSourceFact(formula)) return SmtConcreteFactPreparationStatus.Ready;

            preparedFormula = new SmtBooleanConstant(concreteBoolean);
            changed = true;
            return SmtConcreteFactPreparationStatus.Ready;
        }

        if (TryGetRegexFact(formula, out var regexMatch, out var expectedMatch) &&
            TryGetConcreteString(regexMatch.Value, facts, out var concreteInput))
        {
            if (!_regexValidator.TryValidate(
                    concreteInput,
                    regexMatch.Pattern,
                    regexMatch.Options,
                    out var actualMatch))
                return SmtConcreteFactPreparationStatus.Unknown;

            if (actualMatch != expectedMatch) return SmtConcreteFactPreparationStatus.Unsatisfiable;

            preparedFormula = new SmtBooleanConstant(true);
            changed = true;
        }

        return SmtConcreteFactPreparationStatus.Ready;
    }

    private static bool EvaluateStringPredicate(
        StringPredicateKind kind,
        string value,
        string argument)
    {
        return kind switch
        {
            StringPredicateKind.Contains => value.Contains(argument),
            StringPredicateKind.StartsWith => value.StartsWith(argument, StringComparison.Ordinal),
            StringPredicateKind.EndsWith => value.EndsWith(argument, StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool TryGetRegexFact(
        SmtFormula formula,
        out SmtRegexMatchFormula regexMatch,
        out bool expectedMatch)
    {
        return TryGetPolarizedFact(formula, TryGetPositiveRegexFact, out regexMatch, out expectedMatch);
    }

    private static bool TryGetPositiveRegexFact(SmtFormula formula, out SmtRegexMatchFormula regexMatch)
    {
        if (formula is SmtRegexMatchFormula match)
        {
            regexMatch = match;
            return true;
        }

        regexMatch = null!;
        return false;
    }

    private static bool TryGetPolarizedFact<TFact>(
        SmtFormula formula,
        TryGetPositiveFact<TFact> tryGetPositiveFact,
        out TFact fact,
        out bool expectedValue)
    {
        if (tryGetPositiveFact(formula, out fact))
        {
            expectedValue = true;
            return true;
        }

        if (formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } notFormula &&
            tryGetPositiveFact(notFormula.Operand, out fact))
        {
            expectedValue = false;
            return true;
        }

        fact = default!;
        expectedValue = false;
        return false;
    }

    private static bool TryGetPositiveStringPredicateFact(
        SmtFormula formula,
        out StringPredicateFact predicate)
    {
        switch (formula)
        {
            case SmtStringContainsFormula contains:
                predicate = new StringPredicateFact(StringPredicateKind.Contains, contains.Value, contains.Search);
                return true;
            case SmtStringStartsWithFormula startsWith:
                predicate = new StringPredicateFact(StringPredicateKind.StartsWith, startsWith.Value,
                    startsWith.Prefix);
                return true;
            case SmtStringEndsWithFormula endsWith:
                predicate = new StringPredicateFact(StringPredicateKind.EndsWith, endsWith.Value, endsWith.Suffix);
                return true;
            default:
                predicate = default;
                return false;
        }
    }

    private static bool TryGetConcreteString(
        SmtFormula formula,
        ConcreteFactContext facts,
        out string value)
    {
        if (formula is SmtStringConstant stringConstant)
        {
            value = stringConstant.Value;
            return true;
        }

        if (facts.StringEqualities.TryGetValue(formula, out var found))
        {
            value = found;
            return true;
        }

        if (formula is SmtStringConcatTerm stringConcatTerm &&
            TryGetConcreteString(stringConcatTerm.Left, facts, out var left) &&
            TryGetConcreteString(stringConcatTerm.Right, facts, out var right))
        {
            value = string.Concat(left, right);
            return true;
        }

        if (formula is SmtConditionalFormula { Kind: SmtValueKind.String } conditionalFormula &&
            TryEvaluateConcreteBoolean(conditionalFormula.Condition, facts, out var selectedBranch))
            return TryGetConcreteString(
                selectedBranch ? conditionalFormula.WhenTrue : conditionalFormula.WhenFalse,
                facts,
                out value);

        value = string.Empty;
        return false;
    }

    private delegate bool CheckedLongBinaryOperation(long left, long right, out long value);

    private delegate bool TryGetPositiveFact<TFact>(SmtFormula formula, out TFact fact);

    private struct StringShapeFact
    {
        public string? Prefix;

        public string? Suffix;

        public long MinLength;

        public SmtConcreteFactPreparationStatus AddContains(string value)
        {
            return ApplyMinimumLength(value.Length);
        }

        public SmtConcreteFactPreparationStatus AddPrefix(string value)
        {
            return AddAffix(value, isPrefix: true);
        }

        public SmtConcreteFactPreparationStatus AddSuffix(string value)
        {
            return AddAffix(value, isPrefix: false);
        }

        private SmtConcreteFactPreparationStatus AddAffix(string value, bool isPrefix)
        {
            var current = isPrefix ? Prefix : Suffix;
            if (current != null && !AreCompatibleAffixes(current, value, isPrefix))
                return SmtConcreteFactPreparationStatus.Unsatisfiable;

            if (current == null || value.Length > current.Length)
            {
                if (isPrefix)
                    Prefix = value;
                else
                    Suffix = value;
            }

            return ApplyMinimumLength(value.Length);
        }

        private SmtConcreteFactPreparationStatus ApplyMinimumLength(int length)
        {
            if (length > MinLength) MinLength = length;

            return SmtConcreteFactPreparationStatus.Ready;
        }

        private static bool AreCompatibleAffixes(string left, string right, bool isPrefix)
        {
            var minLength = Math.Min(left.Length, right.Length);
            var leftStart = isPrefix ? 0 : left.Length - minLength;
            var rightStart = isPrefix ? 0 : right.Length - minLength;
            return string.Equals(
                left.Substring(leftStart, minLength),
                right.Substring(rightStart, minLength),
                StringComparison.Ordinal);
        }
    }

    private enum StringPredicateKind
    {
        Contains,
        StartsWith,
        EndsWith
    }

    private readonly struct StringPredicateFact(StringPredicateKind kind, SmtFormula value, SmtFormula argument)
    {
        public StringPredicateKind Kind { get; } = kind;

        public SmtFormula Value { get; } = value;

        public SmtFormula Argument { get; } = argument;
    }
}
