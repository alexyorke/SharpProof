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

        var concreteStringStatus = InferConcreteStringsForRegexValidation(normalizedConditions, facts);
        if (concreteStringStatus != SmtConcreteFactPreparationStatus.Ready)
        {
            preparedConditions = Array.Empty<SmtFormula>();
            return concreteStringStatus;
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

                if (facts.TryGetKnownInteger(integerBinaryTerm.Right, out var denominator))
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

        if (facts.TryGetKnownInteger(formula, out var concrete))
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

        if (facts.TryGetKnownString(value, out var concrete))
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
                if (facts.TryGetKnownInteger(term.Left, out var leftConstant))
                    return TryScaleBounds(rightLower, rightUpper, leftConstant, out lower, out upper);

                if (facts.TryGetKnownInteger(term.Right, out var rightConstant))
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
        return formula is SmtVariable { Kind: SmtValueKind.Bool } or SmtRuntimeTypeTestFormula
            ? facts.TryEvaluateBoolean(formula, out value)
            : facts.TryEvaluateDerivedBoolean(formula, out value);
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

    private static SmtConcreteFactPreparationStatus InferConcreteStringsForRegexValidation(
        IEnumerable<SmtFormula> conditions,
        ConcreteFactContext facts)
    {
        var conjuncts = conditions.SelectMany(SmtFormulaTraversal.EnumerateConjuncts).ToArray();
        var lengths = new Dictionary<SmtFormula, long>();
        foreach (var conjunct in conjuncts)
        {
            if (!TryGetStringLengthEquality(conjunct, out var value, out var length)) continue;
            if (length < 0 || lengths.TryGetValue(value, out var existing) && existing != length)
                return SmtConcreteFactPreparationStatus.Unsatisfiable;

            lengths[value] = length;
        }

        foreach (var conjunct in conjuncts)
        {
            if (!TryGetPositiveStringPredicate(conjunct, out var value, out var argument) ||
                !lengths.TryGetValue(value, out var length) ||
                !facts.TryGetKnownString(argument, out var concreteArgument) ||
                length != concreteArgument.Length)
                continue;

            if (facts.StringEqualities.TryGetValue(value, out var existing))
            {
                if (!string.Equals(existing, concreteArgument, StringComparison.Ordinal))
                    return SmtConcreteFactPreparationStatus.Unsatisfiable;
            }
            else
            {
                facts.StringEqualities.Add(value, concreteArgument);
            }
        }

        return SmtConcreteFactPreparationStatus.Ready;
    }

    private static bool TryGetStringLengthEquality(SmtFormula formula, out SmtFormula value, out long length)
    {
        value = null!;
        length = default;
        if (formula is not SmtBinaryFormula { Operator: SmtBinaryOperator.Equal } equality) return false;

        if (equality.Left is SmtStringLengthTerm left && equality.Right is SmtIntegerConstant right)
        {
            value = left.Value;
            length = right.Value;
            return true;
        }

        if (equality.Left is SmtIntegerConstant leftConstant && equality.Right is SmtStringLengthTerm rightLength)
        {
            value = rightLength.Value;
            length = leftConstant.Value;
            return true;
        }

        return false;
    }

    private static bool TryGetPositiveStringPredicate(
        SmtFormula formula,
        out SmtFormula value,
        out SmtFormula argument)
    {
        (value, argument) = formula switch
        {
            SmtStringContainsFormula contains => (contains.Value, contains.Search),
            SmtStringStartsWithFormula startsWith => (startsWith.Value, startsWith.Prefix),
            SmtStringEndsWithFormula endsWith => (endsWith.Value, endsWith.Suffix),
            _ => (null!, null!)
        };
        return value != null;
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
            facts.TryGetKnownString(regexMatch.Value, out var concreteInput))
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

    private delegate bool CheckedLongBinaryOperation(long left, long right, out long value);

    private delegate bool TryGetPositiveFact<TFact>(SmtFormula formula, out TFact fact);
}
