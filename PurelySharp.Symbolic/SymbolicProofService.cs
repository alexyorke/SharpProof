using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using PurelySharp.Symbolic.Ir;
using PurelySharp.Symbolic.Smt;
using SearchLib.Purity;
using SearchLib.Smt;

namespace PurelySharp.Symbolic
{
    internal sealed class SymbolicProofService
    {
        private readonly SmtAnalysisService? smtAnalysis;

        public SymbolicProofService(SmtAnalysisService? smtAnalysis)
        {
            this.smtAnalysis = smtAnalysis;
        }

        public SymbolicIrProofResult ClassifyReachability(SymbolicState state)
        {
            if (!TryEncodeState(state, out var pathConditions, out var unknownReason))
            {
                return SymbolicIrProofResult.Unknown(unknownReason);
            }

            var result = ClassifyFormulaPathFeasibility(pathConditions);
            return SymbolicIrProofResult.FromReachability(result, CreateBudgetInfo());
        }

        internal bool TryEncode(SymbolicState state, out ImmutableArray<SmtFormula> pathConditions)
        {
            return TryEncodeState(state, out pathConditions, out _);
        }

        public SymbolicIrProofResult ClassifyImplication(SymbolicState state, SymbolicFact fact)
        {
            if (!TryEncodeState(state, out var pathConditions, out var unknownReason) ||
                !SymbolicIrFormulaEncoder.TryEncode(fact, out var factFormula))
            {
                return SymbolicIrProofResult.Unknown(unknownReason);
            }

            var result = ClassifyFormulaImplication(pathConditions, factFormula);
            return SymbolicIrProofResult.FromImplication(result, CreateBudgetInfo());
        }

        public SymbolicIrProofResult ClassifyImplication(SymbolicState state, SymbolicCondition condition)
        {
            if (!TryEncodeState(state, out var pathConditions, out var unknownReason) ||
                !SymbolicIrFormulaEncoder.TryEncode(condition, out var formula))
            {
                return SymbolicIrProofResult.Unknown(unknownReason);
            }

            var result = ClassifyFormulaImplication(pathConditions, formula);
            return SymbolicIrProofResult.FromImplication(result, CreateBudgetInfo());
        }

        internal PurityProofResult ClassifyFormulaPathFeasibility(IEnumerable<SmtFormula> pathConditions)
        {
            return ClassifyWithFallback(service => service.ClassifyPathFeasibility(pathConditions));
        }

        internal PurityProofResult ClassifyFormulaImplication(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula factFormula)
        {
            if (factFormula == null)
            {
                throw new ArgumentNullException(nameof(factFormula));
            }

            return ClassifyWithFallback(service => service.ClassifyImplication(pathConditions, factFormula));
        }

        internal PurityProofResult ClassifyFormulaBranchReachability(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula branchCondition)
        {
            if (branchCondition == null)
            {
                throw new ArgumentNullException(nameof(branchCondition));
            }

            return ClassifyWithFallback(service => service.Classify(new PurityProofQuery(
                pathConditions.ToArray(),
                new PurityHazard(PurityHazardKind.BranchReachability, branchCondition))));
        }

        private PurityProofResult ClassifyWithFallback(Func<SmtAnalysisService, PurityProofResult> classify)
        {
            if (smtAnalysis != null)
            {
                return classify(smtAnalysis);
            }

            using var fallback = new SmtAnalysisService(SmtAnalysisOptions.Default);
            return classify(fallback);
        }

        private SymbolicBudgetInfo? CreateBudgetInfo()
        {
            var service = smtAnalysis;
            if (service == null)
            {
                return null;
            }

            return new SymbolicBudgetInfo(
                service.Options.MaxPathConditions,
                service.Options.MaxExpressionNodes,
                (int)service.Options.QueryTimeout.TotalMilliseconds,
                (int)service.Options.MethodBudget.TotalMilliseconds,
                service.ExecutedQueryCount,
                service.CacheEntryCount);
        }

        private static bool TryEncodeState(
            SymbolicState state,
            out ImmutableArray<SmtFormula> pathConditions,
            out SymbolicUnknownReason unknownReason)
        {
            var builder = ImmutableArray.CreateBuilder<SmtFormula>(
                state.Facts.Length + state.PathConditions.Length);

            foreach (var fact in state.Facts)
            {
                if (!SymbolicIrFormulaEncoder.TryEncode(fact, out var formula))
                {
                    pathConditions = ImmutableArray<SmtFormula>.Empty;
                    unknownReason = SymbolicUnknownReason.UnsupportedIrEncoding;
                    return false;
                }

                builder.Add(formula);
            }

            foreach (var condition in state.PathConditions)
            {
                if (!SymbolicIrFormulaEncoder.TryEncode(condition, out var formula))
                {
                    pathConditions = ImmutableArray<SmtFormula>.Empty;
                    unknownReason = SymbolicUnknownReason.UnsupportedIrEncoding;
                    return false;
                }

                builder.Add(formula);
            }

            pathConditions = builder.ToImmutable();
            unknownReason = SymbolicUnknownReason.None;
            return true;
        }
    }

    internal sealed class SymbolicIrProofResult
    {
        private SymbolicIrProofResult(PurityProofResult? rawResult, SymbolicProofInfo info)
        {
            RawResult = rawResult;
            Info = info;
        }

        public PurityProofResult? RawResult { get; }

        public SymbolicProofInfo Info { get; }

        public static SymbolicIrProofResult Unknown(SymbolicUnknownReason reason)
        {
            return new SymbolicIrProofResult(
                rawResult: null,
                new SymbolicProofInfo(
                    SymbolicProofStatus.Unknown,
                    SymbolicProofBackend.None,
                    reason,
                    reason.ToString(),
                    cacheHit: false,
                    budget: null));
        }

        public static SymbolicIrProofResult FromReachability(
            PurityProofResult result,
            SymbolicBudgetInfo? budget)
        {
            var status = result.PathFeasibility switch
            {
                Feasibility.Satisfiable => SymbolicProofStatus.Reachable,
                Feasibility.Unsatisfiable => SymbolicProofStatus.Unreachable,
                _ => SymbolicProofStatus.Unknown,
            };

            return FromResult(result, status, budget);
        }

        public static SymbolicIrProofResult FromImplication(
            PurityProofResult result,
            SymbolicBudgetInfo? budget)
        {
            var status = result.Outcome switch
            {
                PurityProofOutcome.ProvablyPure => SymbolicProofStatus.ProvenTrue,
                PurityProofOutcome.ProvablyImpure => SymbolicProofStatus.ProvenFalse,
                _ => SymbolicProofStatus.Unknown,
            };

            return FromResult(result, status, budget);
        }

        private static SymbolicIrProofResult FromResult(
            PurityProofResult result,
            SymbolicProofStatus status,
            SymbolicBudgetInfo? budget)
        {
            return new SymbolicIrProofResult(
                result,
                new SymbolicProofInfo(
                    status,
                    SymbolicProofBackend.Smt,
                    MapUnknownReason(result.Reason),
                    result.Reason,
                    cacheHit: false,
                    budget));
        }

        private static SymbolicUnknownReason MapUnknownReason(string reason)
        {
            return reason switch
            {
                "smt_disabled" => SymbolicUnknownReason.SmtDisabled,
                "smt_unavailable" => SymbolicUnknownReason.SmtUnavailable,
                "smt_method_budget_exceeded" => SymbolicUnknownReason.MethodBudgetExceeded,
                "smt_path_condition_budget_exceeded" => SymbolicUnknownReason.PathConditionBudgetExceeded,
                "smt_expression_budget_exceeded" => SymbolicUnknownReason.ExpressionBudgetExceeded,
                "smt_encoding_failure" => SymbolicUnknownReason.EncodingFailure,
                _ => SymbolicUnknownReason.None,
            };
        }
    }
}
