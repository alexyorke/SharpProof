using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using PurelySharp.Symbolic.Ir;
using PurelySharp.Symbolic.Smt;
using SearchLib.Purity;
using SearchLib.Smt;

namespace PurelySharp.Symbolic
{
    internal sealed class SymbolicProofService
    {
        private static readonly ConditionalWeakTable<SmtAnalysisService, ProofResultCache> s_serviceCaches = new();
        private static readonly ProofResultCache s_fallbackCache = new();
        private readonly SmtAnalysisService? smtAnalysis;

        public SymbolicProofService(SmtAnalysisService? smtAnalysis)
        {
            this.smtAnalysis = smtAnalysis;
        }

        public SymbolicIrProofResult ClassifyReachability(SymbolicState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            state = NormalizeState(state);
            if (state.IsContradictory)
            {
                return SymbolicIrProofResult.Syntactic(
                    SymbolicProofStatus.Unreachable,
                    "ir_state_contradictory");
            }

            if (state.Facts.Length == 0 && state.PathConditions.Length == 0)
            {
                return SymbolicIrProofResult.Syntactic(
                    SymbolicProofStatus.Reachable,
                    "ir_state_empty");
            }

            return ClassifyWithIrCache(
                "reachability:" + state.NormalizedProofKey,
                () =>
                {
                    if (!TryEncodeState(state, out var pathConditions, out var unknownReason))
                    {
                        return SymbolicIrProofResult.Unknown(unknownReason);
                    }

                    var result = ClassifyFormulaPathFeasibility(pathConditions);
                    return SymbolicIrProofResult.FromReachability(result, CreateBudgetInfo());
                });
        }

        internal bool TryEncode(SymbolicState state, out ImmutableArray<SmtFormula> pathConditions)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            state = NormalizeState(state);
            return TryEncodeState(state, out pathConditions, out _);
        }

        public SymbolicIrProofResult ClassifyImplication(SymbolicState state, SymbolicFact fact)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (fact == null)
            {
                throw new ArgumentNullException(nameof(fact));
            }

            state = NormalizeState(state);
            if (state.IsContradictory)
            {
                return SymbolicIrProofResult.Syntactic(
                    SymbolicProofStatus.ProvenTrue,
                    "ir_state_contradictory");
            }

            if (StateContainsFact(state, fact))
            {
                return SymbolicIrProofResult.Syntactic(
                    SymbolicProofStatus.ProvenTrue,
                    "ir_state_contains_fact");
            }

            if (StateContradictsFact(state, fact))
            {
                return SymbolicIrProofResult.Syntactic(
                    SymbolicProofStatus.ProvenFalse,
                    "ir_state_contradicts_fact");
            }

            return ClassifyWithIrCache(
                "implication-fact:" + state.NormalizedProofKey + "\n" + SymbolicState.CreateProofFactKey(fact),
                () =>
                {
                    if (!TryEncodeState(state, out var pathConditions, out var unknownReason))
                    {
                        return SymbolicIrProofResult.Unknown(unknownReason);
                    }

                    if (!SymbolicIrFormulaEncoder.TryEncode(fact, out var factFormula))
                    {
                        return SymbolicIrProofResult.Unknown(SymbolicUnknownReason.UnsupportedIrEncoding);
                    }

                    var result = ClassifyFormulaImplication(pathConditions, factFormula);
                    return SymbolicIrProofResult.FromImplication(result, CreateBudgetInfo());
                });
        }

        public SymbolicIrProofResult ClassifyImplication(SymbolicState state, SymbolicCondition condition)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }

            state = NormalizeState(state);
            if (state.IsContradictory)
            {
                return SymbolicIrProofResult.Syntactic(
                    SymbolicProofStatus.ProvenTrue,
                    "ir_state_contradictory");
            }

            if (TryClassifySyntacticConditionTruth(condition, out var syntacticStatus) &&
                syntacticStatus == SymbolicProofStatus.ProvenTrue)
            {
                return SymbolicIrProofResult.Syntactic(
                    SymbolicProofStatus.ProvenTrue,
                    "ir_condition_syntactic_truth");
            }

            if (StateContainsCondition(state, condition))
            {
                return SymbolicIrProofResult.Syntactic(
                    SymbolicProofStatus.ProvenTrue,
                    "ir_state_contains_condition");
            }

            if (StateContradictsCondition(state, condition))
            {
                return SymbolicIrProofResult.Syntactic(
                    SymbolicProofStatus.ProvenFalse,
                    "ir_state_contradicts_condition");
            }

            return ClassifyWithIrCache(
                "implication-condition:" + state.NormalizedProofKey + "\n" + SymbolicState.CreateProofConditionKey(condition),
                () =>
                {
                    if (!TryEncodeState(state, out var pathConditions, out var unknownReason))
                    {
                        return SymbolicIrProofResult.Unknown(unknownReason);
                    }

                    if (!SymbolicIrFormulaEncoder.TryEncode(condition, out var formula))
                    {
                        return SymbolicIrProofResult.Unknown(SymbolicUnknownReason.UnsupportedIrEncoding);
                    }

                    var result = ClassifyFormulaImplication(pathConditions, formula);
                    return SymbolicIrProofResult.FromImplication(result, CreateBudgetInfo());
                });
        }

        public SymbolicIrProofResult ClassifyBranchFeasibility(SymbolicState state, SymbolicCondition branchCondition)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (branchCondition == null)
            {
                throw new ArgumentNullException(nameof(branchCondition));
            }

            state = NormalizeState(state);
            if (state.IsContradictory)
            {
                return SymbolicIrProofResult.Syntactic(
                    SymbolicProofStatus.Unreachable,
                    "ir_state_contradictory");
            }

            if (TryClassifySyntacticConditionTruth(branchCondition, out var syntacticStatus))
            {
                return syntacticStatus == SymbolicProofStatus.ProvenTrue
                    ? ClassifyReachability(state)
                    : SymbolicIrProofResult.Syntactic(
                        SymbolicProofStatus.Unreachable,
                        "ir_branch_syntactic_false");
            }

            return ClassifyReachability(state.AddPathCondition(branchCondition));
        }

        public SymbolicIrProofResult ClassifyConditionTruth(SymbolicState state, SymbolicCondition condition)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }

            state = NormalizeState(state);
            if (state.IsContradictory)
            {
                return SymbolicIrProofResult.Syntactic(
                    SymbolicProofStatus.Unreachable,
                    "ir_state_contradictory");
            }

            if (TryClassifySyntacticConditionTruth(condition, out var syntacticStatus))
            {
                return SymbolicIrProofResult.Syntactic(
                    syntacticStatus,
                    "ir_condition_syntactic_truth");
            }

            if (StateContainsCondition(state, condition))
            {
                return SymbolicIrProofResult.Syntactic(
                    SymbolicProofStatus.ProvenTrue,
                    "ir_state_contains_condition");
            }

            if (StateContradictsCondition(state, condition))
            {
                return SymbolicIrProofResult.Syntactic(
                    SymbolicProofStatus.ProvenFalse,
                    "ir_state_contradicts_condition");
            }

            var reachability = ClassifyReachability(state);
            if (reachability.Info.Status == SymbolicProofStatus.Unreachable)
            {
                return reachability;
            }

            return ClassifyWithIrCache(
                "condition-truth:" + state.NormalizedProofKey + "\n" + SymbolicState.CreateProofConditionKey(condition),
                () =>
                {
                    var trueBranch = ClassifyBranchFeasibility(state, condition);
                    if (trueBranch.Info.Status == SymbolicProofStatus.Unreachable)
                    {
                        return trueBranch.RawResult != null
                            ? SymbolicIrProofResult.FromConditionTruth(
                                trueBranch.RawResult,
                                SymbolicProofStatus.ProvenFalse,
                                CreateBudgetInfo())
                            : SymbolicIrProofResult.Syntactic(
                                SymbolicProofStatus.ProvenFalse,
                                trueBranch.Info.Reason);
                    }

                    var falseBranch = ClassifyBranchFeasibility(state, new SymbolicNotCondition(condition));
                    if (falseBranch.Info.Status == SymbolicProofStatus.Unreachable)
                    {
                        return falseBranch.RawResult != null
                            ? SymbolicIrProofResult.FromConditionTruth(
                                falseBranch.RawResult,
                                SymbolicProofStatus.ProvenTrue,
                                CreateBudgetInfo())
                            : SymbolicIrProofResult.Syntactic(
                                SymbolicProofStatus.ProvenTrue,
                                falseBranch.Info.Reason);
                    }

                    return SymbolicIrProofResult.Unknown(falseBranch.Info.UnknownReason);
                });
        }

        public SymbolicIrProofResult ClassifyHazardTrigger(SymbolicState state, SymbolicFact triggerPrecondition)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (triggerPrecondition == null)
            {
                throw new ArgumentNullException(nameof(triggerPrecondition));
            }

            state = NormalizeState(state);
            return ClassifyWithIrCache(
                "hazard-trigger:" + state.NormalizedProofKey + "\n" + SymbolicState.CreateProofFactKey(triggerPrecondition),
                () =>
                {
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
                });
        }

        internal SymbolicIrProofResult ClassifyFormulaReachability(IEnumerable<SmtFormula> pathConditions)
        {
            var result = ClassifyFormulaPathFeasibility(pathConditions);
            return SymbolicIrProofResult.FromReachability(result, CreateBudgetInfo());
        }

        internal SymbolicIrProofResult ClassifyFormulaConditionTruth(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula conditionFormula)
        {
            if (conditionFormula == null)
            {
                throw new ArgumentNullException(nameof(conditionFormula));
            }

            var trueProof = ClassifyFormulaImplication(pathConditions, conditionFormula);
            if (trueProof.Outcome == PurityProofOutcome.ProvablyPure)
            {
                var status = string.Equals(trueProof.Reason, "path_unsatisfiable", StringComparison.Ordinal)
                    ? SymbolicProofStatus.Unreachable
                    : SymbolicProofStatus.ProvenTrue;
                return SymbolicIrProofResult.FromConditionTruth(
                    trueProof,
                    status,
                    CreateBudgetInfo());
            }

            var falseProof = ClassifyFormulaImplication(
                pathConditions,
                new SmtUnaryFormula(SmtUnaryOperator.Not, conditionFormula));
            if (falseProof.Outcome == PurityProofOutcome.ProvablyPure)
            {
                var status = string.Equals(falseProof.Reason, "path_unsatisfiable", StringComparison.Ordinal)
                    ? SymbolicProofStatus.Unreachable
                    : SymbolicProofStatus.ProvenFalse;
                return SymbolicIrProofResult.FromConditionTruth(
                    falseProof,
                    status,
                    CreateBudgetInfo());
            }

            return SymbolicIrProofResult.FromConditionTruth(
                trueProof,
                SymbolicProofStatus.Unknown,
                CreateBudgetInfo());
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

            return ClassifyWithFallback(
                service => service.Classify(new PurityProofQuery(
                    pathConditions.ToArray(),
                    new PurityHazard(PurityHazardKind.BranchReachability, branchCondition))));
        }

        private PurityProofResult ClassifyFormulaPathFeasibility(IEnumerable<SmtFormula> pathConditions)
        {
            return ClassifyWithFallback(service => service.ClassifyPathFeasibility(pathConditions));
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

        private SymbolicIrProofResult ClassifyWithIrCache(
            string key,
            Func<SymbolicIrProofResult> classify)
        {
            var cache = GetProofResultCache();
            if (cache.Results.TryGetValue(key, out var cached))
            {
                return cached.WithCacheHit(CreateBudgetInfo());
            }

            var result = classify();
            cache.Results.TryAdd(key, result);
            return result;
        }

        private ProofResultCache GetProofResultCache()
        {
            return smtAnalysis != null
                ? s_serviceCaches.GetOrCreateValue(smtAnalysis)
                : s_fallbackCache;
        }

        private static SymbolicState NormalizeState(SymbolicState state)
        {
            return state.Normalize();
        }

        private static bool TryClassifySyntacticConditionTruth(
            SymbolicCondition condition,
            out SymbolicProofStatus status)
        {
            switch (SymbolicState.CreateProofConditionKey(condition))
            {
                case "const:true":
                    status = SymbolicProofStatus.ProvenTrue;
                    return true;
                case "const:false":
                    status = SymbolicProofStatus.ProvenFalse;
                    return true;
                default:
                    status = SymbolicProofStatus.Unknown;
                    return false;
            }
        }

        private static bool StateContainsFact(SymbolicState state, SymbolicFact fact)
        {
            var factKey = SymbolicState.CreateProofFactKey(fact);
            var factConditionKey = "fact-condition:" + factKey;
            return state.Facts.Any(candidate => string.Equals(
                    SymbolicState.CreateProofFactKey(candidate),
                    factKey,
                    StringComparison.Ordinal)) ||
                state.PathConditions.Any(candidate => string.Equals(
                    SymbolicState.CreateProofConditionKey(candidate),
                    factConditionKey,
                    StringComparison.Ordinal));
        }

        private static bool StateContradictsFact(SymbolicState state, SymbolicFact fact)
        {
            return StateContainsFact(state, fact.Negate());
        }

        private static bool StateContainsCondition(SymbolicState state, SymbolicCondition condition)
        {
            if (condition is SymbolicFactCondition factCondition &&
                StateContainsFact(state, factCondition.Fact))
            {
                return true;
            }

            var conditionKey = SymbolicState.CreateProofConditionKey(condition);
            return state.Facts.Any(candidate => string.Equals(
                    "fact-condition:" + SymbolicState.CreateProofFactKey(candidate),
                    conditionKey,
                    StringComparison.Ordinal)) ||
                state.PathConditions.Any(candidate => string.Equals(
                    SymbolicState.CreateProofConditionKey(candidate),
                    conditionKey,
                    StringComparison.Ordinal));
        }

        private static bool StateContradictsCondition(SymbolicState state, SymbolicCondition condition)
        {
            return StateContainsCondition(state, new SymbolicNotCondition(condition));
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

        private bool TryEncodeState(
            SymbolicState state,
            out ImmutableArray<SmtFormula> pathConditions,
            out SymbolicUnknownReason unknownReason)
        {
            var entry = GetProofResultCache().EncodedStates.GetOrAdd(
                state.NormalizedProofKey,
                _ => EncodeStateUncached(state));
            pathConditions = entry.PathConditions;
            unknownReason = entry.UnknownReason;
            return entry.Success;
        }

        private static EncodedStateCacheEntry EncodeStateUncached(SymbolicState state)
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
                return new EncodedStateCacheEntry(
                    Success: false,
                    ImmutableArray<SmtFormula>.Empty,
                    SymbolicUnknownReason.UnsupportedIrEncoding);
            }

            return new EncodedStateCacheEntry(
                Success: true,
                builder.ToImmutable(),
                SymbolicUnknownReason.None);
        }

        private sealed class ProofResultCache
        {
            internal ConcurrentDictionary<string, SymbolicIrProofResult> Results { get; } = new(StringComparer.Ordinal);

            internal ConcurrentDictionary<string, EncodedStateCacheEntry> EncodedStates { get; } = new(StringComparer.Ordinal);
        }

        private readonly record struct EncodedStateCacheEntry(
            bool Success,
            ImmutableArray<SmtFormula> PathConditions,
            SymbolicUnknownReason UnknownReason);
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

        public static SymbolicIrProofResult Syntactic(
            SymbolicProofStatus status,
            string reason)
        {
            return new SymbolicIrProofResult(
                rawResult: null,
                new SymbolicProofInfo(
                    status,
                    SymbolicProofBackend.Syntactic,
                    SymbolicUnknownReason.None,
                    reason,
                    cacheHit: false,
                    budget: null));
        }

        internal SymbolicIrProofResult WithCacheHit(SymbolicBudgetInfo? budget)
        {
            return new SymbolicIrProofResult(
                RawResult,
                new SymbolicProofInfo(
                    Info.Status,
                    Info.Backend,
                    Info.UnknownReason,
                    Info.Reason,
                    cacheHit: true,
                    budget ?? Info.Budget,
                    Info.Target,
                    Info.ConditionText,
                    Info.DisplayKind));
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
                "smt_timeout" => SymbolicUnknownReason.Timeout,
                "smt_method_budget_exceeded" => SymbolicUnknownReason.MethodBudgetExceeded,
                "smt_path_condition_budget_exceeded" => SymbolicUnknownReason.PathConditionBudgetExceeded,
                "smt_expression_budget_exceeded" => SymbolicUnknownReason.ExpressionBudgetExceeded,
                "smt_encoding_failure" => SymbolicUnknownReason.EncodingFailure,
                _ => SymbolicUnknownReason.None,
            };
        }
    }
}
