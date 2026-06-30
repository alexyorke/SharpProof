using System.Text.RegularExpressions;
using Microsoft.Z3;

namespace SearchLib.Smt
{
    public enum Feasibility
    {
        Satisfiable,
        Unsatisfiable,
        Unknown,
    }

    public sealed class SmtSolver : IDisposable
    {
        private static readonly TimeSpan ConcreteRegexValidationTimeout = TimeSpan.FromMilliseconds(50);
        private const int MaxEqualitySubstitutionPasses = 4;
        private const int MaxEqualitySubstitutionReplacementNodes = 32;
        private readonly Z3FormulaEncoder _encoder = new();
        private readonly Dictionary<RegexValidationKey, RegexValidationResult> _regexValidationCache = new();

        public Feasibility IsSatisfiable(IEnumerable<SmtFormula> pathConditions, TimeSpan timeout)
        {
            var preparedStatus = PrepareConcreteFacts(pathConditions.ToArray(), out var preparedConditions);
            if (preparedStatus != ConcreteFactPreparationStatus.Ready)
            {
                return preparedStatus == ConcreteFactPreparationStatus.Unsatisfiable
                    ? Feasibility.Unsatisfiable
                    : Feasibility.Unknown;
            }

            return IsSatisfiableRaw(preparedConditions, timeout);
        }

        private Feasibility IsSatisfiableRaw(IEnumerable<SmtFormula> pathConditions, TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero)
            {
                return Feasibility.Unknown;
            }

            try
            {
                var conditions = pathConditions as SmtFormula[] ?? pathConditions.ToArray();
                var containsApproximateRegex = ContainsApproximateRegex(conditions);
                using var solver = _encoder.CreateSolver(timeout);
                foreach (var formula in conditions)
                {
                    solver.Assert(_encoder.EncodeCondition(formula));
                }

                return AdjustForApproximation(ToFeasibility(solver.Check()), containsApproximateRegex);
            }
            catch (Exception ex) when (IsConservativeSolverFailure(ex))
            {
                return Feasibility.Unknown;
            }
        }

        public Feasibility Implies(IEnumerable<SmtFormula> pathConditions, SmtFormula conclusion, TimeSpan timeout)
        {
            var combinedConditions = pathConditions
                .Concat(new[] { new SmtUnaryFormula(SmtUnaryOperator.Not, conclusion) })
                .ToArray();
            return IsSatisfiable(combinedConditions, timeout);
        }

        public (Feasibility PathFeasibility, Feasibility ImpurityFeasibility) CheckPathAndImpurity(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula impurityCondition,
            TimeSpan timeout)
        {
            var originalPathConditions = pathConditions.ToArray();
            var pathPreparationStatus = PrepareConcreteFacts(originalPathConditions, out var preparedPathConditions);
            if (pathPreparationStatus != ConcreteFactPreparationStatus.Ready)
            {
                return (pathPreparationStatus == ConcreteFactPreparationStatus.Unsatisfiable
                    ? Feasibility.Unsatisfiable
                    : Feasibility.Unknown, Feasibility.Unknown);
            }

            if (timeout <= TimeSpan.Zero)
            {
                return (Feasibility.Unknown, Feasibility.Unknown);
            }

            try
            {
                using var solver = _encoder.CreateSolver(timeout);
                foreach (var formula in preparedPathConditions)
                {
                    solver.Assert(_encoder.EncodeCondition(formula));
                }

                var pathFeasibility = ToFeasibility(solver.Check());
                if (pathFeasibility != Feasibility.Satisfiable)
                {
                    return (pathFeasibility, Feasibility.Unknown);
                }

                // A SAT path under regex approximation is only "may be feasible"; still check the
                // combined query because UNSAT under the over-approximation remains a safe proof.
                //
                // Use the original path facts for the combined query. The path-only preparation pass
                // may remove equalities as already-satisfied facts, but those equalities can still be
                // required to prove the hazard condition unreachable.
                var combinedConditions = originalPathConditions.Concat(new[] { impurityCondition }).ToArray();
                var combinedPreparationStatus = PrepareConcreteFacts(combinedConditions, out var preparedCombinedConditions);
                if (combinedPreparationStatus != ConcreteFactPreparationStatus.Ready)
                {
                    return (pathFeasibility, combinedPreparationStatus == ConcreteFactPreparationStatus.Unsatisfiable
                        ? Feasibility.Unsatisfiable
                        : Feasibility.Unknown);
                }

                if (!ReferenceEquals(preparedCombinedConditions, combinedConditions))
                {
                    return (pathFeasibility, IsSatisfiableRaw(preparedCombinedConditions, timeout));
                }

                solver.Push();
                try
                {
                    solver.Assert(_encoder.EncodeCondition(impurityCondition));
                    var combinedContainsApproximateRegex = ContainsApproximateRegex(combinedConditions);
                    return (pathFeasibility, AdjustForApproximation(
                        ToFeasibility(solver.Check()),
                        combinedContainsApproximateRegex));
                }
                finally
                {
                    solver.Pop();
                }
            }
            catch (Exception ex) when (IsConservativeSolverFailure(ex))
            {
                return (Feasibility.Unknown, Feasibility.Unknown);
            }
        }

        public void Dispose()
        {
            _encoder.Dispose();
        }

        private static Feasibility ToFeasibility(Status status)
        {
            return status switch
            {
                Status.SATISFIABLE => Feasibility.Satisfiable,
                Status.UNSATISFIABLE => Feasibility.Unsatisfiable,
                _ => Feasibility.Unknown,
            };
        }

        private bool ContainsApproximateRegex(IEnumerable<SmtFormula> formulas)
        {
            return formulas.Any(_encoder.ContainsApproximateRegex);
        }

        private static Feasibility AdjustForApproximation(Feasibility feasibility, bool containsApproximateRegex)
        {
            return feasibility == Feasibility.Satisfiable && containsApproximateRegex
                ? Feasibility.Unknown
                : feasibility;
        }

        private static bool IsConservativeSolverFailure(Exception ex)
        {
            return ex is InvalidOperationException ||
                ex is Z3Exception ||
                ex is ArgumentException ||
                ex is InvalidCastException ||
                ex is ArithmeticException;
        }

        private enum ConcreteFactPreparationStatus
        {
            Ready,
            Unsatisfiable,
            Unknown,
        }

        private sealed class ConcreteFactContext
        {
            public Dictionary<SmtFormula, string> StringEqualities { get; } = new();

            public Dictionary<SmtFormula, long> IntegerEqualities { get; } = new();

            public Dictionary<SmtFormula, IntegerBounds> IntegerBounds { get; } = new();

            public HashSet<SmtFormula> IntegerNonZeroFacts { get; } = new();

            public Dictionary<SmtFormula, bool> BooleanEqualities { get; } = new();

            public Dictionary<SmtFormula, bool> ReferenceNullEqualities { get; } = new();
        }

        private struct IntegerBounds
        {
            public long? Lower;

            public long? Upper;

            public bool ExcludesZero;

            public bool IsUnsatisfiable =>
                Lower.HasValue &&
                Upper.HasValue &&
                Lower.Value > Upper.Value ||
                ExcludesZero &&
                Lower.HasValue &&
                Upper.HasValue &&
                Lower.Value == 0 &&
                Upper.Value == 0;
        }

        private ConcreteFactPreparationStatus PrepareConcreteFacts(
            SmtFormula[] conditions,
            out SmtFormula[] preparedConditions)
        {
            var normalizedConditions = new List<SmtFormula>(conditions.Length);
            var changed = false;
            foreach (var condition in conditions)
            {
                var normalizedCondition = SimplifyBooleanConstants(condition, out var conditionChanged);
                changed |= conditionChanged;
                if (normalizedCondition is SmtBooleanConstant { Value: false })
                {
                    preparedConditions = Array.Empty<SmtFormula>();
                    return ConcreteFactPreparationStatus.Unsatisfiable;
                }

                if (normalizedCondition is SmtBooleanConstant { Value: true })
                {
                    changed = true;
                    continue;
                }

                normalizedConditions.Add(normalizedCondition);
            }

            var substitutionStatus = TryApplyEqualitySubstitutions(normalizedConditions, ref changed);
            if (substitutionStatus != ConcreteFactPreparationStatus.Ready)
            {
                preparedConditions = Array.Empty<SmtFormula>();
                return substitutionStatus;
            }

            var facts = new ConcreteFactContext();
            if (!TryCollectBooleanFacts(normalizedConditions, facts))
            {
                preparedConditions = Array.Empty<SmtFormula>();
                return ConcreteFactPreparationStatus.Unsatisfiable;
            }

            var conditionalStatus = SimplifyKnownConditionalTerms(normalizedConditions, facts, ref changed);
            if (conditionalStatus != ConcreteFactPreparationStatus.Ready)
            {
                preparedConditions = Array.Empty<SmtFormula>();
                return conditionalStatus;
            }

            if (!TryCollectBooleanFacts(normalizedConditions, facts))
            {
                preparedConditions = Array.Empty<SmtFormula>();
                return ConcreteFactPreparationStatus.Unsatisfiable;
            }

            var referenceStatus = TryCollectReferenceFacts(normalizedConditions, facts);
            if (referenceStatus != ConcreteFactPreparationStatus.Ready)
            {
                preparedConditions = Array.Empty<SmtFormula>();
                return referenceStatus;
            }

            if (!TryCollectStringEqualities(normalizedConditions, facts))
            {
                preparedConditions = Array.Empty<SmtFormula>();
                return ConcreteFactPreparationStatus.Unsatisfiable;
            }

            var integerStatus = TryCollectIntegerFacts(normalizedConditions, facts);
            if (integerStatus != ConcreteFactPreparationStatus.Ready)
            {
                preparedConditions = Array.Empty<SmtFormula>();
                return integerStatus;
            }

            foreach (var condition in normalizedConditions)
            {
                integerStatus = ValidateIntegerTermSafety(condition, facts);
                if (integerStatus != ConcreteFactPreparationStatus.Ready)
                {
                    preparedConditions = Array.Empty<SmtFormula>();
                    return integerStatus;
                }
            }

            var stringLengthEqualities = new Dictionary<SmtFormula, long>();
            foreach (var condition in normalizedConditions)
            {
                if (!TryCollectStringLengthEqualities(condition, stringLengthEqualities))
                {
                    preparedConditions = Array.Empty<SmtFormula>();
                    return ConcreteFactPreparationStatus.Unsatisfiable;
                }
            }

            foreach (var condition in normalizedConditions)
            {
                var status = TryInferStringEqualitiesFromLengthConstrainedPredicates(
                    condition,
                    stringLengthEqualities,
                    facts);
                if (status != ConcreteFactPreparationStatus.Ready)
                {
                    preparedConditions = Array.Empty<SmtFormula>();
                    return status;
                }
            }

            var stringShapeStatus = TryApplyStringShapeFacts(
                normalizedConditions,
                stringLengthEqualities,
                facts);
            if (stringShapeStatus != ConcreteFactPreparationStatus.Ready)
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
                if (status != ConcreteFactPreparationStatus.Ready)
                {
                    preparedConditions = Array.Empty<SmtFormula>();
                    return status;
                }

                changed |= conditionChanged;
                if (preparedCondition is SmtBooleanConstant { Value: false })
                {
                    preparedConditions = Array.Empty<SmtFormula>();
                    return ConcreteFactPreparationStatus.Unsatisfiable;
                }

                if (preparedCondition is SmtBooleanConstant { Value: true })
                {
                    changed = true;
                    continue;
                }

                builder.Add(preparedCondition);
            }

            preparedConditions = changed ? builder.ToArray() : conditions;
            return ConcreteFactPreparationStatus.Ready;
        }

        private static ConcreteFactPreparationStatus TryApplyEqualitySubstitutions(
            List<SmtFormula> conditions,
            ref bool changed)
        {
            for (var pass = 0; pass < MaxEqualitySubstitutionPasses; pass++)
            {
                var substitutions = new Dictionary<SmtVariable, SmtFormula>();
                foreach (var condition in conditions)
                {
                    TryCollectEqualitySubstitutions(condition, substitutions);
                }

                if (substitutions.Count == 0)
                {
                    return ConcreteFactPreparationStatus.Ready;
                }

                var passChanged = false;
                for (var index = conditions.Count - 1; index >= 0; index--)
                {
                    var substituted = SubstituteEqualityAliases(
                        conditions[index],
                        substitutions,
                        out var substitutedChanged);
                    if (substitutedChanged)
                    {
                        substituted = SimplifyBooleanConstants(substituted, out _);
                    }

                    if (substituted is SmtBooleanConstant { Value: false })
                    {
                        return ConcreteFactPreparationStatus.Unsatisfiable;
                    }

                    if (substituted is SmtBooleanConstant { Value: true })
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
                if (!passChanged)
                {
                    return ConcreteFactPreparationStatus.Ready;
                }
            }

            return ConcreteFactPreparationStatus.Ready;
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
                {
                    TryAddEqualitySubstitution(rightVariable, leftVariable, substitutions);
                }
                else if (comparison > 0)
                {
                    TryAddEqualitySubstitution(leftVariable, rightVariable, substitutions);
                }

                return;
            }

            if (left is SmtVariable variableLeft)
            {
                TryAddEqualitySubstitution(variableLeft, right, substitutions);
                return;
            }

            if (right is SmtVariable variableRight)
            {
                TryAddEqualitySubstitution(variableRight, left, substitutions);
            }
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
            {
                return;
            }

            if (substitutions.TryGetValue(source, out var existing))
            {
                return;
            }

            substitutions.Add(source, replacement);
        }

        private static bool WouldCreateSubstitutionCycle(
            SmtVariable source,
            SmtFormula replacement,
            IReadOnlyDictionary<SmtVariable, SmtFormula> substitutions,
            int remainingDepth)
        {
            if (remainingDepth < 0)
            {
                return true;
            }

            switch (replacement)
            {
                case SmtVariable variable:
                    if (EqualityComparer<SmtFormula>.Default.Equals(variable, source))
                    {
                        return true;
                    }

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
                    return WouldCreateSubstitutionCycle(source, conditionalFormula.Condition, substitutions, remainingDepth) ||
                        WouldCreateSubstitutionCycle(source, conditionalFormula.WhenTrue, substitutions, remainingDepth) ||
                        WouldCreateSubstitutionCycle(source, conditionalFormula.WhenFalse, substitutions, remainingDepth);
                default:
                    return false;
            }
        }

        private static SmtFormula SubstituteEqualityAliases(
            SmtFormula formula,
            IReadOnlyDictionary<SmtVariable, SmtFormula> substitutions,
            out bool changed)
        {
            return SubstituteEqualityAliases(formula, substitutions, substitutions.Count + 1, out changed);
        }

        private static SmtFormula SubstituteEqualityAliases(
            SmtFormula formula,
            IReadOnlyDictionary<SmtVariable, SmtFormula> substitutions,
            int remainingDepth,
            out bool changed)
        {
            changed = false;
            if (remainingDepth < 0)
            {
                return formula;
            }

            switch (formula)
            {
                case SmtVariable variable when substitutions.TryGetValue(variable, out var replacement):
                    changed = true;
                    return SubstituteEqualityAliases(
                        replacement,
                        substitutions,
                        remainingDepth - 1,
                        out _);
                case SmtUnaryFormula unaryFormula:
                    {
                        var operand = SubstituteEqualityAliases(
                            unaryFormula.Operand,
                            substitutions,
                            remainingDepth,
                            out var operandChanged);
                        changed = operandChanged;
                        return operandChanged
                            ? new SmtUnaryFormula(unaryFormula.Operator, operand)
                            : formula;
                    }
                case SmtBinaryFormula binaryFormula:
                    {
                        var left = SubstituteEqualityAliases(
                            binaryFormula.Left,
                            substitutions,
                            remainingDepth,
                            out var leftChanged);
                        var right = SubstituteEqualityAliases(
                            binaryFormula.Right,
                            substitutions,
                            remainingDepth,
                            out var rightChanged);
                        changed = leftChanged || rightChanged;
                        return changed
                            ? new SmtBinaryFormula(binaryFormula.Operator, left, right)
                            : formula;
                    }
                case SmtIntegerUnaryTerm integerUnaryTerm:
                    {
                        var operand = SubstituteEqualityAliases(
                            integerUnaryTerm.Operand,
                            substitutions,
                            remainingDepth,
                            out var operandChanged);
                        changed = operandChanged;
                        return operandChanged
                            ? new SmtIntegerUnaryTerm(integerUnaryTerm.Operator, operand)
                            : formula;
                    }
                case SmtIntegerBinaryTerm integerBinaryTerm:
                    {
                        var left = SubstituteEqualityAliases(
                            integerBinaryTerm.Left,
                            substitutions,
                            remainingDepth,
                            out var leftChanged);
                        var right = SubstituteEqualityAliases(
                            integerBinaryTerm.Right,
                            substitutions,
                            remainingDepth,
                            out var rightChanged);
                        changed = leftChanged || rightChanged;
                        return changed
                            ? new SmtIntegerBinaryTerm(integerBinaryTerm.Operator, left, right)
                            : formula;
                    }
                case SmtStringLengthTerm stringLengthTerm:
                    {
                        var value = SubstituteEqualityAliases(
                            stringLengthTerm.Value,
                            substitutions,
                            remainingDepth,
                            out var valueChanged);
                        changed = valueChanged;
                        return valueChanged ? new SmtStringLengthTerm(value) : formula;
                    }
                case SmtStringConcatTerm stringConcatTerm:
                    {
                        var left = SubstituteEqualityAliases(
                            stringConcatTerm.Left,
                            substitutions,
                            remainingDepth,
                            out var leftChanged);
                        var right = SubstituteEqualityAliases(
                            stringConcatTerm.Right,
                            substitutions,
                            remainingDepth,
                            out var rightChanged);
                        changed = leftChanged || rightChanged;
                        return changed ? new SmtStringConcatTerm(left, right) : formula;
                    }
                case SmtStringContainsFormula stringContains:
                    {
                        var value = SubstituteEqualityAliases(
                            stringContains.Value,
                            substitutions,
                            remainingDepth,
                            out var valueChanged);
                        var search = SubstituteEqualityAliases(
                            stringContains.Search,
                            substitutions,
                            remainingDepth,
                            out var searchChanged);
                        changed = valueChanged || searchChanged;
                        return changed ? new SmtStringContainsFormula(value, search) : formula;
                    }
                case SmtStringStartsWithFormula stringStartsWith:
                    {
                        var value = SubstituteEqualityAliases(
                            stringStartsWith.Value,
                            substitutions,
                            remainingDepth,
                            out var valueChanged);
                        var prefix = SubstituteEqualityAliases(
                            stringStartsWith.Prefix,
                            substitutions,
                            remainingDepth,
                            out var prefixChanged);
                        changed = valueChanged || prefixChanged;
                        return changed ? new SmtStringStartsWithFormula(value, prefix) : formula;
                    }
                case SmtStringEndsWithFormula stringEndsWith:
                    {
                        var value = SubstituteEqualityAliases(
                            stringEndsWith.Value,
                            substitutions,
                            remainingDepth,
                            out var valueChanged);
                        var suffix = SubstituteEqualityAliases(
                            stringEndsWith.Suffix,
                            substitutions,
                            remainingDepth,
                            out var suffixChanged);
                        changed = valueChanged || suffixChanged;
                        return changed ? new SmtStringEndsWithFormula(value, suffix) : formula;
                    }
                case SmtRegexMatchFormula regexMatch:
                    {
                        var value = SubstituteEqualityAliases(
                            regexMatch.Value,
                            substitutions,
                            remainingDepth,
                            out var valueChanged);
                        changed = valueChanged;
                        return valueChanged
                            ? new SmtRegexMatchFormula(value, regexMatch.Pattern, regexMatch.Options)
                            : formula;
                    }
                case SmtRuntimeTypeTestFormula runtimeTypeTest:
                    {
                        var value = SubstituteEqualityAliases(
                            runtimeTypeTest.Value,
                            substitutions,
                            remainingDepth,
                            out var valueChanged);
                        changed = valueChanged;
                        return valueChanged
                            ? new SmtRuntimeTypeTestFormula(value, runtimeTypeTest.TypeKey)
                            : formula;
                    }
                case SmtConditionalFormula conditionalFormula:
                    {
                        var condition = SubstituteEqualityAliases(
                            conditionalFormula.Condition,
                            substitutions,
                            remainingDepth,
                            out var conditionChanged);
                        var whenTrue = SubstituteEqualityAliases(
                            conditionalFormula.WhenTrue,
                            substitutions,
                            remainingDepth,
                            out var trueChanged);
                        var whenFalse = SubstituteEqualityAliases(
                            conditionalFormula.WhenFalse,
                            substitutions,
                            remainingDepth,
                            out var falseChanged);
                        changed = conditionChanged || trueChanged || falseChanged;
                        return changed
                            ? new SmtConditionalFormula(condition, whenTrue, whenFalse, conditionalFormula.ResultKind)
                            : formula;
                    }
                default:
                    return formula;
            }
        }

        private static int CountFormulaNodes(SmtFormula formula)
        {
            return formula switch
            {
                SmtUnaryFormula unaryFormula => 1 + CountFormulaNodes(unaryFormula.Operand),
                SmtBinaryFormula binaryFormula => 1 + CountFormulaNodes(binaryFormula.Left) + CountFormulaNodes(binaryFormula.Right),
                SmtIntegerUnaryTerm integerUnaryTerm => 1 + CountFormulaNodes(integerUnaryTerm.Operand),
                SmtIntegerBinaryTerm integerBinaryTerm => 1 + CountFormulaNodes(integerBinaryTerm.Left) + CountFormulaNodes(integerBinaryTerm.Right),
                SmtStringLengthTerm stringLengthTerm => 1 + CountFormulaNodes(stringLengthTerm.Value),
                SmtStringConcatTerm stringConcatTerm => 1 + CountFormulaNodes(stringConcatTerm.Left) + CountFormulaNodes(stringConcatTerm.Right),
                SmtStringContainsFormula stringContains => 1 + CountFormulaNodes(stringContains.Value) + CountFormulaNodes(stringContains.Search),
                SmtStringStartsWithFormula stringStartsWith => 1 + CountFormulaNodes(stringStartsWith.Value) + CountFormulaNodes(stringStartsWith.Prefix),
                SmtStringEndsWithFormula stringEndsWith => 1 + CountFormulaNodes(stringEndsWith.Value) + CountFormulaNodes(stringEndsWith.Suffix),
                SmtRegexMatchFormula regexMatch => 1 + CountFormulaNodes(regexMatch.Value),
                SmtRuntimeTypeTestFormula runtimeTypeTest => 1 + CountFormulaNodes(runtimeTypeTest.Value),
                SmtConditionalFormula conditionalFormula => 1 + CountFormulaNodes(conditionalFormula.Condition) +
                    CountFormulaNodes(conditionalFormula.WhenTrue) +
                    CountFormulaNodes(conditionalFormula.WhenFalse),
                _ => 1,
            };
        }

        private static ConcreteFactPreparationStatus SimplifyKnownConditionalTerms(
            List<SmtFormula> conditions,
            ConcreteFactContext facts,
            ref bool changed)
        {
            for (var index = 0; index < conditions.Count; index++)
            {
                var simplified = SimplifyKnownConditionalTerms(conditions[index], facts, out var conditionChanged);
                changed |= conditionChanged;
                if (simplified is SmtBooleanConstant { Value: false })
                {
                    return ConcreteFactPreparationStatus.Unsatisfiable;
                }

                conditions[index] = simplified;
            }

            return ConcreteFactPreparationStatus.Ready;
        }

        private static SmtFormula SimplifyKnownConditionalTerms(
            SmtFormula formula,
            ConcreteFactContext facts,
            out bool changed)
        {
            changed = false;
            switch (formula)
            {
                case SmtUnaryFormula unaryFormula:
                    {
                        var operand = SimplifyKnownConditionalTerms(unaryFormula.Operand, facts, out var operandChanged);
                        changed = operandChanged;
                        return operandChanged
                            ? new SmtUnaryFormula(unaryFormula.Operator, operand)
                            : formula;
                    }
                case SmtBinaryFormula binaryFormula:
                    {
                        var left = SimplifyKnownConditionalTerms(binaryFormula.Left, facts, out var leftChanged);
                        var right = SimplifyKnownConditionalTerms(binaryFormula.Right, facts, out var rightChanged);
                        changed = leftChanged || rightChanged;
                        return changed
                            ? new SmtBinaryFormula(binaryFormula.Operator, left, right)
                            : formula;
                    }
                case SmtIntegerUnaryTerm integerUnaryTerm:
                    {
                        var operand = SimplifyKnownConditionalTerms(integerUnaryTerm.Operand, facts, out var operandChanged);
                        changed = operandChanged;
                        return operandChanged
                            ? new SmtIntegerUnaryTerm(integerUnaryTerm.Operator, operand)
                            : formula;
                    }
                case SmtIntegerBinaryTerm integerBinaryTerm:
                    {
                        var left = SimplifyKnownConditionalTerms(integerBinaryTerm.Left, facts, out var leftChanged);
                        var right = SimplifyKnownConditionalTerms(integerBinaryTerm.Right, facts, out var rightChanged);
                        changed = leftChanged || rightChanged;
                        return changed
                            ? new SmtIntegerBinaryTerm(integerBinaryTerm.Operator, left, right)
                            : formula;
                    }
                case SmtStringLengthTerm stringLengthTerm:
                    {
                        var value = SimplifyKnownConditionalTerms(stringLengthTerm.Value, facts, out var valueChanged);
                        changed = valueChanged;
                        return valueChanged
                            ? new SmtStringLengthTerm(value)
                            : formula;
                    }
                case SmtStringConcatTerm stringConcatTerm:
                    {
                        var left = SimplifyKnownConditionalTerms(stringConcatTerm.Left, facts, out var leftChanged);
                        var right = SimplifyKnownConditionalTerms(stringConcatTerm.Right, facts, out var rightChanged);
                        changed = leftChanged || rightChanged;
                        return changed
                            ? new SmtStringConcatTerm(left, right)
                            : formula;
                    }
                case SmtStringContainsFormula stringContains:
                    {
                        var value = SimplifyKnownConditionalTerms(stringContains.Value, facts, out var valueChanged);
                        var search = SimplifyKnownConditionalTerms(stringContains.Search, facts, out var searchChanged);
                        changed = valueChanged || searchChanged;
                        return changed
                            ? new SmtStringContainsFormula(value, search)
                            : formula;
                    }
                case SmtStringStartsWithFormula stringStartsWith:
                    {
                        var value = SimplifyKnownConditionalTerms(stringStartsWith.Value, facts, out var valueChanged);
                        var prefix = SimplifyKnownConditionalTerms(stringStartsWith.Prefix, facts, out var prefixChanged);
                        changed = valueChanged || prefixChanged;
                        return changed
                            ? new SmtStringStartsWithFormula(value, prefix)
                            : formula;
                    }
                case SmtStringEndsWithFormula stringEndsWith:
                    {
                        var value = SimplifyKnownConditionalTerms(stringEndsWith.Value, facts, out var valueChanged);
                        var suffix = SimplifyKnownConditionalTerms(stringEndsWith.Suffix, facts, out var suffixChanged);
                        changed = valueChanged || suffixChanged;
                        return changed
                            ? new SmtStringEndsWithFormula(value, suffix)
                            : formula;
                    }
                case SmtRegexMatchFormula regexMatch:
                    {
                        var value = SimplifyKnownConditionalTerms(regexMatch.Value, facts, out var valueChanged);
                        changed = valueChanged;
                        return valueChanged
                            ? new SmtRegexMatchFormula(value, regexMatch.Pattern, regexMatch.Options)
                            : formula;
                    }
                case SmtRuntimeTypeTestFormula runtimeTypeTest:
                    {
                        var value = SimplifyKnownConditionalTerms(runtimeTypeTest.Value, facts, out var valueChanged);
                        changed = valueChanged;
                        return valueChanged
                            ? new SmtRuntimeTypeTestFormula(value, runtimeTypeTest.TypeKey)
                            : formula;
                    }
                case SmtConditionalFormula conditionalFormula:
                    {
                        if (EqualityComparer<SmtFormula>.Default.Equals(
                                conditionalFormula.WhenTrue,
                                conditionalFormula.WhenFalse))
                        {
                            changed = true;
                            return SimplifyKnownConditionalTerms(
                                conditionalFormula.WhenTrue,
                                facts,
                                out _);
                        }

                        if (TryEvaluateConcreteBoolean(conditionalFormula.Condition, facts, out var selectedBranch))
                        {
                            changed = true;
                            return SimplifyKnownConditionalTerms(
                                selectedBranch ? conditionalFormula.WhenTrue : conditionalFormula.WhenFalse,
                                facts,
                                out _);
                        }

                        var condition = SimplifyKnownConditionalTerms(conditionalFormula.Condition, facts, out var conditionChanged);
                        var whenTrue = SimplifyKnownConditionalTerms(conditionalFormula.WhenTrue, facts, out var whenTrueChanged);
                        var whenFalse = SimplifyKnownConditionalTerms(conditionalFormula.WhenFalse, facts, out var whenFalseChanged);
                        changed = conditionChanged || whenTrueChanged || whenFalseChanged;
                        return changed
                            ? new SmtConditionalFormula(condition, whenTrue, whenFalse, conditionalFormula.ResultKind)
                            : formula;
                    }
                default:
                    return formula;
            }
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

            if (formula is not SmtBinaryFormula binaryFormula)
            {
                return formula;
            }

            if (binaryFormula.Operator is not SmtBinaryOperator.And and not SmtBinaryOperator.Or)
            {
                return formula;
            }

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
                    _ => default,
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
            return left is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } leftNot &&
                    EqualityComparer<SmtFormula>.Default.Equals(leftNot.Operand, right) ||
                right is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } rightNot &&
                    EqualityComparer<SmtFormula>.Default.Equals(rightNot.Operand, left);
        }

        private static bool TryCollectBooleanFacts(
            IReadOnlyList<SmtFormula> conditions,
            ConcreteFactContext facts)
        {
            var iterationLimit = Math.Max(1, conditions.Count * 4);
            var changed = false;
            do
            {
                changed = false;
                foreach (var condition in conditions)
                {
                    if (!TryCollectBooleanFacts(condition, facts, ref changed))
                    {
                        return false;
                    }
                }

                iterationLimit--;
            }
            while (changed && iterationLimit > 0);

            return true;
        }

        private static bool TryCollectBooleanFacts(
            SmtFormula formula,
            ConcreteFactContext facts,
            ref bool changed)
        {
            if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
            {
                return TryCollectBooleanFacts(andFormula.Left, facts, ref changed) &&
                    TryCollectBooleanFacts(andFormula.Right, facts, ref changed);
            }

            if (formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } notFormula)
            {
                return CanCacheBooleanFact(notFormula.Operand)
                    ? TryAddBooleanEquality(facts, notFormula.Operand, false, ref changed)
                    : true;
            }

            if (formula is SmtBinaryFormula
                {
                    Operator: SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual
                } equalityFormula &&
                equalityFormula.Left.Kind == SmtValueKind.Bool &&
                equalityFormula.Right.Kind == SmtValueKind.Bool)
            {
                if (TryEvaluateConcreteBoolean(equalityFormula.Left, facts, out var leftValue))
                {
                    var expectedRight = equalityFormula.Operator == SmtBinaryOperator.Equal
                        ? leftValue
                        : !leftValue;
                    return TryAddBooleanEquality(facts, equalityFormula.Right, expectedRight, ref changed);
                }

                if (TryEvaluateConcreteBoolean(equalityFormula.Right, facts, out var rightValue))
                {
                    var expectedLeft = equalityFormula.Operator == SmtBinaryOperator.Equal
                        ? rightValue
                        : !rightValue;
                    return TryAddBooleanEquality(facts, equalityFormula.Left, expectedLeft, ref changed);
                }
            }

            if (formula.Kind == SmtValueKind.Bool &&
                CanCacheBooleanFact(formula))
            {
                return TryAddBooleanEquality(facts, formula, true, ref changed);
            }

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
            {
                return true;
            }

            if (facts.BooleanEqualities.TryGetValue(formula, out var existing))
            {
                return existing == value;
            }

            facts.BooleanEqualities.Add(formula, value);
            changed = true;
            return true;
        }

        private static bool CanCacheBooleanFact(SmtFormula formula)
        {
            if (formula is SmtVariable { Kind: SmtValueKind.Bool })
            {
                return true;
            }

            if (formula is SmtRuntimeTypeTestFormula)
            {
                return true;
            }

            if (formula is not SmtBinaryFormula binaryFormula)
            {
                return false;
            }

            if (binaryFormula.Operator is SmtBinaryOperator.And or SmtBinaryOperator.Or)
            {
                return false;
            }

            if (binaryFormula.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual &&
                binaryFormula.Left.Kind == SmtValueKind.Bool &&
                binaryFormula.Right.Kind == SmtValueKind.Bool)
            {
                return false;
            }

            return !ContainsRegexOrStringPredicate(binaryFormula);
        }

        private static bool ContainsRegexOrStringPredicate(SmtFormula formula)
        {
            return formula switch
            {
                SmtRegexMatchFormula => true,
                SmtStringContainsFormula => true,
                SmtStringStartsWithFormula => true,
                SmtStringEndsWithFormula => true,
                SmtUnaryFormula unaryFormula => ContainsRegexOrStringPredicate(unaryFormula.Operand),
                SmtBinaryFormula binaryFormula => ContainsRegexOrStringPredicate(binaryFormula.Left) ||
                    ContainsRegexOrStringPredicate(binaryFormula.Right),
                SmtIntegerUnaryTerm integerUnaryTerm => ContainsRegexOrStringPredicate(integerUnaryTerm.Operand),
                SmtIntegerBinaryTerm integerBinaryTerm => ContainsRegexOrStringPredicate(integerBinaryTerm.Left) ||
                    ContainsRegexOrStringPredicate(integerBinaryTerm.Right),
                SmtStringLengthTerm stringLengthTerm => ContainsRegexOrStringPredicate(stringLengthTerm.Value),
                SmtStringConcatTerm stringConcatTerm => ContainsRegexOrStringPredicate(stringConcatTerm.Left) ||
                    ContainsRegexOrStringPredicate(stringConcatTerm.Right),
                SmtConditionalFormula conditionalFormula => ContainsRegexOrStringPredicate(conditionalFormula.Condition) ||
                    ContainsRegexOrStringPredicate(conditionalFormula.WhenTrue) ||
                    ContainsRegexOrStringPredicate(conditionalFormula.WhenFalse),
                SmtRuntimeTypeTestFormula runtimeTypeTest => ContainsRegexOrStringPredicate(runtimeTypeTest.Value),
                _ => false,
            };
        }

        private static ConcreteFactPreparationStatus TryCollectReferenceFacts(
            IReadOnlyList<SmtFormula> conditions,
            ConcreteFactContext facts)
        {
            var iterationLimit = Math.Max(1, conditions.Count * 4);
            var changed = false;
            do
            {
                changed = false;
                foreach (var condition in conditions)
                {
                    var status = TryCollectReferenceFacts(condition, facts, ref changed);
                    if (status != ConcreteFactPreparationStatus.Ready)
                    {
                        return status;
                    }
                }

                iterationLimit--;
            }
            while (changed && iterationLimit > 0);

            return ConcreteFactPreparationStatus.Ready;
        }

        private static ConcreteFactPreparationStatus TryCollectReferenceFacts(
            SmtFormula formula,
            ConcreteFactContext facts,
            ref bool changed)
        {
            if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
            {
                var leftStatus = TryCollectReferenceFacts(andFormula.Left, facts, ref changed);
                if (leftStatus != ConcreteFactPreparationStatus.Ready)
                {
                    return leftStatus;
                }

                return TryCollectReferenceFacts(andFormula.Right, facts, ref changed);
            }

            if (formula is not SmtBinaryFormula
                {
                    Operator: SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual
                } binaryFormula ||
                binaryFormula.Left.Kind != SmtValueKind.Reference ||
                binaryFormula.Right.Kind != SmtValueKind.Reference)
            {
                return ConcreteFactPreparationStatus.Ready;
            }

            var isEquality = binaryFormula.Operator == SmtBinaryOperator.Equal;
            if (EqualityComparer<SmtFormula>.Default.Equals(binaryFormula.Left, binaryFormula.Right))
            {
                return isEquality
                    ? ConcreteFactPreparationStatus.Ready
                    : ConcreteFactPreparationStatus.Unsatisfiable;
            }

            if (binaryFormula.Left is SmtNullConstant)
            {
                return TryAddReferenceNullEquality(facts, binaryFormula.Right, isEquality, ref changed)
                    ? ConcreteFactPreparationStatus.Ready
                    : ConcreteFactPreparationStatus.Unsatisfiable;
            }

            if (binaryFormula.Right is SmtNullConstant)
            {
                return TryAddReferenceNullEquality(facts, binaryFormula.Left, isEquality, ref changed)
                    ? ConcreteFactPreparationStatus.Ready
                    : ConcreteFactPreparationStatus.Unsatisfiable;
            }

            var leftKnown = TryEvaluateReferenceNull(binaryFormula.Left, facts, out var leftIsNull);
            var rightKnown = TryEvaluateReferenceNull(binaryFormula.Right, facts, out var rightIsNull);
            if (leftKnown && rightKnown && (leftIsNull || rightIsNull))
            {
                var equal = leftIsNull && rightIsNull;
                return CompareEquality(binaryFormula.Operator, equal)
                    ? ConcreteFactPreparationStatus.Ready
                    : ConcreteFactPreparationStatus.Unsatisfiable;
            }

            if (!isEquality)
            {
                return ConcreteFactPreparationStatus.Ready;
            }

            if (leftKnown)
            {
                return TryAddReferenceNullEquality(facts, binaryFormula.Right, leftIsNull, ref changed)
                    ? ConcreteFactPreparationStatus.Ready
                    : ConcreteFactPreparationStatus.Unsatisfiable;
            }

            if (rightKnown)
            {
                return TryAddReferenceNullEquality(facts, binaryFormula.Left, rightIsNull, ref changed)
                    ? ConcreteFactPreparationStatus.Ready
                    : ConcreteFactPreparationStatus.Unsatisfiable;
            }

            return ConcreteFactPreparationStatus.Ready;
        }

        private static bool TryAddReferenceNullEquality(
            ConcreteFactContext facts,
            SmtFormula formula,
            bool isNull,
            ref bool changed)
        {
            if (formula.Kind != SmtValueKind.Reference)
            {
                return true;
            }

            if (formula is SmtNullConstant)
            {
                return isNull;
            }

            if (facts.ReferenceNullEqualities.TryGetValue(formula, out var existing))
            {
                return existing == isNull;
            }

            facts.ReferenceNullEqualities.Add(formula, isNull);
            changed = true;
            return true;
        }

        private static ConcreteFactPreparationStatus TryCollectIntegerFacts(
            IReadOnlyList<SmtFormula> conditions,
            ConcreteFactContext facts)
        {
            var iterationLimit = Math.Max(1, conditions.Count * 4);
            var changed = false;
            do
            {
                changed = false;
                foreach (var condition in conditions)
                {
                    var status = TryCollectIntegerFacts(
                        condition,
                        facts,
                        ref changed);
                    if (status != ConcreteFactPreparationStatus.Ready)
                    {
                        return status;
                    }
                }

                iterationLimit--;
            }
            while (changed && iterationLimit > 0);

            return ConcreteFactPreparationStatus.Ready;
        }

        private static ConcreteFactPreparationStatus TryCollectIntegerFacts(
            SmtFormula formula,
            ConcreteFactContext facts,
            ref bool changed)
        {
            if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
            {
                var leftStatus = TryCollectIntegerFacts(
                    andFormula.Left,
                    facts,
                    ref changed);
                if (leftStatus != ConcreteFactPreparationStatus.Ready)
                {
                    return leftStatus;
                }

                return TryCollectIntegerFacts(
                    andFormula.Right,
                    facts,
                    ref changed);
            }

            if (formula is not SmtBinaryFormula binaryFormula)
            {
                return ConcreteFactPreparationStatus.Ready;
            }

            TryCollectIntegerNonZeroFact(binaryFormula, facts, ref changed);
            var boundStatus = TryCollectIntegerBoundFact(binaryFormula, facts, ref changed);
            if (boundStatus != ConcreteFactPreparationStatus.Ready)
            {
                return boundStatus;
            }

            if (binaryFormula.Operator == SmtBinaryOperator.NotEqual &&
                TryEvaluateInteger(binaryFormula.Left, facts, out var notEqualLeft) &&
                TryEvaluateInteger(binaryFormula.Right, facts, out var notEqualRight) &&
                notEqualLeft == notEqualRight)
            {
                return ConcreteFactPreparationStatus.Unsatisfiable;
            }

            if (binaryFormula.Operator != SmtBinaryOperator.Equal)
            {
                return ConcreteFactPreparationStatus.Ready;
            }

            var leftIsConcrete = TryEvaluateInteger(binaryFormula.Left, facts, out var leftValue);
            var rightIsConcrete = TryEvaluateInteger(binaryFormula.Right, facts, out var rightValue);
            if (leftIsConcrete && rightIsConcrete)
            {
                return leftValue == rightValue
                    ? ConcreteFactPreparationStatus.Ready
                    : ConcreteFactPreparationStatus.Unsatisfiable;
            }

            if (TrySolveAffineIntegerEquality(binaryFormula, facts, ref changed, out var affineStatus))
            {
                return affineStatus;
            }

            if (leftIsConcrete)
            {
                return TryAddIntegerEquality(facts, binaryFormula.Right, leftValue, ref changed)
                    ? ConcreteFactPreparationStatus.Ready
                    : ConcreteFactPreparationStatus.Unsatisfiable;
            }

            if (rightIsConcrete)
            {
                return TryAddIntegerEquality(facts, binaryFormula.Left, rightValue, ref changed)
                    ? ConcreteFactPreparationStatus.Ready
                    : ConcreteFactPreparationStatus.Unsatisfiable;
            }

            return ConcreteFactPreparationStatus.Ready;
        }

        private static bool TryAddIntegerEquality(
            ConcreteFactContext facts,
            SmtFormula formula,
            long value,
            ref bool changed)
        {
            if (formula.Kind != SmtValueKind.Int)
            {
                return true;
            }

            if (facts.IntegerEqualities.TryGetValue(formula, out var existing))
            {
                return existing == value;
            }

            facts.IntegerEqualities.Add(formula, value);
            if (!TryMergeIntegerBounds(
                    facts,
                    formula,
                    lower: value,
                    upper: value,
                    excludesZero: false,
                    ref changed))
            {
                return false;
            }

            changed = true;
            return true;
        }

        private static void TryCollectIntegerNonZeroFact(
            SmtBinaryFormula formula,
            ConcreteFactContext facts,
            ref bool changed)
        {
            if (!TryNormalizeIntegerComparisonToConstant(formula, out var expression, out var op, out var constant))
            {
                return;
            }

            var isNonZero = op switch
            {
                SmtBinaryOperator.NotEqual => constant == 0,
                SmtBinaryOperator.GreaterThan => constant >= 0,
                SmtBinaryOperator.GreaterThanOrEqual => constant >= 1,
                SmtBinaryOperator.LessThan => constant <= 0,
                SmtBinaryOperator.LessThanOrEqual => constant <= -1,
                _ => false,
            };

            if (isNonZero && facts.IntegerNonZeroFacts.Add(expression))
            {
                changed = true;
            }
        }

        private static ConcreteFactPreparationStatus TryCollectIntegerBoundFact(
            SmtBinaryFormula formula,
            ConcreteFactContext facts,
            ref bool changed)
        {
            if (!TryNormalizeIntegerComparisonToConstant(formula, out var expression, out var op, out var constant))
            {
                return ConcreteFactPreparationStatus.Ready;
            }

            long? lower = null;
            long? upper = null;
            var excludesZero = false;
            switch (op)
            {
                case SmtBinaryOperator.Equal:
                    lower = constant;
                    upper = constant;
                    break;
                case SmtBinaryOperator.NotEqual:
                    excludesZero = constant == 0;
                    break;
                case SmtBinaryOperator.LessThan:
                    if (TryCheckedAdd(constant, -1, out var lessThanUpper))
                    {
                        upper = lessThanUpper;
                    }

                    break;
                case SmtBinaryOperator.LessThanOrEqual:
                    upper = constant;
                    break;
                case SmtBinaryOperator.GreaterThan:
                    if (TryCheckedAdd(constant, 1, out var greaterThanLower))
                    {
                        lower = greaterThanLower;
                    }

                    break;
                case SmtBinaryOperator.GreaterThanOrEqual:
                    lower = constant;
                    break;
            }

            return TryMergeIntegerBounds(facts, expression, lower, upper, excludesZero, ref changed)
                ? ConcreteFactPreparationStatus.Ready
                : ConcreteFactPreparationStatus.Unsatisfiable;
        }

        private static bool TryMergeIntegerBounds(
            ConcreteFactContext facts,
            SmtFormula expression,
            long? lower,
            long? upper,
            bool excludesZero,
            ref bool changed)
        {
            if (expression.Kind != SmtValueKind.Int)
            {
                return true;
            }

            facts.IntegerBounds.TryGetValue(expression, out var bounds);
            if (lower.HasValue && (!bounds.Lower.HasValue || lower.Value > bounds.Lower.Value))
            {
                bounds.Lower = lower.Value;
                changed = true;
            }

            if (upper.HasValue && (!bounds.Upper.HasValue || upper.Value < bounds.Upper.Value))
            {
                bounds.Upper = upper.Value;
                changed = true;
            }

            if (excludesZero && !bounds.ExcludesZero)
            {
                bounds.ExcludesZero = true;
                changed = true;
            }

            if (bounds.IsUnsatisfiable)
            {
                return false;
            }

            facts.IntegerBounds[expression] = bounds;
            return true;
        }

        private static bool TrySolveAffineIntegerEquality(
            SmtBinaryFormula formula,
            ConcreteFactContext facts,
            ref bool changed,
            out ConcreteFactPreparationStatus status)
        {
            status = ConcreteFactPreparationStatus.Ready;
            if (formula.Operator != SmtBinaryOperator.Equal)
            {
                return false;
            }

            if (TryGetAffineTerm(formula.Left, facts, out var leftBase, out var leftCoefficient, out var leftConstant) &&
                leftBase is not null &&
                TryEvaluateInteger(formula.Right, facts, out var rightValue))
            {
                return TrySolveAffineEquality(
                    facts,
                    leftBase,
                    leftCoefficient,
                    leftConstant,
                    rightValue,
                    ref changed,
                    out status);
            }

            if (TryGetAffineTerm(formula.Right, facts, out var rightBase, out var rightCoefficient, out var rightConstant) &&
                rightBase is not null &&
                TryEvaluateInteger(formula.Left, facts, out var leftValue))
            {
                return TrySolveAffineEquality(
                    facts,
                    rightBase,
                    rightCoefficient,
                    rightConstant,
                    leftValue,
                    ref changed,
                    out status);
            }

            return false;
        }

        private static bool TrySolveAffineEquality(
            ConcreteFactContext facts,
            SmtFormula variable,
            long coefficient,
            long constant,
            long value,
            ref bool changed,
            out ConcreteFactPreparationStatus status)
        {
            status = ConcreteFactPreparationStatus.Ready;
            if (coefficient == 0)
            {
                status = constant == value
                    ? ConcreteFactPreparationStatus.Ready
                    : ConcreteFactPreparationStatus.Unsatisfiable;
                return true;
            }

            if (!TryCheckedSubtract(value, constant, out var adjusted) ||
                adjusted % coefficient != 0)
            {
                status = ConcreteFactPreparationStatus.Unsatisfiable;
                return true;
            }

            var solvedValue = adjusted / coefficient;
            status = TryAddIntegerEquality(facts, variable, solvedValue, ref changed)
                ? ConcreteFactPreparationStatus.Ready
                : ConcreteFactPreparationStatus.Unsatisfiable;
            return true;
        }

        private static bool TryGetAffineTerm(
            SmtFormula formula,
            ConcreteFactContext facts,
            out SmtFormula? variable,
            out long coefficient,
            out long constant)
        {
            variable = null;
            coefficient = 0;
            constant = 0;

            if (TryEvaluateInteger(formula, facts, out var concrete))
            {
                constant = concrete;
                return true;
            }

            switch (formula)
            {
                case SmtVariable { Kind: SmtValueKind.Int }:
                    variable = formula;
                    coefficient = 1;
                    return true;
                case SmtIntegerUnaryTerm { Operator: SmtIntegerUnaryOperator.Negate } unaryTerm:
                    if (!TryGetAffineTerm(unaryTerm.Operand, facts, out variable, out coefficient, out constant) ||
                        !TryCheckedNegate(coefficient, out coefficient) ||
                        !TryCheckedNegate(constant, out constant))
                    {
                        return false;
                    }

                    return true;
                case SmtIntegerBinaryTerm binaryTerm:
                    return TryGetAffineTerm(binaryTerm, facts, out variable, out coefficient, out constant);
                default:
                    return false;
            }
        }

        private static bool TryGetAffineTerm(
            SmtIntegerBinaryTerm term,
            ConcreteFactContext facts,
            out SmtFormula? variable,
            out long coefficient,
            out long constant)
        {
            variable = null;
            coefficient = 0;
            constant = 0;

            if (term.Operator is SmtIntegerBinaryOperator.Add or SmtIntegerBinaryOperator.Subtract)
            {
                if (!TryGetAffineTerm(term.Left, facts, out var leftVariable, out var leftCoefficient, out var leftConstant) ||
                    !TryGetAffineTerm(term.Right, facts, out var rightVariable, out var rightCoefficient, out var rightConstant))
                {
                    return false;
                }

                if (term.Operator == SmtIntegerBinaryOperator.Subtract)
                {
                    if (!TryCheckedNegate(rightCoefficient, out rightCoefficient) ||
                        !TryCheckedNegate(rightConstant, out rightConstant))
                    {
                        return false;
                    }
                }

                if (leftVariable is not null &&
                    rightVariable is not null &&
                    !EqualityComparer<SmtFormula>.Default.Equals(leftVariable, rightVariable))
                {
                    return false;
                }

                variable = leftVariable ?? rightVariable;
                return TryCheckedAdd(leftCoefficient, rightCoefficient, out coefficient) &&
                    TryCheckedAdd(leftConstant, rightConstant, out constant);
            }

            if (term.Operator == SmtIntegerBinaryOperator.Multiply)
            {
                if (TryEvaluateInteger(term.Left, facts, out var leftConstant) &&
                    TryGetAffineTerm(term.Right, facts, out variable, out coefficient, out constant))
                {
                    return TryCheckedMultiply(coefficient, leftConstant, out coefficient) &&
                        TryCheckedMultiply(constant, leftConstant, out constant);
                }

                if (TryEvaluateInteger(term.Right, facts, out var rightConstant) &&
                    TryGetAffineTerm(term.Left, facts, out variable, out coefficient, out constant))
                {
                    return TryCheckedMultiply(coefficient, rightConstant, out coefficient) &&
                        TryCheckedMultiply(constant, rightConstant, out constant);
                }
            }

            return false;
        }

        private static bool TryCheckedAdd(long left, long right, out long value)
        {
            try
            {
                value = checked(left + right);
                return true;
            }
            catch (OverflowException)
            {
                value = default;
                return false;
            }
        }

        private static bool TryCheckedSubtract(long left, long right, out long value)
        {
            try
            {
                value = checked(left - right);
                return true;
            }
            catch (OverflowException)
            {
                value = default;
                return false;
            }
        }

        private static bool TryCheckedMultiply(long left, long right, out long value)
        {
            try
            {
                value = checked(left * right);
                return true;
            }
            catch (OverflowException)
            {
                value = default;
                return false;
            }
        }

        private static bool TryCheckedNegate(long operand, out long value)
        {
            try
            {
                value = checked(-operand);
                return true;
            }
            catch (OverflowException)
            {
                value = default;
                return false;
            }
        }

        private static bool TryNormalizeIntegerComparisonToConstant(
            SmtBinaryFormula formula,
            out SmtFormula expression,
            out SmtBinaryOperator op,
            out long constant)
        {
            if (formula.Left is SmtIntegerConstant leftConstant && formula.Right.Kind == SmtValueKind.Int)
            {
                expression = formula.Right;
                op = SwapComparisonOperator(formula.Operator);
                constant = leftConstant.Value;
                return IsIntegerComparisonOperator(op);
            }

            if (formula.Right is SmtIntegerConstant rightConstant && formula.Left.Kind == SmtValueKind.Int)
            {
                expression = formula.Left;
                op = formula.Operator;
                constant = rightConstant.Value;
                return IsIntegerComparisonOperator(op);
            }

            expression = null!;
            op = default;
            constant = default;
            return false;
        }

        private static bool IsIntegerComparisonOperator(SmtBinaryOperator op)
        {
            return op is SmtBinaryOperator.Equal or
                SmtBinaryOperator.NotEqual or
                SmtBinaryOperator.LessThan or
                SmtBinaryOperator.LessThanOrEqual or
                SmtBinaryOperator.GreaterThan or
                SmtBinaryOperator.GreaterThanOrEqual;
        }

        private static SmtBinaryOperator SwapComparisonOperator(SmtBinaryOperator op)
        {
            return op switch
            {
                SmtBinaryOperator.LessThan => SmtBinaryOperator.GreaterThan,
                SmtBinaryOperator.LessThanOrEqual => SmtBinaryOperator.GreaterThanOrEqual,
                SmtBinaryOperator.GreaterThan => SmtBinaryOperator.LessThan,
                SmtBinaryOperator.GreaterThanOrEqual => SmtBinaryOperator.LessThanOrEqual,
                _ => op,
            };
        }

        private static ConcreteFactPreparationStatus ValidateIntegerTermSafety(
            SmtFormula formula,
            ConcreteFactContext facts)
        {
            switch (formula)
            {
                case SmtUnaryFormula unaryFormula:
                    return ValidateIntegerTermSafety(unaryFormula.Operand, facts);
                case SmtBinaryFormula binaryFormula:
                    var leftStatus = ValidateIntegerTermSafety(binaryFormula.Left, facts);
                    if (leftStatus != ConcreteFactPreparationStatus.Ready)
                    {
                        return leftStatus;
                    }

                    return ValidateIntegerTermSafety(binaryFormula.Right, facts);
                case SmtIntegerUnaryTerm integerUnaryTerm:
                    return ValidateIntegerTermSafety(integerUnaryTerm.Operand, facts);
                case SmtIntegerBinaryTerm integerBinaryTerm:
                    var integerLeftStatus = ValidateIntegerTermSafety(integerBinaryTerm.Left, facts);
                    if (integerLeftStatus != ConcreteFactPreparationStatus.Ready)
                    {
                        return integerLeftStatus;
                    }

                    var integerRightStatus = ValidateIntegerTermSafety(integerBinaryTerm.Right, facts);
                    if (integerRightStatus != ConcreteFactPreparationStatus.Ready)
                    {
                        return integerRightStatus;
                    }

                    if (integerBinaryTerm.Operator is not (SmtIntegerBinaryOperator.Divide or SmtIntegerBinaryOperator.Remainder))
                    {
                        return ConcreteFactPreparationStatus.Ready;
                    }

                    if (TryEvaluateInteger(integerBinaryTerm.Right, facts, out var denominator))
                    {
                        return denominator == 0
                            ? ConcreteFactPreparationStatus.Unknown
                            : ConcreteFactPreparationStatus.Ready;
                    }

                    if (facts.IntegerNonZeroFacts.Contains(integerBinaryTerm.Right) ||
                        TryIntegerIntervalExcludesZero(integerBinaryTerm.Right, facts))
                    {
                        return ConcreteFactPreparationStatus.Ready;
                    }

                    return TryIntegerIntervalIsExactlyZero(integerBinaryTerm.Right, facts)
                        ? ConcreteFactPreparationStatus.Unknown
                        : ConcreteFactPreparationStatus.Unknown;
                case SmtStringLengthTerm stringLengthTerm:
                    return ValidateIntegerTermSafety(stringLengthTerm.Value, facts);
                case SmtStringConcatTerm stringConcatTerm:
                    var concatLeftStatus = ValidateIntegerTermSafety(stringConcatTerm.Left, facts);
                    if (concatLeftStatus != ConcreteFactPreparationStatus.Ready)
                    {
                        return concatLeftStatus;
                    }

                    return ValidateIntegerTermSafety(stringConcatTerm.Right, facts);
                case SmtStringContainsFormula stringContainsFormula:
                    var containsValueStatus = ValidateIntegerTermSafety(stringContainsFormula.Value, facts);
                    if (containsValueStatus != ConcreteFactPreparationStatus.Ready)
                    {
                        return containsValueStatus;
                    }

                    return ValidateIntegerTermSafety(stringContainsFormula.Search, facts);
                case SmtStringStartsWithFormula stringStartsWithFormula:
                    var startsWithValueStatus = ValidateIntegerTermSafety(stringStartsWithFormula.Value, facts);
                    if (startsWithValueStatus != ConcreteFactPreparationStatus.Ready)
                    {
                        return startsWithValueStatus;
                    }

                    return ValidateIntegerTermSafety(stringStartsWithFormula.Prefix, facts);
                case SmtStringEndsWithFormula stringEndsWithFormula:
                    var endsWithValueStatus = ValidateIntegerTermSafety(stringEndsWithFormula.Value, facts);
                    if (endsWithValueStatus != ConcreteFactPreparationStatus.Ready)
                    {
                        return endsWithValueStatus;
                    }

                    return ValidateIntegerTermSafety(stringEndsWithFormula.Suffix, facts);
                case SmtRegexMatchFormula regexMatchFormula:
                    return ValidateIntegerTermSafety(regexMatchFormula.Value, facts);
                case SmtRuntimeTypeTestFormula runtimeTypeTest:
                    return ValidateIntegerTermSafety(runtimeTypeTest.Value, facts);
                case SmtConditionalFormula conditionalFormula:
                    var conditionStatus = ValidateIntegerTermSafety(conditionalFormula.Condition, facts);
                    if (conditionStatus != ConcreteFactPreparationStatus.Ready)
                    {
                        return conditionStatus;
                    }

                    if (TryEvaluateConcreteBoolean(conditionalFormula.Condition, facts, out var selectedBranch))
                    {
                        return ValidateIntegerTermSafety(
                            selectedBranch ? conditionalFormula.WhenTrue : conditionalFormula.WhenFalse,
                            facts);
                    }

                    var trueStatus = ValidateIntegerTermSafety(conditionalFormula.WhenTrue, facts);
                    if (trueStatus != ConcreteFactPreparationStatus.Ready)
                    {
                        return trueStatus;
                    }

                    return ValidateIntegerTermSafety(conditionalFormula.WhenFalse, facts);
                default:
                    return ConcreteFactPreparationStatus.Ready;
            }
        }

        private static bool TryIntegerIntervalExcludesZero(SmtFormula formula, ConcreteFactContext facts)
        {
            if (TryGetIntegerInterval(formula, facts, out var lower, out var upper))
            {
                return lower.HasValue && lower.Value > 0 ||
                    upper.HasValue && upper.Value < 0;
            }

            return facts.IntegerBounds.TryGetValue(formula, out var bounds) &&
                bounds.ExcludesZero;
        }

        private static bool TryIntegerIntervalIsExactlyZero(SmtFormula formula, ConcreteFactContext facts)
        {
            return TryGetIntegerInterval(formula, facts, out var lower, out var upper) &&
                lower.HasValue &&
                upper.HasValue &&
                lower.Value == 0 &&
                upper.Value == 0;
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
            if (facts.IntegerBounds.TryGetValue(formula, out var bounds))
            {
                lower = bounds.Lower;
                upper = bounds.Upper;
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
                    if (!TryGetIntegerInterval(unaryTerm.Operand, facts, out var operandLower, out var operandUpper))
                    {
                        break;
                    }

                    if (operandUpper.HasValue)
                    {
                        if (!TryCheckedNegate(operandUpper.Value, out var negatedUpper))
                        {
                            break;
                        }

                        structuralLower = negatedUpper;
                    }

                    if (operandLower.HasValue)
                    {
                        if (!TryCheckedNegate(operandLower.Value, out var negatedLower))
                        {
                            break;
                        }

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
                {
                    lower = structuralLower.Value;
                }

                if (structuralUpper.HasValue && (!upper.HasValue || structuralUpper.Value < upper.Value))
                {
                    upper = structuralUpper.Value;
                }

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
                {
                    return false;
                }

                return TryCombineBounds(leftLower, rightLower, TryCheckedAdd, out lower) &&
                    TryCombineBounds(leftUpper, rightUpper, TryCheckedAdd, out upper);
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
            {
                return false;
            }

            switch (term.Operator)
            {
                case SmtIntegerBinaryOperator.Add:
                    return TryCombineBounds(leftLower, rightLower, TryCheckedAdd, out lower) &&
                        TryCombineBounds(leftUpper, rightUpper, TryCheckedAdd, out upper);
                case SmtIntegerBinaryOperator.Subtract:
                    return TryCombineBounds(leftLower, rightUpper, TryCheckedSubtract, out lower) &&
                        TryCombineBounds(leftUpper, rightLower, TryCheckedSubtract, out upper);
                case SmtIntegerBinaryOperator.Multiply:
                    if (TryEvaluateInteger(term.Left, facts, out var leftConstant))
                    {
                        return TryScaleBounds(rightLower, rightUpper, leftConstant, out lower, out upper);
                    }

                    if (TryEvaluateInteger(term.Right, facts, out var rightConstant))
                    {
                        return TryScaleBounds(leftLower, leftUpper, rightConstant, out lower, out upper);
                    }

                    return false;
                default:
                    return false;
            }
        }

        private delegate bool CheckedLongBinaryOperation(long left, long right, out long value);

        private static bool TryCombineBounds(
            long? left,
            long? right,
            CheckedLongBinaryOperation operation,
            out long? value)
        {
            value = null;
            if (!left.HasValue || !right.HasValue)
            {
                return true;
            }

            if (!operation(left.Value, right.Value, out var combined))
            {
                return false;
            }

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
            scaledLower = null;
            scaledUpper = null;
            if (multiplier == 0)
            {
                scaledLower = 0;
                scaledUpper = 0;
                return true;
            }

            if (multiplier > 0)
            {
                return TryScaleBound(lower, multiplier, out scaledLower) &&
                    TryScaleBound(upper, multiplier, out scaledUpper);
            }

            return TryScaleBound(upper, multiplier, out scaledLower) &&
                TryScaleBound(lower, multiplier, out scaledUpper);
        }

        private static bool TryScaleBound(long? bound, long multiplier, out long? scaled)
        {
            scaled = null;
            if (!bound.HasValue)
            {
                return true;
            }

            if (!TryCheckedMultiply(bound.Value, multiplier, out var scaledValue))
            {
                return false;
            }

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
                    {
                        return TryEvaluateConcreteBoolean(
                            selectedBranch ? conditionalFormula.WhenTrue : conditionalFormula.WhenFalse,
                            facts,
                            out value);
                    }

                    break;
                default:
                    break;
            }

            return facts.BooleanEqualities.TryGetValue(formula, out value);
        }

        private static bool ShouldPreserveSourceFact(SmtFormula formula)
        {
            if (formula is not SmtBinaryFormula binaryFormula ||
                !IsIntegerComparisonOperator(binaryFormula.Operator))
            {
                return false;
            }

            if (IsLiteral(binaryFormula.Left) && IsLiteral(binaryFormula.Right))
            {
                return false;
            }

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

            if (TryEvaluateStringLengthComparison(formula, facts, out value))
            {
                return true;
            }

            if (formula.Left.Kind == SmtValueKind.Int &&
                formula.Right.Kind == SmtValueKind.Int &&
                TryEvaluateIntegerIntervalComparison(formula, facts, out value))
            {
                return true;
            }

            if (formula.Left.Kind == SmtValueKind.Int &&
                formula.Right.Kind == SmtValueKind.Int &&
                TryEvaluateInteger(formula.Left, facts, out var leftInteger) &&
                TryEvaluateInteger(formula.Right, facts, out var rightInteger))
            {
                value = CompareIntegers(formula.Operator, leftInteger, rightInteger);
                return true;
            }

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
                if (TryEvaluateReferenceNull(formula.Left, facts, out var leftIsNull) &&
                    TryEvaluateReferenceNull(formula.Right, facts, out var rightIsNull) &&
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
                (formula.Left is SmtNullConstant && formula.Right is SmtNullConstant ||
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
            {
                value = CompareIntegers(op, concreteString.Length, constant);
                return true;
            }

            value = op switch
            {
                SmtBinaryOperator.Equal => constant < 0 ? false : default,
                SmtBinaryOperator.NotEqual => constant < 0 ? true : default,
                SmtBinaryOperator.LessThan => constant <= 0 ? false : default,
                SmtBinaryOperator.LessThanOrEqual => constant < 0 ? false : default,
                SmtBinaryOperator.GreaterThan => constant < 0 ? true : default,
                SmtBinaryOperator.GreaterThanOrEqual => constant <= 0 ? true : default,
                _ => default,
            };

            return op switch
            {
                SmtBinaryOperator.Equal => constant < 0,
                SmtBinaryOperator.NotEqual => constant < 0,
                SmtBinaryOperator.LessThan => constant <= 0,
                SmtBinaryOperator.LessThanOrEqual => constant < 0,
                SmtBinaryOperator.GreaterThan => constant < 0,
                SmtBinaryOperator.GreaterThanOrEqual => constant <= 0,
                _ => false,
            };
        }

        private static bool TryEvaluateIntegerIntervalComparison(
            SmtBinaryFormula formula,
            ConcreteFactContext facts,
            out bool value)
        {
            value = false;
            if (!IsIntegerComparisonOperator(formula.Operator) ||
                !TryGetIntegerInterval(formula.Left, facts, out var leftLower, out var leftUpper) ||
                !TryGetIntegerInterval(formula.Right, facts, out var rightLower, out var rightUpper))
            {
                return false;
            }

            if (IntervalIsInconsistent(leftLower, leftUpper) ||
                IntervalIsInconsistent(rightLower, rightUpper))
            {
                value = false;
                return true;
            }

            switch (formula.Operator)
            {
                case SmtBinaryOperator.Equal:
                    if (leftLower.HasValue &&
                        leftUpper.HasValue &&
                        rightLower.HasValue &&
                        rightUpper.HasValue &&
                        leftLower.Value == leftUpper.Value &&
                        rightLower.Value == rightUpper.Value)
                    {
                        value = leftLower.Value == rightLower.Value;
                        return true;
                    }

                    if (IntervalsAreDisjoint(leftLower, leftUpper, rightLower, rightUpper))
                    {
                        value = false;
                        return true;
                    }

                    return false;
                case SmtBinaryOperator.NotEqual:
                    if (IntervalsAreDisjoint(leftLower, leftUpper, rightLower, rightUpper))
                    {
                        value = true;
                        return true;
                    }

                    if (leftLower.HasValue &&
                        leftUpper.HasValue &&
                        rightLower.HasValue &&
                        rightUpper.HasValue &&
                        leftLower.Value == leftUpper.Value &&
                        rightLower.Value == rightUpper.Value)
                    {
                        value = leftLower.Value != rightLower.Value;
                        return true;
                    }

                    return false;
                case SmtBinaryOperator.LessThan:
                    if (leftUpper.HasValue && rightLower.HasValue && leftUpper.Value < rightLower.Value)
                    {
                        value = true;
                        return true;
                    }

                    if (leftLower.HasValue && rightUpper.HasValue && leftLower.Value >= rightUpper.Value)
                    {
                        value = false;
                        return true;
                    }

                    return false;
                case SmtBinaryOperator.LessThanOrEqual:
                    if (leftUpper.HasValue && rightLower.HasValue && leftUpper.Value <= rightLower.Value)
                    {
                        value = true;
                        return true;
                    }

                    if (leftLower.HasValue && rightUpper.HasValue && leftLower.Value > rightUpper.Value)
                    {
                        value = false;
                        return true;
                    }

                    return false;
                case SmtBinaryOperator.GreaterThan:
                    if (leftLower.HasValue && rightUpper.HasValue && leftLower.Value > rightUpper.Value)
                    {
                        value = true;
                        return true;
                    }

                    if (leftUpper.HasValue && rightLower.HasValue && leftUpper.Value <= rightLower.Value)
                    {
                        value = false;
                        return true;
                    }

                    return false;
                case SmtBinaryOperator.GreaterThanOrEqual:
                    if (leftLower.HasValue && rightUpper.HasValue && leftLower.Value >= rightUpper.Value)
                    {
                        value = true;
                        return true;
                    }

                    if (leftUpper.HasValue && rightLower.HasValue && leftUpper.Value < rightLower.Value)
                    {
                        value = false;
                        return true;
                    }

                    return false;
                default:
                    return false;
            }
        }

        private static bool IntervalsAreDisjoint(
            long? leftLower,
            long? leftUpper,
            long? rightLower,
            long? rightUpper)
        {
            return leftUpper.HasValue && rightLower.HasValue && leftUpper.Value < rightLower.Value ||
                rightUpper.HasValue && leftLower.HasValue && rightUpper.Value < leftLower.Value;
        }

        private static bool IntervalIsInconsistent(long? lower, long? upper)
        {
            return lower.HasValue && upper.HasValue && lower.Value > upper.Value;
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
                return IsIntegerComparisonOperator(op);
            }

            if (formula.Left is SmtIntegerConstant leftConstant &&
                formula.Right is SmtStringLengthTerm rightLength)
            {
                stringValue = rightLength.Value;
                op = SwapComparisonOperator(formula.Operator);
                constant = leftConstant.Value;
                return IsIntegerComparisonOperator(op);
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
            try
            {
                if (formula is SmtIntegerConstant integerConstant)
                {
                    value = integerConstant.Value;
                    return true;
                }

                if (facts.IntegerEqualities.TryGetValue(formula, out value))
                {
                    return true;
                }

                switch (formula)
                {
                    case SmtIntegerUnaryTerm { Operator: SmtIntegerUnaryOperator.Negate } unaryTerm
                        when TryEvaluateInteger(unaryTerm.Operand, facts, out var operand):
                        value = checked(-operand);
                        return true;
                    case SmtIntegerBinaryTerm binaryTerm:
                        return TryEvaluateIntegerBinary(binaryTerm, facts, out value);
                    case SmtConditionalFormula { Kind: SmtValueKind.Int } conditionalFormula:
                        if (TryEvaluateConcreteBoolean(conditionalFormula.Condition, facts, out var selectedBranch))
                        {
                            return TryEvaluateInteger(
                                selectedBranch ? conditionalFormula.WhenTrue : conditionalFormula.WhenFalse,
                                facts,
                                out value);
                        }

                        value = default;
                        return false;
                    default:
                        value = default;
                        return false;
                }
            }
            catch (OverflowException)
            {
                value = default;
                return false;
            }
            catch (DivideByZeroException)
            {
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
            {
                return false;
            }

            value = term.Operator switch
            {
                SmtIntegerBinaryOperator.Add => checked(left + right),
                SmtIntegerBinaryOperator.Subtract => checked(left - right),
                SmtIntegerBinaryOperator.Multiply => checked(left * right),
                SmtIntegerBinaryOperator.Divide => checked(left / right),
                SmtIntegerBinaryOperator.Remainder => checked(left % right),
                _ => default,
            };
            return true;
        }

        private static bool CompareIntegers(SmtBinaryOperator op, long left, long right)
        {
            return op switch
            {
                SmtBinaryOperator.Equal => left == right,
                SmtBinaryOperator.NotEqual => left != right,
                SmtBinaryOperator.LessThan => left < right,
                SmtBinaryOperator.LessThanOrEqual => left <= right,
                SmtBinaryOperator.GreaterThan => left > right,
                SmtBinaryOperator.GreaterThanOrEqual => left >= right,
                _ => false,
            };
        }

        private static bool CompareEquality(SmtBinaryOperator op, bool equality)
        {
            return op switch
            {
                SmtBinaryOperator.Equal => equality,
                SmtBinaryOperator.NotEqual => !equality,
                _ => false,
            };
        }

        private static bool TryEvaluateReferenceNull(
            SmtFormula formula,
            ConcreteFactContext facts,
            out bool isNull)
        {
            if (formula is SmtNullConstant)
            {
                isNull = true;
                return true;
            }

            if (facts.ReferenceNullEqualities.TryGetValue(formula, out isNull))
            {
                return true;
            }

            if (formula is SmtConditionalFormula { Kind: SmtValueKind.Reference } conditionalFormula &&
                TryEvaluateConcreteBoolean(conditionalFormula.Condition, facts, out var selectedBranch))
            {
                return TryEvaluateReferenceNull(
                    selectedBranch ? conditionalFormula.WhenTrue : conditionalFormula.WhenFalse,
                    facts,
                    out isNull);
            }

            isNull = false;
            return false;
        }

        private static bool TryCollectStringEqualities(
            IReadOnlyList<SmtFormula> conditions,
            ConcreteFactContext facts)
        {
            var iterationLimit = Math.Max(1, conditions.Count * 4);
            var changed = false;
            do
            {
                changed = false;
                foreach (var condition in conditions)
                {
                    if (!TryCollectStringEqualities(condition, facts, ref changed))
                    {
                        return false;
                    }
                }

                iterationLimit--;
            }
            while (changed && iterationLimit > 0);

            return true;
        }

        private static bool TryCollectStringEqualities(
            SmtFormula formula,
            ConcreteFactContext facts,
            ref bool changed)
        {
            if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
            {
                return TryCollectStringEqualities(andFormula.Left, facts, ref changed) &&
                    TryCollectStringEqualities(andFormula.Right, facts, ref changed);
            }

            if (formula is not SmtBinaryFormula { Operator: SmtBinaryOperator.Equal } equalFormula)
            {
                return true;
            }

            if (TryGetConcreteString(equalFormula.Left, facts, out var leftValue) &&
                TryGetConcreteString(equalFormula.Right, facts, out var rightValue))
            {
                return string.Equals(leftValue, rightValue, StringComparison.Ordinal);
            }

            if (equalFormula.Left is SmtStringConstant leftConstant)
            {
                return TryAddStringEquality(facts, equalFormula.Right, leftConstant.Value, ref changed);
            }

            if (equalFormula.Right is SmtStringConstant rightConstant)
            {
                return TryAddStringEquality(facts, equalFormula.Left, rightConstant.Value, ref changed);
            }

            if (TryGetConcreteString(equalFormula.Left, facts, out leftValue))
            {
                return TryAddStringEquality(facts, equalFormula.Right, leftValue, ref changed);
            }

            if (TryGetConcreteString(equalFormula.Right, facts, out rightValue))
            {
                return TryAddStringEquality(facts, equalFormula.Left, rightValue, ref changed);
            }

            return true;
        }

        private static bool TryAddStringEquality(
            ConcreteFactContext facts,
            SmtFormula formula,
            string value,
            ref bool changed)
        {
            if (facts.StringEqualities.TryGetValue(formula, out var existing))
            {
                return string.Equals(existing, value, StringComparison.Ordinal);
            }

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
            {
                return TryCollectStringLengthEqualities(andFormula.Left, stringLengthEqualities) &&
                    TryCollectStringLengthEqualities(andFormula.Right, stringLengthEqualities);
            }

            if (!TryGetStringLengthEquality(formula, out var value, out var length))
            {
                return true;
            }

            if (length < 0)
            {
                return false;
            }

            if (stringLengthEqualities.TryGetValue(value, out var existing))
            {
                return existing == length;
            }

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
            if (formula is not SmtBinaryFormula { Operator: SmtBinaryOperator.Equal } equalFormula)
            {
                return false;
            }

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

        private static ConcreteFactPreparationStatus TryInferStringEqualitiesFromLengthConstrainedPredicates(
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
                if (leftStatus != ConcreteFactPreparationStatus.Ready)
                {
                    return leftStatus;
                }

                return TryInferStringEqualitiesFromLengthConstrainedPredicates(
                    andFormula.Right,
                    stringLengthEqualities,
                    facts);
            }

            if (!TryGetPositiveStringPredicateFact(formula, out var predicate) ||
                !stringLengthEqualities.TryGetValue(predicate.Value, out var knownLength) ||
                !TryGetConcreteString(predicate.Argument, facts, out var concreteArgument))
            {
                return ConcreteFactPreparationStatus.Ready;
            }

            if (knownLength < concreteArgument.Length)
            {
                return ConcreteFactPreparationStatus.Unsatisfiable;
            }

            if (knownLength == concreteArgument.Length)
            {
                if (!TryAddStringEquality(facts, predicate.Value, concreteArgument))
                {
                    return ConcreteFactPreparationStatus.Unsatisfiable;
                }
            }

            return ConcreteFactPreparationStatus.Ready;
        }

        private static ConcreteFactPreparationStatus TryApplyStringShapeFacts(
            IReadOnlyList<SmtFormula> conditions,
            IReadOnlyDictionary<SmtFormula, long> stringLengthEqualities,
            ConcreteFactContext facts)
        {
            var shapeFacts = new Dictionary<SmtFormula, StringShapeFact>();
            foreach (var condition in conditions)
            {
                var status = TryCollectStringShapeFacts(condition, facts, shapeFacts);
                if (status != ConcreteFactPreparationStatus.Ready)
                {
                    return status;
                }
            }

            foreach (var entry in shapeFacts)
            {
                var value = entry.Key;
                var shape = entry.Value;
                long? exactLength = null;
                if (stringLengthEqualities.TryGetValue(value, out var knownLength))
                {
                    exactLength = knownLength;
                }
                else if (TryGetConcreteString(value, facts, out var concreteValue))
                {
                    exactLength = concreteValue.Length;
                }

                if (exactLength.HasValue)
                {
                    if (shape.MinLength > exactLength.Value)
                    {
                        return ConcreteFactPreparationStatus.Unsatisfiable;
                    }

                    if (!TryApplyExactLengthStringShape(value, exactLength.Value, shape, facts))
                    {
                        return ConcreteFactPreparationStatus.Unsatisfiable;
                    }
                }
            }

            return ConcreteFactPreparationStatus.Ready;
        }

        private static ConcreteFactPreparationStatus TryCollectStringShapeFacts(
            SmtFormula formula,
            ConcreteFactContext facts,
            Dictionary<SmtFormula, StringShapeFact> shapeFacts)
        {
            if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
            {
                var leftStatus = TryCollectStringShapeFacts(andFormula.Left, facts, shapeFacts);
                if (leftStatus != ConcreteFactPreparationStatus.Ready)
                {
                    return leftStatus;
                }

                return TryCollectStringShapeFacts(andFormula.Right, facts, shapeFacts);
            }

            if (!TryGetPositiveStringPredicateFact(formula, out var predicate) ||
                !TryGetConcreteString(predicate.Argument, facts, out var argument))
            {
                return ConcreteFactPreparationStatus.Ready;
            }

            var shape = shapeFacts.TryGetValue(predicate.Value, out var existing)
                ? existing
                : default;

            var status = predicate.Kind switch
            {
                StringPredicateKind.Contains => shape.AddContains(argument),
                StringPredicateKind.StartsWith => shape.AddPrefix(argument),
                StringPredicateKind.EndsWith => shape.AddSuffix(argument),
                _ => ConcreteFactPreparationStatus.Ready,
            };

            if (status != ConcreteFactPreparationStatus.Ready)
            {
                return status;
            }

            shapeFacts[predicate.Value] = shape;
            return ConcreteFactPreparationStatus.Ready;
        }

        private static bool TryApplyExactLengthStringShape(
            SmtFormula value,
            long exactLength,
            StringShapeFact shape,
            ConcreteFactContext facts)
        {
            if (exactLength > int.MaxValue)
            {
                return true;
            }

            var length = (int)exactLength;
            var prefix = shape.Prefix;
            var suffix = shape.Suffix;
            if (prefix is not null &&
                prefix.Length != 0 &&
                prefix.Length == length)
            {
                return TryAddStringEquality(facts, value, prefix);
            }

            if (suffix is not null &&
                suffix.Length != 0 &&
                suffix.Length == length)
            {
                return TryAddStringEquality(facts, value, suffix);
            }

            if (prefix is not null &&
                suffix is not null &&
                prefix.Length != 0 &&
                suffix.Length != 0 &&
                prefix.Length + suffix.Length >= length)
            {
                var characters = new char?[length];
                if (!TryOverlayString(characters, 0, prefix) ||
                    !TryOverlayString(characters, length - suffix.Length, suffix))
                {
                    return false;
                }

                if (characters.All(static c => c.HasValue))
                {
                    return TryAddStringEquality(
                        facts,
                        value,
                        new string(characters.Select(static c => c!.Value).ToArray()));
                }
            }

            return true;
        }

        private static bool TryOverlayString(char?[] target, int start, string value)
        {
            if (start < 0 ||
                start + value.Length > target.Length)
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                var index = start + i;
                if (target[index].HasValue && target[index]!.Value != value[i])
                {
                    return false;
                }

                target[index] = value[i];
            }

            return true;
        }

        private struct StringShapeFact
        {
            public string? Prefix;

            public string? Suffix;

            public long MinLength;

            public ConcreteFactPreparationStatus AddContains(string value)
            {
                return ApplyMinimumLength(value.Length);
            }

            public ConcreteFactPreparationStatus AddPrefix(string value)
            {
                if (Prefix is not null &&
                    !AreCompatiblePrefixes(Prefix, value))
                {
                    return ConcreteFactPreparationStatus.Unsatisfiable;
                }

                if (Prefix is null || value.Length > Prefix.Length)
                {
                    Prefix = value;
                }

                return ApplyMinimumLength(value.Length);
            }

            public ConcreteFactPreparationStatus AddSuffix(string value)
            {
                if (Suffix is not null &&
                    !AreCompatibleSuffixes(Suffix, value))
                {
                    return ConcreteFactPreparationStatus.Unsatisfiable;
                }

                if (Suffix is null || value.Length > Suffix.Length)
                {
                    Suffix = value;
                }

                return ApplyMinimumLength(value.Length);
            }

            private ConcreteFactPreparationStatus ApplyMinimumLength(int length)
            {
                if (length > MinLength)
                {
                    MinLength = length;
                }

                return ConcreteFactPreparationStatus.Ready;
            }

            private static bool AreCompatiblePrefixes(string left, string right)
            {
                var minLength = Math.Min(left.Length, right.Length);
                return string.Equals(
                    left.Substring(0, minLength),
                    right.Substring(0, minLength),
                    StringComparison.Ordinal);
            }

            private static bool AreCompatibleSuffixes(string left, string right)
            {
                var minLength = Math.Min(left.Length, right.Length);
                return string.Equals(
                    left.Substring(left.Length - minLength, minLength),
                    right.Substring(right.Length - minLength, minLength),
                    StringComparison.Ordinal);
            }
        }

        private ConcreteFactPreparationStatus SimplifyConcreteFacts(
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
                if (leftStatus != ConcreteFactPreparationStatus.Ready)
                {
                    return leftStatus;
                }

                var rightStatus = SimplifyConcreteFacts(
                    andFormula.Right,
                    facts,
                    out var right,
                    out var rightChanged);
                if (rightStatus != ConcreteFactPreparationStatus.Ready)
                {
                    return rightStatus;
                }

                changed = leftChanged || rightChanged;
                if (left is SmtBooleanConstant { Value: false } ||
                    right is SmtBooleanConstant { Value: false })
                {
                    preparedFormula = new SmtBooleanConstant(false);
                    changed = true;
                    return ConcreteFactPreparationStatus.Ready;
                }

                if (left is SmtBooleanConstant { Value: true })
                {
                    preparedFormula = right;
                    changed = true;
                    return ConcreteFactPreparationStatus.Ready;
                }

                if (right is SmtBooleanConstant { Value: true })
                {
                    preparedFormula = left;
                    changed = true;
                    return ConcreteFactPreparationStatus.Ready;
                }

                if (changed)
                {
                    preparedFormula = new SmtBinaryFormula(SmtBinaryOperator.And, left, right);
                }

                return ConcreteFactPreparationStatus.Ready;
            }

            if (TryEvaluateConcreteBoolean(formula, facts, out var concreteBoolean))
            {
                if (concreteBoolean && ShouldPreserveSourceFact(formula))
                {
                    return ConcreteFactPreparationStatus.Ready;
                }

                preparedFormula = new SmtBooleanConstant(concreteBoolean);
                changed = true;
                return ConcreteFactPreparationStatus.Ready;
            }

            if (TryGetRegexFact(formula, out var regexMatch, out var expectedMatch) &&
                TryGetConcreteString(regexMatch.Value, facts, out var concreteInput))
            {
                var validationStatus = TryValidateRegexMatch(
                    concreteInput,
                    regexMatch.Pattern,
                    regexMatch.Options,
                    out var actualMatch);
                if (validationStatus != ConcreteFactPreparationStatus.Ready)
                {
                    return validationStatus;
                }

                if (actualMatch != expectedMatch)
                {
                    return ConcreteFactPreparationStatus.Unsatisfiable;
                }

                preparedFormula = new SmtBooleanConstant(true);
                changed = true;
            }

            if (TryGetStringPredicateFact(formula, out var predicate, out var expectedPredicateValue) &&
                TryGetConcreteString(predicate.Value, facts, out var concreteValue) &&
                TryGetConcreteString(predicate.Argument, facts, out var concreteArgument))
            {
                var actualPredicateValue = EvaluateStringPredicate(predicate.Kind, concreteValue, concreteArgument);

                if (actualPredicateValue != expectedPredicateValue)
                {
                    return ConcreteFactPreparationStatus.Unsatisfiable;
                }

                preparedFormula = new SmtBooleanConstant(true);
                changed = true;
            }

            return ConcreteFactPreparationStatus.Ready;
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
                _ => false,
            };
        }

        private static bool TryGetRegexFact(
            SmtFormula formula,
            out SmtRegexMatchFormula regexMatch,
            out bool expectedMatch)
        {
            if (formula is SmtRegexMatchFormula positiveRegexMatch)
            {
                regexMatch = positiveRegexMatch;
                expectedMatch = true;
                return true;
            }

            if (formula is SmtUnaryFormula
                {
                    Operator: SmtUnaryOperator.Not,
                    Operand: SmtRegexMatchFormula negativeRegexMatch
                })
            {
                regexMatch = negativeRegexMatch;
                expectedMatch = false;
                return true;
            }

            regexMatch = null!;
            expectedMatch = false;
            return false;
        }

        private enum StringPredicateKind
        {
            Contains,
            StartsWith,
            EndsWith,
        }

        private readonly struct StringPredicateFact
        {
            public StringPredicateFact(StringPredicateKind kind, SmtFormula value, SmtFormula argument)
            {
                Kind = kind;
                Value = value;
                Argument = argument;
            }

            public StringPredicateKind Kind { get; }

            public SmtFormula Value { get; }

            public SmtFormula Argument { get; }
        }

        private static bool TryGetStringPredicateFact(
            SmtFormula formula,
            out StringPredicateFact predicate,
            out bool expectedValue)
        {
            if (TryGetPositiveStringPredicateFact(formula, out predicate))
            {
                expectedValue = true;
                return true;
            }

            if (formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } notFormula &&
                TryGetPositiveStringPredicateFact(notFormula.Operand, out predicate))
            {
                expectedValue = false;
                return true;
            }

            expectedValue = false;
            predicate = default;
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
                    predicate = new StringPredicateFact(StringPredicateKind.StartsWith, startsWith.Value, startsWith.Prefix);
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
            {
                return TryGetConcreteString(
                    selectedBranch ? conditionalFormula.WhenTrue : conditionalFormula.WhenFalse,
                    facts,
                    out value);
            }

            value = string.Empty;
            return false;
        }

        private ConcreteFactPreparationStatus TryValidateRegexMatch(
            string input,
            string pattern,
            RegexOptions options,
            out bool isMatch)
        {
            var key = new RegexValidationKey(input, pattern, options);
            if (_regexValidationCache.TryGetValue(key, out var cached))
            {
                isMatch = cached.IsMatch;
                return cached.Status;
            }

            try
            {
                isMatch = Regex.IsMatch(
                    input,
                    pattern,
                    options,
                    ConcreteRegexValidationTimeout);
                _regexValidationCache[key] = new RegexValidationResult(ConcreteFactPreparationStatus.Ready, isMatch);
                return ConcreteFactPreparationStatus.Ready;
            }
            catch (ArgumentException)
            {
                isMatch = false;
                _regexValidationCache[key] = new RegexValidationResult(ConcreteFactPreparationStatus.Unknown, isMatch);
                return ConcreteFactPreparationStatus.Unknown;
            }
            catch (RegexMatchTimeoutException)
            {
                isMatch = false;
                _regexValidationCache[key] = new RegexValidationResult(ConcreteFactPreparationStatus.Unknown, isMatch);
                return ConcreteFactPreparationStatus.Unknown;
            }
        }

        private readonly struct RegexValidationKey : IEquatable<RegexValidationKey>
        {
            private readonly string _input;
            private readonly string _pattern;
            private readonly RegexOptions _options;

            public RegexValidationKey(string input, string pattern, RegexOptions options)
            {
                _input = input;
                _pattern = pattern;
                _options = options;
            }

            public bool Equals(RegexValidationKey other)
            {
                return string.Equals(_input, other._input, StringComparison.Ordinal) &&
                    string.Equals(_pattern, other._pattern, StringComparison.Ordinal) &&
                    _options == other._options;
            }

            public override bool Equals(object? obj)
            {
                return obj is RegexValidationKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (StringComparer.Ordinal.GetHashCode(_input) * 397) ^
                        (StringComparer.Ordinal.GetHashCode(_pattern) * 397) ^
                        (int)_options;
                }
            }
        }

        private readonly struct RegexValidationResult
        {
            public RegexValidationResult(ConcreteFactPreparationStatus status, bool isMatch)
            {
                Status = status;
                IsMatch = isMatch;
            }

            public ConcreteFactPreparationStatus Status { get; }

            public bool IsMatch { get; }
        }
    }
}
