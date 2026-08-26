namespace SharpProof.Worker.Protocol;

/// <summary>
/// Validates response evidence against the compiler artifact that produced it.
/// The protocol assembly owns the boundary contract while the compiler-artifact
/// assembly supplies the artifact-aware implementation.
/// </summary>
public interface IWorkerResponseEvidenceAuthority
{
    /// <summary>
    /// Validates response evidence against the authority held by the caller.
    /// </summary>
    /// <param name="response">The deserialized worker response to validate.</param>
    /// <returns>
    /// Protocol error codes for evidence that is not authoritative. Return an
    /// empty sequence when the evidence is valid.
    /// </returns>
    IEnumerable<string> Validate(WorkerVerifyResponse response);
}
