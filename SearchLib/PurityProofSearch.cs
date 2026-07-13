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
    private static readonly HazardDescriptor GenericImpurityDescriptor =
        HazardDescriptor.Triggered(
            "impurity_unreachable",
            "impurity_reachable",
            "impurity_feasibility_unknown");

    private static readonly IReadOnlyDictionary<PurityHazardKind, HazardDescriptor> HazardDescriptors =
        new Dictionary<PurityHazardKind, HazardDescriptor>
        {
            [PurityHazardKind.BranchReachability] = HazardDescriptor.Triggered(
                "branch_unreachable",
                "branch_reachable",
                "branch_feasibility_unknown"),
            [PurityHazardKind.ImpureCallReachability] = HazardDescriptor.Triggered(
                "impure_call_unreachable",
                "impure_call_reachable",
                "impure_call_feasibility_unknown"),
            [PurityHazardKind.StaticCacheRead] = HazardDescriptor.InternalEffect("safe_static_cache_read"),
            [PurityHazardKind.FreshOwnedObjectWrite] =
                HazardDescriptor.InternalEffect("fresh_owned_object_write"),
            [PurityHazardKind.FreshOwnedArrayWrite] =
                HazardDescriptor.InternalEffect("fresh_owned_array_write"),
            [PurityHazardKind.CallerVisibleMemoryWrite] = HazardDescriptor.Triggered(
                "memory_write_unreachable",
                "caller_visible_memory_write_reachable",
                "caller_visible_memory_write_feasibility_unknown",
                acceptsInternalOnlyVisibility: true),
            [PurityHazardKind.NullDereference] = HazardDescriptor.Triggered(
                "null_dereference_unreachable",
                "null_dereference_reachable",
                "null_dereference_feasibility_unknown"),
            [PurityHazardKind.DivideByZero] = HazardDescriptor.Triggered(
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
        return ClassifyTriggeredHazard(pathConditions, impurityCondition, timeout, GenericImpurityDescriptor);
    }

    public PurityProofResult ClassifyStaticCacheRead(
        IEnumerable<SmtFormula> pathConditions,
        TimeSpan timeout)
    {
        return ClassifyKnownHazard(PurityHazardKind.StaticCacheRead, pathConditions, null, timeout);
    }

    public PurityProofResult ClassifyFreshOwnedObjectWrite(
        IEnumerable<SmtFormula> pathConditions,
        TimeSpan timeout)
    {
        return ClassifyKnownHazard(PurityHazardKind.FreshOwnedObjectWrite, pathConditions, null, timeout);
    }

    public PurityProofResult ClassifyFreshOwnedArrayWrite(
        IEnumerable<SmtFormula> pathConditions,
        TimeSpan timeout)
    {
        return ClassifyKnownHazard(PurityHazardKind.FreshOwnedArrayWrite, pathConditions, null, timeout);
    }

    public PurityProofResult ClassifyCallerVisibleMemoryWrite(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula writeCondition,
        TimeSpan timeout)
    {
        return ClassifyKnownHazard(
            PurityHazardKind.CallerVisibleMemoryWrite,
            pathConditions,
            writeCondition,
            timeout);
    }

    public PurityProofResult Classify(PurityProofQuery query, TimeSpan timeout)
    {
        if (query == null || query.Hazard == null)
            return UnknownWithoutProof("invalid_proof_query");

        var pathConditions = query.PathConditions ?? Array.Empty<SmtFormula>();
        if (!HazardDescriptors.TryGetValue(query.Hazard.Kind, out var descriptor))
            return UnknownWithoutProof("unsupported_hazard_kind");

        if (query.Hazard.Visibility == PurityEffectVisibility.InternalOnly &&
            !descriptor.AcceptsInternalOnlyVisibility)
            return UnknownWithoutProof("invalid_internal_only_hazard");

        return ClassifyKnownHazard(
            query.Hazard.Kind,
            pathConditions,
            query.Hazard.TriggerCondition,
            timeout);
    }

    private PurityProofResult ClassifyInternalOnlyEffect(
        IEnumerable<SmtFormula> pathConditions,
        TimeSpan timeout,
        string pureReason)
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
        return ClassifyKnownHazard(
            PurityHazardKind.BranchReachability,
            pathConditions,
            branchReachabilityCondition,
            timeout);
    }

    public PurityProofResult ClassifyImpureCallReachability(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula callReachabilityCondition,
        TimeSpan timeout)
    {
        return ClassifyKnownHazard(
            PurityHazardKind.ImpureCallReachability,
            pathConditions,
            callReachabilityCondition,
            timeout);
    }

    public PurityProofResult ClassifyNullDereference(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula receiverIsNullCondition,
        TimeSpan timeout)
    {
        return ClassifyKnownHazard(
            PurityHazardKind.NullDereference,
            pathConditions,
            receiverIsNullCondition,
            timeout);
    }

    public PurityProofResult ClassifyDivideByZero(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula divisorIsZeroCondition,
        TimeSpan timeout)
    {
        return ClassifyKnownHazard(
            PurityHazardKind.DivideByZero,
            pathConditions,
            divisorIsZeroCondition,
            timeout);
    }

    private PurityProofResult ClassifyKnownHazard(
        PurityHazardKind kind,
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula? triggerCondition,
        TimeSpan timeout)
    {
        var descriptor = HazardDescriptors[kind];
        return descriptor.Mode == HazardClassificationMode.InternalEffect
            ? ClassifyInternalOnlyEffect(pathConditions, timeout, descriptor.PureReason)
            : ClassifyTriggeredHazard(pathConditions, triggerCondition!, timeout, descriptor);
    }

    private PurityProofResult ClassifyTriggeredHazard(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula impurityCondition,
        TimeSpan timeout,
        HazardDescriptor descriptor)
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
                descriptor.PureReason);

        if (pathFeasibility == Feasibility.Unknown)
            return new PurityProofResult(
                PurityProofOutcome.Unknown,
                Attempted(check.Path),
                Attempted(check.Impurity),
                "path_feasibility_unknown");

        return impurityFeasibility switch
        {
            Feasibility.Satisfiable => new PurityProofResult(
                PurityProofOutcome.ProvablyImpure,
                Attempted(check.Path),
                Attempted(check.Impurity),
                descriptor.ImpureReason),
            _ => new PurityProofResult(
                PurityProofOutcome.Unknown,
                Attempted(check.Path),
                Attempted(check.Impurity),
                descriptor.UnknownReason)
        };
    }

    private static PurityProofResult UnknownWithoutProof(string reason)
    {
        return new PurityProofResult(
            PurityProofOutcome.Unknown,
            NotAttempted(),
            NotAttempted(),
            reason);
    }

    private static ProofCheckInfo Attempted(SmtFeasibilityResult result)
    {
        return new ProofCheckInfo(true, result.Feasibility, result.Witness);
    }

    private static ProofCheckInfo NotAttempted()
    {
        return new ProofCheckInfo(false, Feasibility.Unknown);
    }

    private enum HazardClassificationMode
    {
        Triggered,
        InternalEffect
    }

    private readonly record struct HazardDescriptor(
        HazardClassificationMode Mode,
        string PureReason,
        string ImpureReason,
        string UnknownReason,
        bool AcceptsInternalOnlyVisibility)
    {
        internal static HazardDescriptor Triggered(
            string pureReason,
            string impureReason,
            string unknownReason,
            bool acceptsInternalOnlyVisibility = false)
        {
            return new HazardDescriptor(
                HazardClassificationMode.Triggered,
                pureReason,
                impureReason,
                unknownReason,
                acceptsInternalOnlyVisibility);
        }

        internal static HazardDescriptor InternalEffect(string pureReason)
        {
            return new HazardDescriptor(
                HazardClassificationMode.InternalEffect,
                pureReason,
                string.Empty,
                string.Empty,
                true);
        }
    }
}
