namespace SharpProof.Verify;

public enum AbstentionReason
{
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

public abstract class ProofOutcome
{
    private protected ProofOutcome()
    {
    }
}

public sealed partial class ProvenOutcome : ProofOutcome
{
}

public sealed partial class ValidatedModel
{
}

public sealed partial class RefutedOutcome : ProofOutcome
{
}

public sealed partial class UnknownOutcome : ProofOutcome
{
}

public static class OutcomeCachePolicy
{
    public static bool IsCacheable(ProofOutcome outcome)
    {
        return outcome == null ? throw new ArgumentNullException(nameof(outcome)) :
        outcome is ProvenOutcome or RefutedOutcome;
    }
}
