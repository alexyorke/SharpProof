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
            try
            {
                using var solver = _encoder.CreateSolver(timeout);
                foreach (var formula in pathConditions)
                {
                    solver.Assert(_encoder.EncodeCondition(formula));
                }

                return ToFeasibility(solver.Check());
            }
            catch (InvalidOperationException)
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
                    return (pathFeasibility, ToFeasibility(solver.Check()));
                }
                finally
                {
                    solver.Pop();
                }
            }
            catch (InvalidOperationException)
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
                CollectStringEqualities(condition, stringEqualities);
            }

            var builder = new List<SmtFormula>(normalizedConditions.Count);
            foreach (var condition in normalizedConditions)
            {
                var status = SimplifyConcreteFacts(
                    condition,
                    stringEqualities,
                    out var preparedCondition,
                    out var conditionChanged);
                if (status != ConcreteFactPreparationStatus.Ready)
                {
                    preparedConditions = Array.Empty<SmtFormula>();
                    return status;
                }

                changed |= conditionChanged;
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

        private static void CollectStringEqualities(
            SmtFormula formula,
            Dictionary<SmtFormula, string> stringEqualities)
        {
            if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } andFormula)
            {
                CollectStringEqualities(andFormula.Left, stringEqualities);
                CollectStringEqualities(andFormula.Right, stringEqualities);
                return;
            }

            if (formula is not SmtBinaryFormula { Operator: SmtBinaryOperator.Equal } equalFormula)
            {
                return;
            }

            if (equalFormula.Left is SmtStringConstant leftConstant)
            {
                AddStringEquality(stringEqualities, equalFormula.Right, leftConstant.Value);
            }

            if (equalFormula.Right is SmtStringConstant rightConstant)
            {
                AddStringEquality(stringEqualities, equalFormula.Left, rightConstant.Value);
            }
        }

        private static void AddStringEquality(
            Dictionary<SmtFormula, string> stringEqualities,
            SmtFormula formula,
            string value)
        {
            if (!stringEqualities.ContainsKey(formula))
            {
                stringEqualities.Add(formula, value);
            }
        }

        private ConcreteFactPreparationStatus SimplifyConcreteFacts(
            SmtFormula formula,
            IReadOnlyDictionary<SmtFormula, string> stringEqualities,
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
                    out var left,
                    out var leftChanged);
                if (leftStatus != ConcreteFactPreparationStatus.Ready)
                {
                    return leftStatus;
                }

                var rightStatus = SimplifyConcreteFacts(
                    andFormula.Right,
                    stringEqualities,
                    out var right,
                    out var rightChanged);
                if (rightStatus != ConcreteFactPreparationStatus.Ready)
                {
                    return rightStatus;
                }

                changed = leftChanged || rightChanged;
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

            if (TryGetRegexFact(formula, out var regexMatch, out var expectedMatch) &&
                stringEqualities.TryGetValue(regexMatch.Value, out var concreteInput))
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
