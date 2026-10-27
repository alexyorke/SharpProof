namespace SharpProof.Verify;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1008",
    Justification = "The sparse numeric values are part of the proof-outcome contract.")]
internal enum AbstentionReason
{
    UnsupportedEncoding = 3,
    ResourceLimit = 4,
    Timeout = 5,
    BackendUnavailable = 6,
    InfrastructureFailure = 7,
    MalformedBackendResult = 8,
    CounterexampleReplayFailed = 9,
    PostconditionMayBeUndefined = 10,
    InternalConsistencyMayBeUndefined = 11,
    SolverIncomplete = 12,
    CounterexampleNotReplayable = 13
}

internal abstract class ProofOutcome
{
    private protected ProofOutcome()
    {
    }
}

internal sealed partial class ProvenOutcome : ProofOutcome
{
}

internal sealed partial class ValidatedModel
{
}

internal sealed partial class RefutedOutcome : ProofOutcome
{
}

internal sealed partial class UnknownOutcome : ProofOutcome
{
}
