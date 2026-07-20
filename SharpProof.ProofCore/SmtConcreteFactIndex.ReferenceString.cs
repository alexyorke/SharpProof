namespace SharpProof.ProofCore.Smt;

internal sealed partial class SmtConcreteFactIndex {
        private bool TryAddStringValueFact(SmtFormula formula, out bool hasContradiction) {
            hasContradiction = false;
            if (!TryGetStringComparison(formula, out var term, out var op, out var value)) return false;

            term = NormalizeAliases(term);
            if (op == SmtBinaryOperator.NotEqual) {
                if (_exactStrings.TryGetValue(term, out var exactValue) &&
                    string.Equals(exactValue, value, StringComparison.Ordinal))
                    hasContradiction = true;

                if (!_excludedStrings.TryGetValue(term, out var excluded))
                    _excludedStrings[term] = excluded = new HashSet<string>(StringComparer.Ordinal);
                excluded.Add(value);
                return true;
            }

            if (_exactStrings.TryGetValue(term, out var existing) &&
                !string.Equals(existing, value, StringComparison.Ordinal))
                hasContradiction = true;

            if (_excludedStrings.TryGetValue(term, out var excludedValues) &&
                excludedValues.Contains(value))
                hasContradiction = true;

            _exactStrings[term] = value;
            AddStringLengthFact(term, value.Length, out var lengthContradiction);
            hasContradiction |= lengthContradiction;
            return true;
        }

        private bool TryAddReferenceNullFact(SmtFormula formula, out bool hasContradiction) {
            hasContradiction = false;
            if (!TryGetReferenceNullComparison(formula, out var term, out var isNull)) return false;

            term = NormalizeAliases(term);
            if (term is SmtNullConstant) {
                hasContradiction = !isNull;
                return hasContradiction;
            }

            if (_referenceNullStates.TryGetValue(term, out var existing) &&
                existing != isNull)
                hasContradiction = true;
            else
                _referenceNullStates[term] = isNull;

            if (term is SmtConditionalFormula { Kind: SmtValueKind.Reference } conditional &&
                TryEvaluateBoolean(conditional.Condition, out var conditionValue)) {
                var selectedBranch = conditionValue ? conditional.WhenTrue : conditional.WhenFalse;
                var selectedBranchFact = new SmtBinaryFormula(
                    isNull ? SmtBinaryOperator.Equal : SmtBinaryOperator.NotEqual,
                    selectedBranch,
                    new SmtNullConstant());
                TryAddReferenceNullFact(selectedBranchFact, out var branchContradiction);
                hasContradiction |= branchContradiction;
            }

            return true;
        }

        private static bool TryGetReferenceNullComparison(
            SmtFormula formula,
            out SmtFormula term,
            out bool isNull) {
            term = null!;
            isNull = false;

            if (!SmtComparisonOperatorFacts.TryExtract(
                    formula,
                    out var binary,
                    out var negationCount))
                return false;

            var op = SmtComparisonOperatorFacts.ApplyNegations(binary.Operator, negationCount);
            if (op is not (SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual)) return false;

            var comparisonIsNull = op == SmtBinaryOperator.Equal;
            if (binary.Left is SmtNullConstant && binary.Right.Kind == SmtValueKind.Reference) {
                term = binary.Right;
                isNull = comparisonIsNull;
                return true;
            }

            if (binary.Right is SmtNullConstant && binary.Left.Kind == SmtValueKind.Reference) {
                term = binary.Left;
                isNull = comparisonIsNull;
                return true;
            }

            return false;
        }

        private bool TryEvaluateReferenceComparison(SmtBinaryFormula binary, out bool value) {
            value = false;
            if (binary.Operator is not (SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual) ||
                binary.Left.Kind != SmtValueKind.Reference ||
                binary.Right.Kind != SmtValueKind.Reference)
                return false;

            var left = NormalizeAliases(binary.Left);
            var right = NormalizeAliases(binary.Right);
            if (left.Equals(right)) {
                value = binary.Operator == SmtBinaryOperator.Equal;
                return true;
            }

            var hasLeftNullState = TryGetKnownReferenceNullState(left, out var leftIsNull);
            var hasRightNullState = TryGetKnownReferenceNullState(right, out var rightIsNull);
            if (!hasLeftNullState || !hasRightNullState) return false;

            if (!leftIsNull && !rightIsNull) return false;

            var areEqual = leftIsNull && rightIsNull;
            value = binary.Operator == SmtBinaryOperator.Equal
                ? areEqual
                : !areEqual;
            return true;
        }

        internal bool TryGetKnownReferenceNullState(SmtFormula formula, out bool isNull) {
            formula = NormalizeAliases(formula);
            if (formula is SmtNullConstant) {
                isNull = true;
                return true;
            }

            if (_referenceNullStates.TryGetValue(formula, out isNull)) return true;

            if (formula is SmtConditionalFormula { Kind: SmtValueKind.Reference } conditional &&
                TryEvaluateBoolean(conditional.Condition, out var conditionValue))
                return TryGetKnownReferenceNullState(
                    conditionValue ? conditional.WhenTrue : conditional.WhenFalse,
                    out isNull);

            isNull = false;
            return false;
        }

        private void AddStringLengthFact(
            SmtFormula stringFormula,
            int length,
            out bool hasContradiction) {
            stringFormula = NormalizeAliases(stringFormula);
            var lengthFormula = new SmtStringLengthTerm(stringFormula);
            var interval = _integerIntervals.TryGetValue(lengthFormula, out var existing)
                ? existing
                : SmtIntegerInterval.Unbounded;
            interval = interval.Apply(SmtBinaryOperator.Equal, length);
            hasContradiction = interval.IsContradictory;
            _integerIntervals[lengthFormula] = interval;
        }

        internal bool TryGetKnownString(SmtFormula formula, out string value) {
            formula = NormalizeAliases(formula);
            if (_exactStrings.TryGetValue(formula, out var exactValue)) {
                value = exactValue;
                return true;
            }

            switch (formula) {
                case SmtStringConstant stringConstant:
                    value = stringConstant.Value;
                    return true;
                case SmtStringConcatTerm concat
                    when TryGetKnownString(concat.Left, out var left) &&
                         TryGetKnownString(concat.Right, out var right):
                    value = left + right;
                    return true;
                case SmtConditionalFormula conditional
                    when conditional.Kind == SmtValueKind.String &&
                         TryEvaluateBoolean(conditional.Condition, out var conditionValue):
                    return TryGetKnownString(conditionValue ? conditional.WhenTrue : conditional.WhenFalse, out value);
                default:
                    value = string.Empty;
                    return false;
            }
        }

        private bool TryGetKnownStringLength(SmtFormula formula, out long length) {
            formula = NormalizeAliases(formula);
            if (TryGetKnownString(formula, out var stringValue)) {
                length = stringValue.Length;
                return true;
            }

            var lengthFormula = new SmtStringLengthTerm(formula);
            if (_integerIntervals.TryGetValue(lengthFormula, out var interval) &&
                interval.ExactValue is { } exactLength &&
                exactLength >= 0) {
                length = exactLength;
                return true;
            }

            switch (formula) {
                case SmtStringConcatTerm concat
                    when TryGetKnownStringLength(concat.Left, out var leftLength) &&
                         TryGetKnownStringLength(concat.Right, out var rightLength):
                    try {
                        checked {
                            length = leftLength + rightLength;
                        }

                        return true;
                    }
                    catch (OverflowException) {
                        break;
                    }

                case SmtConditionalFormula conditional
                    when conditional.Kind == SmtValueKind.String &&
                         TryEvaluateBoolean(conditional.Condition, out var conditionValue):
                    return TryGetKnownStringLength(
                        conditionValue ? conditional.WhenTrue : conditional.WhenFalse,
                        out length);

                case SmtConditionalFormula conditional
                    when conditional.Kind == SmtValueKind.String &&
                         TryGetKnownStringLength(conditional.WhenTrue, out var trueLength) &&
                         TryGetKnownStringLength(conditional.WhenFalse, out var falseLength) &&
                         trueLength == falseLength:
                    length = trueLength;
                    return true;
            }

            length = 0;
            return false;
        }

        private static bool TryGetStringComparison(
            SmtFormula formula,
            out SmtFormula term,
            out SmtBinaryOperator op,
            out string value) {
            term = null!;
            op = default;
            value = string.Empty;
            if (!SmtComparisonOperatorFacts.TryExtract(
                    formula,
                    out var binary,
                    out var negationCount))
                return false;

            var effectiveOperator = SmtComparisonOperatorFacts.ApplyNegations(binary.Operator, negationCount);
            if (effectiveOperator is not (SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual)) return false;

            if (binary.Left.Kind == SmtValueKind.String &&
                binary.Right is SmtStringConstant rightConstant) {
                term = binary.Left;
                op = effectiveOperator;
                value = rightConstant.Value;
                return true;
            }

            if (binary.Left is SmtStringConstant leftConstant &&
                binary.Right.Kind == SmtValueKind.String) {
                term = binary.Right;
                op = effectiveOperator;
                value = leftConstant.Value;
                return true;
            }

            return false;
        }

}
