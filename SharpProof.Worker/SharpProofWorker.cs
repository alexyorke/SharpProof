namespace SharpProof.Worker;
#pragma warning disable IDE0055 // Compact orchestration preserves the fixed production-size ceiling.
public sealed class SharpProofWorker : IDisposable {
    private readonly Lazy<ISmtBackend> _backend; private readonly Func<long>? _readConsumedResourceCount;
    private readonly bool _ownsBackend; private readonly SemaphoreSlim _methodResourceGate = new(1, 1); private bool _disposed;
    public SharpProofWorker(ISmtBackend backend) : this(
        backend, backend is IrSmtBackend concrete ? () => concrete.ConsumedResourceCount : null) { }
    internal SharpProofWorker(ISmtBackend backend, Func<long>? readConsumedResourceCount) {
        ArgumentNullException.ThrowIfNull(backend); _backend = new(() => backend);
        _readConsumedResourceCount = readConsumedResourceCount;
    }
    internal SharpProofWorker(Func<ISmtBackend> backendFactory) {
        ArgumentNullException.ThrowIfNull(backendFactory);
        _backend = new(() => backendFactory() ?? throw new InvalidOperationException("The backend factory returned null."));
        _ownsBackend = true;
    }
    public static SharpProofWorker Create(WorkerBudgets budgets) {
        ArgumentNullException.ThrowIfNull(budgets); return new SharpProofWorker(
            () => new IrSmtBackend(new IrSmtBackendOptions(budgets.QueryRlimit)));
    }
    public async Task<WorkerVerifyResponse> VerifyAsync(
        WorkerVerifyRequest request, CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var started = Stopwatch.GetTimestamp();
        var validation = WorkerProtocolJson.Validate(request);
        if (!validation.IsValid)
            return Failure(string.Empty, WorkerRunFailureReason.InvalidRequest, new WorkerBudgets(), started, validation.Errors);
        ArgumentNullException.ThrowIfNull(request);
        var requestHash = WorkerProtocolJson.ComputeRequestHash(request);
        using var projectBoundary = cancellationToken.IsCancellationRequested
            ? new CancellationTokenSource()
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        projectBoundary.CancelAfter(request.Budgets.ProjectWallTimeMilliseconds);
        WorkerVerifyResponse Failed(WorkerRunFailureReason reason, string code, string message, string inputHash = "") =>
            Failure(inputHash, reason, request.Budgets, started, Error(code, message), requestHash);
        WorkerVerifyResponse Interrupted(WorkerInputSnapshot? input = null) {
            var canceled = cancellationToken.IsCancellationRequested;
            return WorkerResultAssembler.CreateIncomplete(
                input?.InputHash ?? WorkerResultAssembler.EmptyInputHash, requestHash,
                input?.CompilerManifest.Manifest ?? WorkerResultAssembler.EmptyManifest(), request.Budgets,
                canceled ? WorkerRunStatus.Canceled : WorkerRunStatus.TimedOut, WorkerRunFailureReason.None,
                canceled ? WorkerCallableCoverageReason.Canceled : WorkerCallableCoverageReason.ProjectTimeout,
                canceled ? WorkerClaimReason.Canceled : WorkerClaimReason.ProjectTimeout,
                versions: Versions(), elapsedMilliseconds: Elapsed(started));
        }
        WorkerInputSnapshot snapshot;
        try {
            snapshot = await WorkerInputSnapshot.LoadAsync(request, projectBoundary.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return Interrupted(); }
        catch (IOException exception) when (exception.Message == WorkerInputSnapshot.ManifestUnavailable) {
            return Failed(WorkerRunFailureReason.InputUnavailable, "compiler_manifest.unavailable",
                "The compiler manifest could not be loaded.");
        }
        catch (IOException exception) when (exception.Message == WorkerInputSnapshot.ManifestInvalid) {
            return Failed(WorkerRunFailureReason.CompilerManifestMismatch, "compiler_manifest.invalid",
                "The compiler manifest digest or schema is invalid.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException) {
            return Failed(WorkerRunFailureReason.InputUnavailable, "input.unavailable",
                "The compiler artifact or a referenced image could not be loaded.");
        }
        WorkerVerifyResponse FailedAfterManifest(WorkerRunFailureReason reason,
            IEnumerable<WorkerProtocolError> errors, WorkerClaimReason claimReason = WorkerClaimReason.InfrastructureFailure) =>
            ManifestFailure(snapshot.InputHash, snapshot.CompilerManifest.Manifest,
                request.Budgets, started, reason, errors, requestHash, claimReason);
        if (cancellationToken.IsCancellationRequested) return Interrupted(snapshot);
        try {
            projectBoundary.Token.ThrowIfCancellationRequested();
            if (snapshot.CompilerManifest.MaximumExpressionDepth != request.Budgets.MaximumExpressionDepth)
                return FailedAfterManifest(WorkerRunFailureReason.CompilerManifestMismatch,
                    Error("compiler_manifest.options", "The compiler artifact options do not match the request."));
            if (snapshot.CompilerManifest.CompilerDiagnostics.Length != 0)
                return FailedAfterManifest(WorkerRunFailureReason.CompilationFailure,
                    snapshot.CompilerManifest.CompilerDiagnostics.Select(static item =>
                        new WorkerProtocolError { Code = item.Code, Message = item.Message }));
            ImmutableArray<CompilerCallablePreparation> targets;
            try { targets = CompilerManifestArtifactJson.DecodeCallables(snapshot.CompilerManifest); }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and not OperationCanceledException) {
                return FailedAfterManifest(WorkerRunFailureReason.CompilerManifestMismatch,
                    Error("compiler_manifest.lowered_ir",
                        "The lowered compiler artifact is invalid: " +
                        exception.GetBaseException().Message));
            }
            projectBoundary.Token.ThrowIfCancellationRequested();
            var manifest = snapshot.CompilerManifest.Manifest;
            WorkerVerifyResponse Assemble(WorkerRunStatus status, WorkerRunFailureReason reason,
                IEnumerable<WorkerCallableResult> callables, IEnumerable<WorkerClaimResult> claims,
                WorkerCacheStatus resultCacheStatus, IEnumerable<WorkerProtocolError>? errors = null) =>
                WorkerResultAssembler.Create(snapshot.InputHash, manifest, status, reason, callables, claims,
                    request.Budgets, resultCacheStatus, Elapsed(started), errors,
                    requestHash, Versions());
            WorkerVerifyResponse Canceled(WorkerCacheStatus status) {
                var lanes = targets.Select(target => Unknown(
                    target, WorkerClaimReason.Canceled, WorkerCallableCoverageReason.Canceled)).ToArray();
                return Assemble(WorkerRunStatus.Canceled, WorkerRunFailureReason.None,
                    lanes.Select(static lane => lane.Callable), lanes.SelectMany(static lane => lane.Claims), status);
            }
            using var cache = CreateCacheIfEnabled(request,
                snapshot.CompilerManifest.Compilation.ProjectDirectory, out var cacheStatus);
            if (cache != null) {
                var cached = await cache.TryReadAsync(
                    snapshot.InputHash, manifest, request.Budgets, projectBoundary.Token).ConfigureAwait(false);
                projectBoundary.Token.ThrowIfCancellationRequested();
                if (cached != null)
                    return Assemble(WorkerRunStatus.Complete, WorkerRunFailureReason.None,
                        cached.CallableResults, cached.ClaimResults, WorkerCacheStatus.Hit);
            }
            ISmtBackend backend;
            try { backend = _backend.Value; }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and not OperationCanceledException) {
                return FailedAfterManifest(WorkerRunFailureReason.BackendUnavailable,
                    Error("backend.unavailable", "The native SMT backend is unavailable: " + exception.GetBaseException().Message),
                    WorkerClaimReason.BackendUnavailable);
            }
            var readResources = _readConsumedResourceCount ??
                (backend is IrSmtBackend concrete ? () => concrete.ConsumedResourceCount : null);
            var verifier = new CallableVerifier(backend, request.Budgets.MaximumExpressionDepth);
            using var parallelism = new SemaphoreSlim(request.Budgets.MaxParallelism, request.Budgets.MaxParallelism);
            var tasks = targets
                .OrderBy(static target => target.Entry.CallableId, StringComparer.Ordinal)
                .Select(target => VerifyTargetAsync(verifier, target, request.Budgets,
                    parallelism, _methodResourceGate, readResources, projectBoundary, cancellationToken))
                .ToArray();
            var lanes = await Task.WhenAll(tasks).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested) return Canceled(cacheStatus);
            var callableResults = lanes.Select(static lane => lane.Callable).ToArray();
            var claimResults = lanes.SelectMany(static lane => lane.Claims).ToArray();
            var (runStatus, failureReason) = Classify(callableResults, claimResults);
            var response = Assemble(runStatus, failureReason, callableResults, claimResults, cacheStatus);
            var responseValidation = WorkerProtocolJson.Validate(response, snapshot.InputHash, manifest);
            if (!responseValidation.IsValid) {
                var malformed = targets.Select(target => Unknown(target, WorkerClaimReason.InfrastructureFailure,
                    WorkerCallableCoverageReason.MissingClaimResult)).ToArray();
                return Assemble(WorkerRunStatus.Failed, WorkerRunFailureReason.MalformedResult,
                    malformed.Select(static lane => lane.Callable),
                    malformed.SelectMany(static lane => lane.Claims),
                    WorkerCacheStatus.Rejected, responseValidation.Errors);
            }
            if (cache != null && CacheableWorkerResponse.TryCreate(
                    response, snapshot.InputHash, manifest, out var cacheable)) {
                var written = await cache.TryWriteAsync(cacheable, projectBoundary.Token).ConfigureAwait(false);
                projectBoundary.Token.ThrowIfCancellationRequested();
                response = Assemble(runStatus, failureReason, callableResults, claimResults,
                    written ? WorkerCacheStatus.Written : WorkerCacheStatus.Unavailable);
            }
            return response;
        }
        catch (OperationCanceledException) { return Interrupted(snapshot); }
    }
    public void Dispose() {
        if (_disposed) return; _disposed = true;
        if (_ownsBackend && _backend.IsValueCreated &&
            _backend.Value is IDisposable owned) owned.Dispose();
        _methodResourceGate.Dispose();
    }
    private static async Task<CallableVerificationResult> VerifyTargetAsync(
        CallableVerifier verifier, CompilerCallablePreparation target, WorkerBudgets budgets,
        SemaphoreSlim parallelism, SemaphoreSlim methodResourceGate, Func<long>? readConsumedResourceCount,
        CancellationTokenSource projectBoundary, CancellationToken callerCancellation) {
        if (callerCancellation.IsCancellationRequested)
            return Unknown(target, WorkerClaimReason.Canceled, WorkerCallableCoverageReason.Canceled);
        if (!target.IsSuccess)
            return Unknown(target, target.FailureReason,
                target.Entry.SelectedFeatures.Contains(WorkerSelectedFeature.Effects)
                    ? WorkerCallableCoverageReason.UnsupportedContract
                    : target.FailureReason == WorkerClaimReason.UnsupportedCallable
                    ? WorkerCallableCoverageReason.UnsupportedCallable
                    : WorkerCallableCoverageReason.SemanticUnknown);
        var ownsParallelLane = false; var ownsResourceLane = false;
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
                var records = await verifier.VerifyAsync(target, resourceBudget, methodBoundary.Token).ConfigureAwait(false);
                var unsupportedEffects = target.Entry.SelectedFeatures.Contains(WorkerSelectedFeature.Effects);
                var hasUnknown = records.Any(static record => record.Outcome == WorkerClaimOutcome.Unknown);
                var reason = unsupportedEffects ? WorkerCallableCoverageReason.UnsupportedContract
                    : hasUnknown ? WorkerCallableCoverageReason.SemanticUnknown : WorkerCallableCoverageReason.None;
                return Result(target, reason, records);
            }
            catch (OperationCanceledException) {
                if (callerCancellation.IsCancellationRequested)
                    return Unknown(target, WorkerClaimReason.Canceled, WorkerCallableCoverageReason.Canceled);
                var project = projectBoundary.IsCancellationRequested;
                return Unknown(target, project ? WorkerClaimReason.ProjectTimeout : WorkerClaimReason.MethodTimeout,
                    project ? WorkerCallableCoverageReason.ProjectTimeout : WorkerCallableCoverageReason.MethodTimeout);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException) {
                return Unknown(target, WorkerClaimReason.InfrastructureFailure, WorkerCallableCoverageReason.InfrastructureFailure);
            }
        }
        finally {
            if (ownsResourceLane) methodResourceGate.Release();
            if (ownsParallelLane) parallelism.Release();
        }
    }
    private static CallableVerificationResult Unknown(CompilerCallablePreparation target,
        WorkerClaimReason claimReason, WorkerCallableCoverageReason callableReason) =>
        Result(target, callableReason, [.. target.Entry.ClaimIds.Select(claimId => new WorkerClaimResult {
            ClaimId = claimId, Outcome = WorkerClaimOutcome.Unknown, Reason = claimReason,
            Assumptions = [.. target.Entry.Assumptions]
        })]);
    private static CallableVerificationResult Result(CompilerCallablePreparation target,
        WorkerCallableCoverageReason reason, ImmutableArray<WorkerClaimResult> claims) =>
        new(new WorkerCallableResult {
            CallableId = target.Entry.CallableId,
            Coverage = reason == WorkerCallableCoverageReason.None ?
                WorkerCallableCoverage.Complete : WorkerCallableCoverage.Incomplete,
            Reason = reason, Assumptions = [.. target.Entry.Assumptions]
        }, claims);
    private static (WorkerRunStatus Status, WorkerRunFailureReason Failure) Classify(
        IEnumerable<WorkerCallableResult> callables, IEnumerable<WorkerClaimResult> claims) {
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
            if (reasons.Contains(mapping.Item1)) return (WorkerRunStatus.Failed, mapping.Item2);
        if (coverageReasons.Contains(WorkerCallableCoverageReason.Canceled) || reasons.Contains(WorkerClaimReason.Canceled))
            return (WorkerRunStatus.Canceled, WorkerRunFailureReason.None);
        var timedOut = coverageReasons.Any(static reason => reason is
                WorkerCallableCoverageReason.MethodTimeout or WorkerCallableCoverageReason.ProjectTimeout) ||
            reasons.Any(static reason => reason is WorkerClaimReason.MethodTimeout or WorkerClaimReason.ProjectTimeout);
        return timedOut ? (WorkerRunStatus.TimedOut, WorkerRunFailureReason.None) :
            (WorkerRunStatus.Complete, WorkerRunFailureReason.None);
    }

    private static VerificationCache? CreateCacheIfEnabled(
        WorkerVerifyRequest request, string projectDirectory, out WorkerCacheStatus status) {
        if (!request.Cache.Enabled) { status = WorkerCacheStatus.Disabled; return null; }
        try {
            status = WorkerCacheStatus.Miss;
            projectDirectory = Path.GetFullPath(projectDirectory);
            var directory = string.IsNullOrWhiteSpace(request.Cache.Directory)
                ? Path.Combine(projectDirectory, "obj", "SharpProof", "cache")
                : Path.IsPathFullyQualified(request.Cache.Directory)
                    ? request.Cache.Directory
                    : Path.GetFullPath(request.Cache.Directory, projectDirectory);
            return new VerificationCache(directory, request.Cache.MaximumBytes);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException) {
            status = WorkerCacheStatus.Unavailable; return null;
        }
    }

    private static WorkerVerifyResponse ManifestFailure(
        string inputHash, WorkerClaimManifest manifest, WorkerBudgets budgets, long started,
        WorkerRunFailureReason reason, IEnumerable<WorkerProtocolError> errors, string requestHash,
        WorkerClaimReason claimReason = WorkerClaimReason.InfrastructureFailure) =>
        WorkerResultAssembler.CreateIncomplete(
            inputHash, requestHash, manifest, budgets,
            WorkerRunStatus.Failed, reason,
            WorkerCallableCoverageReason.InfrastructureFailure,
            claimReason, errors, Versions(),
            Elapsed(started));

    private static WorkerVerifyResponse Failure(string inputHash, WorkerRunFailureReason reason,
        WorkerBudgets budgets, long started, IEnumerable<WorkerProtocolError> errors, string? requestHash = null) =>
        WorkerResultAssembler.Create(
            string.IsNullOrEmpty(inputHash) ? WorkerResultAssembler.EmptyInputHash : inputHash,
            WorkerResultAssembler.EmptyManifest(), WorkerRunStatus.Failed, reason,
            [], [], budgets, WorkerCacheStatus.Disabled, Elapsed(started), errors,
            requestHash, Versions());

    private static WorkerVersionSummary Versions() => new() {
        WorkerVersion = WorkerCacheIdentity.Current.ToolVersion, ApiSpecVersion = WorkerCacheIdentity.Current.ApiSpecVersion
    };
    private static WorkerProtocolError[] Error(string code, string message) =>
        [new WorkerProtocolError { Code = code, Message = message }];
    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    private sealed record CallableVerificationResult(WorkerCallableResult Callable, ImmutableArray<WorkerClaimResult> Claims);
}
