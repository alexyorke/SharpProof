namespace SharpProof.Worker;
public sealed class SharpProofWorker : IDisposable {
    private readonly ISmtBackend _backend;
    private readonly Func<long>? _readConsumedResourceCount;
    private readonly SemaphoreSlim _methodResourceGate = new(1, 1);
    private readonly IrSmtBackend? _ownedBackend;
    private bool _disposed;
    public SharpProofWorker(ISmtBackend backend)
        : this(backend, backend is IrSmtBackend concrete ? () => concrete.ConsumedResourceCount : null) {
    }
    internal SharpProofWorker(ISmtBackend backend, Func<long>? readConsumedResourceCount) {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _readConsumedResourceCount = readConsumedResourceCount;
    }
    private SharpProofWorker(IrSmtBackend backend)
        : this(backend, () => backend.ConsumedResourceCount) =>
        _ownedBackend = backend;
    public static SharpProofWorker Create(WorkerBudgets budgets) {
        ArgumentNullException.ThrowIfNull(budgets);
        return new SharpProofWorker(new IrSmtBackend(new IrSmtBackendOptions(budgets.QueryRlimit)));
    }
    public async Task<WorkerVerifyResponse> VerifyAsync(
        WorkerVerifyRequest request,
        CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var started = Stopwatch.GetTimestamp();
        var validation = WorkerProtocolJson.Validate(request);
        if (!validation.IsValid)
            return Failure(string.Empty, WorkerRunFailureReason.InvalidRequest, new WorkerBudgets(), started, validation.Errors);
        ArgumentNullException.ThrowIfNull(request);
        WorkerInputSnapshot snapshot;
        try {
            snapshot = await WorkerInputSnapshot.LoadAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or ArgumentException) {
            return Failure(
                string.Empty, WorkerRunFailureReason.InputUnavailable, request.Budgets, started,
                Error("input.unavailable", "A source or reference input could not be loaded."));
        }
        CSharpCompilation compilation;
        try {
            compilation = WorkerCompilation.Create(request, snapshot);
        }
        catch (Exception exception) when (exception is ArgumentException or BadImageFormatException) {
            return Failure(
                snapshot.InputHash, WorkerRunFailureReason.CompilationFailure, request.Budgets, started,
                Error("project.invalid_input", "A parse option or reference assembly is invalid."));
        }
        var compilerErrors = compilation.GetDiagnostics(cancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .OrderBy(static diagnostic => diagnostic.Location.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
            .ThenBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .Select(static diagnostic => new WorkerProtocolError {
                Code = "compiler." + diagnostic.Id,
                Message = diagnostic.GetMessage(CultureInfo.InvariantCulture)
            })
            .ToArray();
        if (compilerErrors.Length != 0)
            return Failure(
                snapshot.InputHash, WorkerRunFailureReason.CompilationFailure,
                request.Budgets, started, compilerErrors);
        ClaimManifestBuildResult discovery;
        try {
            discovery = new ClaimManifestBuilder(compilation, request.Features).Build();
        }
        catch (Exception exception) when (exception is not
            OutOfMemoryException and not StackOverflowException) {
            return Failure(
                snapshot.InputHash, WorkerRunFailureReason.InfrastructureFailure, request.Budgets, started,
                Error("manifest.failed", "The selected claim manifest could not be produced."));
        }
        WorkerVerifyResponse Assemble(
            WorkerRunStatus status,
            WorkerRunFailureReason reason,
            IEnumerable<WorkerCallableResult> callables,
            IEnumerable<WorkerClaimResult> claims,
            WorkerCacheStatus resultCacheStatus,
            IEnumerable<WorkerProtocolError>? errors = null) =>
            WorkerResultAssembler.Create(
                snapshot.InputHash, discovery.Manifest, status, reason, callables, claims,
                request.Budgets, resultCacheStatus, Elapsed(started), errors);
        WorkerVerifyResponse Canceled(WorkerCacheStatus status) {
            var lanes = discovery.Targets.Values.Select(target => Unknown(
                target,
                WorkerClaimReason.Canceled,
                WorkerCallableCoverageReason.Canceled)).ToArray();
            return Assemble(
                WorkerRunStatus.Canceled,
                WorkerRunFailureReason.None,
                lanes.Select(static lane => lane.Callable),
                lanes.SelectMany(static lane => lane.Claims),
                status);
        }
        using var cache = CreateCacheIfEnabled(request, out var cacheStatus);
        if (cache != null) {
            if (cancellationToken.IsCancellationRequested) return Canceled(cacheStatus);
            var cached = await cache.TryReadAsync(
                snapshot.InputHash, discovery.Manifest, request.Budgets, CancellationToken.None).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) return Canceled(cacheStatus);
            if (cached != null)
                return Assemble(
                    WorkerRunStatus.Complete,
                    WorkerRunFailureReason.None,
                    cached.CallableResults,
                    cached.ClaimResults,
                    WorkerCacheStatus.Hit);
        }
        var verifier = new CallableVerifier(compilation, _backend, request.Budgets.MaximumExpressionDepth);
        using var projectBoundary = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        projectBoundary.CancelAfter(request.Budgets.ProjectWallTimeMilliseconds);
        using var parallelism = new SemaphoreSlim(
            request.Budgets.MaxParallelism,
            request.Budgets.MaxParallelism);
        var tasks = discovery.Targets.Values
            .OrderBy(static target => target.Entry.CallableId, StringComparer.Ordinal)
            .Select(target => VerifyTargetAsync(
                verifier,
                target,
                request.Budgets,
                parallelism,
                _methodResourceGate,
                _readConsumedResourceCount,
                projectBoundary,
                cancellationToken))
            .ToArray();
        var lanes = await Task.WhenAll(tasks).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested) return Canceled(cacheStatus);
        var callableResults = lanes.Select(static lane => lane.Callable).ToArray();
        var claimResults = lanes.SelectMany(static lane => lane.Claims).ToArray();
        var (runStatus, failureReason) = Classify(callableResults, claimResults);
        var response = Assemble(
            runStatus, failureReason, callableResults, claimResults, cacheStatus);
        var responseValidation = WorkerProtocolJson.Validate(response, snapshot.InputHash, discovery.Manifest);
        if (!responseValidation.IsValid) {
            var malformed = discovery.Targets.Values.Select(target => Unknown(
                target,
                WorkerClaimReason.InfrastructureFailure,
                WorkerCallableCoverageReason.MissingClaimResult)).ToArray();
            return Assemble(
                WorkerRunStatus.Failed,
                WorkerRunFailureReason.MalformedResult,
                malformed.Select(static lane => lane.Callable),
                malformed.SelectMany(static lane => lane.Claims),
                WorkerCacheStatus.Rejected,
                responseValidation.Errors);
        }
        if (cache != null &&
            CacheableWorkerResponse.TryCreate(
                response,
                snapshot.InputHash,
                discovery.Manifest,
            out var cacheable)) {
            if (cancellationToken.IsCancellationRequested) return Canceled(cacheStatus);
            var written = await cache.TryWriteAsync(
                cacheable, CancellationToken.None).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
                return Canceled(written
                    ? WorkerCacheStatus.Written
                    : WorkerCacheStatus.Unavailable);
            response = Assemble(
                runStatus,
                failureReason,
                callableResults,
                claimResults,
                written
                    ? WorkerCacheStatus.Written
                    : WorkerCacheStatus.Unavailable);
        }
        return response;
    }
    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _ownedBackend?.Dispose();
        _methodResourceGate.Dispose();
    }
    private static async Task<CallableVerificationResult> VerifyTargetAsync(
        CallableVerifier verifier,
        ManifestCallableTarget target,
        WorkerBudgets budgets,
        SemaphoreSlim parallelism,
        SemaphoreSlim methodResourceGate,
        Func<long>? readConsumedResourceCount,
        CancellationTokenSource projectBoundary,
        CancellationToken callerCancellation) {
        if (callerCancellation.IsCancellationRequested)
            return Unknown(target, WorkerClaimReason.Canceled, WorkerCallableCoverageReason.Canceled);
        if (!target.IsVerifierSupported ||
            target.Declaration is not BaseMethodDeclarationSyntax ||
            target.SemanticModel == null)
            return Unknown(target, WorkerClaimReason.UnsupportedCallable, WorkerCallableCoverageReason.UnsupportedCallable);
        var ownsParallelLane = false;
        var ownsResourceLane = false;
        try {
            try {
                await parallelism.WaitAsync(projectBoundary.Token).ConfigureAwait(false);
                ownsParallelLane = true;
                if (readConsumedResourceCount != null) {
                    await methodResourceGate.WaitAsync(projectBoundary.Token).ConfigureAwait(false);
                    ownsResourceLane = true;
                }
            }
            catch (OperationCanceledException) {
                if (callerCancellation.IsCancellationRequested)
                    return Unknown(target, WorkerClaimReason.Canceled, WorkerCallableCoverageReason.Canceled);
                return Unknown(target, WorkerClaimReason.ProjectTimeout, WorkerCallableCoverageReason.ProjectTimeout);
            }
            using var methodBoundary = CancellationTokenSource.CreateLinkedTokenSource(projectBoundary.Token);
            methodBoundary.CancelAfter(budgets.MethodWallTimeMilliseconds);
            try {
                var resourceBudget = new MethodResourceBudget(
                    readConsumedResourceCount, budgets.QueryRlimit, budgets.MethodRlimit);
                var records = await verifier.VerifyAsync(
                    target, resourceBudget, methodBoundary.Token).ConfigureAwait(false);
                foreach (var record in records)
                    record.Assumptions = MergeAssumptions(target.Assumptions, record.Assumptions);
                var unsupportedEffects = target.Entry.SelectedFeatures.Contains(WorkerSelectedFeature.Effects);
                var hasUnknown = records.Any(static record => record.Outcome == WorkerClaimOutcome.Unknown);
                var reason = unsupportedEffects
                    ? WorkerCallableCoverageReason.UnsupportedContract
                    : hasUnknown
                        ? WorkerCallableCoverageReason.SemanticUnknown
                        : WorkerCallableCoverageReason.None;
                return Result(target, reason, records);
            }
            catch (OperationCanceledException) {
                if (callerCancellation.IsCancellationRequested)
                    return Unknown(target, WorkerClaimReason.Canceled, WorkerCallableCoverageReason.Canceled);
                var project = projectBoundary.IsCancellationRequested;
                return Unknown(
                    target,
                    project ? WorkerClaimReason.ProjectTimeout : WorkerClaimReason.MethodTimeout,
                    project ? WorkerCallableCoverageReason.ProjectTimeout : WorkerCallableCoverageReason.MethodTimeout);
            }
            catch (Exception exception) when (exception is not
                OutOfMemoryException and not StackOverflowException) {
                return Unknown(target, WorkerClaimReason.InfrastructureFailure, WorkerCallableCoverageReason.InfrastructureFailure);
            }
        }
        finally {
            if (ownsResourceLane) methodResourceGate.Release();
            if (ownsParallelLane) parallelism.Release();
        }
    }
    private static CallableVerificationResult Unknown(
        ManifestCallableTarget target,
        WorkerClaimReason claimReason,
        WorkerCallableCoverageReason callableReason) =>
        Result(
            target,
            callableReason,
            [.. target.Claims.Select(claim => new WorkerClaimResult {
                ClaimId = claim.Entry.ClaimId,
                Outcome = WorkerClaimOutcome.Unknown,
                Reason = claimReason,
                Assumptions = [.. target.Assumptions]
            })]);
    private static CallableVerificationResult Result(
        ManifestCallableTarget target,
        WorkerCallableCoverageReason reason,
        ImmutableArray<WorkerClaimResult> claims) =>
        new(
            new WorkerCallableResult {
                CallableId = target.Entry.CallableId,
                Coverage = reason == WorkerCallableCoverageReason.None
                    ? WorkerCallableCoverage.Complete
                    : WorkerCallableCoverage.Incomplete,
                Reason = reason,
                Assumptions = [.. target.Assumptions]
            },
            claims);
    private static WorkerAssumptionEvidence[] MergeAssumptions(
        IEnumerable<WorkerAssumptionEvidence> declared,
        IEnumerable<WorkerAssumptionEvidence>? observed) =>
        [.. declared
            .Concat(observed ?? [])
            .GroupBy(static evidence => evidence.Id, StringComparer.Ordinal)
            .Select(static group => new WorkerAssumptionEvidence {
                Id = group.Key,
                Kind = group.Select(static evidence =>
                    evidence.Kind).First(),
                Used = group.Any(static evidence => evidence.Used)
            })
            .OrderBy(static evidence => evidence.Id, StringComparer.Ordinal)];
    private static (WorkerRunStatus Status, WorkerRunFailureReason Failure) Classify(
        IEnumerable<WorkerCallableResult> callables,
        IEnumerable<WorkerClaimResult> claims) {
        var coverageReasons = callables.Select(static callable => callable.Reason).ToArray();
        if (coverageReasons.Contains(WorkerCallableCoverageReason.InfrastructureFailure))
            return (WorkerRunStatus.Failed, WorkerRunFailureReason.InfrastructureFailure);
        if (coverageReasons.Contains(WorkerCallableCoverageReason.MissingClaimResult))
            return (WorkerRunStatus.Failed, WorkerRunFailureReason.MalformedResult);
        var reasons = claims.Select(static claim => claim.Reason).ToArray();
        foreach (var mapping in new[] {
                     (WorkerClaimReason.BackendUnavailable, WorkerRunFailureReason.BackendUnavailable),
                     (WorkerClaimReason.InfrastructureFailure, WorkerRunFailureReason.InfrastructureFailure),
                     (WorkerClaimReason.MalformedBackendResult, WorkerRunFailureReason.MalformedResult),
                     (WorkerClaimReason.CounterexampleReplayFailed, WorkerRunFailureReason.CounterexampleReplayFailed)
                 })
            if (reasons.Contains(mapping.Item1))
                return (WorkerRunStatus.Failed, mapping.Item2);
        if (coverageReasons.Contains(WorkerCallableCoverageReason.Canceled) ||
            reasons.Contains(WorkerClaimReason.Canceled))
            return (WorkerRunStatus.Canceled, WorkerRunFailureReason.None);
        var timedOut = coverageReasons.Any(static reason => reason is
                WorkerCallableCoverageReason.MethodTimeout or WorkerCallableCoverageReason.ProjectTimeout)
            || reasons.Any(static reason => reason is
                WorkerClaimReason.MethodTimeout or WorkerClaimReason.ProjectTimeout);
        return timedOut
            ? (WorkerRunStatus.TimedOut, WorkerRunFailureReason.None)
            : (WorkerRunStatus.Complete, WorkerRunFailureReason.None);
    }

    private static VerificationCache? CreateCacheIfEnabled(
        WorkerVerifyRequest request, out WorkerCacheStatus status) {
        if (!request.Cache.Enabled) {
            status = WorkerCacheStatus.Disabled;
            return null;
        }
        try {
            status = WorkerCacheStatus.Miss;
            var projectDirectory = Path.GetFullPath(request.ProjectDirectory);
            var directory = string.IsNullOrWhiteSpace(request.Cache.Directory)
                ? Path.Combine(projectDirectory, "obj", "SharpProof", "cache")
                : Path.IsPathFullyQualified(request.Cache.Directory)
                    ? request.Cache.Directory
                    : Path.GetFullPath(request.Cache.Directory, projectDirectory);
            return new VerificationCache(directory, request.Cache.MaximumBytes);
        }
        catch (Exception exception) when (exception is
            ArgumentException or NotSupportedException) {
            status = WorkerCacheStatus.Unavailable;
            return null;
        }
    }

    private static WorkerVerifyResponse Failure(
        string inputHash,
        WorkerRunFailureReason reason,
        WorkerBudgets budgets,
        long started,
        IEnumerable<WorkerProtocolError> errors) =>
        WorkerResultAssembler.Create(
            string.IsNullOrEmpty(inputHash) ? WorkerResultAssembler.EmptyInputHash : inputHash,
            WorkerResultAssembler.EmptyManifest(),
            WorkerRunStatus.Failed,
            reason,
            [],
            [],
            budgets,
            WorkerCacheStatus.Disabled,
            Elapsed(started),
            errors);

    private static WorkerProtocolError[] Error(string code, string message) =>
        [new WorkerProtocolError { Code = code, Message = message }];

    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    private sealed record CallableVerificationResult(
        WorkerCallableResult Callable, ImmutableArray<WorkerClaimResult> Claims);
}
