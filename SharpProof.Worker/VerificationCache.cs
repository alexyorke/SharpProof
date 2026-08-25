namespace SharpProof.Worker;

internal sealed partial class VerificationCache(string directory, long maximumBytes)
{
    private const int CacheLockRetryMilliseconds = 25;
    private static readonly TimeSpan CacheLockWait = TimeSpan.FromSeconds(1);
    private readonly string _directory = Path.GetFullPath(
        ArgumentNullGuard.NotNull(directory, nameof(directory)));
    private readonly long _maximumBytes = ArgumentNullGuard.RequirePositive(
        maximumBytes, nameof(maximumBytes));
    internal static Action<string, string>? PathValidationOverride;
    internal static Action? TransactionRollbackOverride;
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
        FileStream? cacheLock = null;
        try
        {
            cacheLock = await AcquireLockAsync(
                    _directory,
                    cancellationToken)
                .ConfigureAwait(false);
            RecoverTransactionDebris(cancellationToken);
            ValidatePath(path);
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                committed = true;
                return null;
            }
            if (file.Length > Math.Min(
                    _maximumBytes,
                    WorkerProtocolJson.MaximumJsonBytes))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidatePath(path);
                file.Delete();
                DiscardStaged(staged);
                committed = true;
                return null;
            }
            if (!TryStageCapacity(path, staged, cancellationToken))
            {
                RestoreStaged(staged);
                return null;
            }
            var json = await WorkerProtocolJson.ReadUtf8FileAsync(path, cancellationToken)
                .ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<CacheEnvelope>(json, WorkerProtocolJson.Options);
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
                DiscardStaged(staged);
                committed = true;
                return null;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var payload = JsonSerializer.Deserialize<CachePayload>(envelope.Payload, WorkerProtocolJson.Options);
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
                DiscardStaged(staged);
                committed = true;
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
                DiscardStaged(staged);
                committed = true;
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            ValidatePath(path);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            committed = true;
            DiscardStaged(staged);
            return response;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LastReadUnavailable = true;
            return null;
        }
        catch (Exception exception) when (exception is
            ArgumentException or JsonException or InvalidDataException)
        {
            DiscardStaged(staged);
            committed = true;
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
                    await cacheLock.DisposeAsync().ConfigureAwait(false);
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
        FileStream? cacheLock = null;
        try
        {
            cacheLock = await AcquireLockAsync(
                    _directory,
                    cancellationToken)
                .ConfigureAwait(false);
            RecoverTransactionDebris(cancellationToken);
            var payload = JsonSerializer.Serialize(new CachePayload(
                manifest.Hash, response.CallableResults, response.ClaimResults), WorkerProtocolJson.Options);
            var envelope = new CacheEnvelope(WorkerCacheVersions.Current,
                inputHash, HashText(payload), payload);
            var json = JsonSerializer.Serialize(envelope, WorkerProtocolJson.Options);
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
                    await cacheLock.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private static FileStream AcquireLock(string directory)
    {
        var lockPath = Path.Combine(directory, ".sharp-proof-cache.lock");
        ValidatePath(directory, lockPath);
        Directory.CreateDirectory(directory);
        ValidatePath(directory, lockPath);
        var cacheLock = new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        var ownershipTransferred = false;
        try
        {
            ValidatePath(directory, lockPath);
            ownershipTransferred = true;
            return cacheLock;
        }
        finally
        {
            if (!ownershipTransferred)
            {
                cacheLock.Dispose();
            }
        }
    }

    private static async Task<FileStream> AcquireLockAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            try
            {
                return AcquireLock(directory);
            }
            catch (IOException) when (
                Stopwatch.GetElapsedTime(started) < CacheLockWait)
            {
                await Task.Delay(
                        CacheLockRetryMilliseconds,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private void RecoverTransactionDebris(CancellationToken cancellationToken)
    {
        foreach (var debrisPath in new DirectoryInfo(_directory)
                     .EnumerateFiles()
                     .Where(static file =>
                         file.Name.EndsWith(".rollback", StringComparison.Ordinal) ||
                         file.Name.EndsWith(".eviction", StringComparison.Ordinal)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var suffix = debrisPath.Name.EndsWith(
                ".rollback", StringComparison.Ordinal)
                ? ".rollback"
                : ".eviction";
            var separator = debrisPath.Name.LastIndexOf(
                '.', debrisPath.Name.Length - suffix.Length - 1);
            if (separator <= 0 ||
                !IsOwnedCacheEntry(debrisPath.Name[..separator]))
            {
                continue;
            }

            var originalPath = Path.Combine(
                _directory,
                debrisPath.Name[..separator]);
            ValidatePath(debrisPath.FullName);
            ValidatePath(originalPath);
            if (File.Exists(originalPath))
            {
                File.Delete(debrisPath.FullName);
            }
            else
            {
                File.Move(debrisPath.FullName, originalPath);
            }
        }
    }

    private bool TryStageCapacity(
        string protectedPath,
        List<StagedEntry> staged,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = new List<FileInfo>();
        foreach (var file in new DirectoryInfo(_directory)
                     .EnumerateFiles("*.sharp-proof-cache.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsOwnedCacheEntry(file.Name))
            {
                files.Add(file);
            }
        }
        files.Sort(static (left, right) =>
        {
            var result = left.LastWriteTimeUtc.CompareTo(right.LastWriteTimeUtc);
            return result != 0
                ? result
                : StringComparer.Ordinal.Compare(left.Name, right.Name);
        });
        long total = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidatePath(file.FullName);
            checked
            {
                total += file.Length;
            }
        }
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (total <= _maximumBytes)
            {
                break;
            }
            if (string.Equals(
                    file.FullName,
                    protectedPath,
                    StringComparison.Ordinal))
            {
                continue;
            }

            ValidatePath(file.FullName);
            var length = file.Length;
            var stagedPath = file.FullName + "." +
                Guid.NewGuid().ToString("N") + ".eviction";
            ValidatePath(stagedPath);
            File.Move(file.FullName, stagedPath);
            staged.Add(new StagedEntry(file.FullName, stagedPath));
            total -= length;
        }

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

    private static void RestoreStaged(List<StagedEntry> staged)
    {
        for (var index = staged.Count - 1; index >= 0; index--)
        {
            var entry = staged[index];
            try
            {
                if (File.Exists(entry.StagedPath) &&
                    !File.Exists(entry.OriginalPath))
                {
                    File.Move(entry.StagedPath, entry.OriginalPath);
                }
            }
            catch (Exception exception) when (exception is
                ArgumentException or IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void RestorePrevious(string? path, string? previousPath)
    {
        if (path == null || previousPath == null)
        {
            return;
        }
        try
        {
            if (File.Exists(previousPath) && !File.Exists(path))
            {
                File.Move(previousPath, path);
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
        const string suffix = ".sharp-proof-cache.json";
        return fileName.Length == 64 + suffix.Length &&
            fileName.EndsWith(suffix, StringComparison.Ordinal) &&
            fileName.Take(64).All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
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

        return Path.Combine(_directory, inputHash + ".sharp-proof-cache.json");
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
        WorkerProtocolJson.Validate(response, expectedInputHash, expectedManifest).IsValid &&
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
            StringComparer.Ordinal);
        var claimById = manifest.Claims.ToDictionary(
            static claim => claim.ClaimId,
            StringComparer.Ordinal);
        foreach (var claim in claims)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!claimById.TryGetValue(claim.ClaimId, out var declaration) ||
                declaration.Kind != WorkerClaimKind.Postcondition ||
                !targetByCallable.TryGetValue(declaration.CallableId, out var target) ||
                !TryCreateModel(target, claim.Model, out var model))
            {
                return false;
            }

            var postconditions = target.Clauses.Where(static clause =>
                clause.Kind == CompilerContractKind.Ensures).ToArray();
            var ordinal = Array.FindIndex(
                postconditions,
                clause => clause.ClaimId == claim.ClaimId);
            if (ordinal < 0 ||
                !EntryAssumptionsHold(target, model, cancellationToken) ||
                CallableCounterexampleReplayer.Replay(
                    target,
                    ordinal,
                    model,
                    cancellationToken) != WorkerClaimReason.None)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryCreateModel(
        CompilerCallablePreparation target,
        WorkerModelValue[] rows,
        out ImmutableDictionary<IrVarId, IrValue> model)
    {
        model = ImmutableDictionary<IrVarId, IrValue>.Empty;
        if (rows == null)
        {
            return false;
        }

        var variables = target.Variables.ToDictionary(
            static variable => variable.ModelLabel,
            StringComparer.Ordinal);
        var result = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
        foreach (var row in rows)
        {
            if (row == null ||
                !variables.TryGetValue(row.Variable, out var variable) ||
                !TryCreateValue(target.Factory, variable, row, out var value) ||
                !result.TryAdd(variable.Variable, value))
            {
                return false;
            }
        }

        foreach (var variable in target.Variables.Where(static variable =>
                     variable.Role is CompilerVariableRole.Receiver or
                         CompilerVariableRole.Parameter))
        {
            var type = target.Factory.GetVariableInfo(variable.Variable).Type;
            if ((type != target.Factory.BooleanType &&
                 type != target.Factory.IntegerType) ||
                !result.ContainsKey(variable.Variable))
            {
                return false;
            }
        }

        model = result.ToImmutable();
        return true;
    }

    private static bool TryCreateValue(
        IrFactory factory,
        CompilerCanonicalVariable variable,
        WorkerModelValue row,
        out IrValue value)
    {
        var type = factory.GetVariableInfo(variable.Variable).Type;
        if (type == factory.BooleanType &&
            row is { Kind: nameof(IrValueKind.Boolean), Value: "true" or "false" })
        {
            value = factory.CreateBooleanValue(row.Value == "true");
            return true;
        }

        if (type == factory.IntegerType &&
            row is { Kind: nameof(IrValueKind.Integer) } &&
            long.TryParse(
                row.Value,
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var integer) &&
            row.Value == integer.ToString(CultureInfo.InvariantCulture) &&
            (variable.SourceIntegerInterval is not { } interval ||
             integer >= interval.Minimum &&
             integer <= interval.Maximum))
        {
            value = factory.CreateIntegerValue(integer);
            return true;
        }

        value = null!;
        return false;
    }

    private static bool EntryAssumptionsHold(
        CompilerCallablePreparation target,
        ImmutableDictionary<IrVarId, IrValue> model,
        CancellationToken cancellationToken)
    {
        var interpreter = new IrInterpreter(target.Factory);
        foreach (var clause in target.Clauses.Where(static clause =>
                     clause.Kind is CompilerContractKind.Requires or
                         CompilerContractKind.Assume))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evaluated = interpreter.Evaluate(
                clause.Condition,
                model,
                cancellationToken);
            if (evaluated.Status != IrEvaluationStatus.Value ||
                evaluated.Value is not { Kind: IrValueKind.Boolean, Boolean: true })
            {
                return false;
            }
        }

        return true;
    }

}
