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

            public IntegerInterval Intersect(IntegerInterval other)
            {
                var interval = this;
                if (other.IsImpossible)
                {
                    interval = interval.Impossible();
                }

                if (other.LowerBound.HasValue)
                {
                    interval = interval.WithLowerBound(other.LowerBound.Value);
                }

                if (other.UpperBound.HasValue)
                {
                    interval = interval.WithUpperBound(other.UpperBound.Value);
                }

                foreach (var excludedValue in other.ExcludedValues)
                {
                    interval = interval.Apply(SmtBinaryOperator.NotEqual, excludedValue);
                }

                return interval;
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
            private const int MaxAffineExpansionDepth = 8;

            private readonly Dictionary<SmtFormula, IntegerInterval> _integerIntervals = new();
            private readonly Dictionary<SmtFormula, string> _exactStrings = new();
            private readonly Dictionary<SmtFormula, ImmutableHashSet<string>> _excludedStrings = new();
            private readonly Dictionary<SmtFormula, bool> _referenceNullStates = new();
            private readonly Dictionary<SmtFormula, bool> _exactBooleans = new();
            private readonly Dictionary<SmtFormula, SmtFormula> _aliases = new();
            private readonly Dictionary<SmtFormula, BooleanEquivalenceParent> _booleanEquivalences = new();

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
                {
                    hasContradiction = true;
                }

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
                        Right.Kind: SmtValueKind.Int,
                    })
                {
                    return added || hasContradiction;
                }

                if (!TryGetAliasComparison(formula, out var left, out var right))
                {
                    return added;
                }

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
                {
                    return false;
                }

                var leftFormula = NormalizeAliases(binary.Left);
                var rightFormula = NormalizeAliases(binary.Right);
                if (!TryGetAffineIntegerTerm(leftFormula, depth: 0, out var left) ||
                    !TryGetAffineIntegerTerm(rightFormula, depth: 0, out var right))
                {
                    return false;
                }

                if (TrySubtract(left, right, out var difference))
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
                        if (hasContradiction)
                        {
                            return true;
                        }

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
                AffineIntegerTerm left,
                AffineIntegerTerm right,
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
                {
                    return false;
                }

                SmtFormula alias;
                SmtFormula baseTerm;
                long offset;
                var leftHasInterval = _integerIntervals.ContainsKey(left.BaseTerm);
                var rightHasInterval = _integerIntervals.ContainsKey(right.BaseTerm);
                if (leftHasInterval && !rightHasInterval)
                {
                    alias = right.BaseTerm;
                    baseTerm = left.BaseTerm;
                    if (!TrySubtract(left.Offset, right.Offset, out offset))
                    {
                        return false;
                    }
                }
                else if (rightHasInterval && !leftHasInterval)
                {
                    alias = left.BaseTerm;
                    baseTerm = right.BaseTerm;
                    if (!TrySubtract(right.Offset, left.Offset, out offset))
                    {
                        return false;
                    }
                }
                else if (string.CompareOrdinal(left.BaseTerm.ToString(), right.BaseTerm.ToString()) <= 0)
                {
                    alias = right.BaseTerm;
                    baseTerm = left.BaseTerm;
                    if (!TrySubtract(left.Offset, right.Offset, out offset))
                    {
                        return false;
                    }
                }
                else
                {
                    alias = left.BaseTerm;
                    baseTerm = right.BaseTerm;
                    if (!TrySubtract(right.Offset, left.Offset, out offset))
                    {
                        return false;
                    }
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
                {
                    return false;
                }

                _aliases[alias] = canonical;
                MergeIntegerFacts(canonical, alias, out var integerContradiction);
                MergeStringFacts(canonical, alias, out var stringContradiction);
                MergeReferenceFacts(canonical, alias, out var referenceContradiction);
                hasContradiction = integerContradiction || stringContradiction || referenceContradiction;
                return true;
            }

            private static bool TrySolveSingleAffineEquality(
                AffineIntegerTerm difference,
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
                    if (!TryNegate(quotient, out constant))
                    {
                        return false;
                    }

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
                {
                    return false;
                }

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
                if (left.Equals(right))
                {
                    return false;
                }

                var leftText = left.ToString();
                var rightText = right.ToString();
                var canonical = string.CompareOrdinal(leftText, rightText) <= 0 ? left : right;
                var alias = canonical.Equals(left) ? right : left;
                _aliases[alias] = canonical;
                MergeIntegerFacts(canonical, alias, out var integerContradiction);
                MergeStringFacts(canonical, alias, out var stringContradiction);
                MergeReferenceFacts(canonical, alias, out var referenceContradiction);
                hasContradiction = integerContradiction || stringContradiction || referenceContradiction;
                return true;
            }

            private SmtFormula FindCanonical(SmtFormula formula)
            {
                if (!_aliases.TryGetValue(formula, out var parent))
                {
                    return formula;
                }

                var canonical = FindCanonical(parent);
                if (!canonical.Equals(parent))
                {
                    _aliases[formula] = canonical;
                }

                return canonical;
            }

            private void MergeIntegerFacts(
                SmtFormula canonical,
                SmtFormula alias,
                out bool hasContradiction)
            {
                hasContradiction = false;
                if (!_integerIntervals.TryGetValue(alias, out var aliasInterval))
                {
                    return;
                }

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

                if (!_exactStrings.TryGetValue(alias, out var aliasExact))
                {
                    return;
                }

                if (_exactStrings.TryGetValue(canonical, out var existingExact) &&
                    !string.Equals(existingExact, aliasExact, StringComparison.Ordinal))
                {
                    hasContradiction = true;
                }

                if (_excludedStrings.TryGetValue(canonical, out var excluded) &&
                    excluded.Contains(aliasExact))
                {
                    hasContradiction = true;
                }

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
                if (!_referenceNullStates.TryGetValue(alias, out var aliasIsNull))
                {
                    return;
                }

                if (_referenceNullStates.TryGetValue(canonical, out var canonicalIsNull))
                {
                    hasContradiction = canonicalIsNull != aliasIsNull;
                }
                else
                {
                    _referenceNullStates[canonical] = aliasIsNull;
                }

                _referenceNullStates.Remove(alias);
            }

            public bool TryEvaluateBoolean(SmtFormula formula, out bool value)
            {
                formula = NormalizeAliases(formula);
                var canonical = FindBooleanCanonical(formula, out var isNegatedFromCanonical);
                if (!canonical.Equals(formula))
                {
                    if (_exactBooleans.TryGetValue(canonical, out var canonicalExactValue))
                    {
                        value = canonicalExactValue ^ isNegatedFromCanonical;
                        return true;
                    }

                    if (TryEvaluateBoolean(canonical, out var canonicalValue))
                    {
                        value = canonicalValue ^ isNegatedFromCanonical;
                        return true;
                    }
                }

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
                {
                    hasContradiction = true;
                }

                _exactBooleans[canonical] = canonicalValue;
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
                    !CanRelateBooleanTerm(formula.Left) ||
                    !CanRelateBooleanTerm(formula.Right))
                {
                    return false;
                }

                var left = NormalizeAliases(formula.Left);
                var right = NormalizeAliases(formula.Right);
                var differs = formula.Operator == SmtBinaryOperator.NotEqual;
                if (!value)
                {
                    differs = !differs;
                }

                return UnionBooleanEquivalences(left, right, differs, out hasContradiction);
            }

            private static bool CanRelateBooleanTerm(SmtFormula formula)
            {
                if (formula.Kind != SmtValueKind.Bool)
                {
                    return false;
                }

                return formula switch
                {
                    SmtVariable => true,
                    SmtStringContainsFormula => true,
                    SmtStringStartsWithFormula => true,
                    SmtStringEndsWithFormula => true,
                    SmtRegexMatchFormula => true,
                    SmtRuntimeTypeTestFormula => true,
                    SmtBinaryFormula binary => binary.Operator is (
                            SmtBinaryOperator.Equal or
                            SmtBinaryOperator.NotEqual or
                            SmtBinaryOperator.LessThan or
                            SmtBinaryOperator.LessThanOrEqual or
                            SmtBinaryOperator.GreaterThan or
                            SmtBinaryOperator.GreaterThanOrEqual) &&
                        binary.Left.Kind != SmtValueKind.Bool &&
                        binary.Right.Kind != SmtValueKind.Bool,
                    _ => false,
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

                var leftText = leftRoot.ToString();
                var rightText = rightRoot.ToString();
                var canonical = string.CompareOrdinal(leftText, rightText) <= 0 ? leftRoot : rightRoot;
                var alias = canonical.Equals(leftRoot) ? rightRoot : leftRoot;
                _booleanEquivalences[alias] = new BooleanEquivalenceParent(canonical, rootDiffers);
                MergeBooleanFacts(canonical, alias, rootDiffers, out hasContradiction);
                return true;
            }

            private SmtFormula FindBooleanCanonical(SmtFormula formula, out bool isNegatedFromCanonical)
            {
                if (!_booleanEquivalences.TryGetValue(formula, out var parent))
                {
                    isNegatedFromCanonical = false;
                    return formula;
                }

                var canonical = FindBooleanCanonical(parent.Parent, out var parentNegated);
                isNegatedFromCanonical = parent.IsNegatedFromParent ^ parentNegated;
                if (!canonical.Equals(parent.Parent))
                {
                    _booleanEquivalences[formula] = new BooleanEquivalenceParent(canonical, isNegatedFromCanonical);
                }

                return canonical;
            }

            private void MergeBooleanFacts(
                SmtFormula canonical,
                SmtFormula alias,
                bool aliasDiffersFromCanonical,
                out bool hasContradiction)
            {
                hasContradiction = false;
                if (!_exactBooleans.TryGetValue(alias, out var aliasValue))
                {
                    return;
                }

                var canonicalValue = aliasValue ^ aliasDiffersFromCanonical;
                if (_exactBooleans.TryGetValue(canonical, out var existing) &&
                    existing != canonicalValue)
                {
                    hasContradiction = true;
                }

                _exactBooleans[canonical] = canonicalValue;
                _exactBooleans.Remove(alias);
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

                if (TryEvaluateBooleanEquivalenceComparison(binary, out value))
                {
                    return true;
                }

                if (TryEvaluateReferenceComparison(binary, out value))
                {
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
                {
                    return false;
                }

                var left = NormalizeAliases(binary.Left);
                var right = NormalizeAliases(binary.Right);
                var leftRoot = FindBooleanCanonical(left, out var leftNegated);
                var rightRoot = FindBooleanCanonical(right, out var rightNegated);
                if (!leftRoot.Equals(rightRoot))
                {
                    return false;
                }

                var areEqual = leftNegated == rightNegated;
                value = binary.Operator == SmtBinaryOperator.Equal
                    ? areEqual
                    : !areEqual;
                return true;
            }

            private bool TryAddStringValueFact(SmtFormula formula, out bool hasContradiction)
            {
                hasContradiction = false;
                if (!TryGetStringComparison(formula, out var term, out var op, out var value))
                {
                    return false;
                }

                term = NormalizeAliases(term);
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

            private bool TryAddReferenceNullFact(SmtFormula formula, out bool hasContradiction)
            {
                hasContradiction = false;
                if (!TryGetReferenceNullComparison(formula, out var term, out var isNull))
                {
                    return false;
                }

                term = NormalizeAliases(term);
                if (term is SmtNullConstant)
                {
                    hasContradiction = !isNull;
                    return hasContradiction;
                }

                if (_referenceNullStates.TryGetValue(term, out var existing) &&
                    existing != isNull)
                {
                    hasContradiction = true;
                }
                else
                {
                    _referenceNullStates[term] = isNull;
                }

                return true;
            }

            private static bool TryGetReferenceNullComparison(
                SmtFormula formula,
                out SmtFormula term,
                out bool isNull)
            {
                term = null!;
                isNull = false;

                if (formula is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } negated &&
                    TryGetReferenceNullComparison(negated.Operand, out term, out isNull))
                {
                    isNull = !isNull;
                    return true;
                }

                if (formula is not SmtBinaryFormula binary ||
                    binary.Operator is not (SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual))
                {
                    return false;
                }

                var comparisonIsNull = binary.Operator == SmtBinaryOperator.Equal;
                if (binary.Left is SmtNullConstant && binary.Right is SmtNullConstant)
                {
                    term = binary.Right;
                    isNull = comparisonIsNull;
                    return true;
                }

                if (binary.Left is SmtNullConstant && binary.Right.Kind == SmtValueKind.Reference)
                {
                    term = binary.Right;
                    isNull = comparisonIsNull;
                    return true;
                }

                if (binary.Right is SmtNullConstant && binary.Left.Kind == SmtValueKind.Reference)
                {
                    term = binary.Left;
                    isNull = comparisonIsNull;
                    return true;
                }

                return false;
            }

            private bool TryEvaluateReferenceComparison(SmtBinaryFormula binary, out bool value)
            {
                value = false;
                if (binary.Operator is not (SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual) ||
                    binary.Left.Kind != SmtValueKind.Reference ||
                    binary.Right.Kind != SmtValueKind.Reference)
                {
                    return false;
                }

                var left = NormalizeAliases(binary.Left);
                var right = NormalizeAliases(binary.Right);
                if (left.Equals(right))
                {
                    value = binary.Operator == SmtBinaryOperator.Equal;
                    return true;
                }

                var hasLeftNullState = TryGetKnownReferenceNullState(left, out var leftIsNull);
                var hasRightNullState = TryGetKnownReferenceNullState(right, out var rightIsNull);
                if (!hasLeftNullState || !hasRightNullState)
                {
                    return false;
                }

                if (!leftIsNull && !rightIsNull)
                {
                    return false;
                }

                var areEqual = leftIsNull && rightIsNull;
                value = binary.Operator == SmtBinaryOperator.Equal
                    ? areEqual
                    : !areEqual;
                return true;
            }

            private bool TryGetKnownReferenceNullState(SmtFormula formula, out bool isNull)
            {
                formula = NormalizeAliases(formula);
                if (formula is SmtNullConstant)
                {
                    isNull = true;
                    return true;
                }

                if (_referenceNullStates.TryGetValue(formula, out isNull))
                {
                    return true;
                }

                if (formula is SmtConditionalFormula { Kind: SmtValueKind.Reference } conditional &&
                    TryEvaluateBoolean(conditional.Condition, out var conditionValue))
                {
                    return TryGetKnownReferenceNullState(
                        conditionValue ? conditional.WhenTrue : conditional.WhenFalse,
                        out isNull);
                }

                isNull = false;
                return false;
            }

            private void AddStringLengthFact(
                SmtFormula stringFormula,
                int length,
                out bool hasContradiction)
            {
                stringFormula = NormalizeAliases(stringFormula);
                var lengthFormula = new SmtStringLengthTerm(stringFormula);
                var interval = _integerIntervals.TryGetValue(lengthFormula, out var existing)
                    ? existing
                    : IntegerInterval.Unbounded;
                interval = interval.Apply(SmtBinaryOperator.Equal, length);
                hasContradiction = interval.IsContradictory;
                _integerIntervals[lengthFormula] = interval;
            }

            private SmtFormula NormalizeAliases(SmtFormula formula)
            {
                return NormalizeAliases(formula, new HashSet<SmtFormula>());
            }

            private SmtFormula NormalizeAliases(SmtFormula formula, HashSet<SmtFormula> visiting)
            {
                var directCanonical = FindCanonical(formula);
                if (!directCanonical.Equals(formula) &&
                    !ReferencesFormula(directCanonical, formula))
                {
                    return directCanonical;
                }

                if (!visiting.Add(formula))
                {
                    return formula;
                }

                var normalized = formula switch
                {
                    SmtUnaryFormula unary => NormalizeUnaryFormula(unary, visiting),
                    SmtBinaryFormula binary => NormalizeBinaryFormula(binary, visiting),
                    SmtIntegerUnaryTerm unary => NormalizeIntegerUnaryTerm(unary, visiting),
                    SmtIntegerBinaryTerm binary => NormalizeIntegerBinaryTerm(binary, visiting),
                    SmtStringLengthTerm stringLength => NormalizeStringLengthTerm(stringLength, visiting),
                    SmtStringConcatTerm stringConcat => NormalizeStringConcatTerm(stringConcat, visiting),
                    SmtStringContainsFormula stringContains => NormalizeStringContainsFormula(stringContains, visiting),
                    SmtStringStartsWithFormula stringStartsWith => NormalizeStringStartsWithFormula(stringStartsWith, visiting),
                    SmtStringEndsWithFormula stringEndsWith => NormalizeStringEndsWithFormula(stringEndsWith, visiting),
                    SmtRegexMatchFormula regexMatch => NormalizeRegexMatchFormula(regexMatch, visiting),
                    SmtRuntimeTypeTestFormula runtimeTypeTest => NormalizeRuntimeTypeTestFormula(runtimeTypeTest, visiting),
                    SmtConditionalFormula conditional => NormalizeConditionalFormula(conditional, visiting),
                    _ => formula,
                };

                visiting.Remove(formula);
                var normalizedCanonical = FindCanonical(normalized);
                return !normalizedCanonical.Equals(normalized) &&
                    !ReferencesFormula(normalizedCanonical, normalized)
                    ? normalizedCanonical
                    : normalized;
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

            private SmtFormula NormalizeStringContainsFormula(SmtStringContainsFormula formula, HashSet<SmtFormula> visiting)
            {
                var value = NormalizeAliases(formula.Value, visiting);
                var search = NormalizeAliases(formula.Search, visiting);
                return value.Equals(formula.Value) && search.Equals(formula.Search)
                    ? formula
                    : new SmtStringContainsFormula(value, search);
            }

            private SmtFormula NormalizeStringStartsWithFormula(SmtStringStartsWithFormula formula, HashSet<SmtFormula> visiting)
            {
                var value = NormalizeAliases(formula.Value, visiting);
                var prefix = NormalizeAliases(formula.Prefix, visiting);
                return value.Equals(formula.Value) && prefix.Equals(formula.Prefix)
                    ? formula
                    : new SmtStringStartsWithFormula(value, prefix);
            }

            private SmtFormula NormalizeStringEndsWithFormula(SmtStringEndsWithFormula formula, HashSet<SmtFormula> visiting)
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

            private SmtFormula NormalizeRuntimeTypeTestFormula(SmtRuntimeTypeTestFormula formula, HashSet<SmtFormula> visiting)
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
                if (TryEvaluateBoolean(condition, out var conditionValue))
                {
                    return conditionValue ? whenTrue : whenFalse;
                }

                if (whenTrue.Equals(whenFalse))
                {
                    return whenTrue;
                }

                return condition.Equals(formula.Condition) &&
                    whenTrue.Equals(formula.WhenTrue) &&
                    whenFalse.Equals(formula.WhenFalse)
                    ? formula
                    : new SmtConditionalFormula(condition, whenTrue, whenFalse, formula.ResultKind);
            }

            private static bool ReferencesFormula(SmtFormula formula, SmtFormula candidate)
            {
                if (formula.Equals(candidate))
                {
                    return true;
                }

                return formula switch
                {
                    SmtUnaryFormula unary => ReferencesFormula(unary.Operand, candidate),
                    SmtBinaryFormula binary => ReferencesFormula(binary.Left, candidate) ||
                        ReferencesFormula(binary.Right, candidate),
                    SmtIntegerUnaryTerm unary => ReferencesFormula(unary.Operand, candidate),
                    SmtIntegerBinaryTerm binary => ReferencesFormula(binary.Left, candidate) ||
                        ReferencesFormula(binary.Right, candidate),
                    SmtStringLengthTerm stringLength => ReferencesFormula(stringLength.Value, candidate),
                    SmtStringConcatTerm stringConcat => ReferencesFormula(stringConcat.Left, candidate) ||
                        ReferencesFormula(stringConcat.Right, candidate),
                    SmtStringContainsFormula stringContains => ReferencesFormula(stringContains.Value, candidate) ||
                        ReferencesFormula(stringContains.Search, candidate),
                    SmtStringStartsWithFormula stringStartsWith => ReferencesFormula(stringStartsWith.Value, candidate) ||
                        ReferencesFormula(stringStartsWith.Prefix, candidate),
                    SmtStringEndsWithFormula stringEndsWith => ReferencesFormula(stringEndsWith.Value, candidate) ||
                        ReferencesFormula(stringEndsWith.Suffix, candidate),
                    SmtRegexMatchFormula regexMatch => ReferencesFormula(regexMatch.Value, candidate),
                    SmtRuntimeTypeTestFormula runtimeTypeTest => ReferencesFormula(runtimeTypeTest.Value, candidate),
                    SmtConditionalFormula conditional => ReferencesFormula(conditional.Condition, candidate) ||
                        ReferencesFormula(conditional.WhenTrue, candidate) ||
                        ReferencesFormula(conditional.WhenFalse, candidate),
                    _ => false,
                };
            }

            private bool TryGetKnownString(SmtFormula formula, out string value)
            {
                formula = NormalizeAliases(formula);
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
                formula = NormalizeAliases(formula);
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

            private bool TryAddIntegerIntervalFact(
                SmtFormula formula,
                out bool hasContradiction)
            {
                hasContradiction = false;
                if (!TryGetIntegerComparison(formula, out var term, out var op, out var constant))
                {
                    return false;
                }

                term = NormalizeAliases(term);
                var added = AddIntegerIntervalFact(term, op, constant, out hasContradiction);
                if (hasContradiction)
                {
                    return true;
                }

                if (!TryNormalizeAffineIntegerComparison(
                    term,
                    op,
                    constant,
                    out var normalizedTerm,
                    out var normalizedOp,
                    out var normalizedConstant,
                    out var affineContradiction,
                    out var affineTautology))
                {
                    return added;
                }

                if (affineContradiction)
                {
                    hasContradiction = true;
                    return true;
                }

                if (affineTautology)
                {
                    return added;
                }

                if (normalizedTerm.Equals(term) &&
                    normalizedOp == op &&
                    normalizedConstant == constant)
                {
                    return added;
                }

                added |= AddIntegerIntervalFact(normalizedTerm, normalizedOp, normalizedConstant, out var normalizedContradiction);
                hasContradiction |= normalizedContradiction;
                return added;
            }

            private bool AddIntegerIntervalFact(
                SmtFormula term,
                SmtBinaryOperator op,
                long constant,
                out bool hasContradiction)
            {
                var interval = _integerIntervals.TryGetValue(term, out var existing)
                    ? existing
                    : IntegerInterval.Unbounded;
                interval = interval.Apply(op, constant);
                hasContradiction = interval.IsContradictory;
                _integerIntervals[term] = interval;
                return true;
            }

            private bool TryNormalizeAffineIntegerComparison(
                SmtFormula term,
                SmtBinaryOperator op,
                long constant,
                out SmtFormula normalizedTerm,
                out SmtBinaryOperator normalizedOp,
                out long normalizedConstant,
                out bool hasContradiction,
                out bool isTautology)
            {
                normalizedTerm = term;
                normalizedOp = op;
                normalizedConstant = constant;
                hasContradiction = false;
                isTautology = false;

                if (!TryGetAffineIntegerTerm(term, depth: 0, out var affine))
                {
                    return false;
                }

                if (affine.BaseTerm == null ||
                    affine.Scale == 0)
                {
                    return TryEvaluateConstantComparison(
                        affine.Offset,
                        op,
                        constant,
                        out hasContradiction,
                        out isTautology);
                }

                var scale = affine.Scale;
                if (scale < 0)
                {
                    if (!TryNegate(scale, out scale))
                    {
                        return false;
                    }

                    op = ReverseComparison(op);
                }

                if (scale <= 0 ||
                    !TrySubtract(constant, affine.Offset, out var adjustedConstant))
                {
                    return false;
                }

                if (!TryInvertPositiveScaleComparison(
                    op,
                    adjustedConstant,
                    scale,
                    out normalizedOp,
                    out normalizedConstant,
                    out hasContradiction,
                    out isTautology))
                {
                    return false;
                }

                normalizedTerm = NormalizeAliases(affine.BaseTerm);
                return true;
            }

            private static bool TryEvaluateConstantComparison(
                long left,
                SmtBinaryOperator op,
                long right,
                out bool hasContradiction,
                out bool isTautology)
            {
                var value = op switch
                {
                    SmtBinaryOperator.Equal => left == right,
                    SmtBinaryOperator.NotEqual => left != right,
                    SmtBinaryOperator.LessThan => left < right,
                    SmtBinaryOperator.LessThanOrEqual => left <= right,
                    SmtBinaryOperator.GreaterThan => left > right,
                    SmtBinaryOperator.GreaterThanOrEqual => left >= right,
                    _ => false,
                };

                if (op is not (SmtBinaryOperator.Equal or
                    SmtBinaryOperator.NotEqual or
                    SmtBinaryOperator.LessThan or
                    SmtBinaryOperator.LessThanOrEqual or
                    SmtBinaryOperator.GreaterThan or
                    SmtBinaryOperator.GreaterThanOrEqual))
                {
                    hasContradiction = false;
                    isTautology = false;
                    return false;
                }

                hasContradiction = !value;
                isTautology = value;
                return true;
            }

            private static bool TryInvertPositiveScaleComparison(
                SmtBinaryOperator op,
                long adjustedConstant,
                long positiveScale,
                out SmtBinaryOperator normalizedOp,
                out long normalizedConstant,
                out bool hasContradiction,
                out bool isTautology)
            {
                normalizedOp = op;
                normalizedConstant = adjustedConstant;
                hasContradiction = false;
                isTautology = false;

                switch (op)
                {
                    case SmtBinaryOperator.Equal:
                        if (adjustedConstant % positiveScale != 0)
                        {
                            hasContradiction = true;
                            return true;
                        }

                        normalizedConstant = adjustedConstant / positiveScale;
                        return true;
                    case SmtBinaryOperator.NotEqual:
                        if (adjustedConstant % positiveScale != 0)
                        {
                            isTautology = true;
                            return true;
                        }

                        normalizedConstant = adjustedConstant / positiveScale;
                        return true;
                    case SmtBinaryOperator.GreaterThan:
                        normalizedConstant = FloorDiv(adjustedConstant, positiveScale);
                        return true;
                    case SmtBinaryOperator.GreaterThanOrEqual:
                        normalizedConstant = CeilingDiv(adjustedConstant, positiveScale);
                        return true;
                    case SmtBinaryOperator.LessThan:
                        normalizedConstant = CeilingDiv(adjustedConstant, positiveScale);
                        return true;
                    case SmtBinaryOperator.LessThanOrEqual:
                        normalizedConstant = FloorDiv(adjustedConstant, positiveScale);
                        return true;
                    default:
                        return false;
                }
            }

            private bool TryGetAffineIntegerTerm(
                SmtFormula formula,
                int depth,
                out AffineIntegerTerm affine)
            {
                formula = NormalizeAliases(formula);
                if (depth > MaxAffineExpansionDepth)
                {
                    return TryCreateUnitAffineTerm(formula, out affine);
                }

                switch (formula)
                {
                    case SmtIntegerConstant constant:
                        affine = AffineIntegerTerm.Constant(constant.Value);
                        return true;
                    case SmtIntegerUnaryTerm { Operator: SmtIntegerUnaryOperator.Negate } unary
                        when TryGetAffineIntegerTerm(unary.Operand, depth + 1, out var operand) &&
                             TryNegate(operand, out affine):
                        return true;
                    case SmtIntegerBinaryTerm binary:
                        return TryGetAffineIntegerBinaryTerm(binary, depth, out affine) ||
                            TryCreateUnitAffineTerm(formula, out affine);
                    default:
                        return TryCreateUnitAffineTerm(formula, out affine);
                }
            }

            private bool TryGetAffineIntegerBinaryTerm(
                SmtIntegerBinaryTerm binary,
                int depth,
                out AffineIntegerTerm affine)
            {
                affine = default;
                if (binary.Operator == SmtIntegerBinaryOperator.Multiply)
                {
                    if (TryGetKnownInteger(binary.Left, out var leftConstant) &&
                        TryGetAffineIntegerTerm(binary.Right, depth + 1, out var rightAffine))
                    {
                        return TryScale(rightAffine, leftConstant, out affine);
                    }

                    if (TryGetKnownInteger(binary.Right, out var rightConstant) &&
                        TryGetAffineIntegerTerm(binary.Left, depth + 1, out var leftAffine))
                    {
                        return TryScale(leftAffine, rightConstant, out affine);
                    }

                    return false;
                }

                if (binary.Operator is not (SmtIntegerBinaryOperator.Add or SmtIntegerBinaryOperator.Subtract) ||
                    !TryGetAffineIntegerTerm(binary.Left, depth + 1, out var left) ||
                    !TryGetAffineIntegerTerm(binary.Right, depth + 1, out var right))
                {
                    return false;
                }

                return binary.Operator == SmtIntegerBinaryOperator.Add
                    ? TryAdd(left, right, out affine)
                    : TrySubtract(left, right, out affine);
            }

            private static bool TryCreateUnitAffineTerm(SmtFormula formula, out AffineIntegerTerm affine)
            {
                if (formula.Kind != SmtValueKind.Int)
                {
                    affine = default;
                    return false;
                }

                affine = AffineIntegerTerm.Term(formula);
                return true;
            }

            private static bool TryAdd(
                AffineIntegerTerm left,
                AffineIntegerTerm right,
                out AffineIntegerTerm result)
            {
                return TryCombine(left, right, subtractRight: false, out result);
            }

            private static bool TrySubtract(
                AffineIntegerTerm left,
                AffineIntegerTerm right,
                out AffineIntegerTerm result)
            {
                return TryCombine(left, right, subtractRight: true, out result);
            }

            private static bool TryCombine(
                AffineIntegerTerm left,
                AffineIntegerTerm right,
                bool subtractRight,
                out AffineIntegerTerm result)
            {
                result = default;
                var rightScale = right.Scale;
                var rightOffset = right.Offset;
                if (subtractRight &&
                    (!TryNegate(rightScale, out rightScale) ||
                     !TryNegate(rightOffset, out rightOffset)))
                {
                    return false;
                }

                try
                {
                    checked
                    {
                        if (left.BaseTerm == null &&
                            right.BaseTerm == null)
                        {
                            result = AffineIntegerTerm.Constant(left.Offset + rightOffset);
                            return true;
                        }

                        if (left.BaseTerm == null)
                        {
                            var offset = left.Offset + rightOffset;
                            result = rightScale == 0
                                ? AffineIntegerTerm.Constant(offset)
                                : new AffineIntegerTerm(right.BaseTerm, rightScale, offset);
                            return true;
                        }

                        if (right.BaseTerm == null)
                        {
                            var offset = left.Offset + rightOffset;
                            result = left.Scale == 0
                                ? AffineIntegerTerm.Constant(offset)
                                : new AffineIntegerTerm(left.BaseTerm, left.Scale, offset);
                            return true;
                        }

                        if (!left.BaseTerm.Equals(right.BaseTerm))
                        {
                            return false;
                        }

                        var scale = left.Scale + rightScale;
                        var combinedOffset = left.Offset + rightOffset;
                        result = scale == 0
                            ? AffineIntegerTerm.Constant(combinedOffset)
                            : new AffineIntegerTerm(left.BaseTerm, scale, combinedOffset);
                        return true;
                    }
                }
                catch (OverflowException)
                {
                    return false;
                }
            }

            private static bool TryScale(
                AffineIntegerTerm value,
                long scale,
                out AffineIntegerTerm result)
            {
                result = default;
                try
                {
                    checked
                    {
                        var scaledScale = value.Scale * scale;
                        var scaledOffset = value.Offset * scale;
                        result = value.BaseTerm == null || scaledScale == 0
                            ? AffineIntegerTerm.Constant(scaledOffset)
                            : new AffineIntegerTerm(value.BaseTerm, scaledScale, scaledOffset);
                        return true;
                    }
                }
                catch (OverflowException)
                {
                    return false;
                }
            }

            private static bool TryNegate(AffineIntegerTerm value, out AffineIntegerTerm result)
            {
                result = default;
                if (!TryNegate(value.Scale, out var scale) ||
                    !TryNegate(value.Offset, out var offset))
                {
                    return false;
                }

                result = value.BaseTerm == null || scale == 0
                    ? AffineIntegerTerm.Constant(offset)
                    : new AffineIntegerTerm(value.BaseTerm, scale, offset);
                return true;
            }

            private static bool TrySubtract(long left, long right, out long result)
            {
                try
                {
                    checked
                    {
                        result = left - right;
                    }

                    return true;
                }
                catch (OverflowException)
                {
                    result = default;
                    return false;
                }
            }

            private static bool TryNegate(long value, out long result)
            {
                if (value == long.MinValue)
                {
                    result = default;
                    return false;
                }

                result = -value;
                return true;
            }

            private static long FloorDiv(long dividend, long positiveDivisor)
            {
                var quotient = dividend / positiveDivisor;
                var remainder = dividend % positiveDivisor;
                return remainder != 0 && dividend < 0
                    ? quotient - 1
                    : quotient;
            }

            private static long CeilingDiv(long dividend, long positiveDivisor)
            {
                var quotient = dividend / positiveDivisor;
                var remainder = dividend % positiveDivisor;
                return remainder != 0 && dividend > 0
                    ? quotient + 1
                    : quotient;
            }

            private readonly struct BooleanEquivalenceParent
            {
                public BooleanEquivalenceParent(SmtFormula parent, bool isNegatedFromParent)
                {
                    Parent = parent;
                    IsNegatedFromParent = isNegatedFromParent;
                }

                public SmtFormula Parent { get; }
                public bool IsNegatedFromParent { get; }
            }

            private readonly struct AffineIntegerTerm
            {
                public AffineIntegerTerm(SmtFormula? baseTerm, long scale, long offset)
                {
                    BaseTerm = scale == 0 ? null : baseTerm;
                    Scale = BaseTerm == null ? 0 : scale;
                    Offset = offset;
                }

                public SmtFormula? BaseTerm { get; }
                public long Scale { get; }
                public long Offset { get; }

                public static AffineIntegerTerm Constant(long value)
                {
                    return new AffineIntegerTerm(null, 0, value);
                }

                public static AffineIntegerTerm Term(SmtFormula term)
                {
                    return new AffineIntegerTerm(term, 1, 0);
                }
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
