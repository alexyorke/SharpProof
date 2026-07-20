namespace SharpProof.ProofCore.Smt;

internal static class SmtFormulaNormalizer {
    private const int MaxEqualitySubstitutionPasses = 4;
    private const int MaxEqualitySubstitutionReplacementNodes = 32;

    internal static bool TryNormalizeInitial(
        IReadOnlyList<SmtFormula> conditions,
        out List<SmtFormula> normalizedConditions,
        out bool changed) {
        normalizedConditions = new List<SmtFormula>(conditions.Count);
        changed = false;
        foreach (var condition in conditions) {
            var normalizedCondition = SimplifyBooleanConstants(condition, out var conditionChanged);
            changed |= conditionChanged;
            if (!TryClassifyCondition(normalizedCondition, out var shouldKeep)) return false;

            if (!shouldKeep) {
                changed = true;
                continue;
            }

            normalizedConditions.Add(normalizedCondition);
        }

        return TryApplyEqualitySubstitutions(normalizedConditions, ref changed);
    }

    internal static bool TryClassifyCondition(SmtFormula condition, out bool shouldKeep) {
        shouldKeep = condition is not SmtBooleanConstant;
        return condition is not SmtBooleanConstant { Value: false };
    }

    private static bool TryApplyEqualitySubstitutions(
        List<SmtFormula> conditions,
        ref bool changed) {
        for (var pass = 0; pass < MaxEqualitySubstitutionPasses; pass++) {
            var substitutions = new Dictionary<SmtVariable, SmtFormula>();
            foreach (var condition in conditions) TryCollectEqualitySubstitutions(condition, substitutions);

            if (substitutions.Count == 0) return true;

            var passChanged = false;
            for (var index = conditions.Count - 1; index >= 0; index--) {
                var substituted = SubstituteEqualityAliases(
                    conditions[index],
                    substitutions,
                    out var substitutedChanged);
                if (substitutedChanged) substituted = SimplifyBooleanConstants(substituted, out _);

                if (!TryClassifyCondition(substituted, out var shouldKeep)) return false;

                if (!shouldKeep) {
                    conditions.RemoveAt(index);
                    passChanged = true;
                    continue;
                }

                if (substitutedChanged) {
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
        Dictionary<SmtVariable, SmtFormula> substitutions) {
        switch (formula) {
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
        Dictionary<SmtVariable, SmtFormula> substitutions) {
        if (left is SmtVariable leftVariable && right is SmtVariable rightVariable) {
            var comparison = string.CompareOrdinal(leftVariable.Name, rightVariable.Name);
            if (comparison < 0)
                TryAddEqualitySubstitution(rightVariable, leftVariable, substitutions);
            else if (comparison > 0) TryAddEqualitySubstitution(leftVariable, rightVariable, substitutions);

            return;
        }

        if (left is SmtVariable variableLeft) {
            TryAddEqualitySubstitution(variableLeft, right, substitutions);
            return;
        }

        if (right is SmtVariable variableRight) TryAddEqualitySubstitution(variableRight, left, substitutions);
    }

    private static void TryAddEqualitySubstitution(
        SmtVariable source,
        SmtFormula replacement,
        Dictionary<SmtVariable, SmtFormula> substitutions) {
        if (source.Kind != replacement.Kind ||
            EqualityComparer<SmtFormula>.Default.Equals(source, replacement) ||
            SmtFormulaTraversal.Enumerate(replacement).Skip(MaxEqualitySubstitutionReplacementNodes).Any() ||
            WouldCreateSubstitutionCycle(source, replacement, substitutions, substitutions.Count + 1))
            return;

        if (substitutions.ContainsKey(source)) return;

        substitutions.Add(source, replacement);
    }

    private static bool WouldCreateSubstitutionCycle(
        SmtVariable source,
        SmtFormula replacement,
        IReadOnlyDictionary<SmtVariable, SmtFormula> substitutions,
        int remainingDepth) {
        if (remainingDepth < 0) return true;
        foreach (var candidate in SmtFormulaTraversal.Enumerate(replacement).OfType<SmtVariable>()) {
            if (candidate.Equals(source)) return true;
            if (substitutions.TryGetValue(candidate, out var nested) &&
                WouldCreateSubstitutionCycle(source, nested, substitutions, remainingDepth - 1))
                return true;
        }

        return false;
    }

    private static SmtFormula SubstituteEqualityAliases(
        SmtFormula formula,
        IReadOnlyDictionary<SmtVariable, SmtFormula> substitutions,
        out bool changed) {
        changed = false;
        var current = formula;
        for (var pass = 0; pass <= substitutions.Count; pass++) {
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

    private static SmtFormula SimplifyBooleanConstants(SmtFormula formula, out bool changed) {
        changed = false;
        var current = formula;
        while (true) {
            current = SmtFormulaTraversal.RewriteBottomUp(current, SimplifyBooleanNode, out var passChanged);
            if (!passChanged) break;
            changed = true;
        }

        return current;
    }

    private static SmtFormula SimplifyBooleanNode(SmtFormula formula) {
        if (formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } negated) {
            if (negated.Operand is SmtBooleanConstant booleanConstant)
                return new SmtBooleanConstant(!booleanConstant.Value);
            if (negated.Operand is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } nested)
                return nested.Operand;
            if (negated.Operand is SmtBinaryFormula comparison &&
                SmtComparisonOperatorFacts.IsComparison(comparison.Operator))
                return new SmtBinaryFormula(
                    SmtComparisonOperatorFacts.Negate(comparison.Operator),
                    comparison.Left,
                    comparison.Right);
            if (negated.Operand is SmtBinaryFormula {
                    Operator: SmtBinaryOperator.And or SmtBinaryOperator.Or
                } logical)
                return new SmtBinaryFormula(
                    logical.Operator == SmtBinaryOperator.And ? SmtBinaryOperator.Or : SmtBinaryOperator.And,
                    new SmtUnaryFormula(SmtUnaryOperator.Not, logical.Left),
                    new SmtUnaryFormula(SmtUnaryOperator.Not, logical.Right));
            return formula;
        }

        if (formula is not SmtBinaryFormula {
                Operator: SmtBinaryOperator.And or SmtBinaryOperator.Or
            } binary)
            return formula;

        var isAnd = binary.Operator == SmtBinaryOperator.And;
        if (binary.Left is SmtBooleanConstant left)
            return left.Value == isAnd ? binary.Right : new SmtBooleanConstant(!isAnd);
        if (binary.Right is SmtBooleanConstant right)
            return right.Value == isAnd ? binary.Left : new SmtBooleanConstant(!isAnd);
        if (binary.Left.Equals(binary.Right)) return binary.Left;
        return SmtComparisonOperatorFacts.AreComplements(binary.Left, binary.Right)
            ? new SmtBooleanConstant(!isAnd)
            : formula;
    }
}
