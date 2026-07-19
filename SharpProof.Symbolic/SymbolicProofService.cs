using SharpProof.ProofCore.Purity;

namespace SharpProof.Symbolic;

internal sealed class SymbolicProofService
{
    private const string ContradictoryStateReason = "path_unsatisfiable";
    private readonly SymbolicProofPipeline proofPipeline;
    private readonly SymbolicProofCache proofCache;
    private readonly SmtAnalysisService? smtAnalysis;

    public SymbolicProofService(SmtAnalysisService? smtAnalysis)
    {
        this.smtAnalysis = smtAnalysis;
        proofPipeline = new SymbolicProofPipeline(smtAnalysis);
        proofCache = SymbolicProofCacheStore.Get(smtAnalysis);
    }

    public SymbolicIrProofResult ClassifyReachability(SymbolicState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        state = SymbolicProofStateFacts.NormalizeState(state);
        if (state.IsContradictory)
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.Unreachable,
                ContradictoryStateReason);

        if (state.Facts.Length == 0 && state.PathConditions.Length == 0)
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.Reachable,
                "ir_state_empty");

        return ClassifyWithIrCache(
            "reachability:" + state.NormalizedProofKey,
            () =>
            {
                if (!TryEncodeState(state, out var pathConditions, out var unknownReason))
                    return SymbolicIrProofResult.Unknown(unknownReason);

                return proofPipeline.ClassifyReachability(
                    pathConditions,
                    CreateBudgetInfo,
                    SymbolicProofSupport.Exact);
            });
    }

    public SymbolicIrProofResult ClassifyImplication(SymbolicState state, SymbolicFact fact)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        if (fact == null) throw new ArgumentNullException(nameof(fact));

        state = SymbolicProofStateFacts.NormalizeState(state);
        fact = SymbolicProofStateFacts.RewriteQueryFactToCurrentVersions(fact, state);
        if (state.IsContradictory)
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.ProvenTrue,
                ContradictoryStateReason);

        if (SymbolicState.TryEvaluateProofFact(fact, out var factValue))
            return factValue
                ? SymbolicIrProofResult.Syntactic(
                    SymbolicProofStatus.ProvenTrue,
                    "ir_target_fact_syntactic_true")
                : ClassifySyntacticallyFalseImplication(state, "ir_target_fact_syntactic_false");

        if (SymbolicProofStateFacts.StateContainsFact(state, fact))
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.ProvenTrue,
                "ir_state_contains_fact");

        if (SymbolicProofStateFacts.StateContradictsFact(state, fact))
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.ProvenFalse,
                "ir_state_contradicts_fact");

        return ClassifyWithIrCache(
            "implication-fact:" + state.NormalizedProofKey + "\n" + SymbolicState.CreateProofFactKey(fact),
            () =>
            {
                if (!TryEncodeState(state, out var pathConditions, out var unknownReason))
                    return SymbolicIrProofResult.Unknown(unknownReason);

                if (!SymbolicProofEncoder.TryEncodeFactWithPathState(fact, state, out var factFormula))
                    return SymbolicIrProofResult.Unknown(SymbolicUnknownReason.UnsupportedIrEncoding);

                return proofPipeline.ClassifyImplication(
                    pathConditions,
                    factFormula,
                    CreateBudgetInfo,
                    SymbolicProofSupport.Exact);
            });
    }

    public SymbolicIrProofResult ClassifyBranchFeasibility(SymbolicState state, SymbolicCondition branchCondition)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        if (branchCondition == null) throw new ArgumentNullException(nameof(branchCondition));

        state = SymbolicProofStateFacts.NormalizeState(state);
        branchCondition = SymbolicProofStateFacts.RewriteQueryConditionToCurrentVersions(branchCondition, state);
        if (state.IsContradictory)
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.Unreachable,
                ContradictoryStateReason);

        if (SymbolicProofStateFacts.TryClassifySyntacticConditionTruth(branchCondition, out var syntacticStatus))
            return syntacticStatus == SymbolicProofStatus.ProvenTrue
                ? ClassifyReachability(state)
                : SymbolicIrProofResult.Syntactic(
                    SymbolicProofStatus.Unreachable,
                    "ir_branch_syntactic_false");

        if (SymbolicProofStateFacts.StateContainsCondition(state, branchCondition)) return ClassifyReachability(state);

        if (SymbolicProofStateFacts.StateContradictsCondition(state, branchCondition))
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.Unreachable,
                "ir_state_contradicts_branch");

        if (!SymbolicProofEncoder.TryEncodeConditionWithPathState(branchCondition, state, out _))
            return SymbolicIrProofResult.Unknown(SymbolicUnknownReason.UnsupportedIrEncoding);

        return ClassifyReachability(state.AddPathCondition(branchCondition));
    }

    public SymbolicIrProofResult ClassifyConditionTruth(SymbolicState state, SymbolicCondition condition)
    {
        if (TryClassifyConditionPreliminarily(
                state,
                condition,
                ConditionClassificationMode.Truth,
                out state,
                out condition,
                out var preliminaryResult))
            return preliminaryResult;

        var reachability = ClassifyReachability(state);
        if (reachability.Info.Status == SymbolicProofStatus.Unreachable) return reachability;

        return ClassifyWithIrCache(
            "condition-truth:" + state.NormalizedProofKey + "\n" + SymbolicState.CreateProofConditionKey(condition),
            () =>
            {
                var trueBranch = ClassifyBranchFeasibility(state, condition);
                if (trueBranch.Info.Status == SymbolicProofStatus.Unreachable)
                    return trueBranch.RawResult != null
                        ? SymbolicIrProofResult.FromConditionTruth(
                            trueBranch.RawResult,
                            SymbolicProofStatus.ProvenFalse,
                            CreateBudgetInfo())
                        : SymbolicIrProofResult.Syntactic(
                            SymbolicProofStatus.ProvenFalse,
                            trueBranch.Info.Reason);
                if (trueBranch.Info.Status == SymbolicProofStatus.Unknown)
                    return trueBranch.WithStatus(
                        SymbolicProofStatus.Unknown,
                        "ir_condition_true_branch_feasibility_unknown");

                var falseBranch = ClassifyBranchFeasibility(state, new SymbolicNotCondition(condition));
                if (falseBranch.Info.Status == SymbolicProofStatus.Unreachable)
                    return falseBranch.RawResult != null
                        ? SymbolicIrProofResult.FromConditionTruth(
                            falseBranch.RawResult,
                            SymbolicProofStatus.ProvenTrue,
                            CreateBudgetInfo())
                        : SymbolicIrProofResult.Syntactic(
                            SymbolicProofStatus.ProvenTrue,
                            falseBranch.Info.Reason);
                if (falseBranch.Info.Status == SymbolicProofStatus.Unknown)
                    return falseBranch.WithStatus(
                        SymbolicProofStatus.Unknown,
                        "ir_condition_false_branch_feasibility_unknown");

                return falseBranch.WithStatus(
                    SymbolicProofStatus.Unknown,
                    "ir_condition_both_branches_feasible");
            });
    }

    private SymbolicIrProofResult ClassifySyntacticallyFalseImplication(
        SymbolicState state,
        string reachableReason)
    {
        var reachability = ClassifyReachability(state);
        return reachability.Info.Status switch
        {
            SymbolicProofStatus.Unreachable => reachability.WithStatus(
                SymbolicProofStatus.ProvenTrue,
                reachability.Info.Reason),
            SymbolicProofStatus.Reachable => reachability.WithStatus(
                SymbolicProofStatus.ProvenFalse,
                reachableReason),
            _ => reachability.WithStatus(
                SymbolicProofStatus.Unknown,
                "ir_false_implication_state_reachability_unknown")
        };
    }

    private bool TryClassifyConditionPreliminarily(
        SymbolicState state,
        SymbolicCondition condition,
        ConditionClassificationMode mode,
        out SymbolicState normalizedState,
        out SymbolicCondition rewrittenCondition,
        out SymbolicIrProofResult result)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (condition == null) throw new ArgumentNullException(nameof(condition));

        normalizedState = SymbolicProofStateFacts.NormalizeState(state);
        rewrittenCondition = SymbolicProofStateFacts.RewriteQueryConditionToCurrentVersions(condition, normalizedState);
        if (normalizedState.IsContradictory)
        {
            result = SymbolicIrProofResult.Syntactic(
                mode == ConditionClassificationMode.Implication
                    ? SymbolicProofStatus.ProvenTrue
                    : SymbolicProofStatus.Unreachable,
                ContradictoryStateReason);
            return true;
        }

        if (SymbolicProofStateFacts.TryClassifySyntacticConditionTruth(rewrittenCondition, out var syntacticStatus))
        {
            if (mode == ConditionClassificationMode.Implication &&
                syntacticStatus == SymbolicProofStatus.ProvenFalse)
                result = ClassifySyntacticallyFalseImplication(
                    normalizedState,
                    "ir_condition_syntactic_false_reachable");
            else
                result = SymbolicIrProofResult.Syntactic(
                    syntacticStatus,
                    mode == ConditionClassificationMode.Implication
                        ? "ir_condition_syntactic_truth"
                        : syntacticStatus == SymbolicProofStatus.ProvenTrue
                            ? "ir_condition_syntactic_true"
                            : "ir_condition_syntactic_false");
            return true;
        }

        if (SymbolicProofStateFacts.StateContainsCondition(normalizedState, rewrittenCondition))
        {
            result = SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.ProvenTrue,
                "ir_state_contains_condition");
            return true;
        }

        if (SymbolicProofStateFacts.StateContradictsCondition(normalizedState, rewrittenCondition))
        {
            result = SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.ProvenFalse,
                "ir_state_contradicts_condition");
            return true;
        }

        result = null!;
        return false;
    }

    private enum ConditionClassificationMode
    {
        Implication,
        Truth
    }

    public SymbolicIrProofResult ClassifyHazardTrigger(SymbolicState state, SymbolicFact triggerPrecondition)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        if (triggerPrecondition == null) throw new ArgumentNullException(nameof(triggerPrecondition));

        state = SymbolicProofStateFacts.NormalizeState(state);
        triggerPrecondition = SymbolicProofStateFacts.RewriteQueryFactToCurrentVersions(triggerPrecondition, state);
        if (state.IsContradictory)
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.Unreachable,
                ContradictoryStateReason);

        return ClassifyWithIrCache(
            "hazard-trigger:" + state.NormalizedProofKey + "\n" + SymbolicState.CreateProofFactKey(triggerPrecondition),
            () =>
            {
                var triggerCondition = ClassifyExceptionTriggerCondition(state, triggerPrecondition);
                if (triggerCondition.Info.Status == SymbolicProofStatus.ProvenTrue) return triggerCondition;

                if (triggerCondition.Info.Status is SymbolicProofStatus.ProvenFalse or SymbolicProofStatus.Unreachable)
                    return triggerCondition.Info.Status == SymbolicProofStatus.Unreachable
                        ? triggerCondition
                        : triggerCondition.WithStatus(SymbolicProofStatus.Unreachable);

                var proven = ClassifyImplication(state, triggerPrecondition);
                if (proven.Info.Status == SymbolicProofStatus.ProvenTrue) return proven;

                var triggerFeasibility = ClassifyBranchFeasibility(
                    state,
                    new SymbolicFactCondition(triggerPrecondition));
                return triggerFeasibility.Info.Status == SymbolicProofStatus.Unreachable
                    ? triggerFeasibility
                    : proven;
            });
    }

    private SymbolicIrProofResult ClassifyExceptionTriggerCondition(SymbolicState state,
        SymbolicFact triggerPrecondition)
    {
        if (triggerPrecondition is { Polarity: true, Atom: SymbolicExceptionPreconditionAtom precondition })
            return ClassifyConditionTruth(state, precondition.Trigger);

        return SymbolicIrProofResult.Unknown(SymbolicUnknownReason.UnsupportedIrEncoding);
    }

    private SymbolicIrProofResult ClassifyWithIrCache(
        string key,
        Func<SymbolicIrProofResult> classify)
    {
        if (proofCache.TryGetResult(key, out var cached)) return cached.WithCacheHit(CreateBudgetInfo());

        var result = classify();
        proofCache.TryAddResult(key, result);
        return result;
    }

    private SymbolicBudgetInfo? CreateBudgetInfo()
    {
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
            SymbolicSmtDiagnostics.ToBoundedMilliseconds(service.Options.QueryTimeout),
            SymbolicSmtDiagnostics.ToBoundedMilliseconds(service.Options.MethodBudget),
            service.ExecutedQueryCount,
            cache.Entries,
            cache);
    }

    private bool TryEncodeState(
        SymbolicState state,
        out ImmutableArray<SmtFormula> pathConditions,
        out SymbolicUnknownReason unknownReason)
    {
        if (!proofCache.TryGetEncodedState(state.NormalizedProofKey, out var entry))
        {
            entry = SymbolicProofEncoder.EncodeState(state);
            proofCache.TryAddEncodedState(state.NormalizedProofKey, entry);
        }

        pathConditions = entry.PathConditions;
        unknownReason = entry.UnknownReason;
        return entry.Success;
    }


}
