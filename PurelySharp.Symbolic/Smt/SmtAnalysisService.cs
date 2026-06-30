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

            if (IsMethodBudgetExceeded())
            {
                return Unknown("smt_method_budget_exceeded");
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
            var facts = new SyntacticFactSet();
            var conjuncts = ImmutableArray.CreateBuilder<SmtFormula>();
            foreach (var pathCondition in pathConditions)
            {
                foreach (var conjunct in EnumerateConjuncts(pathCondition))
                {
                    conjuncts.Add(conjunct);
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

                    if (facts.Add(conjunct, out var hasContradiction) &&
                        hasContradiction)
                    {
                        return true;
                    }

                    seen.Add(conjunct);
                }
            }

            facts.AddAll(conjuncts, out var inferredContradiction);
            if (inferredContradiction)
            {
                return true;
            }

            foreach (var conjunct in conjuncts)
            {
                if (facts.TryEvaluateBoolean(conjunct, out var value) &&
                    !value)
                {
                    return true;
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
                return TryGetNegatedIntegerComparison(formula, out term, out op, out constant);
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

        private static bool TryGetNegatedIntegerComparison(
            SmtFormula formula,
            out SmtFormula term,
            out SmtBinaryOperator op,
            out long constant)
        {
            term = null!;
            op = default;
            constant = default;

            if (formula is not SmtUnaryFormula { Operator: SmtUnaryOperator.Not } negated ||
                negated.Operand is not SmtBinaryFormula comparison ||
                !TryGetIntegerComparison(comparison, out term, out op, out constant))
            {
                return false;
            }

            op = NegateComparison(op);
            return true;
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

        private static SmtBinaryOperator NegateComparison(SmtBinaryOperator op)
        {
            return op switch
            {
                SmtBinaryOperator.Equal => SmtBinaryOperator.NotEqual,
                SmtBinaryOperator.NotEqual => SmtBinaryOperator.Equal,
                SmtBinaryOperator.LessThan => SmtBinaryOperator.GreaterThanOrEqual,
                SmtBinaryOperator.LessThanOrEqual => SmtBinaryOperator.GreaterThan,
                SmtBinaryOperator.GreaterThan => SmtBinaryOperator.LessThanOrEqual,
                SmtBinaryOperator.GreaterThanOrEqual => SmtBinaryOperator.LessThan,
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

            public long? ExactValue =>
                !IsContradictory &&
                LowerBound.HasValue &&
                UpperBound.HasValue &&
                LowerBound.Value == UpperBound.Value
                    ? LowerBound.Value
                    : null;

            public IntegerInterval Apply(SmtBinaryOperator op, long constant)
            {
                return op switch
                {
                    SmtBinaryOperator.Equal => WithExactValue(constant),
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

            private IntegerInterval WithExactValue(long value)
            {
                return new IntegerInterval(
                    value,
                    value,
                    ExcludedValues,
                    IsImpossible ||
                    LowerBound.HasValue && value < LowerBound.Value ||
                    UpperBound.HasValue && value > UpperBound.Value);
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

            if (query.Hazard.TriggerCondition is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } negatedTrigger &&
                IsFormulaSyntacticallyEntailed(negatedTrigger.Operand, pathConditions))
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

            if (ContainsSyntacticContradiction(pathConditions.Add(query.Hazard.TriggerCondition)))
            {
                return true;
            }

            pureReason = string.Empty;
            return false;
        }

        private static bool IsFormulaSyntacticallyEntailed(
            SmtFormula formula,
            ImmutableArray<SmtFormula> pathConditions)
        {
            var pathConjuncts = pathConditions
                .SelectMany(EnumerateConjuncts)
                .ToImmutableArray();
            return IsFormulaSyntacticallyEntailed(formula, pathConditions, pathConjuncts);
        }

        private static bool IsFormulaSyntacticallyEntailed(
            SmtFormula formula,
            ImmutableArray<SmtFormula> pathConditions,
            ImmutableArray<SmtFormula> pathConjuncts)
        {
            var facts = SyntacticFactSet.Create(pathConjuncts);
            if (formula is SmtBooleanConstant booleanConstant)
            {
                return booleanConstant.Value;
            }

            if (facts.TryEvaluateBoolean(formula, out var value))
            {
                return value;
            }

            foreach (var pathConjunct in pathConjuncts)
            {
                if (pathConjunct.Equals(formula))
                {
                    return true;
                }
            }

            if (formula is SmtBinaryFormula binary)
            {
                if (binary.Operator == SmtBinaryOperator.And)
                {
                    return IsFormulaSyntacticallyEntailed(binary.Left, pathConditions, pathConjuncts) &&
                        IsFormulaSyntacticallyEntailed(binary.Right, pathConditions, pathConjuncts);
                }

                if (binary.Operator == SmtBinaryOperator.Or)
                {
                    return IsFormulaSyntacticallyEntailed(binary.Left, pathConditions, pathConjuncts) ||
                        IsFormulaSyntacticallyEntailed(binary.Right, pathConditions, pathConjuncts);
                }
            }

            if (formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } negated)
            {
                return ContainsSyntacticContradiction(pathConditions.Add(negated.Operand));
            }

            return ContainsSyntacticContradiction(pathConditions.Add(new SmtUnaryFormula(SmtUnaryOperator.Not, formula)));
        }

        private sealed class SyntacticFactSet
        {
            private readonly Dictionary<SmtFormula, IntegerInterval> _integerIntervals = new();
            private readonly Dictionary<SmtFormula, string> _exactStrings = new();
            private readonly Dictionary<SmtFormula, ImmutableHashSet<string>> _excludedStrings = new();
            private readonly Dictionary<SmtFormula, bool> _exactBooleans = new();

            public static SyntacticFactSet Create(IEnumerable<SmtFormula> formulas)
            {
                var facts = new SyntacticFactSet();
                facts.AddAll(formulas, out _);
                return facts;
            }

            public bool AddAll(IEnumerable<SmtFormula> formulas, out bool hasContradiction)
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

                    if (hasContradiction || !addedThisPass)
                    {
                        break;
                    }
                }

                return anyAdded;
            }

            public bool Add(SmtFormula formula, out bool hasContradiction)
            {
                hasContradiction = false;
                var added = false;
                if (TryAddIntegerIntervalFact(formula, _integerIntervals, out var integerContradiction))
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

                if (TryEvaluateBoolean(formula, out var value) &&
                    !value)
                {
                    hasContradiction = true;
                }

                return added || hasContradiction;
            }

            public bool TryEvaluateBoolean(SmtFormula formula, out bool value)
            {
                if (_exactBooleans.TryGetValue(formula, out var exactValue))
                {
                    value = exactValue;
                    return true;
                }

                switch (formula)
                {
                    case SmtBooleanConstant booleanConstant:
                        value = booleanConstant.Value;
                        return true;
                    case SmtUnaryFormula { Operator: SmtUnaryOperator.Not } negated
                        when TryEvaluateBoolean(negated.Operand, out var operandValue):
                        value = !operandValue;
                        return true;
                    case SmtBinaryFormula { Operator: SmtBinaryOperator.And } binary:
                        {
                            var hasLeft = TryEvaluateBoolean(binary.Left, out var left);
                            var hasRight = TryEvaluateBoolean(binary.Right, out var right);
                            if (hasLeft && !left || hasRight && !right)
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
                            var hasLeft = TryEvaluateBoolean(binary.Left, out var left);
                            var hasRight = TryEvaluateBoolean(binary.Right, out var right);
                            if (hasLeft && left || hasRight && right)
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
                        return TryEvaluateComparison(binary, out value);
                    case SmtConditionalFormula conditional
                        when conditional.Kind == SmtValueKind.Bool &&
                             TryEvaluateBoolean(conditional.Condition, out var conditionValue):
                        return TryEvaluateBoolean(conditionValue ? conditional.WhenTrue : conditional.WhenFalse, out value);
                    case SmtStringContainsFormula contains
                        when TryGetKnownString(contains.Value, out var containsValue) &&
                             TryGetKnownString(contains.Search, out var containsSearch):
                        value = containsValue.Contains(containsSearch, StringComparison.Ordinal);
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

            private bool TryAddBooleanFact(SmtFormula formula, out bool hasContradiction)
            {
                return TryAddBooleanFact(formula, value: true, out hasContradiction);
            }

            private bool TryAddBooleanFact(
                SmtFormula formula,
                bool value,
                out bool hasContradiction)
            {
                hasContradiction = false;
                if (formula.Kind != SmtValueKind.Bool ||
                    formula is SmtBooleanConstant)
                {
                    return false;
                }

                if (formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } negated)
                {
                    return TryAddBooleanFact(negated.Operand, !value, out hasContradiction);
                }

                if (formula is SmtBinaryFormula binary)
                {
                    if (binary.Operator == SmtBinaryOperator.And)
                    {
                        if (value)
                        {
                            var addedLeft = TryAddBooleanFact(binary.Left, value: true, out var leftContradiction);
                            var addedRight = TryAddBooleanFact(binary.Right, value: true, out var rightContradiction);
                            hasContradiction = leftContradiction || rightContradiction;
                            return addedLeft || addedRight;
                        }

                        if (TryEvaluateBoolean(binary.Left, out var left) && left)
                        {
                            return TryAddBooleanFact(binary.Right, value: false, out hasContradiction);
                        }

                        if (TryEvaluateBoolean(binary.Right, out var right) && right)
                        {
                            return TryAddBooleanFact(binary.Left, value: false, out hasContradiction);
                        }
                    }
                    else if (binary.Operator == SmtBinaryOperator.Or)
                    {
                        if (!value)
                        {
                            var addedLeft = TryAddBooleanFact(binary.Left, value: false, out var leftContradiction);
                            var addedRight = TryAddBooleanFact(binary.Right, value: false, out var rightContradiction);
                            hasContradiction = leftContradiction || rightContradiction;
                            return addedLeft || addedRight;
                        }

                        if (TryEvaluateBoolean(binary.Left, out var left) && !left)
                        {
                            return TryAddBooleanFact(binary.Right, value: true, out hasContradiction);
                        }

                        if (TryEvaluateBoolean(binary.Right, out var right) && !right)
                        {
                            return TryAddBooleanFact(binary.Left, value: true, out hasContradiction);
                        }
                    }
                    else if (binary.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual &&
                        binary.Left.Kind == SmtValueKind.Bool &&
                        binary.Right.Kind == SmtValueKind.Bool)
                    {
                        if (TryEvaluateBoolean(binary.Left, out var left))
                        {
                            var expectedRight = binary.Operator == SmtBinaryOperator.Equal == value
                                ? left
                                : !left;
                            return TryAddBooleanFact(binary.Right, expectedRight, out hasContradiction);
                        }

                        if (TryEvaluateBoolean(binary.Right, out var right))
                        {
                            var expectedLeft = binary.Operator == SmtBinaryOperator.Equal == value
                                ? right
                                : !right;
                            return TryAddBooleanFact(binary.Left, expectedLeft, out hasContradiction);
                        }
                    }
                }

                var addedComparisonFact = TryAddKnownBooleanComparisonFact(formula, value, out var comparisonContradiction);
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

                if (TryAddIntegerIntervalFact(effectiveFormula, _integerIntervals, out var integerContradiction))
                {
                    added = true;
                    hasContradiction |= integerContradiction;
                }

                if (TryAddStringValueFact(effectiveFormula, out var stringContradiction))
                {
                    added = true;
                    hasContradiction |= stringContradiction;
                }

                return added;
            }

            private bool AddExactBoolean(
                SmtFormula formula,
                bool value,
                out bool hasContradiction)
            {
                hasContradiction = false;
                if (_exactBooleans.TryGetValue(formula, out var existing) &&
                    existing != value)
                {
                    hasContradiction = true;
                }

                _exactBooleans[formula] = value;
                return true;
            }

            private bool TryEvaluateComparison(SmtBinaryFormula binary, out bool value)
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
                        _ => false,
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
                        _ => false,
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
                        _ => false,
                    };
                    return binary.Operator is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual;
                }

                value = false;
                return false;
            }

            private bool TryAddStringValueFact(SmtFormula formula, out bool hasContradiction)
            {
                hasContradiction = false;
                if (!TryGetStringComparison(formula, out var term, out var op, out var value))
                {
                    return false;
                }

                if (op == SmtBinaryOperator.NotEqual)
                {
                    if (_exactStrings.TryGetValue(term, out var exactValue) &&
                        string.Equals(exactValue, value, StringComparison.Ordinal))
                    {
                        hasContradiction = true;
                    }

                    _excludedStrings[term] = _excludedStrings.TryGetValue(term, out var excluded)
                        ? excluded.Add(value)
                        : ImmutableHashSet.Create(StringComparer.Ordinal, value);
                    return true;
                }

                if (_exactStrings.TryGetValue(term, out var existing) &&
                    !string.Equals(existing, value, StringComparison.Ordinal))
                {
                    hasContradiction = true;
                }

                if (_excludedStrings.TryGetValue(term, out var excludedValues) &&
                    excludedValues.Contains(value))
                {
                    hasContradiction = true;
                }

                _exactStrings[term] = value;
                AddStringLengthFact(term, value.Length, out var lengthContradiction);
                hasContradiction |= lengthContradiction;
                return true;
            }

            private void AddStringLengthFact(
                SmtFormula stringFormula,
                int length,
                out bool hasContradiction)
            {
                var lengthFormula = new SmtStringLengthTerm(stringFormula);
                var interval = _integerIntervals.TryGetValue(lengthFormula, out var existing)
                    ? existing
                    : IntegerInterval.Unbounded;
                interval = interval.Apply(SmtBinaryOperator.Equal, length);
                hasContradiction = interval.IsContradictory;
                _integerIntervals[lengthFormula] = interval;
            }

            private bool TryGetKnownString(SmtFormula formula, out string value)
            {
                if (_exactStrings.TryGetValue(formula, out var exactValue))
                {
                    value = exactValue;
                    return true;
                }

                switch (formula)
                {
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

            private bool TryGetKnownInteger(SmtFormula formula, out long value)
            {
                if (_integerIntervals.TryGetValue(formula, out var interval) &&
                    interval.ExactValue.HasValue)
                {
                    value = interval.ExactValue.Value;
                    return true;
                }

                switch (formula)
                {
                    case SmtIntegerConstant integerConstant:
                        value = integerConstant.Value;
                        return true;
                    case SmtStringLengthTerm stringLength
                        when TryGetKnownString(stringLength.Value, out var stringValue):
                        value = stringValue.Length;
                        return true;
                    case SmtConditionalFormula conditional
                        when conditional.Kind == SmtValueKind.Int &&
                             TryEvaluateBoolean(conditional.Condition, out var conditionValue):
                        return TryGetKnownInteger(conditionValue ? conditional.WhenTrue : conditional.WhenFalse, out value);
                    case SmtIntegerUnaryTerm { Operator: SmtIntegerUnaryOperator.Negate } unary
                        when TryGetKnownInteger(unary.Operand, out var operand):
                        value = -operand;
                        return true;
                    case SmtIntegerBinaryTerm binary
                        when TryGetKnownInteger(binary.Left, out var left) &&
                             TryGetKnownInteger(binary.Right, out var right):
                        return TryEvaluateIntegerBinaryTerm(binary.Operator, left, right, out value);
                    default:
                        value = 0;
                        return false;
                }
            }

            private static bool TryEvaluateIntegerBinaryTerm(
                SmtIntegerBinaryOperator op,
                long left,
                long right,
                out long value)
            {
                try
                {
                    checked
                    {
                        switch (op)
                        {
                            case SmtIntegerBinaryOperator.Add:
                                value = left + right;
                                return true;
                            case SmtIntegerBinaryOperator.Subtract:
                                value = left - right;
                                return true;
                            case SmtIntegerBinaryOperator.Multiply:
                                value = left * right;
                                return true;
                            case SmtIntegerBinaryOperator.Divide when right != 0:
                                value = left / right;
                                return true;
                            case SmtIntegerBinaryOperator.Remainder when right != 0:
                                value = left % right;
                                return true;
                        }
                    }
                }
                catch (OverflowException)
                {
                }

                value = 0;
                return false;
            }

            private static bool TryGetStringComparison(
                SmtFormula formula,
                out SmtFormula term,
                out SmtBinaryOperator op,
                out string value)
            {
                term = null!;
                op = default;
                value = string.Empty;
                if (formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } negated &&
                    TryGetStringComparison(negated.Operand, out term, out op, out value))
                {
                    op = NegateComparison(op);
                    return op is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual;
                }

                if (formula is not SmtBinaryFormula binary ||
                    binary.Operator is not (SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual))
                {
                    return false;
                }

                if (binary.Left.Kind == SmtValueKind.String &&
                    binary.Right is SmtStringConstant rightConstant)
                {
                    term = binary.Left;
                    op = binary.Operator;
                    value = rightConstant.Value;
                    return true;
                }

                if (binary.Left is SmtStringConstant leftConstant &&
                    binary.Right.Kind == SmtValueKind.String)
                {
                    term = binary.Right;
                    op = binary.Operator;
                    value = leftConstant.Value;
                    return true;
                }

                return false;
            }
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
                SmtRuntimeTypeTestFormula runtimeTypeTest => 1 + CountFormulaNodes(runtimeTypeTest.Value),
                SmtConditionalFormula conditional => 1 + CountFormulaNodes(conditional.Condition) + CountFormulaNodes(conditional.WhenTrue) + CountFormulaNodes(conditional.WhenFalse),
                _ => 1,
            };
        }
    }
}
