namespace SharpProof.Worker;

internal sealed record WorkerInputSnapshot(
    CompilerManifestArtifact CompilerManifest, string InputHash) {
    internal const string ManifestUnavailable = "The compiler manifest is unavailable.";
    internal const string ManifestInvalid = "The compiler manifest is invalid.";
    internal static async Task<WorkerInputSnapshot> LoadAsync(WorkerVerifyRequest request,
        WorkerCacheIdentity cacheIdentity, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(cacheIdentity);
        var manifestPath = Path.GetFullPath(request.CompilerManifest.Path);
        byte[] manifestBytes;
        try { manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
            throw new IOException(ManifestUnavailable, exception);
        }
        var digest = WorkerProtocolJson.ComputeSha256(manifestBytes);
        CompilerManifestArtifact manifest;
        try {
            if (digest != request.CompilerManifest.Sha256) throw new InvalidDataException();
            manifest = CompilerManifestArtifactJson.Deserialize(DecodeUtf8(manifestBytes));
        }
        catch (Exception exception) when (exception is
            JsonException or InvalidDataException or DecoderFallbackException) {
            throw new IOException(ManifestInvalid, exception);
        }
        var inputHash = CompilerArtifactInputHash.Compute(request, manifestBytes, cacheIdentity.ToolIdentity,
            cacheIdentity.ToolVersion, cacheIdentity.WorkerBinarySha256, cacheIdentity.ApiSpecIdentity,
            cacheIdentity.ApiSpecVersion, cacheIdentity.ApiSpecContentSha256);
        return new WorkerInputSnapshot(manifest, inputHash);
    }
    private static string DecodeUtf8(byte[] bytes) {
        var offset = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
        return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
    }
}

internal sealed class WorkerCacheIdentity(
    string toolIdentity, string toolVersion, string workerBinarySha256,
    string apiSpecIdentity, string apiSpecVersion, string apiSpecContentSha256) {
    internal const string CurrentToolIdentity = "SharpProof.Worker";
    internal static WorkerCacheIdentity Current { get; } = new(
        CurrentToolIdentity, ReadToolVersion(),
        WorkerBinaryIdentity.ComputeSha256(typeof(SharpProofWorker).Assembly.Location),
        ApiSpecTable.DefaultTableIdentity, ApiSpecTable.DefaultTableVersion,
        ApiSpecTable.Default.ContentSha256);
    internal string ToolIdentity { get; } = Required(toolIdentity, nameof(toolIdentity));
    internal string ToolVersion { get; } = Required(toolVersion, nameof(toolVersion));
    internal string WorkerBinarySha256 { get; } = RequiredSha256(workerBinarySha256, nameof(workerBinarySha256));
    internal string ApiSpecIdentity { get; } = Required(apiSpecIdentity, nameof(apiSpecIdentity));
    internal string ApiSpecVersion { get; } = Required(apiSpecVersion, nameof(apiSpecVersion));
    internal string ApiSpecContentSha256 { get; } = RequiredSha256(apiSpecContentSha256, nameof(apiSpecContentSha256));
    private static string ReadToolVersion() =>
        typeof(SharpProofWorker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            throw new InvalidOperationException("The worker tool version is unavailable.");
    private static string Required(string value, string parameterName) =>
        !string.IsNullOrWhiteSpace(value) ? value :
        throw new ArgumentException("Cache identity values cannot be blank.", parameterName);
    private static string RequiredSha256(string value, string parameterName) =>
        WorkerProtocolJson.IsSha256(value) ? value :
        throw new ArgumentException("Cache identity digests must be SHA-256 values.", parameterName);
}
