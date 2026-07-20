namespace SharpProof.ProofCore.Smt;

internal sealed partial class SmtConcreteFactIndex
{
        private const int MaxAffineExpansionDepth = 8;
        private const int MaxBooleanEvaluationDepth = 64;
        private const int MaxBooleanFactInferenceDepth = 16;
        private const int MaxConditionalBranchEvaluationDepth = 4;
        private const int MaxFormulaReferenceDepth = 256;
        private const int MaxSyntacticWorkItems = 1048576;
        private readonly Dictionary<SmtFormula, (SmtFormula Parent, bool Differs)> _aliases = new();
        private readonly Dictionary<SmtFormula, (SmtFormula Parent, bool Differs)> _booleanEquivalences = new();
        private readonly Dictionary<SmtFormula, bool> _exactBooleans = new();
        private readonly Dictionary<SmtFormula, string> _exactStrings = new();
        private readonly Dictionary<SmtFormula, HashSet<string>> _excludedStrings = new();

        private readonly Dictionary<SmtFormula, SmtIntegerInterval> _integerIntervals = new();
        private readonly Dictionary<SmtFormula, bool> _referenceNullStates = new();
        private readonly SyntacticWorkBudget _workBudget;
        private int _booleanEvaluationDepth;
        private int _booleanFactInferenceDepth;
        private int _conditionalBranchEvaluationDepth;

        internal Dictionary<SmtFormula, string> StringEqualities => _exactStrings;
        internal Dictionary<SmtFormula, SmtIntegerInterval> IntegerIntervals => _integerIntervals;

        internal SmtConcreteFactIndex()
        {
            _workBudget = new SyntacticWorkBudget(MaxSyntacticWorkItems);
        }

        private SmtConcreteFactIndex(SmtConcreteFactIndex source)
        {
            _integerIntervals = new Dictionary<SmtFormula, SmtIntegerInterval>(source._integerIntervals);
            _exactStrings = new Dictionary<SmtFormula, string>(source._exactStrings);
            _excludedStrings = source._excludedStrings.ToDictionary(
                static pair => pair.Key,
                static pair => new HashSet<string>(pair.Value, StringComparer.Ordinal));
            _referenceNullStates = new Dictionary<SmtFormula, bool>(source._referenceNullStates);
            _exactBooleans = new Dictionary<SmtFormula, bool>(source._exactBooleans);
            _aliases = new Dictionary<SmtFormula, (SmtFormula, bool)>(source._aliases);
            _booleanEquivalences = new Dictionary<SmtFormula, (SmtFormula, bool)>(source._booleanEquivalences);
            _workBudget = source._workBudget;
            _booleanEvaluationDepth = source._booleanEvaluationDepth;
            _booleanFactInferenceDepth = source._booleanFactInferenceDepth;
            _conditionalBranchEvaluationDepth = source._conditionalBranchEvaluationDepth;
        }

        internal static SmtConcreteFactIndex Create(
            IEnumerable<SmtFormula> formulas,
            out bool hasContradiction)
        {
            var facts = new SmtConcreteFactIndex();
            facts.AddAll(formulas, out hasContradiction);
            return facts;
        }

        private void AddAll(IEnumerable<SmtFormula> formulas, out bool hasContradiction)
        {
            hasContradiction = false;
            var formulaArray = formulas as SmtFormula[] ?? formulas.ToArray();
            for (var pass = 0; pass < 4; pass++)
            {
                var addedThisPass = false;
                foreach (var formula in formulaArray)
                {
                    if (Add(formula, out var formulaContradiction)) addedThisPass = true;

                    hasContradiction |= formulaContradiction;
                }

                if (hasContradiction || !addedThisPass) break;
            }
        }

        internal bool Add(SmtFormula formula, out bool hasContradiction)
        {
            hasContradiction = false;
            if (!_workBudget.TryConsume()) return false;

            formula = NormalizeAliases(formula);
            var added = false;
            if (TryAddAliasFact(formula, out var aliasContradiction))
            {
                added = true;
                hasContradiction |= aliasContradiction;
            }

            if (TryAddIntegerIntervalFact(formula, out var integerContradiction))
            {
                added = true;
                hasContradiction |= integerContradiction;
            }

            if (TryAddBooleanFact(formula, out var booleanContradiction))
            {
                added = true;
                hasContradiction |= booleanContradiction;
            }

            if (TryAddStringValueFact(formula, out var stringContradiction))
            {
                added = true;
                hasContradiction |= stringContradiction;
            }

            if (TryAddReferenceNullFact(formula, out var referenceContradiction))
            {
                added = true;
                hasContradiction |= referenceContradiction;
            }

            if (TryEvaluateBoolean(formula, out var value) &&
                !value)
                hasContradiction = true;

            return added || hasContradiction;
        }

        private bool TryAddAliasFact(SmtFormula formula, out bool hasContradiction)
        {
            hasContradiction = false;
            if (formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } negated)
            {
                if (TryGetAliasComparison(negated.Operand, out var negatedLeft, out var negatedRight))
                {
                    hasContradiction = FindCanonical(negatedLeft).Equals(FindCanonical(negatedRight));
                    return hasContradiction;
                }

                return false;
            }

            var added = false;
            if (TryAddAffineIntegerEqualityFact(formula, out var affineContradiction))
            {
                added = true;
                hasContradiction |= affineContradiction;
            }

            if (hasContradiction ||
                formula is SmtBinaryFormula
                {
                    Operator: SmtBinaryOperator.Equal,
                    Left.Kind: SmtValueKind.Int,
                    Right.Kind: SmtValueKind.Int
                })
                return added || hasContradiction;

            if (!TryGetAliasComparison(formula, out var left, out var right)) return added;

            var addedAlias = UnionAliases(left, right, out var aliasContradiction);
            hasContradiction |= aliasContradiction;
            return added || addedAlias;
        }

        private bool TryAddAffineIntegerEqualityFact(SmtFormula formula, out bool hasContradiction)
        {
            hasContradiction = false;
            if (formula is not SmtBinaryFormula { Operator: SmtBinaryOperator.Equal } binary ||
                binary.Left.Kind != SmtValueKind.Int ||
                binary.Right.Kind != SmtValueKind.Int)
                return false;

            var leftFormula = NormalizeAliases(binary.Left);
            var rightFormula = NormalizeAliases(binary.Right);
            if (!TryGetAffineIntegerTerm(leftFormula, 0, out var left) ||
                !TryGetAffineIntegerTerm(rightFormula, 0, out var right))
                return false;

            if (SmtAffineIntegerTerm.TrySubtract(left, right, out var difference))
            {
                if (difference.BaseTerm == null)
                {
                    hasContradiction = difference.Offset != 0;
                    return hasContradiction;
                }

                if (TrySolveSingleAffineEquality(
                        difference,
                        out var solvedTerm,
                        out var solvedConstant,
                        out hasContradiction))
                {
                    if (hasContradiction) return true;

                    return AddIntegerIntervalFact(
                        solvedTerm,
                        SmtBinaryOperator.Equal,
                        solvedConstant,
                        out hasContradiction);
                }
            }

            return TryAddUnitAffineAlias(left, right, out hasContradiction);
        }

        private bool TryAddUnitAffineAlias(
            SmtAffineIntegerTerm left,
            SmtAffineIntegerTerm right,
            out bool hasContradiction)
        {
            hasContradiction = false;
            if (left.BaseTerm == null ||
                right.BaseTerm == null ||
                left.Scale != 1 ||
                right.Scale != 1 ||
                left.BaseTerm.Equals(right.BaseTerm) ||
                !CanAliasTerm(left.BaseTerm) ||
                !CanAliasTerm(right.BaseTerm))
                return false;

            SmtFormula alias;
            SmtFormula baseTerm;
            long offset;
            var leftHasInterval = _integerIntervals.ContainsKey(left.BaseTerm);
            var rightHasInterval = _integerIntervals.ContainsKey(right.BaseTerm);
            if (leftHasInterval && !rightHasInterval)
            {
                alias = right.BaseTerm;
                baseTerm = left.BaseTerm;
                if (!SmtIntegerArithmetic.TrySubtract(left.Offset, right.Offset, out offset)) return false;
            }
            else if (rightHasInterval && !leftHasInterval)
            {
                alias = left.BaseTerm;
                baseTerm = right.BaseTerm;
                if (!SmtIntegerArithmetic.TrySubtract(right.Offset, left.Offset, out offset)) return false;
            }
            else if (string.CompareOrdinal(left.BaseTerm.ToString(), right.BaseTerm.ToString()) <= 0)
            {
                alias = right.BaseTerm;
                baseTerm = left.BaseTerm;
                if (!SmtIntegerArithmetic.TrySubtract(left.Offset, right.Offset, out offset)) return false;
            }
            else
            {
                alias = left.BaseTerm;
                baseTerm = right.BaseTerm;
                if (!SmtIntegerArithmetic.TrySubtract(right.Offset, left.Offset, out offset)) return false;
            }

            var replacement = CreateOffsetTerm(baseTerm, offset);
            return AddDirectedAlias(alias, replacement, out hasContradiction);
        }

        private bool AddDirectedAlias(
            SmtFormula alias,
            SmtFormula canonical,
            out bool hasContradiction)
        {
            hasContradiction = false;
            alias = NormalizeAliases(alias);
            canonical = NormalizeAliases(canonical);
            if (alias.Kind != canonical.Kind ||
                alias.Equals(canonical) ||
                ReferencesFormula(canonical, alias))
                return false;

            hasContradiction = RegisterAlias(alias, canonical);
            return true;
        }

        private static bool TrySolveSingleAffineEquality(
            SmtAffineIntegerTerm difference,
            out SmtFormula term,
            out long constant,
            out bool hasContradiction)
        {
            term = null!;
            constant = default;
            hasContradiction = false;
            if (difference.BaseTerm == null ||
                difference.Scale == 0)
            {
                hasContradiction = difference.Offset != 0;
                return hasContradiction;
            }

            try
            {
                if (difference.Offset % difference.Scale != 0)
                {
                    hasContradiction = true;
                    return true;
                }

                var quotient = difference.Offset / difference.Scale;
                if (!SmtIntegerArithmetic.TryNegate(quotient, out constant)) return false;

                term = difference.BaseTerm;
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static SmtFormula CreateOffsetTerm(SmtFormula baseTerm, long offset)
        {
            return offset == 0
                ? baseTerm
                : new SmtIntegerBinaryTerm(
                    SmtIntegerBinaryOperator.Add,
                    baseTerm,
                    new SmtIntegerConstant(offset));
        }

        private static SmtFormula CreateAffineTerm(SmtAffineIntegerTerm term)
        {
            if (term.BaseTerm == null ||
                term.Scale == 0)
                return new SmtIntegerConstant(term.Offset);

            var scaled = term.Scale switch
            {
                1 => term.BaseTerm,
                -1 => new SmtIntegerUnaryTerm(SmtIntegerUnaryOperator.Negate, term.BaseTerm),
                _ => new SmtIntegerBinaryTerm(
                    SmtIntegerBinaryOperator.Multiply,
                    term.BaseTerm,
                    new SmtIntegerConstant(term.Scale))
            };

            return term.Offset == 0
                ? scaled
                : new SmtIntegerBinaryTerm(
                    SmtIntegerBinaryOperator.Add,
                    scaled,
                    new SmtIntegerConstant(term.Offset));
        }

        private static bool TryGetIntegerBinaryComparison(
            SmtFormula formula,
            out SmtFormula left,
            out SmtBinaryOperator op,
            out SmtFormula right)
        {
            left = null!;
            op = default;
            right = null!;

        if (!SmtComparisonOperatorFacts.TryExtract(
                    formula,
                    out var binary,
                    out var negationCount) ||
                binary.Left.Kind != SmtValueKind.Int ||
                binary.Right.Kind != SmtValueKind.Int)
                return false;

            left = binary.Left;
        op = SmtComparisonOperatorFacts.ApplyNegations(binary.Operator, negationCount);
            right = binary.Right;
            return true;
        }

        private static bool TryGetAliasComparison(
            SmtFormula formula,
            out SmtFormula left,
            out SmtFormula right)
        {
            left = null!;
            right = null!;
            if (formula is not SmtBinaryFormula { Operator: SmtBinaryOperator.Equal } binary ||
                binary.Left.Kind != binary.Right.Kind ||
                binary.Left.Kind == SmtValueKind.Int ||
                binary.Left.Kind == SmtValueKind.Bool ||
                !CanAliasTerm(binary.Left) ||
                !CanAliasTerm(binary.Right))
                return false;

            left = binary.Left;
            right = binary.Right;
            return true;
        }

        private static bool CanAliasTerm(SmtFormula formula)
        {
            return formula is not SmtBooleanConstant and
                not SmtIntegerConstant and
                not SmtStringConstant and
                not SmtNullConstant;
        }

        private bool UnionAliases(
            SmtFormula left,
            SmtFormula right,
            out bool hasContradiction)
        {
            left = FindCanonical(left);
            right = FindCanonical(right);
            hasContradiction = false;
            if (left.Equals(right)) return false;

            var canonical = SelectCanonical(left, right);
            var alias = canonical.Equals(left) ? right : left;
            hasContradiction = RegisterAlias(alias, canonical);
            return true;
        }

        private bool RegisterAlias(SmtFormula alias, SmtFormula canonical)
        {
            _aliases[alias] = (canonical, false);
            MergeIntegerFacts(canonical, alias, out var integerContradiction);
            MergeStringFacts(canonical, alias, out var stringContradiction);
            MergeReferenceFacts(canonical, alias, out var referenceContradiction);
            return integerContradiction || stringContradiction || referenceContradiction;
        }

        private SmtFormula FindCanonical(SmtFormula formula) =>
            FindCanonical(_aliases, formula, out _);

        private static SmtFormula FindCanonical(
            Dictionary<SmtFormula, (SmtFormula Parent, bool Differs)> equivalences,
            SmtFormula formula,
            out bool differsFromCanonical)
        {
            if (!equivalences.TryGetValue(formula, out var parent))
            {
                differsFromCanonical = false;
                return formula;
            }

            var canonical = FindCanonical(equivalences, parent.Parent, out var parentDiffers);
            differsFromCanonical = parent.Differs ^ parentDiffers;
            if (!canonical.Equals(parent.Parent))
                equivalences[formula] = (canonical, differsFromCanonical);

            return canonical;
        }

        private static SmtFormula SelectCanonical(SmtFormula left, SmtFormula right) =>
            string.CompareOrdinal(left.ToString(), right.ToString()) <= 0 ? left : right;

        private void MergeIntegerFacts(
            SmtFormula canonical,
            SmtFormula alias,
            out bool hasContradiction)
        {
            hasContradiction = false;
            if (!_integerIntervals.TryGetValue(alias, out var aliasInterval)) return;

            var interval = _integerIntervals.TryGetValue(canonical, out var existing)
                ? existing.Intersect(aliasInterval)
                : aliasInterval;
            hasContradiction = interval.IsContradictory;
            _integerIntervals[canonical] = interval;
            _integerIntervals.Remove(alias);
        }

        private void MergeStringFacts(
            SmtFormula canonical,
            SmtFormula alias,
            out bool hasContradiction)
        {
            hasContradiction = false;
            if (_excludedStrings.TryGetValue(alias, out var aliasExcluded))
            {
                if (_excludedStrings.TryGetValue(canonical, out var existingExcluded))
                    existingExcluded.UnionWith(aliasExcluded);
                else
                    _excludedStrings[canonical] = new HashSet<string>(aliasExcluded, StringComparer.Ordinal);
                _excludedStrings.Remove(alias);
            }

            if (!_exactStrings.TryGetValue(alias, out var aliasExact)) return;

            if (_exactStrings.TryGetValue(canonical, out var existingExact) &&
                !string.Equals(existingExact, aliasExact, StringComparison.Ordinal))
                hasContradiction = true;

            if (_excludedStrings.TryGetValue(canonical, out var excluded) &&
                excluded.Contains(aliasExact))
                hasContradiction = true;

            _exactStrings[canonical] = aliasExact;
            _exactStrings.Remove(alias);
            AddStringLengthFact(canonical, aliasExact.Length, out var lengthContradiction);
            hasContradiction |= lengthContradiction;
        }

        private void MergeReferenceFacts(
            SmtFormula canonical,
            SmtFormula alias,
            out bool hasContradiction)
        {
            hasContradiction = false;
            if (!_referenceNullStates.TryGetValue(alias, out var aliasIsNull)) return;

            if (_referenceNullStates.TryGetValue(canonical, out var canonicalIsNull))
                hasContradiction = canonicalIsNull != aliasIsNull;
            else
                _referenceNullStates[canonical] = aliasIsNull;

            _referenceNullStates.Remove(alias);
        }

        private SmtFormula NormalizeAliases(SmtFormula formula)
        {
            if (!_workBudget.TryConsume()) return formula;

            return NormalizeAliases(formula, new HashSet<SmtFormula>());
        }

        private SmtFormula NormalizeAliases(SmtFormula formula, HashSet<SmtFormula> visiting)
        {
            if (!_workBudget.TryConsume()) return formula;

            var directCanonical = FindCanonical(formula);
            if (!directCanonical.Equals(formula) &&
                !ReferencesFormula(directCanonical, formula))
                return directCanonical;

            if (!visiting.Add(formula)) return formula;

            var normalized = SmtFormulaTraversal.MapChildren(
                formula,
                child => NormalizeAliases(child, visiting));
            if (normalized is SmtConditionalFormula conditional)
            {
                if (TryEvaluateBoolean(conditional.Condition, out var conditionValue))
                    normalized = conditionValue ? conditional.WhenTrue : conditional.WhenFalse;
                else if (conditional.WhenTrue.Equals(conditional.WhenFalse))
                    normalized = conditional.WhenTrue;
            }

            visiting.Remove(formula);
            var normalizedCanonical = FindCanonical(normalized);
            return !normalizedCanonical.Equals(normalized) &&
                   !ReferencesFormula(normalizedCanonical, normalized)
                ? normalizedCanonical
                : normalized;
        }

        private sealed class SyntacticWorkBudget(int remaining)
        {
            private int _remaining = remaining;

            internal bool TryConsume()
            {
                if (_remaining <= 0) return false;

                _remaining--;
                return true;
            }
        }

        private static bool ReferencesFormula(SmtFormula formula, SmtFormula candidate)
        {
            return !SmtFormulaTraversal.IsWithinDepth(formula, MaxFormulaReferenceDepth + 1) ||
                   SmtFormulaTraversal.Contains(formula, candidate.Equals);
        }

}
