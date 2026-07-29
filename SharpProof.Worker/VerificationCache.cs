namespace SharpProof.Worker;

internal sealed class VerificationCache(string directory, long maximumBytes)
{
    private readonly string _directory = Path.GetFullPath(
        directory ?? throw new ArgumentNullException(nameof(directory)));
    private readonly long _maximumBytes = maximumBytes > 0 ? maximumBytes :
        throw new ArgumentOutOfRangeException(nameof(maximumBytes));

    internal async Task<WorkerVerifyResponse?> TryReadAsync(string inputHash,
        WorkerClaimManifest manifest, WorkerBudgets budgets, CancellationToken cancellationToken)
    {
        var path = GetPath(inputHash);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<CacheEnvelope>(json, WorkerProtocolJson.Options);
            if (envelope == null || envelope.SchemaVersion != WorkerCacheVersions.Current ||
                !string.Equals(envelope.InputHash, inputHash, StringComparison.Ordinal) ||
                string.IsNullOrEmpty(envelope.Payload) ||
                !string.Equals(envelope.PayloadHash, HashText(envelope.Payload), StringComparison.Ordinal))
            {
                return null;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var payload = JsonSerializer.Deserialize<CachePayload>(envelope.Payload, WorkerProtocolJson.Options);
            if (payload == null ||
                !string.Equals(payload.ManifestHash, manifest.Hash, StringComparison.Ordinal) ||
                payload.CallableResults is not { } callables || callables.Any(static result => result == null) ||
                payload.ClaimResults is not { } claims || claims.Any(static result => result == null))
            {
                return null;
            }

            var response = WorkerResultAssembler.Create(inputHash, manifest,
                WorkerRunStatus.Complete, WorkerRunFailureReason.None, callables,
                claims, budgets, WorkerCacheStatus.Hit, 0);
            if (!IsCacheable(response, inputHash, manifest))
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            return response;
        }
        catch (Exception exception) when (exception is
            JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal async Task<bool> TryWriteAsync(WorkerVerifyResponse response, string inputHash,
        WorkerClaimManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        try
        {
            Directory.CreateDirectory(_directory);
            var payload = JsonSerializer.Serialize(new CachePayload(
                manifest.Hash, response.CallableResults, response.ClaimResults), WorkerProtocolJson.Options);
            var envelope = new CacheEnvelope(WorkerCacheVersions.Current,
                inputHash, HashText(payload), payload);
            var json = JsonSerializer.Serialize(envelope, WorkerProtocolJson.Options);
            var path = GetPath(inputHash);
            await AtomicFile.WriteUtf8Async(path, json, cancellationToken).ConfigureAwait(false);
            Evict(cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cache failures never change semantic verifier outcomes.
            return false;
        }
    }

    private void Evict(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = new DirectoryInfo(_directory)
            .EnumerateFiles("*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file.LastWriteTimeUtc)
            .ThenBy(static file => file.Name, StringComparer.Ordinal)
            .ToArray();
        var total = files.Sum(static file => file.Length);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (total <= _maximumBytes)
            {
                break;
            }

            var length = file.Length;
            try
            {
                file.Delete();
                total -= length;
            }
            catch (Exception exception) when (exception is
                IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private string GetPath(string inputHash)
    {
        if (!WorkerProtocolJson.IsSha256(inputHash))
        {
            throw new ArgumentException("A SHA-256 input hash is required.", nameof(inputHash));
        }

        return Path.Combine(_directory, inputHash + ".json");
    }

    private static string HashText(string value)
    {
        return WorkerProtocolJson.ComputeSha256(Encoding.UTF8.GetBytes(value));
    }

    internal static bool IsCacheable(WorkerVerifyResponse? response, string expectedInputHash,
        WorkerClaimManifest expectedManifest)
    {
        return expectedManifest != null && WorkerProtocolJson.IsSha256(expectedInputHash) && response is
        {
            RunStatus: WorkerRunStatus.Complete,
            Errors.Length: 0,
            CallableResults: { } callables,
            ClaimResults: { } claims
        } &&
        callables.All(static result =>
            result != null && result.Coverage == WorkerCallableCoverage.Complete &&
            result.Reason == WorkerCallableCoverageReason.None) &&
        claims.All(static result => result != null && result.Outcome is
            WorkerClaimOutcome.Proven or WorkerClaimOutcome.Refuted) &&
        WorkerProtocolJson.Validate(response, expectedInputHash, expectedManifest).IsValid;
    }

    private sealed record CacheEnvelope(
        int SchemaVersion, string InputHash, string PayloadHash, string Payload);
    private sealed record CachePayload(string ManifestHash,
        WorkerCallableResult[] CallableResults, WorkerClaimResult[] ClaimResults);
}
