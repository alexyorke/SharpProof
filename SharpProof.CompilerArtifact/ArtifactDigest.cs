using SharpProof.Worker.Protocol;

namespace SharpProof.CompilerArtifact;

// The producer's canonical artifact contains the encoded IR graphs together
// with claim bindings and effect payloads. Hash all of it, including callables
// whose effects do not require a graph.
internal static class ArtifactDigest
{
    internal static string Compute(byte[] canonicalArtifactBytes)
    {
        return WorkerProtocolJson.ComputeSha256(canonicalArtifactBytes);
    }
}
