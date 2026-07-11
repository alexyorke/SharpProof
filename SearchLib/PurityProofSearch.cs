using SearchLib.Smt;

namespace SearchLib.Purity;

public enum PurityProofOutcome
{
    ProvablyPure,
    ProvablyImpure,
    Unknown
}

public sealed record ProofCheckInfo(
    bool WasAttempted,
    Feasibility Feasibility,
    SmtSatisfyingWitness? Witness = null);

public sealed record PurityProofResult(
    PurityProofOutcome Outcome,
    ProofCheckInfo PathCheck,
    ProofCheckInfo ImpurityCheck,
    string Reason);

public sealed class PurityProofSearch : IDisposable
{
    private readonly SmtSolver _solver = new();

    /// <summary>
    ///     Total Z3 rlimit units consumed by classifications on this instance; see
    ///     <see cref="SmtSolver.ConsumedResourceCount" />.
    /// </summary>
    public long ConsumedResourceCount => _solver.ConsumedResourceCount;

    public void Dispose()
    {
        _solver.Dispose();
    }

    public PurityProofResult Classify(SmtFormula impurityCondition, TimeSpan timeout)
    {
        return Classify(Array.Empty<SmtFormula>(), impurityCondition, timeout);
    }

    public PurityProofResult Classify(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula impurityCondition,
        TimeSpan timeout)
    {
        return ClassifyCore(
            pathConditions,
            impurityCondition,
            timeout,
            "impurity_unreachable",
            "impurity_reachable",
            "impurity_feasibility_unknown");
    }

    public PurityProofResult ClassifyStaticCacheRead(
        IEnumerable<SmtFormula> pathConditions,
        TimeSpan timeout)
    {
        return ClassifyInternalOnlyEffect(pathConditions, timeout, "safe_static_cache_read");
    }

    public PurityProofResult ClassifyFreshOwnedObjectWrite(
        IEnumerable<SmtFormula> pathConditions,
        TimeSpan timeout)
    {
        return ClassifyInternalOnlyEffect(pathConditions, timeout, "fresh_owned_object_write");
    }

    public PurityProofResult ClassifyFreshOwnedArrayWrite(
        IEnumerable<SmtFormula> pathConditions,
        TimeSpan timeout)
    {
        return ClassifyInternalOnlyEffect(pathConditions, timeout, "fresh_owned_array_write");
    }

    public PurityProofResult ClassifyCallerVisibleMemoryWrite(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula writeCondition,
        TimeSpan timeout)
    {
        return ClassifyCore(
            pathConditions,
            writeCondition,
            timeout,
            "memory_write_unreachable",
            "caller_visible_memory_write_reachable",
            "caller_visible_memory_write_feasibility_unknown");
    }

    public PurityProofResult Classify(PurityProofQuery query, TimeSpan timeout)
    {
        if (query.Hazard.Kind == PurityHazardKind.StaticCacheRead)
            return ClassifyStaticCacheRead(query.PathConditions, timeout);

        if (query.Hazard.Kind == PurityHazardKind.FreshOwnedObjectWrite)
            return ClassifyFreshOwnedObjectWrite(query.PathConditions, timeout);

        if (query.Hazard.Kind == PurityHazardKind.FreshOwnedArrayWrite)
            return ClassifyFreshOwnedArrayWrite(query.PathConditions, timeout);

        if (query.Hazard.Kind == PurityHazardKind.CallerVisibleMemoryWrite)
            return ClassifyCallerVisibleMemoryWrite(query.PathConditions, query.Hazard.TriggerCondition, timeout);

        if (query.Hazard.Visibility == PurityEffectVisibility.InternalOnly)
            return ClassifyInternalOnlyEffect(query.PathConditions, timeout);

        return query.Hazard.Kind switch
        {
            PurityHazardKind.BranchReachability => ClassifyBranchReachability(query.PathConditions,
                query.Hazard.TriggerCondition, timeout),
            PurityHazardKind.ImpureCallReachability => ClassifyImpureCallReachability(query.PathConditions,
                query.Hazard.TriggerCondition, timeout),
            PurityHazardKind.NullDereference => ClassifyNullDereference(query.PathConditions,
                query.Hazard.TriggerCondition, timeout),
            PurityHazardKind.DivideByZero => ClassifyDivideByZero(query.PathConditions, query.Hazard.TriggerCondition,
                timeout),
            _ => new PurityProofResult(
                PurityProofOutcome.Unknown,
                NotAttempted(),
                NotAttempted(),
                "unsupported_hazard_kind")
        };
    }

    private PurityProofResult ClassifyInternalOnlyEffect(
        IEnumerable<SmtFormula> pathConditions,
        TimeSpan timeout,
        string pureReason = "effect_not_caller_visible")
    {
        var normalizedPathConditions = pathConditions.ToArray();
        var path = _solver.CheckSatisfiability(normalizedPathConditions, timeout);
        return path.Feasibility switch
        {
            Feasibility.Unsatisfiable => new PurityProofResult(
                PurityProofOutcome.ProvablyPure,
                Attempted(path),
                NotAttempted(),
                "path_unsatisfiable"),
            Feasibility.Unknown => new PurityProofResult(
                PurityProofOutcome.Unknown,
                Attempted(path),
                NotAttempted(),
                "path_feasibility_unknown"),
            _ => new PurityProofResult(
                PurityProofOutcome.ProvablyPure,
                Attempted(path),
                NotAttempted(),
                pureReason)
        };
    }

    public PurityProofResult ClassifyBranchReachability(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula branchReachabilityCondition,
        TimeSpan timeout)
    {
        return ClassifyCore(
            pathConditions,
            branchReachabilityCondition,
            timeout,
            "branch_unreachable",
            "branch_reachable",
            "branch_feasibility_unknown");
    }

    public PurityProofResult ClassifyImpureCallReachability(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula callReachabilityCondition,
        TimeSpan timeout)
    {
        return ClassifyCore(
            pathConditions,
            callReachabilityCondition,
            timeout,
            "impure_call_unreachable",
            "impure_call_reachable",
            "impure_call_feasibility_unknown");
    }

    public PurityProofResult ClassifyNullDereference(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula receiverIsNullCondition,
        TimeSpan timeout)
    {
        return ClassifyCore(
            pathConditions,
            receiverIsNullCondition,
            timeout,
            "null_dereference_unreachable",
            "null_dereference_reachable",
            "null_dereference_feasibility_unknown");
    }

    public PurityProofResult ClassifyDivideByZero(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula divisorIsZeroCondition,
        TimeSpan timeout)
    {
        return ClassifyCore(
            pathConditions,
            divisorIsZeroCondition,
            timeout,
            "divide_by_zero_unreachable",
            "divide_by_zero_reachable",
            "divide_by_zero_feasibility_unknown");
    }

    private PurityProofResult ClassifyCore(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula impurityCondition,
        TimeSpan timeout,
        string pureReason,
        string impureReason,
        string unknownReason)
    {
        var normalizedPathConditions = pathConditions.ToArray();
        var check = _solver.CheckPathAndImpurityWithWitness(
            normalizedPathConditions,
            impurityCondition,
            timeout);
        var pathFeasibility = check.Path.Feasibility;
        var impurityFeasibility = check.Impurity.Feasibility;
        if (pathFeasibility == Feasibility.Unsatisfiable)
            return new PurityProofResult(
                PurityProofOutcome.ProvablyPure,
                Attempted(check.Path),
                NotAttempted(),
                "path_unsatisfiable");

        if (impurityFeasibility == Feasibility.Unsatisfiable)
            return new PurityProofResult(
                PurityProofOutcome.ProvablyPure,
                Attempted(check.Path),
                Attempted(check.Impurity),
                pureReason);

        if (pathFeasibility == Feasibility.Unknown)
            return new PurityProofResult(
                PurityProofOutcome.Unknown,
                Attempted(check.Path),
                Attempted(check.Impurity),
                "path_feasibility_unknown");

        return impurityFeasibility switch
        {
            Feasibility.Unsatisfiable => new PurityProofResult(
                PurityProofOutcome.ProvablyPure,
                Attempted(check.Path),
                Attempted(check.Impurity),
                pureReason),
            Feasibility.Satisfiable => new PurityProofResult(
                PurityProofOutcome.ProvablyImpure,
                Attempted(check.Path),
                Attempted(check.Impurity),
                impureReason),
            _ => new PurityProofResult(
                PurityProofOutcome.Unknown,
                Attempted(check.Path),
                Attempted(check.Impurity),
                unknownReason)
        };
    }

    private static ProofCheckInfo Attempted(SmtFeasibilityResult result)
    {
        return new ProofCheckInfo(true, result.Feasibility, result.Witness);
    }

    private static ProofCheckInfo NotAttempted()
    {
        return new ProofCheckInfo(false, Feasibility.Unknown);
    }
}
