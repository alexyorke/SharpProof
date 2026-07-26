namespace SharpProof.Verify;

public enum ProofOutcomeKind {
    Proven,
    Refuted,
    Unknown
}

public enum AbstentionReason {
    UnsupportedOperation,
    ApproximationTouchedGoal,
    MissingApiSpecification,
    UnsupportedEncoding,
    ResourceLimit,
    Timeout,
    BackendUnavailable,
    InfrastructureFailure,
    MalformedBackendResult,
    CounterexampleReplayFailed
}

public abstract class ProofOutcome {
    private protected ProofOutcome(ProofOutcomeKind kind) => Kind = kind;

    public ProofOutcomeKind Kind { get; }
}

public sealed class ProvenOutcome : ProofOutcome {
    internal ProvenOutcome(ImmutableArray<ProofJustification> core)
        : base(ProofOutcomeKind.Proven) => Core = core;

    public ImmutableArray<ProofJustification> Core { get; }
}

public sealed class ValidatedModel {
    internal ValidatedModel(ImmutableDictionary<IrVarId, IrValue> assignments) =>
        Assignments = assignments;

    public ImmutableDictionary<IrVarId, IrValue> Assignments { get; }
}

public sealed class RefutedOutcome : ProofOutcome {
    internal RefutedOutcome(ValidatedModel model)
        : base(ProofOutcomeKind.Refuted) => Model = model;

    public ValidatedModel Model { get; }
}

public sealed class UnknownOutcome : ProofOutcome {
    internal UnknownOutcome(AbstentionReason reason)
        : base(ProofOutcomeKind.Unknown) => Reason = reason;

    public AbstentionReason Reason { get; }
}

public static class OutcomeCachePolicy {
    public static bool IsCacheable(ProofOutcome outcome) {
        if (outcome == null) throw new ArgumentNullException(nameof(outcome));
        return outcome.Kind is ProofOutcomeKind.Proven or ProofOutcomeKind.Refuted;
    }
}
