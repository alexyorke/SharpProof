namespace SharpProof.Worker;

// A content-addressed store of complete worker responses. The key is the
// input hash, which already binds the compiler artifact bytes, worker binary,
// API-spec table, protocol version, and budgets, so an entry can be reused
// verbatim. Writes are atomic renames; eviction is oldest-first. Cache
// failures never change semantic outcomes: they only turn into misses.
internal sealed partial class VerificationCache(string directory, long maximumBytes)
{
    internal const string CacheFileSuffix = ".sharp-proof-cache.json";
    private readonly string _directory = Path.GetFullPath(
        ArgumentNullGuard.NotNull(directory, nameof(directory)));
    private readonly long _maximumBytes = ArgumentNullGuard.RequirePositive(
        maximumBytes, nameof(maximumBytes));

    // Set by the most recent read so the worker can distinguish an
    // operational cache failure from an ordinary miss.
    internal bool LastReadUnavailable { get; private set; }

    internal async Task<WorkerVerifyResponse?> TryReadAsync(
        string inputHash,
        WorkerClaimManifest manifest,
        WorkerBudgets budgets,
        CancellationToken cancellationToken)
    {
        LastReadUnavailable = false;
        var path = GetPath(inputHash);
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return null;
            }
            if (file.Length > Math.Min(_maximumBytes, WorkerProtocolJson.MaximumJsonBytes))
            {
                TryDelete(path);
                return null;
            }

            var json = await WorkerProtocolJson.ReadUtf8FileAsync(path, cancellationToken)
                .ConfigureAwait(false);
            var entry = JsonSerializer.Deserialize<CacheEntry>(json, WorkerProtocolJson.SharedOptions);
            if (entry is not
                {
                    SchemaVersion: WorkerCacheVersions.Current,
                    CallableResults: { } callables,
                    ClaimResults: { } claims
                } ||
                !string.Equals(entry.InputHash, inputHash, StringComparison.Ordinal) ||
                !string.Equals(entry.ManifestHash, manifest.Hash, StringComparison.Ordinal) ||
                callables.Any(static result => result == null) ||
                claims.Any(static result => result == null))
            {
                TryDelete(path);
                return null;
            }

            var response = WorkerResultAssembler.Create(inputHash, manifest,
                WorkerRunStatus.Complete, WorkerRunFailureReason.None, callables,
                claims, budgets, WorkerCacheStatus.Hit, 0);
            if (!IsCacheable(response, inputHash, manifest))
            {
                TryDelete(path);
                return null;
            }

            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            return response;
        }
        catch (JsonException)
        {
            TryDelete(path);
            return null;
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException or
            InvalidDataException or OverflowException)
        {
            LastReadUnavailable = true;
            return null;
        }
    }

    internal async Task<bool> TryWriteAsync(
        WorkerVerifyResponse response,
        string inputHash,
        WorkerClaimManifest manifest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        try
        {
            var json = JsonSerializer.Serialize(
                new CacheEntry(
                    WorkerCacheVersions.Current,
                    inputHash,
                    manifest.Hash,
                    response.CallableResults,
                    response.ClaimResults),
                WorkerProtocolJson.SharedOptions);
            if (Encoding.UTF8.GetByteCount(json) >
                Math.Min(_maximumBytes, WorkerProtocolJson.MaximumJsonBytes))
            {
                return false;
            }

            Directory.CreateDirectory(_directory);
            var path = GetPath(inputHash);
            await AtomicFile.WriteUtf8Async(path, json, cancellationToken).ConfigureAwait(false);
            EvictOldest(path);
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException or
            OverflowException)
        {
            return false;
        }
    }

    // The full key includes the artifact, runtime identity, and budgets.
    // Every valid complete response is reusable, including semantic Unknown
    // outcomes and effects. Failed, timed-out, and canceled runs are not.
    internal static bool IsCacheable(
        WorkerVerifyResponse? response,
        string expectedInputHash,
        WorkerClaimManifest expectedManifest)
    {
        return WorkerProtocolJson.IsSha256(expectedInputHash) && response is
        {
            RunStatus: WorkerRunStatus.Complete,
            FailureReason: WorkerRunFailureReason.None,
            Errors.Length: 0,
            CallableResults: not null,
            ClaimResults: not null
        } &&
        expectedManifest != null &&
        WorkerProtocolJson.ValidateKnownInputHash(
            response,
            expectedInputHash,
            expectedManifest).IsValid;
    }

    private void EvictOldest(string protectedPath)
    {
        var entries = new DirectoryInfo(_directory)
            .EnumerateFiles("*" + CacheFileSuffix, SearchOption.TopDirectoryOnly)
            .OrderBy(static file => file.LastWriteTimeUtc)
            .ThenBy(static file => file.Name, StringComparer.Ordinal)
            .ToList();
        var total = entries.Sum(static file => file.Length);
        foreach (var file in entries)
        {
            if (total <= _maximumBytes)
            {
                return;
            }
            if (string.Equals(file.FullName, protectedPath, StringComparison.Ordinal))
            {
                continue;
            }
            total -= file.Length;
            TryDelete(file.FullName);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException)
        {
        }
    }

    private string GetPath(string inputHash)
    {
        if (!WorkerProtocolJson.IsSha256(inputHash))
        {
            throw new ArgumentException("A SHA-256 input hash is required.", nameof(inputHash));
        }

        return Path.Combine(_directory, inputHash + CacheFileSuffix);
    }
}
