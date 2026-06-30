using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using SearchLib.Purity;
using SearchLib.Smt;

namespace PurelySharp.Symbolic.Smt
{
    public sealed class SmtAnalysisService : IDisposable
    {
        private const int SharedQueryCacheEntryLimit = 4096;
        private static readonly ConcurrentDictionary<string, PurityProofResult> s_sharedQueryCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentQueue<string> s_sharedQueryCacheOrder = new();

        private readonly ConcurrentDictionary<string, PurityProofResult> _queryCache = new(StringComparer.Ordinal);
        private readonly object _solverLock = new();
        private PurityProofSearch? _proofSearch;
        private long _consumedQueryTicks;
        private int _executedQueryCount;
        private bool _solverUnavailable;
        private bool _disposed;

        public SmtAnalysisService(SmtAnalysisOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public SmtAnalysisOptions Options { get; }

        public int ExecutedQueryCount => _executedQueryCount;

        public int CacheEntryCount => _queryCache.Count;

        public PurityProofResult ClassifyPathFeasibility(IEnumerable<SmtFormula> pathConditions)
        {
            return Classify(new PurityProofQuery(
                pathConditions.ToArray(),
                new PurityHazard(
                    PurityHazardKind.BranchReachability,
                    new SmtBooleanConstant(true))));
        }

        public PurityProofResult ClassifyImplication(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula factFormula)
        {
            if (factFormula == null)
            {
                throw new ArgumentNullException(nameof(factFormula));
            }

            return Classify(new PurityProofQuery(
                pathConditions.ToArray(),
                new PurityHazard(
                    PurityHazardKind.BranchReachability,
                    new SmtUnaryFormula(SmtUnaryOperator.Not, factFormula))));
        }

        public bool PathConditionsImply(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula factFormula)
        {
            return ClassifyImplication(pathConditions, factFormula).Outcome == PurityProofOutcome.ProvablyPure;
        }

        public PurityProofResult Classify(PurityProofQuery query)
        {
            if (_disposed)
            {
                return Unknown("smt_disposed");
            }

            if (!Options.IsEnabled)
            {
                return Unknown("smt_disabled");
            }

            if (_solverUnavailable)
            {
                return Unknown("smt_unavailable");
            }

            var pathConditions = NormalizePathConditions(query.PathConditions);
            if (TryClassifySyntactically(query, pathConditions, out var syntacticResult))
            {
                return syntacticResult;
            }

            if (pathConditions.Length > Options.MaxPathConditions)
            {
                return Unknown("smt_path_condition_budget_exceeded");
            }

            if (CountFormulaNodes(pathConditions) + CountFormulaNodes(query.Hazard.TriggerCondition) > Options.MaxExpressionNodes)
            {
                return Unknown("smt_expression_budget_exceeded");
            }

            if (IsMethodBudgetExceeded())
            {
                return Unknown("smt_method_budget_exceeded");
            }

            var normalizedQuery = new PurityProofQuery(pathConditions, query.Hazard);
            var key = CreateQueryKey(normalizedQuery);
            if (_queryCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (TryGetSharedResult(key, out var sharedResult))
            {
                _queryCache.TryAdd(key, sharedResult);
                return sharedResult;
            }

            var result = ClassifyCore(normalizedQuery);
            _queryCache.TryAdd(key, result);
            AddSharedResult(key, result);
            return result;
        }

        private PurityProofResult ClassifyCore(PurityProofQuery query)
        {
            var queryClock = Stopwatch.StartNew();
            try
            {
                lock (_solverLock)
                {
                    if (_disposed)
                    {
                        return Unknown("smt_disposed");
                    }

                    Interlocked.Increment(ref _executedQueryCount);
                    var search = GetOrCreateProofSearch();
                    return search.Classify(query, Options.QueryTimeout);
                }
            }
            catch (InvalidOperationException)
            {
                return Unknown("smt_encoding_failure");
            }
            catch (Exception ex) when (IsZ3OrEncodingFailure(ex))
            {
                _solverUnavailable = true;
                DisposeProofSearch();
                return Unknown("smt_unavailable");
            }
            finally
            {
                queryClock.Stop();
                Interlocked.Add(ref _consumedQueryTicks, queryClock.ElapsedTicks);
            }
        }

        public void Dispose()
        {
            _disposed = true;
            DisposeProofSearch();
        }

        private PurityProofSearch GetOrCreateProofSearch()
        {
            if (_proofSearch != null)
            {
                return _proofSearch;
            }

            _proofSearch = new PurityProofSearch();
            return _proofSearch;
        }

        private void DisposeProofSearch()
        {
            lock (_solverLock)
            {
                _proofSearch?.Dispose();
                _proofSearch = null;
            }
        }

        private bool IsMethodBudgetExceeded()
        {
            var budgetTicks = Options.MethodBudget.TotalSeconds * Stopwatch.Frequency;
            return Interlocked.Read(ref _consumedQueryTicks) > budgetTicks;
        }

        private bool TryGetSharedResult(string queryKey, out PurityProofResult result)
        {
            if (Options.UseSharedResultCache)
            {
                return s_sharedQueryCache.TryGetValue(CreateSharedQueryKey(Options, queryKey), out result);
            }

            result = default!;
            return false;
        }

        private void AddSharedResult(string queryKey, PurityProofResult result)
        {
            if (!Options.UseSharedResultCache ||
                !IsShareableResult(result))
            {
                return;
            }

            var sharedKey = CreateSharedQueryKey(Options, queryKey);
            if (!s_sharedQueryCache.TryAdd(sharedKey, result))
            {
                return;
            }

            s_sharedQueryCacheOrder.Enqueue(sharedKey);
            while (s_sharedQueryCache.Count > SharedQueryCacheEntryLimit &&
                s_sharedQueryCacheOrder.TryDequeue(out var oldestKey))
            {
                s_sharedQueryCache.TryRemove(oldestKey, out _);
            }
        }

        private static bool IsShareableResult(PurityProofResult result)
        {
            return result.Outcome is PurityProofOutcome.ProvablyPure or PurityProofOutcome.ProvablyImpure;
        }

        private static PurityProofResult Unknown(string reason)
        {
            return new PurityProofResult(
                PurityProofOutcome.Unknown,
                Feasibility.Unknown,
                Feasibility.Unknown,
                reason);
        }

        private static bool IsZ3OrEncodingFailure(Exception ex)
        {
            return ex is DllNotFoundException ||
                ex is BadImageFormatException ||
                ex is FileNotFoundException ||
                ex is TypeInitializationException;
        }

        private static string CreateQueryKey(PurityProofQuery query)
        {
            return string.Join(";", query.PathConditions.Select(static condition => condition.ToString())) +
                "|hazard=" +
                query.Hazard.Kind +
                "|" +
                query.Hazard.Visibility +
                "|" +
                query.Hazard.TriggerCondition;
        }

        private static string CreateSharedQueryKey(SmtAnalysisOptions options, string queryKey)
        {
            return options.Mode +
                "|timeout_ms=" +
                (long)options.QueryTimeout.TotalMilliseconds +
                "|max_path=" +
                options.MaxPathConditions +
                "|max_expr=" +
                options.MaxExpressionNodes +
                "|" +
                queryKey;
        }

        private static ImmutableArray<SmtFormula> NormalizePathConditions(IEnumerable<SmtFormula> pathConditions)
        {
            var builder = ImmutableArray.CreateBuilder<SmtFormula>();
            var seen = new HashSet<SmtFormula>();
            foreach (var pathCondition in pathConditions)
            {
                if (pathCondition is SmtBooleanConstant { Value: true })
                {
                    continue;
                }

                if (seen.Add(pathCondition))
                {
                    builder.Add(pathCondition);
                }
            }

            return builder
                .OrderBy(static condition => condition.ToString(), StringComparer.Ordinal)
                .ToImmutableArray();
        }

        private static bool TryClassifySyntactically(
            PurityProofQuery query,
            ImmutableArray<SmtFormula> pathConditions,
            out PurityProofResult result)
        {
            if (ContainsSyntacticContradiction(pathConditions))
            {
                result = new PurityProofResult(
                    PurityProofOutcome.ProvablyPure,
                    Feasibility.Unsatisfiable,
                    Feasibility.Unsatisfiable,
                    "path_unsatisfiable");
                return true;
            }

            if (IsHazardTriggerSyntacticallyUnreachable(query, pathConditions, out var pureReason))
            {
                result = new PurityProofResult(
                    PurityProofOutcome.ProvablyPure,
                    Feasibility.Unknown,
                    Feasibility.Unsatisfiable,
                    pureReason);
                return true;
            }

            result = Unknown("smt_syntactic_no_match");
            return false;
        }

        private static bool ContainsSyntacticContradiction(ImmutableArray<SmtFormula> pathConditions)
        {
            var seen = new List<SmtFormula>(pathConditions.Length);
            var integerIntervals = new Dictionary<SmtFormula, IntegerInterval>();
            foreach (var pathCondition in pathConditions)
            {
                foreach (var conjunct in EnumerateConjuncts(pathCondition))
                {
                    if (conjunct is SmtBooleanConstant { Value: false })
                    {
                        return true;
                    }

                    foreach (var existing in seen)
                    {
                        if (AreSyntacticComplements(conjunct, existing))
                        {
                            return true;
                        }
                    }

                    if (TryAddIntegerIntervalFact(conjunct, integerIntervals, out var hasContradiction) &&
                        hasContradiction)
                    {
                        return true;
                    }

                    seen.Add(conjunct);
                }
            }

            return false;
        }

        private static IEnumerable<SmtFormula> EnumerateConjuncts(SmtFormula formula)
        {
            if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } binary)
            {
                foreach (var left in EnumerateConjuncts(binary.Left))
                {
                    yield return left;
                }

                foreach (var right in EnumerateConjuncts(binary.Right))
                {
                    yield return right;
                }

                yield break;
            }

            yield return formula;
        }

        private static bool TryAddIntegerIntervalFact(
            SmtFormula formula,
            Dictionary<SmtFormula, IntegerInterval> intervals,
            out bool hasContradiction)
        {
            hasContradiction = false;
            if (!TryGetIntegerComparison(formula, out var term, out var op, out var constant))
            {
                return false;
            }

            var interval = intervals.TryGetValue(term, out var existing)
                ? existing
                : IntegerInterval.Unbounded;
            interval = interval.Apply(op, constant);
            hasContradiction = interval.IsContradictory;
            intervals[term] = interval;
            return true;
        }

        private static bool TryGetIntegerComparison(
            SmtFormula formula,
            out SmtFormula term,
            out SmtBinaryOperator op,
            out long constant)
        {
            term = null!;
            op = default;
            constant = default;
            if (formula is not SmtBinaryFormula binary ||
                binary.Operator is not (SmtBinaryOperator.Equal or
                    SmtBinaryOperator.NotEqual or
                    SmtBinaryOperator.LessThan or
                    SmtBinaryOperator.LessThanOrEqual or
                    SmtBinaryOperator.GreaterThan or
                    SmtBinaryOperator.GreaterThanOrEqual))
            {
                return false;
            }

            if (binary.Left.Kind == SmtValueKind.Int &&
                binary.Right is SmtIntegerConstant rightConstant)
            {
                term = binary.Left;
                op = binary.Operator;
                constant = rightConstant.Value;
                return true;
            }

            if (binary.Left is SmtIntegerConstant leftConstant &&
                binary.Right.Kind == SmtValueKind.Int)
            {
                term = binary.Right;
                op = ReverseComparison(binary.Operator);
                constant = leftConstant.Value;
                return true;
            }

            return false;
        }

        private static SmtBinaryOperator ReverseComparison(SmtBinaryOperator op)
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

        private readonly struct IntegerInterval
        {
            private IntegerInterval(
                long? lowerBound,
                long? upperBound,
                ImmutableHashSet<long> excludedValues,
                bool isImpossible)
            {
                LowerBound = lowerBound;
                UpperBound = upperBound;
                ExcludedValues = excludedValues;
                IsImpossible = isImpossible;
            }

            public static IntegerInterval Unbounded { get; } = new IntegerInterval(
                lowerBound: null,
                upperBound: null,
                excludedValues: ImmutableHashSet<long>.Empty,
                isImpossible: false);

            public long? LowerBound { get; }
            public long? UpperBound { get; }
            public ImmutableHashSet<long> ExcludedValues { get; }
            public bool IsImpossible { get; }

            public bool IsContradictory =>
                IsImpossible ||
                LowerBound.HasValue &&
                UpperBound.HasValue &&
                LowerBound.Value > UpperBound.Value ||
                LowerBound.HasValue &&
                UpperBound.HasValue &&
                LowerBound.Value == UpperBound.Value &&
                ExcludedValues.Contains(LowerBound.Value);

            public IntegerInterval Apply(SmtBinaryOperator op, long constant)
            {
                return op switch
                {
                    SmtBinaryOperator.Equal => new IntegerInterval(
                        constant,
                        constant,
                        ExcludedValues,
                        IsImpossible),
                    SmtBinaryOperator.NotEqual => new IntegerInterval(
                        LowerBound,
                        UpperBound,
                        ExcludedValues.Add(constant),
                        IsImpossible),
                    SmtBinaryOperator.GreaterThan => constant == long.MaxValue
                        ? Impossible()
                        : WithLowerBound(constant + 1),
                    SmtBinaryOperator.GreaterThanOrEqual => WithLowerBound(constant),
                    SmtBinaryOperator.LessThan => constant == long.MinValue
                        ? Impossible()
                        : WithUpperBound(constant - 1),
                    SmtBinaryOperator.LessThanOrEqual => WithUpperBound(constant),
                    _ => this,
                };
            }

            private IntegerInterval WithLowerBound(long lowerBound)
            {
                return new IntegerInterval(
                    LowerBound.HasValue ? Math.Max(LowerBound.Value, lowerBound) : lowerBound,
                    UpperBound,
                    ExcludedValues,
                    IsImpossible);
            }

            private IntegerInterval WithUpperBound(long upperBound)
            {
                return new IntegerInterval(
                    LowerBound,
                    UpperBound.HasValue ? Math.Min(UpperBound.Value, upperBound) : upperBound,
                    ExcludedValues,
                    IsImpossible);
            }

            private IntegerInterval Impossible()
            {
                return new IntegerInterval(
                    LowerBound,
                    UpperBound,
                    ExcludedValues,
                    isImpossible: true);
            }
        }

        private static bool IsHazardTriggerSyntacticallyUnreachable(
            PurityProofQuery query,
            ImmutableArray<SmtFormula> pathConditions,
            out string pureReason)
        {
            pureReason = string.Empty;
            if (!TryGetTriggerBasedPureReason(query.Hazard, out pureReason))
            {
                return false;
            }

            if (query.Hazard.TriggerCondition is SmtBooleanConstant { Value: false })
            {
                return true;
            }

            foreach (var pathCondition in pathConditions)
            {
                if (AreSyntacticComplements(pathCondition, query.Hazard.TriggerCondition))
                {
                    return true;
                }
            }

            pureReason = string.Empty;
            return false;
        }

        private static bool TryGetTriggerBasedPureReason(PurityHazard hazard, out string reason)
        {
            reason = string.Empty;
            if (hazard.Visibility == PurityEffectVisibility.InternalOnly)
            {
                return false;
            }

            reason = hazard.Kind switch
            {
                PurityHazardKind.BranchReachability => "branch_unreachable",
                PurityHazardKind.ImpureCallReachability => "impure_call_unreachable",
                PurityHazardKind.CallerVisibleMemoryWrite => "memory_write_unreachable",
                PurityHazardKind.NullDereference => "null_dereference_unreachable",
                PurityHazardKind.DivideByZero => "divide_by_zero_unreachable",
                _ => string.Empty,
            };

            return reason.Length != 0;
        }

        private static bool AreSyntacticComplements(SmtFormula left, SmtFormula right)
        {
            if (left is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } leftNot &&
                leftNot.Operand.Equals(right))
            {
                return true;
            }

            if (right is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } rightNot &&
                rightNot.Operand.Equals(left))
            {
                return true;
            }

            if (left is not SmtBinaryFormula leftBinary ||
                right is not SmtBinaryFormula rightBinary)
            {
                return false;
            }

            if (!HaveSameOperands(leftBinary, rightBinary))
            {
                return false;
            }

            return AreComplementaryOperators(leftBinary.Operator, rightBinary.Operator);
        }

        private static bool HaveSameOperands(SmtBinaryFormula left, SmtBinaryFormula right)
        {
            if (left.Left.Equals(right.Left) && left.Right.Equals(right.Right))
            {
                return true;
            }

            return IsSymmetricComparison(left.Operator) &&
                IsSymmetricComparison(right.Operator) &&
                left.Left.Equals(right.Right) &&
                left.Right.Equals(right.Left);
        }

        private static bool IsSymmetricComparison(SmtBinaryOperator op)
        {
            return op is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual;
        }

        private static bool AreComplementaryOperators(SmtBinaryOperator left, SmtBinaryOperator right)
        {
            return (left, right) switch
            {
                (SmtBinaryOperator.Equal, SmtBinaryOperator.NotEqual) => true,
                (SmtBinaryOperator.NotEqual, SmtBinaryOperator.Equal) => true,
                (SmtBinaryOperator.LessThan, SmtBinaryOperator.GreaterThanOrEqual) => true,
                (SmtBinaryOperator.GreaterThanOrEqual, SmtBinaryOperator.LessThan) => true,
                (SmtBinaryOperator.LessThanOrEqual, SmtBinaryOperator.GreaterThan) => true,
                (SmtBinaryOperator.GreaterThan, SmtBinaryOperator.LessThanOrEqual) => true,
                _ => false,
            };
        }

        private static int CountFormulaNodes(IEnumerable<SmtFormula> formulas)
        {
            var count = 0;
            foreach (var formula in formulas)
            {
                count += CountFormulaNodes(formula);
            }

            return count;
        }

        private static int CountFormulaNodes(SmtFormula formula)
        {
            return formula switch
            {
                SmtUnaryFormula unary => 1 + CountFormulaNodes(unary.Operand),
                SmtBinaryFormula binary => 1 + CountFormulaNodes(binary.Left) + CountFormulaNodes(binary.Right),
                SmtIntegerUnaryTerm unary => 1 + CountFormulaNodes(unary.Operand),
                SmtIntegerBinaryTerm binary => 1 + CountFormulaNodes(binary.Left) + CountFormulaNodes(binary.Right),
                SmtStringLengthTerm stringLength => 1 + CountFormulaNodes(stringLength.Value),
                SmtStringConcatTerm stringConcat => 1 + CountFormulaNodes(stringConcat.Left) + CountFormulaNodes(stringConcat.Right),
                SmtStringContainsFormula stringContains => 1 + CountFormulaNodes(stringContains.Value) + CountFormulaNodes(stringContains.Search),
                SmtStringStartsWithFormula stringStartsWith => 1 + CountFormulaNodes(stringStartsWith.Value) + CountFormulaNodes(stringStartsWith.Prefix),
                SmtStringEndsWithFormula stringEndsWith => 1 + CountFormulaNodes(stringEndsWith.Value) + CountFormulaNodes(stringEndsWith.Suffix),
                SmtRegexMatchFormula regexMatch => 1 + CountFormulaNodes(regexMatch.Value) + Math.Max(1, regexMatch.Pattern.Length / 8),
                SmtConditionalFormula conditional => 1 + CountFormulaNodes(conditional.Condition) + CountFormulaNodes(conditional.WhenTrue) + CountFormulaNodes(conditional.WhenFalse),
                _ => 1,
            };
        }
    }
}
