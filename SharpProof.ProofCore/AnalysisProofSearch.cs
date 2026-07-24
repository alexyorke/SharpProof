namespace SharpProof.ProofCore.Analysis;
internal enum AnalysisProofOutcome {
    Proven,
    Disproven,
    Unknown
}
internal sealed record ProofCheckInfo(bool WasAttempted, Feasibility Feasibility, SmtSatisfyingWitness? Witness = null);
internal sealed record AnalysisProofResult(
    AnalysisProofOutcome Outcome,
    ProofCheckInfo PathCheck,
    ProofCheckInfo HazardCheck,
    string Reason);
internal interface IAnalysisProofSearchSession : IDisposable {
    long ConsumedResourceCount { get; }
    AnalysisProofResult Classify(AnalysisProofQuery query, TimeSpan timeout);
}
internal sealed class AnalysisProofSearch : IAnalysisProofSearchSession {
    private static readonly IReadOnlyDictionary<AnalysisHazardKind, HazardDescriptor> HazardDescriptors =
        new Dictionary<AnalysisHazardKind, HazardDescriptor> {
            [AnalysisHazardKind.BranchReachability] = HazardDescriptor.Triggered("branch"),
            [AnalysisHazardKind.EffectViolationReachability] = HazardDescriptor.Triggered("impure_call"),
            [AnalysisHazardKind.StaticCacheRead] = HazardDescriptor.InternalEffect("safe_static_cache_read"),
            [AnalysisHazardKind.FreshOwnedObjectWrite] =
                HazardDescriptor.InternalEffect("fresh_owned_object_write"),
            [AnalysisHazardKind.FreshOwnedArrayWrite] =
                HazardDescriptor.InternalEffect("fresh_owned_array_write"),
            [AnalysisHazardKind.CallerVisibleMemoryWrite] = HazardDescriptor.Triggered(
                "memory_write_unreachable",
                "caller_visible_memory_write_reachable",
                "caller_visible_memory_write_feasibility_unknown",
                acceptsInternalOnlyVisibility: true),
            [AnalysisHazardKind.NullDereference] = HazardDescriptor.Triggered("null_dereference"),
            [AnalysisHazardKind.DivideByZero] = HazardDescriptor.Triggered("divide_by_zero")
        };
    private readonly SmtSolver _solver = new();
    /// <summary>
    ///     Total Z3 rlimit units consumed by classifications on this instance; see
    ///     <see cref="SmtSolver.ConsumedResourceCount" />.
    /// </summary>
    public long ConsumedResourceCount => _solver.ConsumedResourceCount;
    public void Dispose() => _solver.Dispose();
    public AnalysisProofResult Classify(AnalysisProofQuery query, TimeSpan timeout) {
        if (query == null || query.Hazard == null)
            return UnknownWithoutProof("invalid_proof_query");
        var pathConditions = query.PathConditions ?? [];
        if (!HazardDescriptors.TryGetValue(query.Hazard.Kind, out var descriptor))
            return UnknownWithoutProof("unsupported_hazard_kind");
        if (query.Hazard.Visibility == AnalysisEffectVisibility.InternalOnly &&
            !descriptor.AcceptsInternalOnlyVisibility)
            return UnknownWithoutProof("invalid_internal_only_hazard");
        return descriptor.Mode == HazardClassificationMode.InternalEffect
            ? ClassifyInternalOnlyEffect(pathConditions, timeout, descriptor.PureReason)
            : ClassifyTriggeredHazard(pathConditions, query.Hazard.TriggerCondition!, timeout, descriptor);
    }
    private AnalysisProofResult ClassifyInternalOnlyEffect(
        IReadOnlyList<SmtFormula> pathConditions,
        TimeSpan timeout,
        string pureReason) {
        var path = _solver.CheckSatisfiability(pathConditions.ToArray(), timeout);
        return path.Feasibility switch {
            Feasibility.Unsatisfiable => Result(AnalysisProofOutcome.Proven, path, "path_unsatisfiable"),
            Feasibility.Unknown => Result(AnalysisProofOutcome.Unknown, path, "path_feasibility_unknown"),
            _ => Result(AnalysisProofOutcome.Proven, path, pureReason)
        };
    }
    private AnalysisProofResult ClassifyTriggeredHazard(
        IReadOnlyList<SmtFormula> pathConditions,
        SmtFormula impurityCondition,
        TimeSpan timeout,
        HazardDescriptor descriptor) {
        var check = _solver.CheckPathAndHazardWithWitness(pathConditions.ToArray(), impurityCondition, timeout);
        var pathFeasibility = check.Path.Feasibility;
        var impurityFeasibility = check.Impurity.Feasibility;
        if (pathFeasibility == Feasibility.Unsatisfiable)
            return Result(AnalysisProofOutcome.Proven, check.Path, "path_unsatisfiable");
        if (impurityFeasibility == Feasibility.Unsatisfiable)
            return Result(AnalysisProofOutcome.Proven, check.Path, descriptor.PureReason, check.Impurity);
        if (pathFeasibility == Feasibility.Unknown)
            return Result(AnalysisProofOutcome.Unknown, check.Path, "path_feasibility_unknown", check.Impurity);
        return impurityFeasibility switch {
            Feasibility.Satisfiable =>
                Result(AnalysisProofOutcome.Disproven, check.Path, descriptor.ImpureReason, check.Impurity),
            _ => Result(AnalysisProofOutcome.Unknown, check.Path, descriptor.UnknownReason, check.Impurity)
        };
    }
    private static AnalysisProofResult Result(
        AnalysisProofOutcome outcome,
        SmtFeasibilityResult path,
        string reason,
        SmtFeasibilityResult? hazard = null) =>
        new(outcome, Attempted(path), hazard == null ? NotAttempted() : Attempted(hazard), reason);
    private static AnalysisProofResult UnknownWithoutProof(string reason)
        => new(AnalysisProofOutcome.Unknown, NotAttempted(), NotAttempted(), reason);
    private static ProofCheckInfo Attempted(SmtFeasibilityResult result) =>
        new(true, result.Feasibility, result.Witness);
    private static ProofCheckInfo NotAttempted() =>
        new(false, Feasibility.Unknown);
    enum HazardClassificationMode {
        Triggered,
        InternalEffect
    }
    readonly record struct HazardDescriptor(
        HazardClassificationMode Mode,
        string PureReason,
        string ImpureReason,
        string UnknownReason,
        bool AcceptsInternalOnlyVisibility) {
        internal static HazardDescriptor Triggered(string reason) =>
            Triggered(reason + "_unreachable", reason + "_reachable", reason + "_feasibility_unknown");
        internal static HazardDescriptor Triggered(
            string pureReason,
            string impureReason,
            string unknownReason,
            bool acceptsInternalOnlyVisibility = false) => new(
                HazardClassificationMode.Triggered,
                pureReason,
                impureReason,
                unknownReason,
                acceptsInternalOnlyVisibility);
        internal static HazardDescriptor InternalEffect(string pureReason)
            => new(HazardClassificationMode.InternalEffect, pureReason, string.Empty, string.Empty, true);
    }
}
