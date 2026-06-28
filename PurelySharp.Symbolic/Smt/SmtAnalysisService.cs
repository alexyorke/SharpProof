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
    public sealed class SmtAnalysisService
    {
        private readonly ConcurrentDictionary<string, PurityProofResult> _queryCache = new(StringComparer.Ordinal);
        private readonly Stopwatch _budgetClock = Stopwatch.StartNew();
        private readonly object _solverLock = new();
        private int _executedQueryCount;
        private bool _solverUnavailable;

        public SmtAnalysisService(SmtAnalysisOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public SmtAnalysisOptions Options { get; }

        public int ExecutedQueryCount => _executedQueryCount;

        public int CacheEntryCount => _queryCache.Count;

        public PurityProofResult Classify(PurityProofQuery query)
        {
            if (!Options.IsEnabled)
            {
                return Unknown("smt_disabled");
            }

            if (_solverUnavailable)
            {
                return Unknown("smt_unavailable");
            }

            var pathConditions = query.PathConditions.ToImmutableArray();
            if (pathConditions.Length > Options.MaxPathConditions)
            {
                return Unknown("smt_path_condition_budget_exceeded");
            }

            if (CountFormulaNodes(pathConditions) + CountFormulaNodes(query.Hazard.TriggerCondition) > Options.MaxExpressionNodes)
            {
                return Unknown("smt_expression_budget_exceeded");
            }

            if (_budgetClock.Elapsed > Options.MethodBudget)
            {
                return Unknown("smt_method_budget_exceeded");
            }

            var normalizedQuery = new PurityProofQuery(pathConditions, query.Hazard);
            var key = CreateQueryKey(normalizedQuery);
            if (_queryCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var result = ClassifyCore(normalizedQuery);
            _queryCache.TryAdd(key, result);
            return result;
        }

        private PurityProofResult ClassifyCore(PurityProofQuery query)
        {
            try
            {
                lock (_solverLock)
                {
                    Interlocked.Increment(ref _executedQueryCount);
                    using var search = new PurityProofSearch();
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
                return Unknown("smt_unavailable");
            }
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
                SmtConditionalFormula conditional => 1 + CountFormulaNodes(conditional.Condition) + CountFormulaNodes(conditional.WhenTrue) + CountFormulaNodes(conditional.WhenFalse),
                _ => 1,
            };
        }
    }
}
