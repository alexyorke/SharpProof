using System.Collections.Immutable;
namespace SharpProof.Worker;

// A content-addressed store of complete worker responses. The key is the
// input hash, which already binds the compiler artifact bytes, worker binary,
// API-spec table, protocol version, and budgets, so an entry can be reused
// verbatim. Writes are atomic renames; eviction is oldest-first. Cache
// failures never change semantic outcomes: they only turn into misses.
internal sealed partial class VerificationCache(string directory, long maximumBytes, Action<string>? deleteFile = null)
{
    internal const string CacheFileSuffix = ".sharp-proof-cache.json";
    private readonly string _directory = Path.GetFullPath(
        ArgumentNullGuard.NotNull(directory, nameof(directory)));
    private readonly long _maximumBytes = ArgumentNullGuard.RequirePositive(
        maximumBytes, nameof(maximumBytes));
    private readonly Action<string> _deleteFile = deleteFile ?? File.Delete;

    // Set by the most recent read so the worker can distinguish an
    // operational cache failure from an ordinary miss.
    internal bool LastReadUnavailable { get; private set; }

    internal async Task<WorkerVerifyResponse?> TryReadAsync(
        string inputHash,
        WorkerClaimManifest manifest,
        WorkerBudgets budgets,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(budgets);
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
                callables.IsDefault || claims.IsDefault ||
                !string.Equals(entry.InputHash, inputHash, StringComparison.Ordinal) ||
                !string.Equals(entry.ManifestHash, manifest.Hash, StringComparison.Ordinal) ||
                callables.Any(static (WorkerCallableResult result) => result is null) ||
                claims.Any(static (WorkerClaimResult result) => result is null))
            {
                TryDelete(path);
                return null;
            }

            var response = WorkerResultAssembler.CreateCached(
                inputHash, manifest, callables, claims, budgets);
            if (!IsCacheable(response, inputHash, manifest))
            {
                TryDelete(path);
                return null;
            }

            // Validate stored evidence before normalization can repair malformed fields.
            response = WorkerProtocolJson.CanonicalizeCopy(response);
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
        System.ArgumentNullException.ThrowIfNull(inputHash);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(manifest);
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
            return EvictOldest(path);
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException or
            OverflowException)
        {
            return false;
        }
    }

    // The key includes the artifact, worker and Z3 identities, and budgets.
    // It does not bind CoreLib or effective resource-key mode. Synchronization
    // witnesses certified from that runtime context therefore stay uncacheable.
    // Other complete responses, including semantic Unknown, remain reusable.
    internal static bool IsCacheable(
        WorkerVerifyResponse? response,
        string expectedInputHash,
        WorkerClaimManifest expectedManifest)
    {
        return WorkerProtocolJson.IsSha256(expectedInputHash) && response is not null && !response.Errors.IsDefault && response is
        {
            RunStatus: WorkerRunStatus.Complete,
            FailureReason: WorkerRunFailureReason.None,
            Errors.Length: 0,
            CallableResults.IsDefault: false,
            ClaimResults.IsDefault: false
        } &&
        expectedManifest != null &&
        WorkerProtocolJson.ValidateKnownInputHash(
            response,
            expectedInputHash,
            expectedManifest).IsValid &&
        response.ClaimResults.All(claim => claim is null ? throw new ArgumentNullException(nameof(claim)) :
            claim.EffectWitness is not { Kind: "synchronization-lock", Detail: { } detail } ||
                !detail.StartsWith(IrOpaqueCallSite.ExceptionResourceLockPrefix, StringComparison.Ordinal));
    }

    private bool EvictOldest(string protectedPath)
    {
        System.ArgumentNullException.ThrowIfNull(protectedPath);
        var entries = new DirectoryInfo(_directory)
            .EnumerateFiles("*" + CacheFileSuffix, SearchOption.TopDirectoryOnly)
            .Where(static file => file is not null && IsCacheFileName(file.Name))
            .OrderBy(static file => file is null ? throw new System.ArgumentNullException(nameof(file)) : (file.LastWriteTimeUtc))
            .ThenBy(static file => file is null ? throw new System.ArgumentNullException(nameof(file)) : (file.Name), StringComparer.Ordinal)
            .ToImmutableArray();
        var total = entries.Sum(static file => file is null ? throw new System.ArgumentNullException(nameof(file)) : (file.Length));
        foreach (var file in entries)
        {
            if (total <= _maximumBytes)
            {
                return true;
            }
            if (string.Equals(file.FullName, protectedPath, StringComparison.Ordinal))
            {
                continue;
            }
            var length = file.Length;
            if (TryDelete(file.FullName))
            {
                total -= length;
            }
        }
        return total <= _maximumBytes;
    }

    private static bool IsCacheFileName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Length == 64 + CacheFileSuffix.Length &&
            name.EndsWith(CacheFileSuffix, StringComparison.Ordinal) &&
            WorkerProtocolJson.IsSha256(name.Substring(0, 64));
    }

    private bool TryDelete(string path)
    {
        System.ArgumentNullException.ThrowIfNull(path);
        try
        {
            _deleteFile(path);
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
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
