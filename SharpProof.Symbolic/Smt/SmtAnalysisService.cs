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

namespace SharpProof.Symbolic.Smt
{
    public sealed class SmtAnalysisService : IDisposable
    {
        private const int PreNormalizationFormulaDepthLimit = 1024;
        private const int SharedQueryCacheEntryLimit = 4096;
        private static readonly ConcurrentDictionary<string, PurityProofResult> s_sharedQueryCache = new(StringComparer.Ordinal);
        private static readonly ConcurrentQueue<string> s_sharedQueryCacheOrder = new();

        private readonly ConcurrentDictionary<string, PurityProofResult> _queryCache = new(StringComparer.Ordinal);
        [ThreadStatic]
        private static PurityProofSearch? t_sharedProofSearch;
        private readonly object _solverLock = new();
        private long _consumedQueryTicks;
        private long _consumedResourceCount;
        private int _executedQueryCount;
        private bool _solverUnavailable;
        private bool _disposed;

        public SmtAnalysisService(SmtAnalysisOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        ~SmtAnalysisService()
        {
            Dispose(disposing: false);
        }

        public SmtAnalysisOptions Options { get; }

        public int ExecutedQueryCount => _executedQueryCount;

        public int CacheEntryCount => _queryCache.Count;

        internal PurityProofResult ClassifyPathFeasibility(IEnumerable<SmtFormula> pathConditions)
        {
            return Classify(new PurityProofQuery(
                pathConditions.ToArray(),
                new PurityHazard(
                    PurityHazardKind.BranchReachability,
                    new SmtBooleanConstant(true))));
        }

        internal PurityProofResult ClassifyImplication(
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

        internal bool PathConditionsImply(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula factFormula)
        {
            return ClassifyImplication(pathConditions, factFormula).Outcome == PurityProofOutcome.ProvablyPure;
        }

        internal PurityProofResult Classify(PurityProofQuery query)
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

            if (!IsWithinFormulaDepthBudget(
                    query.PathConditions,
                    query.Hazard.TriggerCondition,
                    PreNormalizationFormulaDepthLimit))
            {
                return Unknown("smt_expression_budget_exceeded");
            }

            var pathConditions = NormalizePathConditions(query.PathConditions);
            if (TryClassifySyntactically(query, pathConditions, out var syntacticResult))
            {
                return syntacticResult;
            }

            if (Options.QueryTimeout <= TimeSpan.Zero)
            {
                return Unknown("smt_timeout");
            }

            if (pathConditions.Length > Options.MaxPathConditions)
            {
                return Unknown("smt_path_condition_budget_exceeded");
            }

            if (!IsWithinFormulaNodeBudget(
                    pathConditions,
                    query.Hazard.TriggerCondition,
                    Options.MaxExpressionNodes))
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
                    var resourcesBefore = search.ConsumedResourceCount;
                    try
                    {
                        return search.Classify(query, Options.QueryTimeout);
                    }
                    finally
                    {
                        Interlocked.Add(ref _consumedResourceCount, search.ConsumedResourceCount - resourcesBefore);
                    }
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
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            // Note: We deliberately do not dispose the thread-local solver context here
            // to allow caching and reuse across SmtAnalysisService instances on the same thread.
        }

        private PurityProofSearch GetOrCreateProofSearch()
        {
            if (t_sharedProofSearch == null)
            {
                t_sharedProofSearch = new PurityProofSearch();
            }

            return t_sharedProofSearch;
        }

        private void DisposeProofSearch()
        {
            lock (_solverLock)
            {
                if (t_sharedProofSearch != null)
                {
                    try
                    {
                        t_sharedProofSearch.Dispose();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Ignore disposal errors on failed context
                    }
                    t_sharedProofSearch = null;
                }
            }
        }

        private bool IsMethodBudgetExceeded()
        {
            // Primary budget is deterministic solver work (rlimit units), so the
            // cutoff does not depend on machine speed or CPU load. Wall-clock stays
            // only as a scaled-up safety net against a wedged solver.
            if (Interlocked.Read(ref _consumedResourceCount) > SmtResourceBudget.GetMethodRlimitBudget(Options.MethodBudget))
            {
                return true;
            }

            var safetyNetTicks = Options.MethodBudget.TotalSeconds * Stopwatch.Frequency * SmtResourceBudget.WallClockSafetyFactor;
            return Interlocked.Read(ref _consumedQueryTicks) > safetyNetTicks;
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
            return SmtSyntacticClassifier.TryClassify(query, pathConditions, out result);
        }
        private static bool IsWithinFormulaNodeBudget(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula triggerCondition,
            int maxNodes)
        {
            var remaining = maxNodes;
            foreach (var formula in pathConditions)
            {
                if (!TryConsumeFormulaNodeBudget(formula, ref remaining))
                {
                    return false;
                }
            }

            return TryConsumeFormulaNodeBudget(triggerCondition, ref remaining);
        }

        private static bool IsWithinFormulaDepthBudget(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula triggerCondition,
            int maxDepth)
        {
            foreach (var formula in pathConditions)
            {
                if (!IsWithinFormulaDepthBudget(formula, maxDepth))
                {
                    return false;
                }
            }

            return IsWithinFormulaDepthBudget(triggerCondition, maxDepth);
        }

        private static bool IsWithinFormulaDepthBudget(SmtFormula root, int maxDepth)
        {
            var stack = new Stack<(SmtFormula Formula, int Depth)>();
            stack.Push((root, 1));
            while (stack.Count != 0)
            {
                var (formula, depth) = stack.Pop();
                if (depth > maxDepth)
                {
                    return false;
                }

                var childDepth = depth + 1;
                switch (formula)
                {
                    case SmtUnaryFormula unary:
                        stack.Push((unary.Operand, childDepth));
                        break;
                    case SmtBinaryFormula binary:
                        stack.Push((binary.Left, childDepth));
                        stack.Push((binary.Right, childDepth));
                        break;
                    case SmtIntegerUnaryTerm unary:
                        stack.Push((unary.Operand, childDepth));
                        break;
                    case SmtIntegerBinaryTerm binary:
                        stack.Push((binary.Left, childDepth));
                        stack.Push((binary.Right, childDepth));
                        break;
                    case SmtStringLengthTerm stringLength:
                        stack.Push((stringLength.Value, childDepth));
                        break;
                    case SmtStringConcatTerm stringConcat:
                        stack.Push((stringConcat.Left, childDepth));
                        stack.Push((stringConcat.Right, childDepth));
                        break;
                    case SmtStringContainsFormula stringContains:
                        stack.Push((stringContains.Value, childDepth));
                        stack.Push((stringContains.Search, childDepth));
                        break;
                    case SmtStringStartsWithFormula stringStartsWith:
                        stack.Push((stringStartsWith.Value, childDepth));
                        stack.Push((stringStartsWith.Prefix, childDepth));
                        break;
                    case SmtStringEndsWithFormula stringEndsWith:
                        stack.Push((stringEndsWith.Value, childDepth));
                        stack.Push((stringEndsWith.Suffix, childDepth));
                        break;
                    case SmtRegexMatchFormula regexMatch:
                        stack.Push((regexMatch.Value, childDepth));
                        break;
                    case SmtRuntimeTypeTestFormula runtimeTypeTest:
                        stack.Push((runtimeTypeTest.Value, childDepth));
                        break;
                    case SmtConditionalFormula conditional:
                        stack.Push((conditional.Condition, childDepth));
                        stack.Push((conditional.WhenTrue, childDepth));
                        stack.Push((conditional.WhenFalse, childDepth));
                        break;
                }
            }

            return true;
        }

        private static bool TryConsumeFormulaNodeBudget(SmtFormula root, ref int remaining)
        {
            var stack = new Stack<SmtFormula>();
            stack.Push(root);
            while (stack.Count != 0)
            {
                var formula = stack.Pop();
                var weight = formula is SmtRegexMatchFormula regexMatch
                    ? 1 + Math.Max(1, regexMatch.Pattern.Length / 8)
                    : 1;
                remaining -= weight;
                if (remaining < 0)
                {
                    return false;
                }

                switch (formula)
                {
                    case SmtUnaryFormula unary:
                        stack.Push(unary.Operand);
                        break;
                    case SmtBinaryFormula binary:
                        stack.Push(binary.Left);
                        stack.Push(binary.Right);
                        break;
                    case SmtIntegerUnaryTerm unary:
                        stack.Push(unary.Operand);
                        break;
                    case SmtIntegerBinaryTerm binary:
                        stack.Push(binary.Left);
                        stack.Push(binary.Right);
                        break;
                    case SmtStringLengthTerm stringLength:
                        stack.Push(stringLength.Value);
                        break;
                    case SmtStringConcatTerm stringConcat:
                        stack.Push(stringConcat.Left);
                        stack.Push(stringConcat.Right);
                        break;
                    case SmtStringContainsFormula stringContains:
                        stack.Push(stringContains.Value);
                        stack.Push(stringContains.Search);
                        break;
                    case SmtStringStartsWithFormula stringStartsWith:
                        stack.Push(stringStartsWith.Value);
                        stack.Push(stringStartsWith.Prefix);
                        break;
                    case SmtStringEndsWithFormula stringEndsWith:
                        stack.Push(stringEndsWith.Value);
                        stack.Push(stringEndsWith.Suffix);
                        break;
                    case SmtRegexMatchFormula regexFormula:
                        stack.Push(regexFormula.Value);
                        break;
                    case SmtRuntimeTypeTestFormula runtimeTypeTest:
                        stack.Push(runtimeTypeTest.Value);
                        break;
                    case SmtConditionalFormula conditional:
                        stack.Push(conditional.Condition);
                        stack.Push(conditional.WhenTrue);
                        stack.Push(conditional.WhenFalse);
                        break;
                }
            }

            return true;
        }
    }
}
