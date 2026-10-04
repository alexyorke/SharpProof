namespace SharpProof.Worker;

// The only file-to-prepared-callables boundary. The compiler artifact is
// trusted build output; check its digest and semantic IR shape once, then
// retain the prepared callables for verification and reporting.
internal static class ArtifactValidator
{
    internal static ValidatedArtifact Decode(
        byte[] bytes,
        string? expectedDigest = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var digest = ArtifactDigest.Compute(bytes);
        if (expectedDigest != null && digest != expectedDigest)
        {
            throw new InvalidDataException("The compiler artifact digest does not match the request.");
        }
        var artifact = CompilerManifestArtifactJson.DeserializePrepared(
            new UTF8Encoding(false, true).GetString(bytes), out var callables, cancellationToken);
        return new ValidatedArtifact(artifact, callables, digest);
    }

    internal static WorkerInputSnapshot Bind(
        WorkerVerifyRequest request,
        ValidatedArtifact artifact,
        WorkerCacheIdentity identity)
    {
        if (request.CompilerManifest.Sha256 != artifact.Digest)
        {
            throw new InvalidDataException("The prepared artifact does not match the request digest.");
        }
        var inputHash = CompilerArtifactInputHash.Compute(request, artifact.Digest,
            identity.ToolIdentity, identity.ToolVersion, identity.WorkerBinarySha256,
            identity.ApiSpecIdentity, identity.ApiSpecVersion, identity.ApiSpecContentSha256);
        return new WorkerInputSnapshot(artifact.Manifest, artifact.Callables, artifact.Digest, inputHash);
    }
}

internal sealed record ValidatedArtifact(
    CompilerManifestArtifact Manifest,
    ImmutableArray<CompilerCallablePreparation> Callables,
    string Digest);
