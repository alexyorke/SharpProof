using System.Collections.Immutable;
using SharpProof.ProofCore.Purity;
using SharpProof.ProofCore.Smt;

namespace SharpProof.Symbolic.Smt;

internal static partial class SmtSyntacticClassifier
{
    public static bool TryClassify(
        PurityProofQuery query,
        ImmutableArray<SmtFormula> pathConditions,
        out PurityProofResult result)
    {
        var exceedsNodeBudget = SmtSyntacticFormulaOperations.ExceedsFormulaNodeBudget(
            query.Hazard.TriggerCondition,
            pathConditions,
            out var containsOpaqueIntegerOperation);
        if (containsOpaqueIntegerOperation || exceedsNodeBudget)
        {
            result = PurityProofResultFactory.Unknown(containsOpaqueIntegerOperation
                ? "smt_syntactic_opaque_integer_operation"
                : "smt_syntactic_budget_exhausted");
            return false;
        }

        if (ContainsSyntacticContradiction(pathConditions))
        {
            result = new PurityProofResult(
                PurityProofOutcome.ProvablyPure,
                new ProofCheckInfo(true, Feasibility.Unsatisfiable),
                new ProofCheckInfo(false, Feasibility.Unknown),
                "path_unsatisfiable");
            return true;
        }

        if (IsHazardTriggerSyntacticallyUnreachable(query, pathConditions, out var pureReason))
        {
            result = new PurityProofResult(
                PurityProofOutcome.ProvablyPure,
                new ProofCheckInfo(false, Feasibility.Unknown),
                new ProofCheckInfo(true, Feasibility.Unsatisfiable),
                pureReason);
            return true;
        }

        result = PurityProofResultFactory.Unknown("smt_syntactic_no_match");
        return false;
    }

    private static bool ContainsSyntacticContradiction(ImmutableArray<SmtFormula> pathConditions)
    {
        var seen = new List<SmtFormula>(pathConditions.Length);
        var facts = new SyntacticFactSet();
        var conjuncts = ImmutableArray.CreateBuilder<SmtFormula>();
        foreach (var pathCondition in pathConditions)
            foreach (var conjunct in SmtSyntacticFormulaOperations.EnumerateConjuncts(pathCondition))
            {
                conjuncts.Add(conjunct);
                if (conjunct is SmtBooleanConstant { Value: false }) return true;

                foreach (var existing in seen)
                    if (SmtSyntacticFormulaOperations.AreSyntacticComplements(conjunct, existing))
                        return true;

                if (facts.Add(conjunct, out var hasContradiction) &&
                    hasContradiction)
                    return true;

                seen.Add(conjunct);
            }

        facts.AddAll(conjuncts, out var inferredContradiction);
        if (inferredContradiction) return true;

        foreach (var conjunct in conjuncts)
            if (facts.TryEvaluateBoolean(conjunct, out var value) &&
                !value)
                return true;

        foreach (var condition in SmtSyntacticFormulaOperations.EnumerateConditionalConditions(conjuncts))
            if (facts.IsContradictoryForBothBooleanBranches(condition))
                return true;

        return false;
    }

    private static bool IsHazardTriggerSyntacticallyUnreachable(
        PurityProofQuery query,
        ImmutableArray<SmtFormula> pathConditions,
        out string pureReason)
    {
        pureReason = string.Empty;
        if (!TryGetTriggerBasedPureReason(query.Hazard, out pureReason)) return false;

        if (query.Hazard.TriggerCondition is SmtBooleanConstant { Value: false }) return true;

        if (query.Hazard.TriggerCondition is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } negatedTrigger &&
            IsFormulaSyntacticallyEntailed(negatedTrigger.Operand, pathConditions))
            return true;

        foreach (var pathCondition in pathConditions)
            if (SmtSyntacticFormulaOperations.AreSyntacticComplements(pathCondition, query.Hazard.TriggerCondition))
                return true;

        if (ContainsSyntacticContradiction(pathConditions.Add(query.Hazard.TriggerCondition))) return true;

        pureReason = string.Empty;
        return false;
    }

    private static bool IsFormulaSyntacticallyEntailed(
        SmtFormula formula,
        ImmutableArray<SmtFormula> pathConditions)
    {
        var pathConjuncts = pathConditions
            .SelectMany(SmtSyntacticFormulaOperations.EnumerateConjuncts)
            .ToImmutableArray();
        return IsFormulaSyntacticallyEntailed(formula, pathConditions, pathConjuncts);
    }

    private static bool IsFormulaSyntacticallyEntailed(
        SmtFormula formula,
        ImmutableArray<SmtFormula> pathConditions,
        ImmutableArray<SmtFormula> pathConjuncts)
    {
        var facts = SyntacticFactSet.Create(pathConjuncts);
        if (formula is SmtBooleanConstant booleanConstant) return booleanConstant.Value;

        if (facts.TryEvaluateBoolean(formula, out var value)) return value;

        if (facts.TryClassifyBooleanFromFacts(formula, out value)) return value;

        foreach (var pathConjunct in pathConjuncts)
            if (pathConjunct.Equals(formula))
                return true;

        if (formula is SmtBinaryFormula binary)
        {
            if (binary.Operator == SmtBinaryOperator.And)
                return IsFormulaSyntacticallyEntailed(binary.Left, pathConditions, pathConjuncts) &&
                       IsFormulaSyntacticallyEntailed(binary.Right, pathConditions, pathConjuncts);

            if (binary.Operator == SmtBinaryOperator.Or)
                return IsFormulaSyntacticallyEntailed(binary.Left, pathConditions, pathConjuncts) ||
                       IsFormulaSyntacticallyEntailed(binary.Right, pathConditions, pathConjuncts);
        }

        if (formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } negated)
            return ContainsSyntacticContradiction(pathConditions.Add(negated.Operand));

        return ContainsSyntacticContradiction(pathConditions.Add(new SmtUnaryFormula(SmtUnaryOperator.Not, formula)));
    }

    private static bool TryGetTriggerBasedPureReason(PurityHazard hazard, out string reason)
    {
        reason = string.Empty;
        if (hazard.Visibility == PurityEffectVisibility.InternalOnly) return false;

        reason = hazard.Kind switch
        {
            PurityHazardKind.BranchReachability => "branch_unreachable",
            PurityHazardKind.ImpureCallReachability => "impure_call_unreachable",
            PurityHazardKind.CallerVisibleMemoryWrite => "memory_write_unreachable",
            PurityHazardKind.NullDereference => "null_dereference_unreachable",
            PurityHazardKind.DivideByZero => "divide_by_zero_unreachable",
            _ => string.Empty
        };

        return reason.Length != 0;
    }

    private sealed partial class SyntacticFactSet
    {
        private const int MaxAffineExpansionDepth = 8;
        private const int MaxBooleanEvaluationDepth = 64;
        private const int MaxBooleanFactInferenceDepth = 16;
        private const int MaxConditionalBranchEvaluationDepth = 4;
        private const int MaxFormulaReferenceDepth = 256;
        private const int MaxSyntacticWorkItems = 1048576;
        private readonly Dictionary<SmtFormula, SmtFormula> _aliases = new();
        private readonly Dictionary<SmtFormula, BooleanEquivalenceParent> _booleanEquivalences = new();
        private readonly Dictionary<SmtFormula, bool> _exactBooleans = new();
        private readonly Dictionary<SmtFormula, string> _exactStrings = new();
        private readonly Dictionary<SmtFormula, ImmutableHashSet<string>> _excludedStrings = new();

        private readonly Dictionary<SmtFormula, SmtIntegerInterval> _integerIntervals = new();
        private readonly Dictionary<SmtFormula, bool> _referenceNullStates = new();
        private readonly SyntacticWorkBudget _workBudget;
        private int _booleanEvaluationDepth;
        private int _booleanFactInferenceDepth;
        private int _conditionalBranchEvaluationDepth;

        internal SyntacticFactSet()
        {
            _workBudget = new SyntacticWorkBudget(MaxSyntacticWorkItems);
        }

        private SyntacticFactSet(SyntacticFactSet source)
        {
            _integerIntervals = new Dictionary<SmtFormula, SmtIntegerInterval>(source._integerIntervals);
            _exactStrings = new Dictionary<SmtFormula, string>(source._exactStrings);
            _excludedStrings = new Dictionary<SmtFormula, ImmutableHashSet<string>>(source._excludedStrings);
            _referenceNullStates = new Dictionary<SmtFormula, bool>(source._referenceNullStates);
            _exactBooleans = new Dictionary<SmtFormula, bool>(source._exactBooleans);
            _aliases = new Dictionary<SmtFormula, SmtFormula>(source._aliases);
            _booleanEquivalences = new Dictionary<SmtFormula, BooleanEquivalenceParent>(source._booleanEquivalences);
            _workBudget = source._workBudget;
            _booleanEvaluationDepth = source._booleanEvaluationDepth;
            _booleanFactInferenceDepth = source._booleanFactInferenceDepth;
            _conditionalBranchEvaluationDepth = source._conditionalBranchEvaluationDepth;
        }

        internal static SyntacticFactSet Create(IEnumerable<SmtFormula> formulas)
        {
            var facts = new SyntacticFactSet();
            facts.AddAll(formulas, out _);
            return facts;
        }

        internal bool AddAll(IEnumerable<SmtFormula> formulas, out bool hasContradiction)
        {
            hasContradiction = false;
            var formulaArray = formulas as SmtFormula[] ?? formulas.ToArray();
            var anyAdded = false;
            for (var pass = 0; pass < 4; pass++)
            {
                var addedThisPass = false;
                foreach (var formula in formulaArray)
                {
                    if (Add(formula, out var formulaContradiction))
                    {
                        addedThisPass = true;
                        anyAdded = true;
                    }

                    hasContradiction |= formulaContradiction;
                }

                if (hasContradiction || !addedThisPass) break;
            }

            return anyAdded;
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

            if (!SmtSyntacticFormulaOperations.TryGetComparison(
                    formula,
                    out var binary,
                    out var negationCount) ||
                binary.Left.Kind != SmtValueKind.Int ||
                binary.Right.Kind != SmtValueKind.Int)
                return false;

            left = binary.Left;
            op = SmtSyntacticFormulaOperations.ApplyNegations(binary.Operator, negationCount);
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

            var leftText = left.ToString();
            var rightText = right.ToString();
            var canonical = string.CompareOrdinal(leftText, rightText) <= 0 ? left : right;
            var alias = canonical.Equals(left) ? right : left;
            hasContradiction = RegisterAlias(alias, canonical);
            return true;
        }

        private bool RegisterAlias(SmtFormula alias, SmtFormula canonical)
        {
            _aliases[alias] = canonical;
            MergeIntegerFacts(canonical, alias, out var integerContradiction);
            MergeStringFacts(canonical, alias, out var stringContradiction);
            MergeReferenceFacts(canonical, alias, out var referenceContradiction);
            return integerContradiction || stringContradiction || referenceContradiction;
        }

        private SmtFormula FindCanonical(SmtFormula formula)
        {
            if (!_aliases.TryGetValue(formula, out var parent)) return formula;

            var canonical = FindCanonical(parent);
            if (!canonical.Equals(parent)) _aliases[formula] = canonical;

            return canonical;
        }

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
                _excludedStrings[canonical] = _excludedStrings.TryGetValue(canonical, out var existingExcluded)
                    ? existingExcluded.Union(aliasExcluded)
                    : aliasExcluded;
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

            var normalized = formula switch
            {
                SmtUnaryFormula unary => NormalizeUnaryFormula(unary, visiting),
                SmtBinaryFormula binary => NormalizeBinaryFormula(binary, visiting),
                SmtIntegerUnaryTerm unary => NormalizeIntegerUnaryTerm(unary, visiting),
                SmtIntegerBinaryTerm binary => NormalizeIntegerBinaryTerm(binary, visiting),
                SmtOpaqueIntegerBinaryTerm binary => NormalizeOpaqueIntegerBinaryTerm(binary, visiting),
                SmtStringLengthTerm stringLength => NormalizeStringLengthTerm(stringLength, visiting),
                SmtStringConcatTerm stringConcat => NormalizeStringConcatTerm(stringConcat, visiting),
                SmtStringContainsFormula stringContains => NormalizeStringContainsFormula(stringContains, visiting),
                SmtStringStartsWithFormula stringStartsWith => NormalizeStringStartsWithFormula(stringStartsWith,
                    visiting),
                SmtStringEndsWithFormula stringEndsWith => NormalizeStringEndsWithFormula(stringEndsWith, visiting),
                SmtRegexMatchFormula regexMatch => NormalizeRegexMatchFormula(regexMatch, visiting),
                SmtRuntimeTypeTestFormula runtimeTypeTest => NormalizeRuntimeTypeTestFormula(runtimeTypeTest, visiting),
                SmtConditionalFormula conditional => NormalizeConditionalFormula(conditional, visiting),
                _ => formula
            };

            visiting.Remove(formula);
            var normalizedCanonical = FindCanonical(normalized);
            return !normalizedCanonical.Equals(normalized) &&
                   !ReferencesFormula(normalizedCanonical, normalized)
                ? normalizedCanonical
                : normalized;
        }

        private sealed class SyntacticWorkBudget
        {
            private int _remaining;

            internal SyntacticWorkBudget(int remaining)
            {
                _remaining = remaining;
            }

            internal bool TryConsume()
            {
                if (_remaining <= 0) return false;

                _remaining--;
                return true;
            }
        }

        private SmtFormula NormalizeUnaryFormula(SmtUnaryFormula formula, HashSet<SmtFormula> visiting)
        {
            var operand = NormalizeAliases(formula.Operand, visiting);
            return operand.Equals(formula.Operand)
                ? formula
                : new SmtUnaryFormula(formula.Operator, operand);
        }

        private SmtFormula NormalizeBinaryFormula(SmtBinaryFormula formula, HashSet<SmtFormula> visiting)
        {
            var left = NormalizeAliases(formula.Left, visiting);
            var right = NormalizeAliases(formula.Right, visiting);
            return left.Equals(formula.Left) && right.Equals(formula.Right)
                ? formula
                : new SmtBinaryFormula(formula.Operator, left, right);
        }

        private SmtFormula NormalizeIntegerUnaryTerm(SmtIntegerUnaryTerm formula, HashSet<SmtFormula> visiting)
        {
            var operand = NormalizeAliases(formula.Operand, visiting);
            return operand.Equals(formula.Operand)
                ? formula
                : new SmtIntegerUnaryTerm(formula.Operator, operand);
        }

        private SmtFormula NormalizeIntegerBinaryTerm(SmtIntegerBinaryTerm formula, HashSet<SmtFormula> visiting)
        {
            var left = NormalizeAliases(formula.Left, visiting);
            var right = NormalizeAliases(formula.Right, visiting);
            return left.Equals(formula.Left) && right.Equals(formula.Right)
                ? formula
                : new SmtIntegerBinaryTerm(formula.Operator, left, right);
        }

        private SmtFormula NormalizeOpaqueIntegerBinaryTerm(
            SmtOpaqueIntegerBinaryTerm formula,
            HashSet<SmtFormula> visiting)
        {
            var left = NormalizeAliases(formula.Left, visiting);
            var right = NormalizeAliases(formula.Right, visiting);
            return left.Equals(formula.Left) && right.Equals(formula.Right)
                ? formula
                : new SmtOpaqueIntegerBinaryTerm(formula.Operator, left, right);
        }

        private SmtFormula NormalizeStringLengthTerm(SmtStringLengthTerm formula, HashSet<SmtFormula> visiting)
        {
            var value = NormalizeAliases(formula.Value, visiting);
            return value.Equals(formula.Value)
                ? formula
                : new SmtStringLengthTerm(value);
        }

        private SmtFormula NormalizeStringConcatTerm(SmtStringConcatTerm formula, HashSet<SmtFormula> visiting)
        {
            var left = NormalizeAliases(formula.Left, visiting);
            var right = NormalizeAliases(formula.Right, visiting);
            return left.Equals(formula.Left) && right.Equals(formula.Right)
                ? formula
                : new SmtStringConcatTerm(left, right);
        }

        private SmtFormula NormalizeStringContainsFormula(SmtStringContainsFormula formula,
            HashSet<SmtFormula> visiting)
        {
            var value = NormalizeAliases(formula.Value, visiting);
            var search = NormalizeAliases(formula.Search, visiting);
            return value.Equals(formula.Value) && search.Equals(formula.Search)
                ? formula
                : new SmtStringContainsFormula(value, search);
        }

        private SmtFormula NormalizeStringStartsWithFormula(SmtStringStartsWithFormula formula,
            HashSet<SmtFormula> visiting)
        {
            var value = NormalizeAliases(formula.Value, visiting);
            var prefix = NormalizeAliases(formula.Prefix, visiting);
            return value.Equals(formula.Value) && prefix.Equals(formula.Prefix)
                ? formula
                : new SmtStringStartsWithFormula(value, prefix);
        }

        private SmtFormula NormalizeStringEndsWithFormula(SmtStringEndsWithFormula formula,
            HashSet<SmtFormula> visiting)
        {
            var value = NormalizeAliases(formula.Value, visiting);
            var suffix = NormalizeAliases(formula.Suffix, visiting);
            return value.Equals(formula.Value) && suffix.Equals(formula.Suffix)
                ? formula
                : new SmtStringEndsWithFormula(value, suffix);
        }

        private SmtFormula NormalizeRegexMatchFormula(SmtRegexMatchFormula formula, HashSet<SmtFormula> visiting)
        {
            var value = NormalizeAliases(formula.Value, visiting);
            return value.Equals(formula.Value)
                ? formula
                : new SmtRegexMatchFormula(value, formula.Pattern, formula.Options);
        }

        private SmtFormula NormalizeRuntimeTypeTestFormula(SmtRuntimeTypeTestFormula formula,
            HashSet<SmtFormula> visiting)
        {
            var value = NormalizeAliases(formula.Value, visiting);
            return value.Equals(formula.Value)
                ? formula
                : new SmtRuntimeTypeTestFormula(value, formula.TypeKey);
        }

        private SmtFormula NormalizeConditionalFormula(SmtConditionalFormula formula, HashSet<SmtFormula> visiting)
        {
            var condition = NormalizeAliases(formula.Condition, visiting);
            var whenTrue = NormalizeAliases(formula.WhenTrue, visiting);
            var whenFalse = NormalizeAliases(formula.WhenFalse, visiting);
            if (TryEvaluateBoolean(condition, out var conditionValue)) return conditionValue ? whenTrue : whenFalse;

            if (whenTrue.Equals(whenFalse)) return whenTrue;

            return condition.Equals(formula.Condition) &&
                   whenTrue.Equals(formula.WhenTrue) &&
                   whenFalse.Equals(formula.WhenFalse)
                ? formula
                : new SmtConditionalFormula(condition, whenTrue, whenFalse, formula.ResultKind);
        }

        private static bool ReferencesFormula(SmtFormula formula, SmtFormula candidate)
        {
            return !SmtFormulaTraversal.IsWithinDepth(formula, MaxFormulaReferenceDepth + 1) ||
                   SmtFormulaTraversal.Contains(formula, candidate.Equals);
        }

    }
}
