namespace SharpProof.Worker;

internal sealed partial class VerificationCache(string directory, long maximumBytes)
{
    private readonly string _directory = Path.GetFullPath(
        ArgumentNullGuard.NotNull(directory, nameof(directory)));
    private readonly long _maximumBytes = ArgumentNullGuard.RequirePositive(
        maximumBytes, nameof(maximumBytes));

    internal async Task<WorkerVerifyResponse?> TryReadAsync(
        string inputHash,
        WorkerClaimManifest manifest,
        ImmutableArray<CompilerCallablePreparation> targets,
        WorkerBudgets budgets,
        CancellationToken cancellationToken)
    {
        var path = GetPath(inputHash);
        try
        {
            var json = await WorkerProtocolJson.ReadUtf8FileAsync(path, cancellationToken)
                .ConfigureAwait(false);
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
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            return response;
        }
        catch (Exception exception) when (exception is
            ArgumentException or JsonException or IOException or InvalidDataException or
                UnauthorizedAccessException)
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

    internal static bool IsCacheable(
        WorkerVerifyResponse? response,
        string expectedInputHash,
        WorkerClaimManifest expectedManifest,
        ImmutableArray<CompilerCallablePreparation> targets,
        CancellationToken cancellationToken = default)
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
        claims.Length != 0 &&
        claims.All(static result =>
            result != null && result.Outcome == WorkerClaimOutcome.Refuted) &&
        expectedManifest.Claims.Length == claims.Length &&
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
            row.Kind == nameof(IrValueKind.Boolean) &&
            row.Value is "true" or "false")
        {
            value = factory.CreateBooleanValue(row.Value == "true");
            return true;
        }

        if (type == factory.IntegerType &&
            row.Kind == nameof(IrValueKind.Integer) &&
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
