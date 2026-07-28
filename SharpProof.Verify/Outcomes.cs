namespace SharpProof.Verify;

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
    CounterexampleReplayFailed,
    PostconditionMayBeUndefined
}

public abstract class ProofOutcome {
    private protected ProofOutcome() { }
}

public sealed class ProvenOutcome : ProofOutcome {
    internal ProvenOutcome(ImmutableArray<ProofJustification> core) => Core = core;

    public ImmutableArray<ProofJustification> Core { get; }
}

public sealed class ValidatedModel {
    internal ValidatedModel(ImmutableDictionary<IrVarId, IrValue> assignments) =>
        Assignments = assignments;

    public ImmutableDictionary<IrVarId, IrValue> Assignments { get; }
}

public sealed class RefutedOutcome : ProofOutcome {
    internal RefutedOutcome(ValidatedModel model) => Model = model;

    public ValidatedModel Model { get; }
}

public sealed class UnknownOutcome : ProofOutcome {
    internal UnknownOutcome(AbstentionReason reason) => Reason = reason;

    public AbstentionReason Reason { get; }
}

public static class OutcomeCachePolicy {
    public static bool IsCacheable(ProofOutcome outcome) =>
        outcome == null ? throw new ArgumentNullException(nameof(outcome)) :
        outcome is ProvenOutcome or RefutedOutcome;
}
