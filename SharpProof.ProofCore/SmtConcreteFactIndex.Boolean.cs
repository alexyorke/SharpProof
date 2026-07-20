namespace SharpProof.ProofCore.Smt;

internal sealed partial class SmtConcreteFactIndex
{
        internal bool TryEvaluateBoolean(SmtFormula formula, out bool value) =>
            TryEvaluateBoolean(formula, out value, 0);

        internal bool TryEvaluateDerivedBoolean(SmtFormula formula, out bool value) =>
            TryEvaluateBoolean(formula, out value, 0, false);

        private bool TryEvaluateBoolean(
            SmtFormula formula,
            out bool value,
            int conditionalBranchDepth)
        {
            return TryEvaluateBoolean(formula, out value, conditionalBranchDepth, true);
        }

        private bool TryEvaluateBoolean(
            SmtFormula formula,
            out bool value,
            int conditionalBranchDepth,
            bool allowDirectFact)
        {
            if (_booleanEvaluationDepth >= MaxBooleanEvaluationDepth ||
                !_workBudget.TryConsume())
            {
                value = false;
                return false;
            }

            _booleanEvaluationDepth++;
            try
            {
                formula = NormalizeAliases(formula);
                var canonical = FindBooleanCanonical(formula, out var isNegatedFromCanonical);
                if (!canonical.Equals(formula))
                {
                    if (allowDirectFact && _exactBooleans.TryGetValue(canonical, out var canonicalExactValue))
                    {
                        value = canonicalExactValue ^ isNegatedFromCanonical;
                        return true;
                    }

                    if (TryEvaluateBoolean(canonical, out var canonicalValue, conditionalBranchDepth))
                    {
                        value = canonicalValue ^ isNegatedFromCanonical;
                        return true;
                    }
                }

                if (allowDirectFact && _exactBooleans.TryGetValue(formula, out var exactValue))
                {
                    value = exactValue;
                    return true;
                }

                if (TryEvaluateKnownComplement(formula, out value)) return true;

                switch (formula)
                {
                    case SmtBooleanConstant booleanConstant:
                        value = booleanConstant.Value;
                        return true;
                    case SmtUnaryFormula { Operator: SmtUnaryOperator.Not } negated
                        when TryEvaluateBoolean(negated.Operand, out var operandValue, conditionalBranchDepth):
                        value = !operandValue;
                        return true;
                    case SmtBinaryFormula { Operator: SmtBinaryOperator.And } binary:
                        {
                            var hasLeft = TryEvaluateBoolean(binary.Left, out var left, conditionalBranchDepth);
                            var hasRight = TryEvaluateBoolean(binary.Right, out var right, conditionalBranchDepth);
                            if ((hasLeft && !left) || (hasRight && !right))
                            {
                                value = false;
                                return true;
                            }

                            if (hasLeft && hasRight)
                            {
                                value = left && right;
                                return true;
                            }

                            break;
                        }
                    case SmtBinaryFormula { Operator: SmtBinaryOperator.Or } binary:
                        {
                            var hasLeft = TryEvaluateBoolean(binary.Left, out var left, conditionalBranchDepth);
                            var hasRight = TryEvaluateBoolean(binary.Right, out var right, conditionalBranchDepth);
                            if ((hasLeft && left) || (hasRight && right))
                            {
                                value = true;
                                return true;
                            }

                            if (hasLeft && hasRight)
                            {
                                value = left || right;
                                return true;
                            }

                            break;
                        }
                    case SmtBinaryFormula binary:
                        return TryEvaluateComparison(binary, out value, conditionalBranchDepth);
                    case SmtConditionalFormula conditional
                        when conditional.Kind == SmtValueKind.Bool:
                        if (TryEvaluateBoolean(conditional.Condition, out var conditionValue, conditionalBranchDepth))
                            return TryEvaluateBoolean(
                                conditionValue ? conditional.WhenTrue : conditional.WhenFalse,
                                out value,
                                conditionalBranchDepth);

                        return TryEvaluateConditionalBranches(
                            conditional.Condition,
                            conditional.WhenTrue,
                            conditional.WhenFalse,
                            out value,
                            conditionalBranchDepth);
                    case SmtStringContainsFormula contains
                        when TryGetKnownString(contains.Value, out var containsValue) &&
                             TryGetKnownString(contains.Search, out var containsSearch):
                value = containsValue.IndexOf(containsSearch, StringComparison.Ordinal) >= 0;
                        return true;
                    case SmtStringStartsWithFormula startsWith
                        when TryGetKnownString(startsWith.Value, out var startsWithValue) &&
                             TryGetKnownString(startsWith.Prefix, out var prefix):
                        value = startsWithValue.StartsWith(prefix, StringComparison.Ordinal);
                        return true;
                    case SmtStringEndsWithFormula endsWith
                        when TryGetKnownString(endsWith.Value, out var endsWithValue) &&
                             TryGetKnownString(endsWith.Suffix, out var suffix):
                        value = endsWithValue.EndsWith(suffix, StringComparison.Ordinal);
                        return true;
                }

                value = false;
                return false;
            }
            finally
            {
                _booleanEvaluationDepth--;
            }
        }

        private bool TryEvaluateKnownComplement(SmtFormula formula, out bool value)
        {
            foreach (var exactBoolean in _exactBooleans)
                if (SmtComparisonOperatorFacts.AreComplements(formula, exactBoolean.Key))
                {
                    value = !exactBoolean.Value;
                    return true;
                }

            value = false;
            return false;
        }

        private bool TryAddBooleanFact(SmtFormula formula, out bool hasContradiction) =>
            TryAddBooleanFact(formula, true, out hasContradiction);

        private bool TryAddBooleanFact(
            SmtFormula formula,
            bool value,
            out bool hasContradiction)
        {
            if (!_workBudget.TryConsume())
            {
                hasContradiction = false;
                return false;
            }

            if (_booleanFactInferenceDepth >= MaxBooleanFactInferenceDepth)
                return AddExactBooleanWithoutInference(formula, value, out hasContradiction);

            _booleanFactInferenceDepth++;
            try
            {
                return TryAddBooleanFactCore(formula, value, out hasContradiction);
            }
            finally
            {
                _booleanFactInferenceDepth--;
            }
        }

        private bool TryAddBooleanFactCore(
            SmtFormula formula,
            bool value,
            out bool hasContradiction)
        {
            hasContradiction = false;
            if (formula.Kind != SmtValueKind.Bool ||
                formula is SmtBooleanConstant)
                return false;

            if (formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } negated)
                return TryAddBooleanFact(negated.Operand, !value, out hasContradiction);

            if (formula is SmtBinaryFormula binary)
            {
                if (binary.Operator == SmtBinaryOperator.And)
                {
                    if (value)
                    {
                        var addedLeft = TryAddBooleanFact(binary.Left, true, out var leftContradiction);
                        var addedRight = TryAddBooleanFact(binary.Right, true, out var rightContradiction);
                        hasContradiction = leftContradiction || rightContradiction;
                        return addedLeft || addedRight;
                    }

                    if (TryEvaluateBoolean(binary.Left, out var left) && left)
                        return TryAddBooleanFact(binary.Right, false, out hasContradiction);

                    if (TryEvaluateBoolean(binary.Right, out var right) && right)
                        return TryAddBooleanFact(binary.Left, false, out hasContradiction);
                }
                else if (binary.Operator == SmtBinaryOperator.Or)
                {
                    if (!value)
                    {
                        var addedLeft = TryAddBooleanFact(binary.Left, false, out var leftContradiction);
                        var addedRight = TryAddBooleanFact(binary.Right, false, out var rightContradiction);
                        hasContradiction = leftContradiction || rightContradiction;
                        return addedLeft || addedRight;
                    }

                    if (TryEvaluateBoolean(binary.Left, out var left) && !left)
                        return TryAddBooleanFact(binary.Right, true, out hasContradiction);

                    if (TryEvaluateBoolean(binary.Right, out var right) && !right)
                        return TryAddBooleanFact(binary.Left, true, out hasContradiction);
                }
                else if (binary.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual &&
                         binary.Left.Kind == SmtValueKind.Bool &&
                         binary.Right.Kind == SmtValueKind.Bool)
                {
                    var addedEquivalence = TryAddBooleanEquivalenceFact(
                        binary,
                        value,
                        out var equivalenceContradiction);
                    if (equivalenceContradiction)
                    {
                        hasContradiction = true;
                        return true;
                    }

                    if (TryEvaluateBoolean(binary.Left, out var left))
                    {
                        var expectedRight = binary.Operator == SmtBinaryOperator.Equal == value
                            ? left
                            : !left;
                        var addedRight = TryAddBooleanFact(binary.Right, expectedRight, out hasContradiction);
                        return addedEquivalence || addedRight;
                    }

                    if (TryEvaluateBoolean(binary.Right, out var right))
                    {
                        var expectedLeft = binary.Operator == SmtBinaryOperator.Equal == value
                            ? right
                            : !right;
                        var addedLeft = TryAddBooleanFact(binary.Left, expectedLeft, out hasContradiction);
                        return addedEquivalence || addedLeft;
                    }
                }
            }

            if (TryEvaluateBoolean(formula, out var knownValue) &&
                knownValue != value)
            {
                hasContradiction = true;
                return true;
            }

            var addedComparisonFact = TryAddKnownBooleanComparisonFact(formula, value, out var comparisonContradiction);
            if (!comparisonContradiction &&
                TryEvaluateBoolean(formula, out knownValue) &&
                knownValue != value)
            {
                hasContradiction = true;
                return true;
            }

            var addedExactBoolean = AddExactBoolean(formula, value, out var exactBooleanContradiction);
            hasContradiction = comparisonContradiction || exactBooleanContradiction;
            return addedComparisonFact || addedExactBoolean;
        }

        private bool TryAddKnownBooleanComparisonFact(
            SmtFormula formula,
            bool value,
            out bool hasContradiction)
        {
            hasContradiction = false;
            var effectiveFormula = value
                ? formula
                : new SmtUnaryFormula(SmtUnaryOperator.Not, formula);
            var added = false;

            if (TryAddIntegerIntervalFact(effectiveFormula, out var integerContradiction))
            {
                added = true;
                hasContradiction |= integerContradiction;
            }

            if (TryAddStringValueFact(effectiveFormula, out var stringContradiction))
            {
                added = true;
                hasContradiction |= stringContradiction;
            }

            if (TryAddReferenceNullFact(effectiveFormula, out var referenceContradiction))
            {
                added = true;
                hasContradiction |= referenceContradiction;
            }

            return added;
        }

        private bool AddExactBoolean(
            SmtFormula formula,
            bool value,
            out bool hasContradiction)
        {
            hasContradiction = false;
            formula = NormalizeAliases(formula);
            var canonical = FindBooleanCanonical(formula, out var isNegatedFromCanonical);
            var canonicalValue = value ^ isNegatedFromCanonical;
            if (_exactBooleans.TryGetValue(canonical, out var existing) &&
                existing != canonicalValue)
                hasContradiction = true;

            _exactBooleans[canonical] = canonicalValue;
            return true;
        }

        private bool AddExactBooleanWithoutInference(
            SmtFormula formula,
            bool value,
            out bool hasContradiction)
        {
            hasContradiction = false;
            if (formula is SmtBooleanConstant booleanConstant)
            {
                hasContradiction = booleanConstant.Value != value;
                return hasContradiction;
            }

            if (_exactBooleans.TryGetValue(formula, out var existing) &&
                existing != value)
                hasContradiction = true;

            _exactBooleans[formula] = value;
            return true;
        }

        private bool TryAddBooleanEquivalenceFact(
            SmtBinaryFormula formula,
            bool value,
            out bool hasContradiction)
        {
            hasContradiction = false;
            if (formula.Operator is not (SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual) ||
                formula.Left.Kind != SmtValueKind.Bool ||
                formula.Right.Kind != SmtValueKind.Bool ||
                !TryGetBooleanRelationTerm(formula.Left, out var left, out var leftNegated) ||
                !TryGetBooleanRelationTerm(formula.Right, out var right, out var rightNegated))
                return false;

            var differs = formula.Operator == SmtBinaryOperator.NotEqual;
            if (!value) differs = !differs;

            differs ^= leftNegated ^ rightNegated;
            return UnionBooleanEquivalences(left, right, differs, out hasContradiction);
        }

        private bool TryGetBooleanRelationTerm(
            SmtFormula formula,
            out SmtFormula term,
            out bool isNegated)
        {
            term = NormalizeAliases(formula);
            isNegated = false;
            while (term is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } negated)
            {
                isNegated = !isNegated;
                term = NormalizeAliases(negated.Operand);
            }

            if (CanRelateBooleanTerm(term)) return true;

            term = null!;
            isNegated = false;
            return false;
        }

        private bool TryGetDirectKnownBooleanValue(SmtFormula formula, out bool value)
        {
            formula = NormalizeAliases(formula);
            var canonical = FindBooleanCanonical(formula, out var isNegatedFromCanonical);
            if (_exactBooleans.TryGetValue(canonical, out var exactValue))
            {
                value = exactValue ^ isNegatedFromCanonical;
                return true;
            }

            if (formula is SmtBooleanConstant booleanConstant)
            {
                value = booleanConstant.Value;
                return true;
            }

            if (formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } negated &&
                TryGetDirectKnownBooleanValue(negated.Operand, out var operandValue))
            {
                value = !operandValue;
                return true;
            }

            value = false;
            return false;
        }

        private static bool CanRelateBooleanTerm(SmtFormula formula)
        {
            if (formula.Kind != SmtValueKind.Bool) return false;

            return formula switch
            {
                SmtVariable => true,
                SmtStringContainsFormula => true,
                SmtStringStartsWithFormula => true,
                SmtStringEndsWithFormula => true,
                SmtRegexMatchFormula => true,
                SmtRuntimeTypeTestFormula => true,
                SmtBinaryFormula binary => binary.Operator is SmtBinaryOperator.Equal or
                                               SmtBinaryOperator.NotEqual or
                                               SmtBinaryOperator.LessThan or
                                               SmtBinaryOperator.LessThanOrEqual or
                                               SmtBinaryOperator.GreaterThan or
                                               SmtBinaryOperator.GreaterThanOrEqual &&
                                           binary.Left.Kind != SmtValueKind.Bool &&
                                           binary.Right.Kind != SmtValueKind.Bool,
                _ => false
            };
        }

        private bool UnionBooleanEquivalences(
            SmtFormula left,
            SmtFormula right,
            bool differs,
            out bool hasContradiction)
        {
            var leftRoot = FindBooleanCanonical(left, out var leftNegated);
            var rightRoot = FindBooleanCanonical(right, out var rightNegated);
            var rootDiffers = differs ^ leftNegated ^ rightNegated;
            hasContradiction = false;

            if (leftRoot.Equals(rightRoot))
            {
                hasContradiction = rootDiffers;
                return hasContradiction;
            }

            var canonical = SelectCanonical(leftRoot, rightRoot);
            var alias = canonical.Equals(leftRoot) ? rightRoot : leftRoot;
            _booleanEquivalences[alias] = (canonical, rootDiffers);
            MergeBooleanFacts(canonical, alias, rootDiffers, out hasContradiction);
            return true;
        }

        private SmtFormula FindBooleanCanonical(SmtFormula formula, out bool isNegatedFromCanonical) =>
            FindCanonical(_booleanEquivalences, formula, out isNegatedFromCanonical);

        private void MergeBooleanFacts(
            SmtFormula canonical,
            SmtFormula alias,
            bool aliasDiffersFromCanonical,
            out bool hasContradiction)
        {
            hasContradiction = false;
            if (!_exactBooleans.TryGetValue(alias, out var aliasValue)) return;

            var canonicalValue = aliasValue ^ aliasDiffersFromCanonical;
            if (_exactBooleans.TryGetValue(canonical, out var existing) &&
                existing != canonicalValue)
                hasContradiction = true;

            _exactBooleans[canonical] = canonicalValue;
            _exactBooleans.Remove(alias);
        }

        private bool TryEvaluateComparison(
            SmtBinaryFormula binary,
            out bool value,
            int conditionalBranchDepth)
        {
            if (TryGetKnownInteger(binary.Left, out var leftInteger) &&
                TryGetKnownInteger(binary.Right, out var rightInteger))
            {
                value = binary.Operator switch
                {
                    SmtBinaryOperator.Equal => leftInteger == rightInteger,
                    SmtBinaryOperator.NotEqual => leftInteger != rightInteger,
                    SmtBinaryOperator.LessThan => leftInteger < rightInteger,
                    SmtBinaryOperator.LessThanOrEqual => leftInteger <= rightInteger,
                    SmtBinaryOperator.GreaterThan => leftInteger > rightInteger,
                    SmtBinaryOperator.GreaterThanOrEqual => leftInteger >= rightInteger,
                    _ => false
                };
                return binary.Operator is SmtBinaryOperator.Equal or
                    SmtBinaryOperator.NotEqual or
                    SmtBinaryOperator.LessThan or
                    SmtBinaryOperator.LessThanOrEqual or
                    SmtBinaryOperator.GreaterThan or
                    SmtBinaryOperator.GreaterThanOrEqual;
            }

            if (TryGetKnownString(binary.Left, out var leftString) &&
                TryGetKnownString(binary.Right, out var rightString))
            {
                value = binary.Operator switch
                {
                    SmtBinaryOperator.Equal => string.Equals(leftString, rightString, StringComparison.Ordinal),
                    SmtBinaryOperator.NotEqual => !string.Equals(leftString, rightString, StringComparison.Ordinal),
                    _ => false
                };
                return binary.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual;
            }

            if (TryEvaluateBoolean(binary.Left, out var leftBoolean) &&
                TryEvaluateBoolean(binary.Right, out var rightBoolean))
            {
                value = binary.Operator switch
                {
                    SmtBinaryOperator.Equal => leftBoolean == rightBoolean,
                    SmtBinaryOperator.NotEqual => leftBoolean != rightBoolean,
                    _ => false
                };
                return binary.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual;
            }

            if (TryEvaluateBooleanEquivalenceComparison(binary, out value)) return true;

            if (TryEvaluateConditionalComparison(binary, out value, conditionalBranchDepth)) return true;

            if (TryEvaluateReferenceComparison(binary, out value)) return true;

            value = false;
            return false;
        }

        private bool TryEvaluateConditionalComparison(
            SmtBinaryFormula binary,
            out bool value,
            int conditionalBranchDepth)
        {
            if (conditionalBranchDepth >= MaxConditionalBranchEvaluationDepth)
            {
                value = false;
                return false;
            }

            if (binary.Left is SmtConditionalFormula leftConditional &&
                leftConditional.Kind == binary.Left.Kind)
                return TryEvaluateConditionalBranches(
                    leftConditional.Condition,
                    new SmtBinaryFormula(binary.Operator, leftConditional.WhenTrue, binary.Right),
                    new SmtBinaryFormula(binary.Operator, leftConditional.WhenFalse, binary.Right),
                    out value,
                    conditionalBranchDepth);

            if (binary.Right is SmtConditionalFormula rightConditional &&
                rightConditional.Kind == binary.Right.Kind)
                return TryEvaluateConditionalBranches(
                    rightConditional.Condition,
                    new SmtBinaryFormula(binary.Operator, binary.Left, rightConditional.WhenTrue),
                    new SmtBinaryFormula(binary.Operator, binary.Left, rightConditional.WhenFalse),
                    out value,
                    conditionalBranchDepth);

            value = false;
            return false;
        }

        private bool TryEvaluateConditionalBranches(
            SmtFormula condition,
            SmtFormula whenTrue,
            SmtFormula whenFalse,
            out bool value,
            int conditionalBranchDepth)
        {
            value = false;
            if (conditionalBranchDepth >= MaxConditionalBranchEvaluationDepth ||
                _conditionalBranchEvaluationDepth >= MaxConditionalBranchEvaluationDepth)
                return false;

            if (TryGetDirectKnownBooleanValue(condition, out var conditionValue))
                return TryEvaluateBoolean(
                    conditionValue ? whenTrue : whenFalse,
                    out value,
                    conditionalBranchDepth + 1);

            _conditionalBranchEvaluationDepth++;
            try
            {
                var trueKnown = TryEvaluateBranchFormula(
                    condition,
                    true,
                    whenTrue,
                    conditionalBranchDepth + 1,
                    out var trueReachable,
                    out var trueValue);
                var falseKnown = TryEvaluateBranchFormula(
                    condition,
                    false,
                    whenFalse,
                    conditionalBranchDepth + 1,
                    out var falseReachable,
                    out var falseValue);

                if (!trueReachable && !falseReachable)
                {
                    value = false;
                    return true;
                }

                if ((trueReachable && !trueKnown) ||
                    (falseReachable && !falseKnown))
                    return false;

                if ((!trueReachable || trueValue) &&
                    (!falseReachable || falseValue))
                {
                    value = true;
                    return true;
                }

                if ((!trueReachable || !trueValue) &&
                    (!falseReachable || !falseValue))
                {
                    value = false;
                    return true;
                }

                return false;
            }
            finally
            {
                _conditionalBranchEvaluationDepth--;
            }
        }

        private bool TryEvaluateBranchFormula(
            SmtFormula condition,
            bool assumptionValue,
            SmtFormula formula,
            int conditionalBranchDepth,
            out bool isReachable,
            out bool value)
        {
            var branchFacts = ForkWithBooleanAssumption(condition, assumptionValue, out var hasContradiction);
            if (hasContradiction)
            {
                isReachable = false;
                value = false;
                return true;
            }

            isReachable = true;
            return branchFacts.TryClassifyBooleanFromFacts(formula, out value, conditionalBranchDepth);
        }

        private SmtConcreteFactIndex ForkWithBooleanAssumption(
            SmtFormula formula,
            bool value,
            out bool hasContradiction)
        {
            var fork = new SmtConcreteFactIndex(this);
            fork.TryAddBooleanFact(formula, value, out hasContradiction);
            if (hasContradiction) return fork;

            fork.ReplayExactBooleanFacts(out var replayContradiction);
            hasContradiction = replayContradiction;
            return fork;
        }

        private bool ReplayExactBooleanFacts(out bool hasContradiction)
        {
            hasContradiction = false;
            var added = false;
            for (var pass = 0; pass < 2; pass++)
            {
                var exactFacts = _exactBooleans.ToArray();
                var addedThisPass = false;
                foreach (var exactFact in exactFacts)
                {
                    if (TryAddBooleanFact(exactFact.Key, exactFact.Value, out var factContradiction))
                    {
                        addedThisPass = true;
                        added = true;
                    }

                    if (factContradiction)
                    {
                        hasContradiction = true;
                        return true;
                    }
                }

                if (!addedThisPass) break;
            }

            return added;
        }

        private bool TryClassifyBooleanFromFacts(
            SmtFormula formula,
            out bool value,
            int conditionalBranchDepth)
        {
            if (TryEvaluateBoolean(formula, out value, conditionalBranchDepth)) return true;

            var falseProbe = new SmtConcreteFactIndex(this);
            falseProbe.Add(new SmtUnaryFormula(SmtUnaryOperator.Not, formula), out var falseContradiction);
            if (falseContradiction)
            {
                value = true;
                return true;
            }

            var trueProbe = new SmtConcreteFactIndex(this);
            trueProbe.Add(formula, out var trueContradiction);
            if (trueContradiction)
            {
                value = false;
                return true;
            }

            value = false;
            return false;
        }

        private bool TryEvaluateBooleanEquivalenceComparison(SmtBinaryFormula binary, out bool value)
        {
            value = false;
            if (binary.Operator is not (SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual) ||
                binary.Left.Kind != SmtValueKind.Bool ||
                binary.Right.Kind != SmtValueKind.Bool)
                return false;

            if (!TryGetBooleanRelationTerm(binary.Left, out var left, out var leftNegated) ||
                !TryGetBooleanRelationTerm(binary.Right, out var right, out var rightNegated))
                return false;

            var leftRoot = FindBooleanCanonical(left, out var leftRootNegated);
            var rightRoot = FindBooleanCanonical(right, out var rightRootNegated);
            if (!leftRoot.Equals(rightRoot)) return false;

            var areEqual = (leftNegated ^ leftRootNegated) == (rightNegated ^ rightRootNegated);
            value = binary.Operator == SmtBinaryOperator.Equal
                ? areEqual
                : !areEqual;
            return true;
        }

}
