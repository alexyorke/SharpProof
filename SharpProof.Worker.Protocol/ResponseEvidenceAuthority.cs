namespace SharpProof.Worker.Protocol;

/// <summary>
/// Validates response evidence against the compiler artifact that produced it.
/// The protocol assembly owns the boundary contract while the compiler-artifact
/// assembly supplies the artifact-aware implementation.
/// </summary>
internal interface IWorkerResponseEvidenceAuthority
{
    IEnumerable<string> Validate(WorkerVerifyResponse response);
}
