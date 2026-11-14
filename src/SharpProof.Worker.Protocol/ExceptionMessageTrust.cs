namespace SharpProof.Worker.Protocol;

// The opt-in `SharpProofTrustExceptionMessages` assumes that a framework
// exception constructor's message lookup (resource managers and culture
// callbacks) has no effect and does not run. Each callable with an effect
// claim then carries this declared, initially unused, trusted assumption;
// the worker marks it used only when a proof core depends on it.
internal static class ExceptionMessageTrust
{
    internal const string AssumptionPrefix = "exception-messages-v1:";

    // Proof-core labels for the per-site facts the worker adds.
    internal const string FactLabel = "exception-messages";

    internal static string AssumptionId(string callableId)
    {
        RepositoryParameterGuard.NotNull(callableId, nameof(callableId));
        return AssumptionPrefix + callableId;
    }
}
