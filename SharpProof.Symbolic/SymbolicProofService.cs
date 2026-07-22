namespace SharpProof.Symbolic;

internal sealed class SymbolicProofService(SmtAnalysisService? smtAnalysis) {
    private const string ContradictoryStateReason = "path_unsatisfiable";
    private readonly SymbolicProofCache proofCache = SymbolicProofCacheStore.Get(smtAnalysis);

    public SymbolicProofInfo ClassifyReachability(SymbolicState state) {
        if (state == null) throw new ArgumentNullException(nameof(state));

        if (!state.IsExact) return SymbolicProofInfo.Unknown(state.UnknownReason);

        state = SymbolicProofStateFacts.NormalizeState(state);
        if (state.IsContradictory)
            return SymbolicProofInfo.Syntactic(SymbolicProofStatus.Unreachable, ContradictoryStateReason);

        if (state.Facts.Length == 0 && state.PathConditions.Length == 0)
            return SymbolicProofInfo.Syntactic(SymbolicProofStatus.Reachable, "ir_state_empty");

        return ClassifyWithIrCache(
            "reachability:" + state.NormalizedProofKey,
            () => {
                if (!TryEncodeState(state, out var pathConditions, out var unknownReason))
                    return SymbolicProofInfo.Unknown(unknownReason);

                return SymbolicProofInfo.FromReachability(ClassifyPathFeasibility(pathConditions), CreateBudgetInfo());
            });
    }
    public SymbolicProofInfo ClassifyImplication(SymbolicState state, SymbolicFact fact) {
        if (state == null) throw new ArgumentNullException(nameof(state));

        if (fact == null) throw new ArgumentNullException(nameof(fact));

        if (!state.IsExact) return SymbolicProofInfo.Unknown(state.UnknownReason);

        state = SymbolicProofStateFacts.NormalizeState(state);
        fact = SymbolicProofStateFacts.RewriteQueryFactToCurrentVersions(fact, state);
        if (state.IsContradictory)
            return SymbolicProofInfo.Syntactic(SymbolicProofStatus.ProvenTrue, ContradictoryStateReason);

        if (SymbolicState.TryEvaluateProofFact(fact, out var factValue))
            return factValue
                ? SymbolicProofInfo.Syntactic(SymbolicProofStatus.ProvenTrue, "ir_target_fact_syntactic_true")
                : ClassifySyntacticallyFalseImplication(state, "ir_target_fact_syntactic_false");

        if (SymbolicProofStateFacts.StateContainsFact(state, fact))
            return SymbolicProofInfo.Syntactic(SymbolicProofStatus.ProvenTrue, "ir_state_contains_fact");

        if (SymbolicProofStateFacts.StateContradictsFact(state, fact))
            return SymbolicProofInfo.Syntactic(SymbolicProofStatus.ProvenFalse, "ir_state_contradicts_fact");

        return ClassifyWithIrCache(
            "implication-fact:" + state.NormalizedProofKey + "\n" + SymbolicState.CreateProofFactKey(fact),
            () => {
                if (!TryEncodeState(state, out var pathConditions, out var unknownReason))
                    return SymbolicProofInfo.Unknown(unknownReason);

                if (!SymbolicProofEncoder.TryEncodeFactWithPathState(fact, state, out var factFormula))
                    return SymbolicProofInfo.Unknown(SymbolicUnknownReason.UnsupportedIrEncoding);

                return SymbolicProofInfo.FromImplication(ClassifyRawImplication(pathConditions, factFormula), CreateBudgetInfo());
            });
    }
    public SymbolicProofInfo ClassifyBranchFeasibility(SymbolicState state, SymbolicCondition branchCondition) {
        if (state == null) throw new ArgumentNullException(nameof(state));

        if (branchCondition == null) throw new ArgumentNullException(nameof(branchCondition));

        if (!state.IsExact) return SymbolicProofInfo.Unknown(state.UnknownReason);

        state = SymbolicProofStateFacts.NormalizeState(state);
        branchCondition = SymbolicProofStateFacts.RewriteQueryConditionToCurrentVersions(branchCondition, state);
        if (state.IsContradictory)
            return SymbolicProofInfo.Syntactic(SymbolicProofStatus.Unreachable, ContradictoryStateReason);

        if (SymbolicProofStateFacts.TryClassifySyntacticConditionTruth(branchCondition, out var syntacticStatus))
            return syntacticStatus == SymbolicProofStatus.ProvenTrue
                ? ClassifyReachability(state)
                : SymbolicProofInfo.Syntactic(SymbolicProofStatus.Unreachable, "ir_branch_syntactic_false");

        if (SymbolicProofStateFacts.StateContainsCondition(state, branchCondition)) return ClassifyReachability(state);

        if (SymbolicProofStateFacts.StateContradictsCondition(state, branchCondition))
            return SymbolicProofInfo.Syntactic(SymbolicProofStatus.Unreachable, "ir_state_contradicts_branch");

        if (!SymbolicProofEncoder.TryEncodeConditionWithPathState(branchCondition, state, out _))
            return SymbolicProofInfo.Unknown(SymbolicUnknownReason.UnsupportedIrEncoding);

        return ClassifyReachability(state.AddPathCondition(branchCondition));
    }
    public SymbolicProofInfo ClassifyConditionTruth(SymbolicState state, SymbolicCondition condition) {
        if (TryClassifyConditionPreliminarily(
                state,
                condition,
                ConditionClassificationMode.Truth,
                out state,
                out condition,
                out var preliminaryResult))
            return preliminaryResult;

        var reachability = ClassifyReachability(state);
        if (reachability.Status == SymbolicProofStatus.Unreachable) return reachability;

        return ClassifyWithIrCache(
            "condition-truth:" + state.NormalizedProofKey + "\n" + SymbolicState.CreateProofConditionKey(condition),
            () => {
                var trueBranch = ClassifyBranchFeasibility(state, condition);
                if (trueBranch.Status == SymbolicProofStatus.Unreachable)
                    return trueBranch.RawResult != null
                        ? SymbolicProofInfo.FromConditionTruth(trueBranch.RawResult, SymbolicProofStatus.ProvenFalse, CreateBudgetInfo())
                        : SymbolicProofInfo.Syntactic(SymbolicProofStatus.ProvenFalse, trueBranch.Reason);
                if (trueBranch.Status == SymbolicProofStatus.Unknown)
                    return trueBranch.WithStatus(SymbolicProofStatus.Unknown, "ir_condition_true_branch_feasibility_unknown");

                var falseBranch = ClassifyBranchFeasibility(state, new SymbolicNotCondition(condition));
                if (falseBranch.Status == SymbolicProofStatus.Unreachable)
                    return falseBranch.RawResult != null
                        ? SymbolicProofInfo.FromConditionTruth(falseBranch.RawResult, SymbolicProofStatus.ProvenTrue, CreateBudgetInfo())
                        : SymbolicProofInfo.Syntactic(SymbolicProofStatus.ProvenTrue, falseBranch.Reason);
                if (falseBranch.Status == SymbolicProofStatus.Unknown)
                    return falseBranch.WithStatus(SymbolicProofStatus.Unknown, "ir_condition_false_branch_feasibility_unknown");

                return falseBranch.WithStatus(SymbolicProofStatus.Unknown, "ir_condition_both_branches_feasible");
            });
    }
    private SymbolicProofInfo ClassifySyntacticallyFalseImplication(SymbolicState state, string reachableReason) {
        var reachability = ClassifyReachability(state);
        return reachability.Status switch {
            SymbolicProofStatus.Unreachable => reachability.WithStatus(SymbolicProofStatus.ProvenTrue, reachability.Reason),
            SymbolicProofStatus.Reachable => reachability.WithStatus(SymbolicProofStatus.ProvenFalse, reachableReason),
            _ => reachability.WithStatus(SymbolicProofStatus.Unknown, "ir_false_implication_state_reachability_unknown")
        };
    }
    private bool TryClassifyConditionPreliminarily(
        SymbolicState state,
        SymbolicCondition condition,
        ConditionClassificationMode mode,
        out SymbolicState normalizedState,
        out SymbolicCondition rewrittenCondition,
        out SymbolicProofInfo result) {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (condition == null) throw new ArgumentNullException(nameof(condition));

        normalizedState = SymbolicProofStateFacts.NormalizeState(state);
        rewrittenCondition = SymbolicProofStateFacts.RewriteQueryConditionToCurrentVersions(condition, normalizedState);
        if (!normalizedState.IsExact) {
            result = SymbolicProofInfo.Unknown(normalizedState.UnknownReason);
            return true;
        }
        if (normalizedState.IsContradictory) {
            result = SymbolicProofInfo.Syntactic(
                mode == ConditionClassificationMode.Implication
                    ? SymbolicProofStatus.ProvenTrue
                    : SymbolicProofStatus.Unreachable,
                ContradictoryStateReason);
            return true;
        }
        if (SymbolicProofStateFacts.TryClassifySyntacticConditionTruth(rewrittenCondition, out var syntacticStatus)) {
            if (mode == ConditionClassificationMode.Implication &&
                syntacticStatus == SymbolicProofStatus.ProvenFalse)
                result = ClassifySyntacticallyFalseImplication(normalizedState, "ir_condition_syntactic_false_reachable");
            else
                result = SymbolicProofInfo.Syntactic(
                    syntacticStatus,
                    mode == ConditionClassificationMode.Implication
                        ? "ir_condition_syntactic_truth"
                        : syntacticStatus == SymbolicProofStatus.ProvenTrue
                            ? "ir_condition_syntactic_true"
                            : "ir_condition_syntactic_false");
            return true;
        }
        if (SymbolicProofStateFacts.StateContainsCondition(normalizedState, rewrittenCondition)) {
            result = SymbolicProofInfo.Syntactic(SymbolicProofStatus.ProvenTrue, "ir_state_contains_condition");
            return true;
        }
        if (SymbolicProofStateFacts.StateContradictsCondition(normalizedState, rewrittenCondition)) {
            result = SymbolicProofInfo.Syntactic(SymbolicProofStatus.ProvenFalse, "ir_state_contradicts_condition");
            return true;
        }
        result = null!;
        return false;
    }
    enum ConditionClassificationMode {
        Implication,
        Truth
    }
    public SymbolicProofInfo ClassifyHazardTrigger(SymbolicState state, SymbolicFact triggerPrecondition) {
        if (state == null) throw new ArgumentNullException(nameof(state));

        if (triggerPrecondition == null) throw new ArgumentNullException(nameof(triggerPrecondition));

        if (!state.IsExact) return SymbolicProofInfo.Unknown(state.UnknownReason);

        state = SymbolicProofStateFacts.NormalizeState(state);
        triggerPrecondition = SymbolicProofStateFacts.RewriteQueryFactToCurrentVersions(triggerPrecondition, state);
        if (state.IsContradictory)
            return SymbolicProofInfo.Syntactic(SymbolicProofStatus.Unreachable, ContradictoryStateReason);

        return ClassifyWithIrCache(
            "hazard-trigger:" + state.NormalizedProofKey + "\n" + SymbolicState.CreateProofFactKey(triggerPrecondition),
            () => {
                var triggerCondition = ClassifyExceptionTriggerCondition(state, triggerPrecondition);
                if (triggerCondition.Status == SymbolicProofStatus.ProvenTrue) return triggerCondition;

                if (triggerCondition.Status is SymbolicProofStatus.ProvenFalse or SymbolicProofStatus.Unreachable)
                    return triggerCondition.Status == SymbolicProofStatus.Unreachable
                        ? triggerCondition
                        : triggerCondition.WithStatus(SymbolicProofStatus.Unreachable);

                var proven = ClassifyImplication(state, triggerPrecondition);
                if (proven.Status == SymbolicProofStatus.ProvenTrue) return proven;

                var triggerFeasibility = ClassifyBranchFeasibility(state, new SymbolicFactCondition(triggerPrecondition));
                return triggerFeasibility.Status == SymbolicProofStatus.Unreachable
                    ? triggerFeasibility
                    : proven;
            });
    }
    private SymbolicProofInfo ClassifyExceptionTriggerCondition(SymbolicState state, SymbolicFact triggerPrecondition) {
        if (triggerPrecondition is { Polarity: true, Atom: SymbolicExceptionPreconditionAtom precondition })
            return ClassifyConditionTruth(state, precondition.Trigger);

        return SymbolicProofInfo.Unknown(SymbolicUnknownReason.UnsupportedIrEncoding);
    }
    private SymbolicProofInfo ClassifyWithIrCache(string key, Func<SymbolicProofInfo> classify) {
        if (proofCache.TryGetResult(key, out var cached)) return cached.WithCacheHit(CreateBudgetInfo());

        var result = classify();
        proofCache.TryAddResult(key, result);
        return result;
    }
    private AnalysisProofResult ClassifyPathFeasibility(IEnumerable<SmtFormula> pathConditions) =>
        Execute(service => service.ClassifyPathFeasibility(pathConditions));

    private AnalysisProofResult ClassifyRawImplication(IEnumerable<SmtFormula> pathConditions, SmtFormula factFormula) =>
        Execute(service => service.ClassifyImplication(pathConditions, factFormula));

    private AnalysisProofResult Execute(Func<SmtAnalysisService, AnalysisProofResult> classify) {
        if (smtAnalysis != null) return classify(smtAnalysis);

        using var fallback = new SmtAnalysisService(SmtAnalysisOptions.Default);
        return classify(fallback);
    }
    private SymbolicBudgetInfo? CreateBudgetInfo() {
        var service = smtAnalysis;
        if (service == null) return null;

        var cache = new SymbolicCacheInfo(
            service.CacheHitCount + proofCache.HitCount,
            service.CacheMissCount + proofCache.MissCount,
            service.CacheEntryCount + proofCache.Count,
            service.CacheEvictionCount + proofCache.EvictionCount);
        return new SymbolicBudgetInfo(
            service.Options.MaxPathConditions,
            service.Options.MaxExpressionNodes,
            ToBoundedMilliseconds(service.Options.QueryTimeout),
            ToBoundedMilliseconds(service.Options.MethodBudget),
            service.ExecutedQueryCount,
            cache.Entries,
            cache);
    }
    private static int ToBoundedMilliseconds(TimeSpan value) =>
        value.TotalMilliseconds >= int.MaxValue ? int.MaxValue :
        value.TotalMilliseconds <= int.MinValue ? int.MinValue : (int)value.TotalMilliseconds;

    private bool TryEncodeState(SymbolicState state, out ImmutableArray<SmtFormula> pathConditions,
        out SymbolicUnknownReason unknownReason) {
        if (!proofCache.TryGetEncodedState(state.NormalizedProofKey, out var entry)) {
            entry = SymbolicProofEncoder.EncodeState(state);
            proofCache.TryAddEncodedState(state.NormalizedProofKey, entry);
        }
        pathConditions = entry.PathConditions;
        unknownReason = entry.UnknownReason;
        return entry.Success;
    }
}
