using System.Collections.Concurrent;

namespace SharpProof.Worker;

internal sealed partial class VerificationCache(string directory, long maximumBytes)
{
    internal const string CacheFileSuffix = ".sharp-proof-cache.json";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim>
        ProcessLocks = new(StringComparer.Ordinal);
    private readonly string _directory = Path.GetFullPath(
        ArgumentNullGuard.NotNull(directory, nameof(directory)));
    private readonly long _maximumBytes = ArgumentNullGuard.RequirePositive(
        maximumBytes, nameof(maximumBytes));
    private static readonly Comparer<(
        DateTime LastWriteTimeUtc,
        string Name)> CapacityPriorityComparer = Comparer<(
            DateTime LastWriteTimeUtc,
            string Name)>.Create(static (left, right) =>
            {
                var timeComparison = left.LastWriteTimeUtc.CompareTo(
                    right.LastWriteTimeUtc);
                return timeComparison != 0
                    ? timeComparison
                    : StringComparer.Ordinal.Compare(left.Name, right.Name);
            });
    private static readonly string[] TransactionSuffixes =
        [".rollback", ".eviction"];
    private const string CacheFilePattern = "*" + CacheFileSuffix;
    internal static Action<string, string>? PathValidationOverride;
    internal static Action? TransactionRollbackOverride;
    // Set for the most recent read so the worker can distinguish an
    // operational cache failure from an ordinary miss. Each cache instance
    // is scoped to one worker request.
    internal bool LastReadUnavailable { get; private set; }

    internal async Task<WorkerVerifyResponse?> TryReadAsync(
        string inputHash,
        WorkerClaimManifest manifest,
        ImmutableArray<CompilerCallablePreparation> targets,
        WorkerBudgets budgets,
        CancellationToken cancellationToken)
    {
        LastReadUnavailable = false;
        var path = GetPath(inputHash);
        var staged = new List<StagedEntry>();
        var committed = false;
        CacheLock? cacheLock = null;
        try
        {
            cacheLock = AcquireLock(_directory);
            RecoverInterruptedTransactions(cancellationToken);
            ValidatePath(path);
            if (!File.Exists(path))
            {
                if (TryStageCapacity(path, staged, cancellationToken))
                {
                    CommitStaged(staged, ref committed);
                }
                return null;
            }
            var file = new FileInfo(path);
            if (file.Length > Math.Min(
                    _maximumBytes,
                    WorkerProtocolJson.MaximumJsonBytes))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidatePath(path);
                file.Delete();
                return null;
            }
            var json = await WorkerProtocolJson.ReadUtf8FileAsync(path, cancellationToken)
                .ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<CacheEnvelope>(json, WorkerProtocolJson.SharedOptions);
            if (envelope is not
                {
                    SchemaVersion: WorkerCacheVersions.Current,
                    InputHash: var envelopeInputHash,
                    Payload: { Length: > 0 } envelopePayload,
                    PayloadHash: var payloadHash
                } ||
                !string.Equals(envelopeInputHash, inputHash, StringComparison.Ordinal) ||
                !string.Equals(payloadHash, HashText(envelopePayload), StringComparison.Ordinal))
            {
                return null;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var payload = JsonSerializer.Deserialize<CachePayload>(envelope.Payload, WorkerProtocolJson.SharedOptions);
            if (payload is not
                {
                    ManifestHash: var payloadManifestHash,
                    CallableResults: { } callables,
                    ClaimResults: { } claims
                } ||
                !string.Equals(payloadManifestHash, manifest.Hash, StringComparison.Ordinal) ||
                callables.Any(static result => result == null) ||
                claims.Any(static result => result == null))
            {
                return null;
            }

            var response = WorkerResultAssembler.Create(inputHash, manifest,
                WorkerRunStatus.Complete, WorkerRunFailureReason.None, callables,
                claims, budgets, WorkerCacheStatus.Hit, 0);
            if (!IsCacheable(
                    response,
                    inputHash,
                    manifest,
                    targets,
                    cancellationToken))
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!TryStageCapacity(path, staged, cancellationToken))
            {
                return null;
            }
            ValidatePath(path);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            committed = true;
            DiscardStaged(staged);
            return response;
        }
        catch (Exception exception) when (exception is
            ArgumentException or JsonException or IOException or InvalidDataException or
                UnauthorizedAccessException or OverflowException)
        {
            // A miss is still a cache maintenance opportunity. In
            // particular, a cache opened with a newly reduced limit must not
            // retain stale entries merely because the requested key is absent
            // or malformed.
            try
            {
                if (TryStageCapacity(path, staged, cancellationToken))
                {
                    CommitStaged(staged, ref committed);
                }
            }
            catch (Exception maintenanceException) when (maintenanceException is
                ArgumentException or IOException or UnauthorizedAccessException or
                OverflowException)
            {
            }
            LastReadUnavailable = true;
            return null;
        }
        finally
        {
            try
            {
                if (!committed && staged.Count > 0)
                {
                    try
                    {
                        TransactionRollbackOverride?.Invoke();
                    }
                    finally
                    {
                        RestoreStaged(staged);
                    }
                }
            }
            finally
            {
                if (cacheLock != null)
                {
                    cacheLock.Dispose();
                }
            }
        }
    }

    internal async Task<bool> TryWriteAsync(WorkerVerifyResponse response, string inputHash,
        WorkerClaimManifest manifest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        var staged = new List<StagedEntry>();
        string? previousPath = null;
        string? path = null;
        var published = false;
        var committed = false;
        CacheLock? cacheLock = null;
        try
        {
            cacheLock = AcquireLock(_directory);
            RecoverInterruptedTransactions(cancellationToken);
            var payload = JsonSerializer.Serialize(new CachePayload(
                manifest.Hash, response.CallableResults, response.ClaimResults), WorkerProtocolJson.SharedOptions);
            var envelope = new CacheEnvelope(WorkerCacheVersions.Current,
                inputHash, HashText(payload), payload);
            var json = JsonSerializer.Serialize(envelope, WorkerProtocolJson.SharedOptions);
            if (Encoding.UTF8.GetByteCount(json) >
                Math.Min(_maximumBytes, WorkerProtocolJson.MaximumJsonBytes))
            {
                return false;
            }

            path = GetPath(inputHash);
            ValidatePath(path);
            if (File.Exists(path))
            {
                previousPath = path + "." +
                    Guid.NewGuid().ToString("N") + ".rollback";
                ValidatePath(previousPath);
                File.Move(path, previousPath);
            }
            await AtomicFile.WriteUtf8Async(path, json, cancellationToken).ConfigureAwait(false);
            published = true;
            ValidatePath(path);
            if (!TryStageCapacity(path, staged, cancellationToken))
            {
                return false;
            }
            cancellationToken.ThrowIfCancellationRequested();
            committed = true;
            DiscardStaged(staged);
            TryDeleteRollbackFile(previousPath);
            return true;
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException or
            OverflowException)
        {
            // Cache failures never change semantic verifier outcomes.
            return false;
        }
        finally
        {
            try
            {
                if (!committed)
                {
                    try
                    {
                        if (published ||
                            previousPath != null ||
                            staged.Count > 0)
                        {
                            TransactionRollbackOverride?.Invoke();
                        }
                    }
                    finally
                    {
                        if (published && path != null)
                        {
                            TryDeletePublishedFile(path);
                        }
                        RestoreStaged(staged);
                        RestorePrevious(path, previousPath);
                    }
                }
            }
            finally
            {
                if (cacheLock != null)
                {
                    cacheLock.Dispose();
                }
            }
        }
    }

    private static CacheLock AcquireLock(string directory)
    {
        var lockPath = Path.Combine(directory, ".sharp-proof-cache.lock");
        ValidatePath(directory, lockPath);
        Directory.CreateDirectory(directory);
        ValidatePath(directory, lockPath);
        var lockIdentity = HashText(Path.GetFullPath(directory));
        var processLock = ProcessLocks.GetOrAdd(
            lockIdentity,
            static _ => new SemaphoreSlim(1, 1));
        if (!processLock.Wait(0))
        {
            throw new IOException("The verification cache is locked.");
        }
        FileStream? cacheLock = null;
        var ownershipTransferred = false;
        try
        {
            cacheLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            ValidatePath(directory, lockPath);
            ownershipTransferred = true;
            return new CacheLock(processLock, cacheLock);
        }
        finally
        {
            if (!ownershipTransferred)
            {
                cacheLock?.Dispose();
                processLock.Release();
            }
        }
    }

    private sealed class CacheLock(SemaphoreSlim processLock, FileStream file)
    {
        public void Dispose()
        {
            file.Dispose();
            processLock.Release();
        }
    }

    private void RecoverInterruptedTransactions(CancellationToken cancellationToken)
    {
        foreach (var file in new DirectoryInfo(_directory).EnumerateFiles(
                     "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetOwnedTransactionOriginal(file.Name, out var originalName))
            {
                continue;
            }

            var originalPath = Path.Combine(_directory, originalName);
            ValidatePath(file.FullName);
            ValidatePath(originalPath);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(originalPath))
            {
                File.Delete(file.FullName);
            }
            else
            {
                File.Move(file.FullName, originalPath);
            }
        }
    }

    private static bool TryGetOwnedTransactionOriginal(
        string fileName,
        out string originalName)
    {
        originalName = string.Empty;
        foreach (var suffix in TransactionSuffixes)
        {
            if (!fileName.EndsWith(suffix, StringComparison.Ordinal))
            {
                continue;
            }

            var markerLength = 1 + 32 + suffix.Length;
            if (fileName.Length <= markerLength)
            {
                continue;
            }

            var markerStart = fileName.Length - markerLength;
            if (fileName[markerStart] != '.' ||
                !IsHexMarker(fileName, markerStart + 1))
            {
                continue;
            }

            var candidate = fileName[..markerStart];
            if (IsOwnedCacheEntry(candidate))
            {
                originalName = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool IsHexDigit(char value)
    {
        return value is >= '0' and <= '9' or >= 'a' and <= 'f';
    }

    private static bool IsHexMarker(string value, int start)
    {
        return IsHexRange(value, start, 32);
    }

    private static bool IsHexRange(string value, int start, int length)
    {
        for (var index = start; index < start + length; index++)
        {
            if (!IsHexDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryStageCapacity(
        string protectedPath,
        List<StagedEntry> staged,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = new PriorityQueue<
            (FileInfo File, long Length),
            (DateTime LastWriteTimeUtc, string Name)>(
                CapacityPriorityComparer);
        long total = 0;
        foreach (var file in new DirectoryInfo(_directory).EnumerateFiles(
                     CacheFilePattern,
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsOwnedCacheEntry(file.Name))
            {
                continue;
            }

            ValidatePath(file.FullName);
            cancellationToken.ThrowIfCancellationRequested();
            var priority = (file.LastWriteTimeUtc, file.Name);
            var length = file.Length;
            checked
            {
                total += length;
            }
            files.Enqueue((file, length), priority);
        }
        cancellationToken.ThrowIfCancellationRequested();
        while (total > _maximumBytes &&
               files.TryDequeue(out var entry, out _))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(
                    entry.File.FullName,
                    protectedPath,
                    StringComparison.Ordinal))
            {
                continue;
            }

            ValidatePath(entry.File.FullName);
            cancellationToken.ThrowIfCancellationRequested();
            var stagedPath = entry.File.FullName + "." +
                Guid.NewGuid().ToString("N") + ".eviction";
            ValidatePath(stagedPath);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(entry.File.FullName, stagedPath);
            staged.Add(new StagedEntry(entry.File.FullName, stagedPath));
            total -= entry.Length;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return total <= _maximumBytes;
    }

    private static void DiscardStaged(List<StagedEntry> staged)
    {
        foreach (var entry in staged)
        {
            TryDeleteRollbackFile(entry.StagedPath);
        }
        staged.Clear();
    }

    private static void CommitStaged(
        List<StagedEntry> staged,
        ref bool committed)
    {
        committed = true;
        DiscardStaged(staged);
    }

    private static void RestoreStaged(List<StagedEntry> staged)
    {
        for (var index = staged.Count - 1; index >= 0; index--)
        {
            var entry = staged[index];
            TryRestoreFile(entry.StagedPath, entry.OriginalPath);
        }
    }

    private static void RestorePrevious(string? path, string? previousPath)
    {
        if (path == null || previousPath == null)
        {
            return;
        }
        TryRestoreFile(previousPath, path);
    }

    private static void TryRestoreFile(string source, string destination)
    {
        try
        {
            if (File.Exists(source) && !File.Exists(destination))
            {
                File.Move(source, destination);
            }
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeletePublishedFile(string path)
    {
        try
        {
            SharpProof.Host.LinuxPathIdentity.Canonicalize(path);
            File.Delete(path);
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteRollbackFile(string? path)
    {
        if (path == null)
        {
            return;
        }
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record StagedEntry(string OriginalPath, string StagedPath);

    private static bool IsOwnedCacheEntry(string fileName)
    {
        return fileName.Length == 64 + CacheFileSuffix.Length &&
            fileName.EndsWith(CacheFileSuffix, StringComparison.Ordinal) &&
            IsHexRange(fileName, 0, 64);
    }

    private void ValidatePath(string path)
    {
        ValidatePath(_directory, path);
    }

    private static void ValidatePath(string directory, string path)
    {
        if (PathValidationOverride is { } validator)
        {
            validator(directory, path);
            return;
        }

        SharpProof.Host.LinuxPathIdentity.RequireLocalPath(directory);
        SharpProof.Host.LinuxPathIdentity.Canonicalize(path);
    }

    private string GetPath(string inputHash)
    {
        if (!WorkerProtocolJson.IsSha256(inputHash))
        {
            throw new ArgumentException("A SHA-256 input hash is required.", nameof(inputHash));
        }

        return Path.Combine(_directory, inputHash + CacheFileSuffix);
    }

    private static string HashText(string value)
    {
        return WorkerProtocolJson.ComputeSha256(Encoding.UTF8.GetBytes(value));
    }

    internal static bool IsCacheable(
        WorkerVerifyResponse? response,
        string expectedInputHash,
        WorkerClaimManifest expectedManifest,
        ImmutableArray<CompilerCallablePreparation> targets,
        CancellationToken cancellationToken = default)
    {
        return WorkerProtocolJson.IsSha256(expectedInputHash) && response is
        {
            RunStatus: WorkerRunStatus.Complete,
            Errors.Length: 0,
            CallableResults: { } callables,
            ClaimResults: { Length: > 0 } claims
        } &&
        callables.All(static result =>
            result is
            {
                Coverage: WorkerCallableCoverage.Complete,
                Reason: WorkerCallableCoverageReason.None
            }) &&
        claims.All(static result =>
            result is { Outcome: WorkerClaimOutcome.Refuted }) &&
        expectedManifest is { Claims: { Length: var claimCount } } &&
        claimCount == claims.Length &&
        expectedManifest.Claims.All(static claim =>
            claim.Kind == WorkerClaimKind.Postcondition) &&
        WorkerProtocolJson.ValidateKnownInputHash(
            response,
            expectedInputHash,
            expectedManifest).IsValid &&
        ReplayCachedClaims(claims, expectedManifest, targets, cancellationToken);
    }

    private static bool ReplayCachedClaims(
        WorkerClaimResult[] claims,
        WorkerClaimManifest manifest,
        ImmutableArray<CompilerCallablePreparation> targets,
        CancellationToken cancellationToken)
    {
        if (targets.IsDefault ||
            targets.Length != manifest.Callables.Length)
        {
            return false;
        }

        var targetByCallable = targets.ToDictionary(
            static target => target.Entry.CallableId,
            static target => CreateReplayTarget(target),
            StringComparer.Ordinal);
        var claimById = manifest.Claims.ToDictionary(
            static claim => claim.ClaimId,
            StringComparer.Ordinal);
        foreach (var claim in claims)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!claimById.TryGetValue(claim.ClaimId, out var declaration) ||
                declaration.Kind != WorkerClaimKind.Postcondition ||
                !targetByCallable.TryGetValue(
                    declaration.CallableId,
                    out var preparedTarget) ||
                !TryCreateModel(preparedTarget, claim.Model, out var model))
            {
                return false;
            }

            if (!preparedTarget.PostconditionOrdinals.TryGetValue(
                    claim.ClaimId,
                    out var ordinal) ||
                !CompilerModelValues.EntryAssumptionsHold(
                    preparedTarget.Target,
                    model,
                    cancellationToken) ||
                CallableCounterexampleReplayer.Replay(
                    preparedTarget.Target,
                    ordinal,
                    model,
                    preparedTarget.Postconditions,
                    cancellationToken: cancellationToken) != WorkerClaimReason.None)
            {
                return false;
            }
        }

        return true;
    }

    private static ReplayTarget CreateReplayTarget(
        CompilerCallablePreparation target)
    {
        var postconditions = target.Clauses.Where(static clause =>
            clause.Kind == CompilerContractKind.Ensures).ToArray();
        var variablesByLabel = target.Variables.ToDictionary(
            static variable => variable.ModelLabel,
            StringComparer.Ordinal);
        var requiredInputs = new HashSet<IrVarId>();
        foreach (var variable in target.Variables)
        {
            if (variable.Role is not
                (CompilerVariableRole.Receiver or CompilerVariableRole.Parameter))
            {
                continue;
            }

            var type = target.Factory.GetVariableInfo(variable.Variable).Type;
            if (type == target.Factory.BooleanType ||
                type == target.Factory.IntegerType)
            {
                requiredInputs.Add(variable.Variable);
            }
        }

        var postconditionOrdinals = new Dictionary<string, int>(
            StringComparer.Ordinal);
        for (var index = 0; index < postconditions.Length; index++)
        {
            if (postconditions[index].ClaimId is { } claimId &&
                !postconditionOrdinals.ContainsKey(claimId))
            {
                postconditionOrdinals.Add(claimId, index);
            }
        }

        return new ReplayTarget(
            target,
            postconditions,
            variablesByLabel,
            requiredInputs,
            postconditionOrdinals);
    }

    private static bool TryCreateModel(
        ReplayTarget preparedTarget,
        WorkerModelValue[] rows,
        out ImmutableDictionary<IrVarId, IrValue> model)
    {
        model = ImmutableDictionary<IrVarId, IrValue>.Empty;
        if (rows == null)
        {
            return false;
        }

        var result = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
        foreach (var row in rows)
        {
            if (row == null ||
                !preparedTarget.VariablesByLabel.TryGetValue(
                    row.Variable,
                    out var variable) ||
                !CompilerModelValues.TryCreateValue(
                    preparedTarget.Target.Factory,
                    variable,
                    row,
                    out var value) ||
                !result.TryAdd(variable.Variable, value))
            {
                return false;
            }
        }

        foreach (var variable in preparedTarget.RequiredInputs)
        {
            // Replay models intentionally contain only values needed by the
            // counterexample. Non-scalar inputs cannot be materialized by the
            // scalar model codec, but that is harmless when the replay does
            // not reference them. Scalar inputs remain mandatory so missing
            // values cannot be mistaken for a concrete execution.
            if (!result.ContainsKey(variable))
            {
                return false;
            }
        }

        model = result.ToImmutable();
        return true;
    }

    private sealed record ReplayTarget(
        CompilerCallablePreparation Target,
        CompilerPreparedClause[] Postconditions,
        Dictionary<string, CompilerCanonicalVariable> VariablesByLabel,
        HashSet<IrVarId> RequiredInputs,
        Dictionary<string, int> PostconditionOrdinals);


}
