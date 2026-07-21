namespace SharpProof.ProofCore.Analysis;

internal enum AnalysisProofOutcome {
    Proven,
    Disproven,
    Unknown
}

internal sealed record ProofCheckInfo(
    bool WasAttempted,
    Feasibility Feasibility,
    SmtSatisfyingWitness? Witness = null);

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
            [AnalysisHazardKind.BranchReachability] = HazardDescriptor.Triggered(
                "branch_unreachable",
                "branch_reachable",
                "branch_feasibility_unknown"),
            [AnalysisHazardKind.EffectViolationReachability] = HazardDescriptor.Triggered(
                "impure_call_unreachable",
                "impure_call_reachable",
                "impure_call_feasibility_unknown"),
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
            [AnalysisHazardKind.NullDereference] = HazardDescriptor.Triggered(
                "null_dereference_unreachable",
                "null_dereference_reachable",
                "null_dereference_feasibility_unknown"),
            [AnalysisHazardKind.DivideByZero] = HazardDescriptor.Triggered(
                "divide_by_zero_unreachable",
                "divide_by_zero_reachable",
                "divide_by_zero_feasibility_unknown")
        };

    private readonly SmtSolver _solver = new();

    /// <summary>
    ///     Total Z3 rlimit units consumed by classifications on this instance; see
    ///     <see cref="SmtSolver.ConsumedResourceCount" />.
    /// </summary>
    public long ConsumedResourceCount => _solver.ConsumedResourceCount;

    public void Dispose() {
        _solver.Dispose();
    }

    public AnalysisProofResult Classify(AnalysisProofQuery query, TimeSpan timeout) {
        if (query == null || query.Hazard == null)
            return UnknownWithoutProof("invalid_proof_query");

        var pathConditions = query.PathConditions ?? Array.Empty<SmtFormula>();
        if (!HazardDescriptors.TryGetValue(query.Hazard.Kind, out var descriptor))
            return UnknownWithoutProof("unsupported_hazard_kind");

        if (query.Hazard.Visibility == AnalysisEffectVisibility.InternalOnly &&
            !descriptor.AcceptsInternalOnlyVisibility)
            return UnknownWithoutProof("invalid_internal_only_hazard");

        return ClassifyKnownHazard(
            query.Hazard.Kind,
            pathConditions,
            query.Hazard.TriggerCondition,
            timeout);
    }

    private AnalysisProofResult ClassifyInternalOnlyEffect(
        IEnumerable<SmtFormula> pathConditions,
        TimeSpan timeout,
        string pureReason) {
        var normalizedPathConditions = pathConditions.ToArray();
        var path = _solver.CheckSatisfiability(normalizedPathConditions, timeout);
        return path.Feasibility switch {
            Feasibility.Unsatisfiable => new AnalysisProofResult(
                AnalysisProofOutcome.Proven,
                Attempted(path),
                NotAttempted(),
                "path_unsatisfiable"),
            Feasibility.Unknown => new AnalysisProofResult(
                AnalysisProofOutcome.Unknown,
                Attempted(path),
                NotAttempted(),
                "path_feasibility_unknown"),
            _ => new AnalysisProofResult(
                AnalysisProofOutcome.Proven,
                Attempted(path),
                NotAttempted(),
                pureReason)
        };
    }


    private AnalysisProofResult ClassifyKnownHazard(
        AnalysisHazardKind kind,
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula? triggerCondition,
        TimeSpan timeout) {
        var descriptor = HazardDescriptors[kind];
        return descriptor.Mode == HazardClassificationMode.InternalEffect
            ? ClassifyInternalOnlyEffect(pathConditions, timeout, descriptor.PureReason)
            : ClassifyTriggeredHazard(pathConditions, triggerCondition!, timeout, descriptor);
    }

    private AnalysisProofResult ClassifyTriggeredHazard(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula impurityCondition,
        TimeSpan timeout,
        HazardDescriptor descriptor) {
        var normalizedPathConditions = pathConditions.ToArray();
        var check = _solver.CheckPathAndHazardWithWitness(
            normalizedPathConditions,
            impurityCondition,
            timeout);
        var pathFeasibility = check.Path.Feasibility;
        var impurityFeasibility = check.Impurity.Feasibility;
        if (pathFeasibility == Feasibility.Unsatisfiable)
            return new AnalysisProofResult(
                AnalysisProofOutcome.Proven,
                Attempted(check.Path),
                NotAttempted(),
                "path_unsatisfiable");

        if (impurityFeasibility == Feasibility.Unsatisfiable)
            return new AnalysisProofResult(
                AnalysisProofOutcome.Proven,
                Attempted(check.Path),
                Attempted(check.Impurity),
                descriptor.PureReason);

        if (pathFeasibility == Feasibility.Unknown)
            return new AnalysisProofResult(
                AnalysisProofOutcome.Unknown,
                Attempted(check.Path),
                Attempted(check.Impurity),
                "path_feasibility_unknown");

        return impurityFeasibility switch {
            Feasibility.Satisfiable => new AnalysisProofResult(
                AnalysisProofOutcome.Disproven,
                Attempted(check.Path),
                Attempted(check.Impurity),
                descriptor.ImpureReason),
            _ => new AnalysisProofResult(
                AnalysisProofOutcome.Unknown,
                Attempted(check.Path),
                Attempted(check.Impurity),
                descriptor.UnknownReason)
        };
    }

    private static AnalysisProofResult UnknownWithoutProof(string reason) {
        return new AnalysisProofResult(
            AnalysisProofOutcome.Unknown,
            NotAttempted(),
            NotAttempted(),
            reason);
    }

    private static ProofCheckInfo Attempted(SmtFeasibilityResult result) =>
        new ProofCheckInfo(true, result.Feasibility, result.Witness);

    private static ProofCheckInfo NotAttempted() =>
        new ProofCheckInfo(false, Feasibility.Unknown);

    private enum HazardClassificationMode {
        Triggered,
        InternalEffect
    }

    private readonly record struct HazardDescriptor(
        HazardClassificationMode Mode,
        string PureReason,
        string ImpureReason,
        string UnknownReason,
        bool AcceptsInternalOnlyVisibility) {
        internal static HazardDescriptor Triggered(
            string pureReason,
            string impureReason,
            string unknownReason,
            bool acceptsInternalOnlyVisibility = false) {
            return new HazardDescriptor(
                HazardClassificationMode.Triggered,
                pureReason,
                impureReason,
                unknownReason,
                acceptsInternalOnlyVisibility);
        }

        internal static HazardDescriptor InternalEffect(string pureReason) {
            return new HazardDescriptor(
                HazardClassificationMode.InternalEffect,
                pureReason,
                string.Empty,
                string.Empty,
                true);
        }
    }
}
