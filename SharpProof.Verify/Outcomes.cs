namespace SharpProof.Verify;

public enum AbstentionReason
{
    UnsupportedEncoding = 3,
    ResourceLimit = 4,
    Timeout = 5,
    BackendUnavailable = 6,
    InfrastructureFailure = 7,
    MalformedBackendResult = 8,
    CounterexampleReplayFailed = 9,
    PostconditionMayBeUndefined = 10,
    InternalConsistencyMayBeUndefined = 11
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
