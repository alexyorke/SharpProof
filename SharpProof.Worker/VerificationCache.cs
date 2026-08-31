namespace SharpProof.Worker;

internal sealed partial class VerificationCache(string directory, long maximumBytes)
{
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
    internal static Action<string, string>? PathValidationOverride;
    internal static Action? TransactionRollbackOverride;

    internal async Task<WorkerVerifyResponse?> TryReadAsync(
        string inputHash,
        WorkerClaimManifest manifest,
        ImmutableArray<CompilerCallablePreparation> targets,
        WorkerBudgets budgets,
        CancellationToken cancellationToken)
    {
        var path = GetPath(inputHash);
        var staged = new List<StagedEntry>();
        var committed = false;
        FileStream? cacheLock = null;
        try
        {
            cacheLock = AcquireLock(_directory);
            ValidatePath(path);
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
                UnauthorizedAccessException)
        {
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
            cacheLock = AcquireLock(_directory);
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
                     "*.sharp-proof-cache.json",
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
