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
                var combinedConditions = preparedPathConditions.Concat(new[] { impurityCondition }).ToArray();
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

            var stringEqualities = new Dictionary<SmtFormula, string>();
            foreach (var condition in normalizedConditions)
            {
                if (!TryCollectStringEqualities(condition, stringEqualities))
                {
                    preparedConditions = Array.Empty<SmtFormula>();
                    return ConcreteFactPreparationStatus.Unsatisfiable;
                }
            }

            var integerEqualities = new Dictionary<SmtFormula, long>();
            var integerNonZeroFacts = new HashSet<SmtFormula>();
            var integerStatus = TryCollectIntegerFacts(normalizedConditions, integerEqualities, integerNonZeroFacts);
            if (integerStatus != ConcreteFactPreparationStatus.Ready)
            {
                preparedConditions = Array.Empty<SmtFormula>();
                return integerStatus;
            }

            foreach (var condition in normalizedConditions)
            {
                integerStatus = ValidateIntegerTermSafety(condition, integerEqualities, integerNonZeroFacts);
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
                    stringEqualities);
                if (status != ConcreteFactPreparationStatus.Ready)
                {
                    preparedConditions = Array.Empty<SmtFormula>();
                    return status;
                }
            }

            var builder = new List<SmtFormula>(normalizedConditions.Count);
            foreach (var condition in normalizedConditions)
            {
                var status = SimplifyConcreteFacts(
                    condition,
                    stringEqualities,
                    integerEqualities,
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
            }

            return changed ? new SmtBinaryFormula(binaryFormula.Operator, left, right) : formula;
        }

        private static ConcreteFactPreparationStatus TryCollectIntegerFacts(
            IReadOnlyList<SmtFormula> conditions,
            Dictionary<SmtFormula, long> integerEqualities,
            HashSet<SmtFormula> integerNonZeroFacts)
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
                        integerEqualities,
                        integerNonZeroFacts,
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
            Dictionary<SmtFormula, long> integerEqualities,
            HashSet<SmtFormula> integerNonZeroFacts,
            ref bool changed)
        {
            if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
            {
                var leftStatus = TryCollectIntegerFacts(
                    andFormula.Left,
                    integerEqualities,
                    integerNonZeroFacts,
                    ref changed);
                if (leftStatus != ConcreteFactPreparationStatus.Ready)
                {
                    return leftStatus;
                }

                return TryCollectIntegerFacts(
                    andFormula.Right,
                    integerEqualities,
                    integerNonZeroFacts,
                    ref changed);
            }

            if (formula is not SmtBinaryFormula binaryFormula)
            {
                return ConcreteFactPreparationStatus.Ready;
            }

            TryCollectIntegerNonZeroFact(binaryFormula, integerNonZeroFacts, ref changed);

            if (binaryFormula.Operator == SmtBinaryOperator.NotEqual &&
                TryEvaluateInteger(binaryFormula.Left, integerEqualities, out var notEqualLeft) &&
                TryEvaluateInteger(binaryFormula.Right, integerEqualities, out var notEqualRight) &&
                notEqualLeft == notEqualRight)
            {
                return ConcreteFactPreparationStatus.Unsatisfiable;
            }

            if (binaryFormula.Operator != SmtBinaryOperator.Equal)
            {
                return ConcreteFactPreparationStatus.Ready;
            }

            var leftIsConcrete = TryEvaluateInteger(binaryFormula.Left, integerEqualities, out var leftValue);
            var rightIsConcrete = TryEvaluateInteger(binaryFormula.Right, integerEqualities, out var rightValue);
            if (leftIsConcrete && rightIsConcrete)
            {
                return leftValue == rightValue
                    ? ConcreteFactPreparationStatus.Ready
                    : ConcreteFactPreparationStatus.Unsatisfiable;
            }

            if (leftIsConcrete)
            {
                return TryAddIntegerEquality(integerEqualities, binaryFormula.Right, leftValue, ref changed)
                    ? ConcreteFactPreparationStatus.Ready
                    : ConcreteFactPreparationStatus.Unsatisfiable;
            }

            if (rightIsConcrete)
            {
                return TryAddIntegerEquality(integerEqualities, binaryFormula.Left, rightValue, ref changed)
                    ? ConcreteFactPreparationStatus.Ready
                    : ConcreteFactPreparationStatus.Unsatisfiable;
            }

            return ConcreteFactPreparationStatus.Ready;
        }

        private static bool TryAddIntegerEquality(
            Dictionary<SmtFormula, long> integerEqualities,
            SmtFormula formula,
            long value,
            ref bool changed)
        {
            if (formula.Kind != SmtValueKind.Int)
            {
                return true;
            }

            if (integerEqualities.TryGetValue(formula, out var existing))
            {
                return existing == value;
            }

            integerEqualities.Add(formula, value);
            changed = true;
            return true;
        }

        private static void TryCollectIntegerNonZeroFact(
            SmtBinaryFormula formula,
            HashSet<SmtFormula> integerNonZeroFacts,
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

            if (isNonZero && integerNonZeroFacts.Add(expression))
            {
                changed = true;
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
            IReadOnlyDictionary<SmtFormula, long> integerEqualities,
            HashSet<SmtFormula> integerNonZeroFacts)
        {
            switch (formula)
            {
                case SmtUnaryFormula unaryFormula:
                    return ValidateIntegerTermSafety(unaryFormula.Operand, integerEqualities, integerNonZeroFacts);
                case SmtBinaryFormula binaryFormula:
                    var leftStatus = ValidateIntegerTermSafety(binaryFormula.Left, integerEqualities, integerNonZeroFacts);
                    if (leftStatus != ConcreteFactPreparationStatus.Ready)
                    {
                        return leftStatus;
                    }

                    return ValidateIntegerTermSafety(binaryFormula.Right, integerEqualities, integerNonZeroFacts);
                case SmtIntegerUnaryTerm integerUnaryTerm:
                    return ValidateIntegerTermSafety(integerUnaryTerm.Operand, integerEqualities, integerNonZeroFacts);
                case SmtIntegerBinaryTerm integerBinaryTerm:
                    var integerLeftStatus = ValidateIntegerTermSafety(integerBinaryTerm.Left, integerEqualities, integerNonZeroFacts);
                    if (integerLeftStatus != ConcreteFactPreparationStatus.Ready)
                    {
                        return integerLeftStatus;
                    }

                    var integerRightStatus = ValidateIntegerTermSafety(integerBinaryTerm.Right, integerEqualities, integerNonZeroFacts);
                    if (integerRightStatus != ConcreteFactPreparationStatus.Ready)
                    {
                        return integerRightStatus;
                    }

                    if (integerBinaryTerm.Operator is not (SmtIntegerBinaryOperator.Divide or SmtIntegerBinaryOperator.Remainder))
                    {
                        return ConcreteFactPreparationStatus.Ready;
                    }

                    if (TryEvaluateInteger(integerBinaryTerm.Right, integerEqualities, out var denominator))
                    {
                        return denominator == 0
                            ? ConcreteFactPreparationStatus.Unknown
                            : ConcreteFactPreparationStatus.Ready;
                    }

                    return integerNonZeroFacts.Contains(integerBinaryTerm.Right)
                        ? ConcreteFactPreparationStatus.Ready
                        : ConcreteFactPreparationStatus.Unknown;
                case SmtStringLengthTerm stringLengthTerm:
                    return ValidateIntegerTermSafety(stringLengthTerm.Value, integerEqualities, integerNonZeroFacts);
                case SmtStringConcatTerm stringConcatTerm:
                    var concatLeftStatus = ValidateIntegerTermSafety(stringConcatTerm.Left, integerEqualities, integerNonZeroFacts);
                    if (concatLeftStatus != ConcreteFactPreparationStatus.Ready)
                    {
                        return concatLeftStatus;
                    }

                    return ValidateIntegerTermSafety(stringConcatTerm.Right, integerEqualities, integerNonZeroFacts);
                case SmtStringContainsFormula stringContainsFormula:
                    var containsValueStatus = ValidateIntegerTermSafety(stringContainsFormula.Value, integerEqualities, integerNonZeroFacts);
                    if (containsValueStatus != ConcreteFactPreparationStatus.Ready)
                    {
                        return containsValueStatus;
                    }

                    return ValidateIntegerTermSafety(stringContainsFormula.Search, integerEqualities, integerNonZeroFacts);
                case SmtStringStartsWithFormula stringStartsWithFormula:
                    var startsWithValueStatus = ValidateIntegerTermSafety(stringStartsWithFormula.Value, integerEqualities, integerNonZeroFacts);
                    if (startsWithValueStatus != ConcreteFactPreparationStatus.Ready)
                    {
                        return startsWithValueStatus;
                    }

                    return ValidateIntegerTermSafety(stringStartsWithFormula.Prefix, integerEqualities, integerNonZeroFacts);
                case SmtStringEndsWithFormula stringEndsWithFormula:
                    var endsWithValueStatus = ValidateIntegerTermSafety(stringEndsWithFormula.Value, integerEqualities, integerNonZeroFacts);
                    if (endsWithValueStatus != ConcreteFactPreparationStatus.Ready)
                    {
                        return endsWithValueStatus;
                    }

                    return ValidateIntegerTermSafety(stringEndsWithFormula.Suffix, integerEqualities, integerNonZeroFacts);
                case SmtRegexMatchFormula regexMatchFormula:
                    return ValidateIntegerTermSafety(regexMatchFormula.Value, integerEqualities, integerNonZeroFacts);
                case SmtConditionalFormula conditionalFormula:
                    var conditionStatus = ValidateIntegerTermSafety(conditionalFormula.Condition, integerEqualities, integerNonZeroFacts);
                    if (conditionStatus != ConcreteFactPreparationStatus.Ready)
                    {
                        return conditionStatus;
                    }

                    var trueStatus = ValidateIntegerTermSafety(conditionalFormula.WhenTrue, integerEqualities, integerNonZeroFacts);
                    if (trueStatus != ConcreteFactPreparationStatus.Ready)
                    {
                        return trueStatus;
                    }

                    return ValidateIntegerTermSafety(conditionalFormula.WhenFalse, integerEqualities, integerNonZeroFacts);
                default:
                    return ConcreteFactPreparationStatus.Ready;
            }
        }

        private static bool TryEvaluateConcreteBoolean(
            SmtFormula formula,
            IReadOnlyDictionary<SmtFormula, string> stringEqualities,
            IReadOnlyDictionary<SmtFormula, long> integerEqualities,
            out bool value)
        {
            switch (formula)
            {
                case SmtBooleanConstant booleanConstant:
                    value = booleanConstant.Value;
                    return true;
                case SmtUnaryFormula { Operator: SmtUnaryOperator.Not } unaryFormula
                    when TryEvaluateConcreteBoolean(unaryFormula.Operand, stringEqualities, integerEqualities, out var operand):
                    value = !operand;
                    return true;
                case SmtBinaryFormula binaryFormula:
                    return TryEvaluateConcreteBinaryBoolean(binaryFormula, stringEqualities, integerEqualities, out value);
                default:
                    value = false;
                    return false;
            }
        }

        private static bool ShouldPreserveSourceEqualityFact(SmtFormula formula)
        {
            if (formula is not SmtBinaryFormula { Operator: SmtBinaryOperator.Equal } binaryFormula)
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
            IReadOnlyDictionary<SmtFormula, string> stringEqualities,
            IReadOnlyDictionary<SmtFormula, long> integerEqualities,
            out bool value)
        {
            if (formula.Operator == SmtBinaryOperator.And)
            {
                if (TryEvaluateConcreteBoolean(formula.Left, stringEqualities, integerEqualities, out var left))
                {
                    if (!left)
                    {
                        value = false;
                        return true;
                    }

                    if (TryEvaluateConcreteBoolean(formula.Right, stringEqualities, integerEqualities, out var right))
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
                if (TryEvaluateConcreteBoolean(formula.Left, stringEqualities, integerEqualities, out var left))
                {
                    if (left)
                    {
                        value = true;
                        return true;
                    }

                    if (TryEvaluateConcreteBoolean(formula.Right, stringEqualities, integerEqualities, out var right))
                    {
                        value = right;
                        return true;
                    }
                }

                value = false;
                return false;
            }

            if (TryEvaluateStringLengthComparison(formula, stringEqualities, out value))
            {
                return true;
            }

            if (formula.Left.Kind == SmtValueKind.Int &&
                formula.Right.Kind == SmtValueKind.Int &&
                TryEvaluateInteger(formula.Left, integerEqualities, out var leftInteger) &&
                TryEvaluateInteger(formula.Right, integerEqualities, out var rightInteger))
            {
                value = CompareIntegers(formula.Operator, leftInteger, rightInteger);
                return true;
            }

            if (formula.Left.Kind == SmtValueKind.String &&
                formula.Right.Kind == SmtValueKind.String &&
                TryGetConcreteString(formula.Left, stringEqualities, out var leftString) &&
                TryGetConcreteString(formula.Right, stringEqualities, out var rightString))
            {
                value = CompareEquality(formula.Operator, string.Equals(leftString, rightString, StringComparison.Ordinal));
                return formula.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual;
            }

            if (formula.Left.Kind == SmtValueKind.Bool &&
                formula.Right.Kind == SmtValueKind.Bool &&
                TryEvaluateConcreteBoolean(formula.Left, stringEqualities, integerEqualities, out var leftBoolean) &&
                TryEvaluateConcreteBoolean(formula.Right, stringEqualities, integerEqualities, out var rightBoolean))
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
            IReadOnlyDictionary<SmtFormula, string> stringEqualities,
            out bool value)
        {
            if (!TryNormalizeStringLengthComparison(formula, out var stringValue, out var op, out var constant))
            {
                value = false;
                return false;
            }

            if (TryGetConcreteString(stringValue, stringEqualities, out var concreteString))
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
            IReadOnlyDictionary<SmtFormula, long> integerEqualities,
            out long value)
        {
            try
            {
                if (formula is SmtIntegerConstant integerConstant)
                {
                    value = integerConstant.Value;
                    return true;
                }

                if (integerEqualities.TryGetValue(formula, out value))
                {
                    return true;
                }

                switch (formula)
                {
                    case SmtIntegerUnaryTerm { Operator: SmtIntegerUnaryOperator.Negate } unaryTerm
                        when TryEvaluateInteger(unaryTerm.Operand, integerEqualities, out var operand):
                        value = checked(-operand);
                        return true;
                    case SmtIntegerBinaryTerm binaryTerm:
                        return TryEvaluateIntegerBinary(binaryTerm, integerEqualities, out value);
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
            IReadOnlyDictionary<SmtFormula, long> integerEqualities,
            out long value)
        {
            value = default;
            if (!TryEvaluateInteger(term.Left, integerEqualities, out var left) ||
                !TryEvaluateInteger(term.Right, integerEqualities, out var right))
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

        private static bool TryCollectStringEqualities(
            SmtFormula formula,
            Dictionary<SmtFormula, string> stringEqualities)
        {
            if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
            {
                return TryCollectStringEqualities(andFormula.Left, stringEqualities) &&
                    TryCollectStringEqualities(andFormula.Right, stringEqualities);
            }

            if (formula is not SmtBinaryFormula { Operator: SmtBinaryOperator.Equal } equalFormula)
            {
                return true;
            }

            if (TryGetConcreteString(equalFormula.Left, stringEqualities, out var leftValue) &&
                TryGetConcreteString(equalFormula.Right, stringEqualities, out var rightValue))
            {
                return string.Equals(leftValue, rightValue, StringComparison.Ordinal);
            }

            if (equalFormula.Left is SmtStringConstant leftConstant)
            {
                return TryAddStringEquality(stringEqualities, equalFormula.Right, leftConstant.Value);
            }

            if (equalFormula.Right is SmtStringConstant rightConstant)
            {
                return TryAddStringEquality(stringEqualities, equalFormula.Left, rightConstant.Value);
            }

            if (TryGetConcreteString(equalFormula.Left, stringEqualities, out leftValue))
            {
                return TryAddStringEquality(stringEqualities, equalFormula.Right, leftValue);
            }

            if (TryGetConcreteString(equalFormula.Right, stringEqualities, out rightValue))
            {
                return TryAddStringEquality(stringEqualities, equalFormula.Left, rightValue);
            }

            return true;
        }

        private static bool TryAddStringEquality(
            Dictionary<SmtFormula, string> stringEqualities,
            SmtFormula formula,
            string value)
        {
            if (stringEqualities.TryGetValue(formula, out var existing))
            {
                return string.Equals(existing, value, StringComparison.Ordinal);
            }

            stringEqualities.Add(formula, value);
            return true;
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
            Dictionary<SmtFormula, string> stringEqualities)
        {
            if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
            {
                var leftStatus = TryInferStringEqualitiesFromLengthConstrainedPredicates(
                    andFormula.Left,
                    stringLengthEqualities,
                    stringEqualities);
                if (leftStatus != ConcreteFactPreparationStatus.Ready)
                {
                    return leftStatus;
                }

                return TryInferStringEqualitiesFromLengthConstrainedPredicates(
                    andFormula.Right,
                    stringLengthEqualities,
                    stringEqualities);
            }

            if (!TryGetPositiveStringPredicateFact(formula, out var predicate) ||
                !stringLengthEqualities.TryGetValue(predicate.Value, out var knownLength) ||
                !TryGetConcreteString(predicate.Argument, stringEqualities, out var concreteArgument))
            {
                return ConcreteFactPreparationStatus.Ready;
            }

            if (knownLength < concreteArgument.Length)
            {
                return ConcreteFactPreparationStatus.Unsatisfiable;
            }

            if (knownLength == concreteArgument.Length)
            {
                if (!TryAddStringEquality(stringEqualities, predicate.Value, concreteArgument))
                {
                    return ConcreteFactPreparationStatus.Unsatisfiable;
                }
            }

            return ConcreteFactPreparationStatus.Ready;
        }

        private ConcreteFactPreparationStatus SimplifyConcreteFacts(
            SmtFormula formula,
            IReadOnlyDictionary<SmtFormula, string> stringEqualities,
            IReadOnlyDictionary<SmtFormula, long> integerEqualities,
            out SmtFormula preparedFormula,
            out bool changed)
        {
            preparedFormula = formula;
            changed = false;

            if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
            {
                var leftStatus = SimplifyConcreteFacts(
                    andFormula.Left,
                    stringEqualities,
                    integerEqualities,
                    out var left,
                    out var leftChanged);
                if (leftStatus != ConcreteFactPreparationStatus.Ready)
                {
                    return leftStatus;
                }

                var rightStatus = SimplifyConcreteFacts(
                    andFormula.Right,
                    stringEqualities,
                    integerEqualities,
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

            if (!ShouldPreserveSourceEqualityFact(formula) &&
                TryEvaluateConcreteBoolean(formula, stringEqualities, integerEqualities, out var concreteBoolean))
            {
                preparedFormula = new SmtBooleanConstant(concreteBoolean);
                changed = true;
                return ConcreteFactPreparationStatus.Ready;
            }

            if (TryGetRegexFact(formula, out var regexMatch, out var expectedMatch) &&
                TryGetConcreteString(regexMatch.Value, stringEqualities, out var concreteInput))
            {
                var validationStatus = TryValidateRegexMatch(
                    concreteInput,
                    regexMatch.Pattern,
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
                TryGetConcreteString(predicate.Value, stringEqualities, out var concreteValue) &&
                TryGetConcreteString(predicate.Argument, stringEqualities, out var concreteArgument))
            {
                var actualPredicateValue = predicate.Kind switch
                {
                    StringPredicateKind.Contains => concreteValue.Contains(concreteArgument),
                    StringPredicateKind.StartsWith => concreteValue.StartsWith(concreteArgument, StringComparison.Ordinal),
                    StringPredicateKind.EndsWith => concreteValue.EndsWith(concreteArgument, StringComparison.Ordinal),
                    _ => false,
                };

                if (actualPredicateValue != expectedPredicateValue)
                {
                    return ConcreteFactPreparationStatus.Unsatisfiable;
                }

                preparedFormula = new SmtBooleanConstant(true);
                changed = true;
            }

            return ConcreteFactPreparationStatus.Ready;
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
            IReadOnlyDictionary<SmtFormula, string> stringEqualities,
            out string value)
        {
            if (formula is SmtStringConstant stringConstant)
            {
                value = stringConstant.Value;
                return true;
            }

            if (stringEqualities.TryGetValue(formula, out var found))
            {
                value = found;
                return true;
            }

            if (formula is SmtStringConcatTerm stringConcatTerm &&
                TryGetConcreteString(stringConcatTerm.Left, stringEqualities, out var left) &&
                TryGetConcreteString(stringConcatTerm.Right, stringEqualities, out var right))
            {
                value = string.Concat(left, right);
                return true;
            }

            value = string.Empty;
            return false;
        }

        private ConcreteFactPreparationStatus TryValidateRegexMatch(
            string input,
            string pattern,
            out bool isMatch)
        {
            var key = new RegexValidationKey(input, pattern);
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
                    RegexOptions.None,
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

            public RegexValidationKey(string input, string pattern)
            {
                _input = input;
                _pattern = pattern;
            }

            public bool Equals(RegexValidationKey other)
            {
                return string.Equals(_input, other._input, StringComparison.Ordinal) &&
                    string.Equals(_pattern, other._pattern, StringComparison.Ordinal);
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
                        StringComparer.Ordinal.GetHashCode(_pattern);
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
