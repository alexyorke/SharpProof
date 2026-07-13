namespace SharpProof.ProofCore.Smt;

internal static class SmtFormulaNormalizer
{
    private const int MaxEqualitySubstitutionPasses = 4;
    private const int MaxEqualitySubstitutionReplacementNodes = 32;

    internal static bool TryNormalizeInitial(
        IReadOnlyList<SmtFormula> conditions,
        out List<SmtFormula> normalizedConditions,
        out bool changed)
    {
        normalizedConditions = new List<SmtFormula>(conditions.Count);
        changed = false;
        foreach (var condition in conditions)
        {
            var normalizedCondition = SimplifyBooleanConstants(condition, out var conditionChanged);
            changed |= conditionChanged;
            if (!TryClassifyCondition(normalizedCondition, out var shouldKeep)) return false;

            if (!shouldKeep)
            {
                changed = true;
                continue;
            }

            normalizedConditions.Add(normalizedCondition);
        }

        return TryApplyEqualitySubstitutions(normalizedConditions, ref changed);
    }

    internal static bool TryClassifyCondition(SmtFormula condition, out bool shouldKeep)
    {
        shouldKeep = condition is not SmtBooleanConstant;
        return condition is not SmtBooleanConstant { Value: false };
    }

    private static bool TryApplyEqualitySubstitutions(
        List<SmtFormula> conditions,
        ref bool changed)
    {
        for (var pass = 0; pass < MaxEqualitySubstitutionPasses; pass++)
        {
            var substitutions = new Dictionary<SmtVariable, SmtFormula>();
            foreach (var condition in conditions) TryCollectEqualitySubstitutions(condition, substitutions);

            if (substitutions.Count == 0) return true;

            var passChanged = false;
            for (var index = conditions.Count - 1; index >= 0; index--)
            {
                var substituted = SubstituteEqualityAliases(
                    conditions[index],
                    substitutions,
                    out var substitutedChanged);
                if (substitutedChanged) substituted = SimplifyBooleanConstants(substituted, out _);

                if (!TryClassifyCondition(substituted, out var shouldKeep)) return false;

                if (!shouldKeep)
                {
                    conditions.RemoveAt(index);
                    passChanged = true;
                    continue;
                }

                if (substitutedChanged)
                {
                    conditions[index] = substituted;
                    passChanged = true;
                }
            }

            changed |= passChanged;
            if (!passChanged) return true;
        }

        return true;
    }

    private static void TryCollectEqualitySubstitutions(
        SmtFormula formula,
        Dictionary<SmtVariable, SmtFormula> substitutions)
    {
        switch (formula)
        {
            case SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula:
                TryCollectEqualitySubstitutions(andFormula.Left, substitutions);
                TryCollectEqualitySubstitutions(andFormula.Right, substitutions);
                break;
            case SmtBinaryFormula { Operator: SmtBinaryOperator.Equal } equalityFormula
                when equalityFormula.Left.Kind == equalityFormula.Right.Kind:
                TryCollectEqualitySubstitution(equalityFormula.Left, equalityFormula.Right, substitutions);
                break;
        }
    }

    private static void TryCollectEqualitySubstitution(
        SmtFormula left,
        SmtFormula right,
        Dictionary<SmtVariable, SmtFormula> substitutions)
    {
        if (left is SmtVariable leftVariable && right is SmtVariable rightVariable)
        {
            var comparison = string.CompareOrdinal(leftVariable.Name, rightVariable.Name);
            if (comparison < 0)
                TryAddEqualitySubstitution(rightVariable, leftVariable, substitutions);
            else if (comparison > 0) TryAddEqualitySubstitution(leftVariable, rightVariable, substitutions);

            return;
        }

        if (left is SmtVariable variableLeft)
        {
            TryAddEqualitySubstitution(variableLeft, right, substitutions);
            return;
        }

        if (right is SmtVariable variableRight) TryAddEqualitySubstitution(variableRight, left, substitutions);
    }

    private static void TryAddEqualitySubstitution(
        SmtVariable source,
        SmtFormula replacement,
        Dictionary<SmtVariable, SmtFormula> substitutions)
    {
        if (source.Kind != replacement.Kind ||
            EqualityComparer<SmtFormula>.Default.Equals(source, replacement) ||
            CountFormulaNodes(replacement) > MaxEqualitySubstitutionReplacementNodes ||
            WouldCreateSubstitutionCycle(source, replacement, substitutions, substitutions.Count + 1))
            return;

        if (substitutions.ContainsKey(source)) return;

        substitutions.Add(source, replacement);
    }

    private static bool WouldCreateSubstitutionCycle(
        SmtVariable source,
        SmtFormula replacement,
        IReadOnlyDictionary<SmtVariable, SmtFormula> substitutions,
        int remainingDepth)
    {
        if (remainingDepth < 0) return true;

        switch (replacement)
        {
            case SmtVariable variable:
                if (EqualityComparer<SmtFormula>.Default.Equals(variable, source)) return true;

                return substitutions.TryGetValue(variable, out var nested) &&
                       WouldCreateSubstitutionCycle(source, nested, substitutions, remainingDepth - 1);
            case SmtUnaryFormula unaryFormula:
                return WouldCreateSubstitutionCycle(source, unaryFormula.Operand, substitutions, remainingDepth);
            case SmtBinaryFormula binaryFormula:
                return WouldCreateSubstitutionCycle(source, binaryFormula.Left, substitutions, remainingDepth) ||
                       WouldCreateSubstitutionCycle(source, binaryFormula.Right, substitutions, remainingDepth);
            case SmtIntegerUnaryTerm integerUnaryTerm:
                return WouldCreateSubstitutionCycle(source, integerUnaryTerm.Operand, substitutions, remainingDepth);
            case SmtIntegerBinaryTerm integerBinaryTerm:
                return WouldCreateSubstitutionCycle(source, integerBinaryTerm.Left, substitutions, remainingDepth) ||
                       WouldCreateSubstitutionCycle(source, integerBinaryTerm.Right, substitutions, remainingDepth);
            case SmtOpaqueIntegerBinaryTerm opaqueIntegerTerm:
                return WouldCreateSubstitutionCycle(source, opaqueIntegerTerm.Left, substitutions, remainingDepth) ||
                       WouldCreateSubstitutionCycle(source, opaqueIntegerTerm.Right, substitutions, remainingDepth);
            case SmtStringLengthTerm stringLengthTerm:
                return WouldCreateSubstitutionCycle(source, stringLengthTerm.Value, substitutions, remainingDepth);
            case SmtStringConcatTerm stringConcatTerm:
                return WouldCreateSubstitutionCycle(source, stringConcatTerm.Left, substitutions, remainingDepth) ||
                       WouldCreateSubstitutionCycle(source, stringConcatTerm.Right, substitutions, remainingDepth);
            case SmtStringContainsFormula stringContains:
                return WouldCreateSubstitutionCycle(source, stringContains.Value, substitutions, remainingDepth) ||
                       WouldCreateSubstitutionCycle(source, stringContains.Search, substitutions, remainingDepth);
            case SmtStringStartsWithFormula stringStartsWith:
                return WouldCreateSubstitutionCycle(source, stringStartsWith.Value, substitutions, remainingDepth) ||
                       WouldCreateSubstitutionCycle(source, stringStartsWith.Prefix, substitutions, remainingDepth);
            case SmtStringEndsWithFormula stringEndsWith:
                return WouldCreateSubstitutionCycle(source, stringEndsWith.Value, substitutions, remainingDepth) ||
                       WouldCreateSubstitutionCycle(source, stringEndsWith.Suffix, substitutions, remainingDepth);
            case SmtRegexMatchFormula regexMatch:
                return WouldCreateSubstitutionCycle(source, regexMatch.Value, substitutions, remainingDepth);
            case SmtRuntimeTypeTestFormula runtimeTypeTest:
                return WouldCreateSubstitutionCycle(source, runtimeTypeTest.Value, substitutions, remainingDepth);
            case SmtConditionalFormula conditionalFormula:
                return WouldCreateSubstitutionCycle(source, conditionalFormula.Condition, substitutions,
                           remainingDepth) ||
                       WouldCreateSubstitutionCycle(source, conditionalFormula.WhenTrue, substitutions,
                           remainingDepth) ||
                       WouldCreateSubstitutionCycle(source, conditionalFormula.WhenFalse, substitutions,
                           remainingDepth);
            default:
                return false;
        }
    }

    private static SmtFormula SubstituteEqualityAliases(
        SmtFormula formula,
        IReadOnlyDictionary<SmtVariable, SmtFormula> substitutions,
        out bool changed)
    {
        changed = false;
        var current = formula;
        for (var pass = 0; pass <= substitutions.Count; pass++)
        {
            var rewritten = SmtFormulaTraversal.RewriteBottomUp(
                current,
                candidate => candidate is SmtVariable variable && substitutions.TryGetValue(variable, out var replacement)
                    ? replacement
                    : candidate,
                out var passChanged);
            if (!passChanged) break;

            changed = true;
            current = rewritten;
        }

        return current;
    }

    private static int CountFormulaNodes(SmtFormula formula)
    {
        var count = 0;
        foreach (var unused in SmtFormulaTraversal.Enumerate(formula)) count++;

        return count;
    }

    private static SmtFormula SimplifyBooleanConstants(SmtFormula formula, out bool changed)
    {
        changed = false;
        if (formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } unaryFormula)
        {
            var operand = SimplifyBooleanConstants(unaryFormula.Operand, out var operandChanged);
            if (operand is SmtBooleanConstant booleanConstant)
            {
                changed = true;
                return new SmtBooleanConstant(!booleanConstant.Value);
            }

            if (operand is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } nestedNot)
            {
                changed = true;
                return nestedNot.Operand;
            }

            if (TryNegateFormula(operand, out var negatedFormula))
            {
                changed = true;
                return SimplifyBooleanConstants(negatedFormula, out _);
            }

            changed = operandChanged;
            return operandChanged ? new SmtUnaryFormula(SmtUnaryOperator.Not, operand) : formula;
        }

        if (formula is not SmtBinaryFormula binaryFormula) return formula;

        if (binaryFormula.Operator is not SmtBinaryOperator.And and not SmtBinaryOperator.Or) return formula;

        var left = SimplifyBooleanConstants(binaryFormula.Left, out var leftChanged);
        var right = SimplifyBooleanConstants(binaryFormula.Right, out var rightChanged);
        changed = leftChanged || rightChanged;

        if (binaryFormula.Operator == SmtBinaryOperator.And)
        {
            if (left is SmtBooleanConstant { Value: false } ||
                right is SmtBooleanConstant { Value: false })
            {
                changed = true;
                return new SmtBooleanConstant(false);
            }

            if (left is SmtBooleanConstant { Value: true })
            {
                changed = true;
                return right;
            }

            if (right is SmtBooleanConstant { Value: true })
            {
                changed = true;
                return left;
            }

            if (EqualityComparer<SmtFormula>.Default.Equals(left, right))
            {
                changed = true;
                return left;
            }

            if (AreSyntacticNegations(left, right))
            {
                changed = true;
                return new SmtBooleanConstant(false);
            }
        }
        else
        {
            if (left is SmtBooleanConstant { Value: true } ||
                right is SmtBooleanConstant { Value: true })
            {
                changed = true;
                return new SmtBooleanConstant(true);
            }

            if (left is SmtBooleanConstant { Value: false })
            {
                changed = true;
                return right;
            }

            if (right is SmtBooleanConstant { Value: false })
            {
                changed = true;
                return left;
            }

            if (EqualityComparer<SmtFormula>.Default.Equals(left, right))
            {
                changed = true;
                return left;
            }

            if (AreSyntacticNegations(left, right))
            {
                changed = true;
                return new SmtBooleanConstant(true);
            }
        }

        return changed ? new SmtBinaryFormula(binaryFormula.Operator, left, right) : formula;
    }

    private static bool TryNegateFormula(SmtFormula formula, out SmtFormula negatedFormula)
    {
        if (formula is SmtBinaryFormula binaryFormula)
        {
            var negatedOperator = binaryFormula.Operator switch
            {
                SmtBinaryOperator.Equal => SmtBinaryOperator.NotEqual,
                SmtBinaryOperator.NotEqual => SmtBinaryOperator.Equal,
                SmtBinaryOperator.LessThan => SmtBinaryOperator.GreaterThanOrEqual,
                SmtBinaryOperator.LessThanOrEqual => SmtBinaryOperator.GreaterThan,
                SmtBinaryOperator.GreaterThan => SmtBinaryOperator.LessThanOrEqual,
                SmtBinaryOperator.GreaterThanOrEqual => SmtBinaryOperator.LessThan,
                _ => default
            };

            if (negatedOperator != default)
            {
                negatedFormula = new SmtBinaryFormula(negatedOperator, binaryFormula.Left, binaryFormula.Right);
                return true;
            }

            if (binaryFormula.Operator is SmtBinaryOperator.And or SmtBinaryOperator.Or)
            {
                var operatorAfterNegation = binaryFormula.Operator == SmtBinaryOperator.And
                    ? SmtBinaryOperator.Or
                    : SmtBinaryOperator.And;
                negatedFormula = new SmtBinaryFormula(
                    operatorAfterNegation,
                    new SmtUnaryFormula(SmtUnaryOperator.Not, binaryFormula.Left),
                    new SmtUnaryFormula(SmtUnaryOperator.Not, binaryFormula.Right));
                return true;
            }
        }

        negatedFormula = null!;
        return false;
    }

    private static bool AreSyntacticNegations(SmtFormula left, SmtFormula right)
    {
        return (left is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } leftNot &&
                EqualityComparer<SmtFormula>.Default.Equals(leftNot.Operand, right)) ||
               (right is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } rightNot &&
                EqualityComparer<SmtFormula>.Default.Equals(rightNot.Operand, left));
    }
}
