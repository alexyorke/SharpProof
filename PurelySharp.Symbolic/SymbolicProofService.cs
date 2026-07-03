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

        public SymbolicIrProofResult ClassifyBranchFeasibility(SymbolicState state, SymbolicCondition branchCondition)
        {
            if (branchCondition == null)
            {
                throw new ArgumentNullException(nameof(branchCondition));
            }

            return ClassifyReachability(state.AddPathCondition(branchCondition));
        }

        public SymbolicIrProofResult ClassifyConditionTruth(SymbolicState state, SymbolicCondition condition)
        {
            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }

            var reachability = ClassifyReachability(state);
            if (reachability.Info.Status == SymbolicProofStatus.Unreachable)
            {
                return reachability;
            }

            var trueBranch = ClassifyBranchFeasibility(state, condition);
            if (trueBranch.Info.Status == SymbolicProofStatus.Unreachable &&
                trueBranch.RawResult != null)
            {
                return SymbolicIrProofResult.FromConditionTruth(
                    trueBranch.RawResult,
                    SymbolicProofStatus.ProvenFalse,
                    CreateBudgetInfo());
            }

            var falseBranch = ClassifyBranchFeasibility(state, new SymbolicNotCondition(condition));
            if (falseBranch.Info.Status == SymbolicProofStatus.Unreachable &&
                falseBranch.RawResult != null)
            {
                return SymbolicIrProofResult.FromConditionTruth(
                    falseBranch.RawResult,
                    SymbolicProofStatus.ProvenTrue,
                    CreateBudgetInfo());
            }

            return SymbolicIrProofResult.Unknown(falseBranch.Info.UnknownReason);
        }

        public SymbolicIrProofResult ClassifyHazardTrigger(SymbolicState state, SymbolicFact triggerPrecondition)
        {
            if (triggerPrecondition == null)
            {
                throw new ArgumentNullException(nameof(triggerPrecondition));
            }

            var proven = ClassifyImplication(state, triggerPrecondition);
            if (proven.Info.Status == SymbolicProofStatus.ProvenTrue)
            {
                return proven;
            }

            var triggerFeasibility = ClassifyBranchFeasibility(
                state,
                new SymbolicFactCondition(triggerPrecondition));
            return triggerFeasibility.Info.Status == SymbolicProofStatus.Unreachable
                ? triggerFeasibility
                : proven;
        }

        private PurityProofResult ClassifyFormulaPathFeasibility(IEnumerable<SmtFormula> pathConditions)
        {
            return ClassifyWithFallback(service => service.ClassifyPathFeasibility(pathConditions));
        }

        private PurityProofResult ClassifyFormulaImplication(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula factFormula)
        {
            if (factFormula == null)
            {
                throw new ArgumentNullException(nameof(factFormula));
            }

            return ClassifyWithFallback(service => service.ClassifyImplication(pathConditions, factFormula));
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
            var skippedUnsupported = false;

            foreach (var fact in state.Facts)
            {
                if (!SymbolicIrFormulaEncoder.TryEncode(fact, out var formula))
                {
                    skippedUnsupported = true;
                    continue;
                }

                builder.Add(formula);
            }

            foreach (var condition in state.PathConditions)
            {
                if (!SymbolicIrFormulaEncoder.TryEncode(condition, out var formula))
                {
                    skippedUnsupported = true;
                    continue;
                }

                builder.Add(formula);
            }

            if (skippedUnsupported && builder.Count == 0)
            {
                pathConditions = ImmutableArray<SmtFormula>.Empty;
                unknownReason = SymbolicUnknownReason.UnsupportedIrEncoding;
                return false;
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

        public static SymbolicIrProofResult FromConditionTruth(
            PurityProofResult result,
            SymbolicProofStatus status,
            SymbolicBudgetInfo? budget)
        {
            if (status is not SymbolicProofStatus.ProvenTrue and
                not SymbolicProofStatus.ProvenFalse and
                not SymbolicProofStatus.Unreachable and
                not SymbolicProofStatus.Unknown)
            {
                throw new ArgumentOutOfRangeException(nameof(status), status, "Condition truth proofs must be proven true, proven false, unreachable, or unknown.");
            }

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
