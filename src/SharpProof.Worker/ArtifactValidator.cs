using System.Collections.Immutable;
namespace SharpProof.Worker;

// The only file-to-prepared-callables boundary. The compiler artifact is
// trusted build output; check its digest and semantic IR shape once, then
// retain the prepared callables for verification and reporting.
internal static class ArtifactValidator
{
    internal static ValidatedArtifact Decode(
        ImmutableArray<byte> bytes,
        string? expectedDigest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RepositoryParameterGuard.ThrowIfMissing(bytes.IsDefault, nameof(bytes));
        var digest = ArtifactDigest.Compute(bytes);
        if (expectedDigest != null && digest != expectedDigest)
        {
            throw new InvalidDataException("The compiler artifact digest does not match the request.");
        }
        var artifact = CompilerManifestArtifactJson.DeserializePrepared(
            new UTF8Encoding(false, true).GetString(bytes.AsSpan()), out var callables, cancellationToken);
        return new ValidatedArtifact(artifact, callables, digest);
    }

    internal static WorkerInputSnapshot Bind(
        WorkerVerifyRequest request,
        ValidatedArtifact artifact,
        WorkerCacheIdentity identity)
    {
        request = ArgumentNullGuard.NotNull(request, nameof(request));
        artifact = ArgumentNullGuard.NotNull(artifact, nameof(artifact));
        identity = ArgumentNullGuard.NotNull(identity, nameof(identity));
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
    string Digest)
{
    public CompilerManifestArtifact Manifest { get; init; } = Manifest ?? throw new System.ArgumentNullException(nameof(Manifest));
    public string Digest { get; init; } = Digest ?? throw new System.ArgumentNullException(nameof(Digest));
    public ImmutableArray<CompilerCallablePreparation> Callables { get; init; } = Callables.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Callables)) : Callables;
}
