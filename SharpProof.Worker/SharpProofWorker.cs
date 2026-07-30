using static SharpProof.Worker.CallableVerificationPolicy;

namespace SharpProof.Worker;

public sealed class SharpProofWorker : IDisposable
{
    private readonly ISmtBackend? _backend;
    private readonly Func<ISmtBackend>? _backendFactory;
    private readonly Func<long>? _readConsumedResourceCount;
    private bool _disposed;
    public SharpProofWorker(ISmtBackend backend) : this(
        backend, backend is IrSmtBackend concrete ? () => concrete.ConsumedResourceCount : null)
    {
    }
    internal SharpProofWorker(ISmtBackend backend, Func<long>? readConsumedResourceCount)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        _readConsumedResourceCount = readConsumedResourceCount;
    }
    internal SharpProofWorker(Func<ISmtBackend> backendFactory)
    {
        ArgumentNullException.ThrowIfNull(backendFactory);
        _backendFactory = backendFactory;
    }
    public static SharpProofWorker Create(WorkerBudgets budgets)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        return new SharpProofWorker(
            () => new IrSmtBackend(new IrSmtBackendOptions(budgets.QueryRlimit)));
    }
    public async Task<WorkerVerifyResponse> VerifyAsync(
        WorkerVerifyRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var started = Stopwatch.GetTimestamp();
        var validation = WorkerProtocolJson.Validate(request);
        if (!validation.IsValid)
        {
            return Failure(string.Empty, WorkerRunFailureReason.InvalidRequest, new WorkerBudgets(), started, validation.Errors);
        }

        ArgumentNullException.ThrowIfNull(request);
        var requestHash = WorkerProtocolJson.ComputeRequestHash(request);
        using var projectBoundary = cancellationToken.IsCancellationRequested
            ? new CancellationTokenSource()
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        projectBoundary.CancelAfter(request.Budgets.ProjectWallTimeMilliseconds);
        WorkerVerifyResponse Failed(WorkerRunFailureReason reason, string code, string message, string inputHash = "")
        {
            return Failure(inputHash, reason, request.Budgets, started, Error(code, message), requestHash);
        }

        WorkerVerifyResponse Interrupted(WorkerInputSnapshot? input = null)
        {
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
        VerificationLane[] solverLanes = [];
        try
        {
            // A timeout or cancellation result must remain accountable to the
            // authoritative manifest. The launcher hard limit still bounds a
            // snapshot load that does not complete.
            snapshot = await WorkerInputSnapshot.LoadAsync(
                request, WorkerCacheIdentity.Current, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return Interrupted(); }
        catch (IOException exception) when (exception.Message == WorkerInputSnapshot.ManifestUnavailable)
        {
            return Failed(WorkerRunFailureReason.InputUnavailable, "compiler_manifest.unavailable",
                "The compiler manifest could not be loaded.");
        }
        catch (IOException exception) when (exception.Message == WorkerInputSnapshot.ManifestInvalid)
        {
            return Failed(WorkerRunFailureReason.CompilerManifestMismatch, "compiler_manifest.invalid",
                "The compiler manifest digest or schema is invalid.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Failed(WorkerRunFailureReason.InputUnavailable, "input.unavailable",
                "The compiler artifact or a referenced image could not be loaded.");
        }
        WorkerVerifyResponse FailedAfterManifest(WorkerRunFailureReason reason,
            IEnumerable<WorkerProtocolError> errors, WorkerClaimReason claimReason = WorkerClaimReason.InfrastructureFailure)
        {
            return ManifestFailure(snapshot.InputHash, snapshot.CompilerManifest.Manifest,
                request.Budgets, started, reason, errors, requestHash, claimReason);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Interrupted(snapshot);
        }

        try
        {
            projectBoundary.Token.ThrowIfCancellationRequested();
            if (snapshot.CompilerManifest.MaximumExpressionDepth != request.Budgets.MaximumExpressionDepth)
            {
                return FailedAfterManifest(WorkerRunFailureReason.CompilerManifestMismatch,
                    Error("compiler_manifest.options", "The compiler artifact options do not match the request."));
            }

            if (snapshot.CompilerManifest.CompilerDiagnostics.Length != 0)
            {
                return FailedAfterManifest(WorkerRunFailureReason.CompilationFailure,
                    snapshot.CompilerManifest.CompilerDiagnostics.Select(static item =>
                        new WorkerProtocolError { Code = item.Code, Message = item.Message }));
            }

            ImmutableArray<CompilerCallablePreparation> targets;
            try
            {
                targets = CompilerManifestArtifactJson.DecodeCallables(snapshot.CompilerManifest);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and
                not StackOverflowException and not OperationCanceledException)
            {
                return FailedAfterManifest(WorkerRunFailureReason.CompilerManifestMismatch,
                    Error("compiler_manifest.lowered_ir",
                        "The lowered compiler artifact is invalid: " +
                        exception.GetBaseException().Message));
            }
            projectBoundary.Token.ThrowIfCancellationRequested();
            var manifest = snapshot.CompilerManifest.Manifest;
            WorkerVerifyResponse Assemble(WorkerRunStatus status, WorkerRunFailureReason reason,
                IEnumerable<WorkerCallableResult> callables, IEnumerable<WorkerClaimResult> claims,
                WorkerCacheStatus resultCacheStatus, IEnumerable<WorkerProtocolError>? errors = null)
            {
                return WorkerResultAssembler.Create(snapshot.InputHash, manifest, status, reason, callables, claims,
                    request.Budgets, resultCacheStatus, Elapsed(started), errors,
                    requestHash, Versions());
            }

            WorkerVerifyResponse Canceled(WorkerCacheStatus status)
            {
                var lanes = targets.Select(target => Unknown(
                    target, WorkerClaimReason.Canceled, WorkerCallableCoverageReason.Canceled)).ToArray();
                return Assemble(WorkerRunStatus.Canceled, WorkerRunFailureReason.None,
                    lanes.Select(static lane => lane.Callable), lanes.SelectMany(static lane => lane.Claims), status);
            }
            var cache = CreateCacheIfEnabled(request,
                snapshot.CompilerManifest.Compilation.ProjectDirectory, out var cacheStatus);
            if (cache != null)
            {
                var cached = await cache.TryReadAsync(
                    snapshot.InputHash,
                    manifest,
                    targets,
                    request.Budgets,
                    projectBoundary.Token).ConfigureAwait(false);
                projectBoundary.Token.ThrowIfCancellationRequested();
                if (cached != null)
                {
                    return Assemble(WorkerRunStatus.Complete, WorkerRunFailureReason.None,
                        cached.CallableResults, cached.ClaimResults, WorkerCacheStatus.Hit);
                }
            }
            if (!TryCreateLanes(request.Budgets, targets.Length, out solverLanes, out var backendError))
            {
                return FailedAfterManifest(WorkerRunFailureReason.BackendUnavailable,
                    Error("backend.unavailable", "The native SMT backend is unavailable: " + backendError),
                    WorkerClaimReason.BackendUnavailable);
            }
            var orderedTargets = targets.OrderBy(
                static target => target.Entry.CallableId, StringComparer.Ordinal).ToArray();
            var results = new CallableVerificationResult[orderedTargets.Length];
            var nextTarget = -1;
            async Task RunLane(VerificationLane lane)
            {
                while (true)
                {
                    var index = Interlocked.Increment(ref nextTarget);
                    if (index >= orderedTargets.Length)
                    {
                        return;
                    }

                    var result = await VerifyTargetAsync(lane.Verifier, orderedTargets[index], request.Budgets,
                        lane.ReadConsumedResourceCount, request.Budgets.MethodWallTimeMilliseconds,
                        projectBoundary, cancellationToken).ConfigureAwait(false);
                    results[index] = result;
                    if ((result.Callable.Reason is WorkerCallableCoverageReason.Canceled or
                            WorkerCallableCoverageReason.ProjectTimeout or
                            WorkerCallableCoverageReason.MethodTimeout) &&
                        !projectBoundary.IsCancellationRequested &&
                        !lane.TryRenew(solverLanes, request.Budgets.MaximumExpressionDepth))
                    {
                        return;
                    }
                }
            }
            await Task.WhenAll(solverLanes.Select(RunLane)).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                return Canceled(cacheStatus);
            }

            for (var index = 0; index < results.Length; index++)
            {
                results[index] ??= Unknown(orderedTargets[index],
                    WorkerClaimReason.InfrastructureFailure,
                    WorkerCallableCoverageReason.InfrastructureFailure);
            }

            var callableResults = results.Select(static result => result.Callable).ToArray();
            var claimResults = results.SelectMany(static result => result.Claims).ToArray();
            var run = WorkerResultAssembler.Classify(callableResults, claimResults);
            var response = Assemble(run.Status, run.Failure, callableResults, claimResults, cacheStatus);
            var responseValidation = WorkerProtocolJson.Validate(response, snapshot.InputHash, manifest);
            if (!responseValidation.IsValid)
            {
                var malformed = targets.Select(target => Unknown(target, WorkerClaimReason.InfrastructureFailure,
                    WorkerCallableCoverageReason.MissingClaimResult)).ToArray();
                return Assemble(WorkerRunStatus.Failed, WorkerRunFailureReason.MalformedResult,
                    malformed.Select(static lane => lane.Callable),
                    malformed.SelectMany(static lane => lane.Claims),
                    WorkerCacheStatus.Rejected, responseValidation.Errors);
            }
            if (cache != null && VerificationCache.IsCacheable(
                    response,
                    snapshot.InputHash,
                    manifest,
                    targets,
                    projectBoundary.Token))
            {
                var written = await cache.TryWriteAsync(
                    response, snapshot.InputHash, manifest, projectBoundary.Token).ConfigureAwait(false);
                projectBoundary.Token.ThrowIfCancellationRequested();
                response = Assemble(run.Status, run.Failure, callableResults, claimResults,
                    written ? WorkerCacheStatus.Written : WorkerCacheStatus.Unavailable);
            }
            return response;
        }
        catch (OperationCanceledException) { return Interrupted(snapshot); }
        finally
        {
            foreach (var lane in solverLanes)
            {
                lane.DisposeOwnedBackend();
            }
        }
    }
    public void Dispose()
    {
        _disposed = true;
    }

    private static VerificationCache? CreateCacheIfEnabled(
        WorkerVerifyRequest request, string projectDirectory, out WorkerCacheStatus status)
    {
        if (!request.Cache.Enabled || request.VerifyPolicy == WorkerVerifyPolicy.RequireProven)
        {
            status = WorkerCacheStatus.Disabled;
            return null;
        }
        try
        {
            status = WorkerCacheStatus.Miss;
            projectDirectory = Path.GetFullPath(projectDirectory);
            var directory = string.IsNullOrWhiteSpace(request.Cache.Directory)
                ? Path.Combine(projectDirectory, "obj", "SharpProof", "cache")
                : Path.IsPathFullyQualified(request.Cache.Directory)
                    ? request.Cache.Directory
                    : Path.GetFullPath(request.Cache.Directory, projectDirectory);
            return new VerificationCache(directory, request.Cache.MaximumBytes);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            status = WorkerCacheStatus.Unavailable;
            return null;
        }
    }

    private static WorkerVerifyResponse ManifestFailure(
        string inputHash, WorkerClaimManifest manifest, WorkerBudgets budgets, long started,
        WorkerRunFailureReason reason, IEnumerable<WorkerProtocolError> errors, string requestHash,
        WorkerClaimReason claimReason = WorkerClaimReason.InfrastructureFailure)
    {
        return WorkerResultAssembler.CreateIncomplete(
            inputHash, requestHash, manifest, budgets,
            WorkerRunStatus.Failed, reason,
            WorkerCallableCoverageReason.InfrastructureFailure,
            claimReason, errors, Versions(),
            Elapsed(started));
    }

    private static WorkerVerifyResponse Failure(string inputHash, WorkerRunFailureReason reason,
        WorkerBudgets budgets, long started, IEnumerable<WorkerProtocolError> errors, string? requestHash = null)
    {
        return WorkerResultAssembler.Create(
            string.IsNullOrEmpty(inputHash) ? WorkerResultAssembler.EmptyInputHash : inputHash,
            WorkerResultAssembler.EmptyManifest(), WorkerRunStatus.Failed, reason,
            [], [], budgets, WorkerCacheStatus.Disabled, Elapsed(started), errors,
            requestHash, Versions());
    }

    private static WorkerVersionSummary Versions()
    {
        return new()
        {
            WorkerVersion = WorkerCacheIdentity.Current.ToolVersion,
            ApiSpecVersion = WorkerCacheIdentity.Current.ApiSpecVersion,
            WorkerBinarySha256 = WorkerCacheIdentity.Current.WorkerBinarySha256,
            ApiSpecContentSha256 = WorkerCacheIdentity.Current.ApiSpecContentSha256
        };
    }

    private static WorkerProtocolError[] Error(string code, string message)
    {
        return [new WorkerProtocolError { Code = code, Message = message }];
    }

    private static long Elapsed(long started)
    {
        return (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private bool TryCreateLanes(WorkerBudgets budgets, int targetCount,
        out VerificationLane[] lanes, out string? error)
    {
        lanes = [];
        error = null;
        if (targetCount == 0)
        {
            return true;
        }

        if (_backend != null)
        {
            lanes = [CreateLane(_backend, budgets.MaximumExpressionDepth, null, null,
                _readConsumedResourceCount)];
            return true;
        }
        var created = new List<VerificationLane>();
        try
        {
            for (var index = 0; index < Math.Min(budgets.MaxParallelism, targetCount); index++)
            {
                var backend = _backendFactory!() ??
                    throw new InvalidOperationException("The backend factory returned null.");
                if (created.Any(lane => ReferenceEquals(lane.Backend, backend)))
                {
                    throw new InvalidOperationException("The backend factory returned the same backend for multiple lanes.");
                }

                created.Add(CreateLane(backend, budgets.MaximumExpressionDepth,
                    backend as IDisposable, _backendFactory));
            }
            lanes = [.. created];
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and
            not StackOverflowException and not OperationCanceledException)
        {
            foreach (var lane in created)
            {
                lane.DisposeOwnedBackend();
            }

            error = exception.GetBaseException().Message;
            return false;
        }
    }

    private static VerificationLane CreateLane(
        ISmtBackend backend, int maximumExpressionDepth,
        IDisposable? owner, Func<ISmtBackend>? factory, Func<long>? resourceReader = null)
    {
        return new(backend, new CallableVerifier(backend, maximumExpressionDepth),
            resourceReader ?? ReadResources(backend), owner, factory);
    }

    private static Func<long>? ReadResources(ISmtBackend backend)
    {
        return backend is IrSmtBackend concrete ? () => concrete.ConsumedResourceCount : null;
    }

    private sealed class VerificationLane(
        ISmtBackend backend, CallableVerifier verifier, Func<long>? readConsumedResourceCount,
        IDisposable? ownedBackend, Func<ISmtBackend>? backendFactory)
    {
        private readonly Func<ISmtBackend>? _backendFactory = backendFactory;
        private IDisposable? _ownedBackend = ownedBackend;
        internal ISmtBackend Backend { get; private set; } = backend;
        internal CallableVerifier Verifier { get; private set; } = verifier;
        internal Func<long>? ReadConsumedResourceCount { get; private set; } = readConsumedResourceCount;
        internal bool TryRenew(VerificationLane[] lanes, int maximumExpressionDepth)
        {
            if (_backendFactory == null)
            {
                return false;
            }

            lock (lanes)
            {
                var prior = Backend;
                _ownedBackend?.Dispose();
                _ownedBackend = null;
                IDisposable? replacementOwner = null;
                try
                {
                    var replacement = _backendFactory() ??
                        throw new InvalidOperationException("The backend factory returned null.");
                    if (ReferenceEquals(replacement, prior) ||
                        lanes.Any(lane => !ReferenceEquals(lane, this) &&
                            ReferenceEquals(lane.Backend, replacement)))
                    {
                        return false;
                    }

                    replacementOwner = replacement as IDisposable;
                    Backend = replacement;
                    Verifier = new CallableVerifier(replacement, maximumExpressionDepth);
                    ReadConsumedResourceCount = ReadResources(replacement);
                    _ownedBackend = replacementOwner;
                    return true;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException and
                    not StackOverflowException and not OperationCanceledException)
                {
                    replacementOwner?.Dispose();
                    return false;
                }
            }
        }
        internal void DisposeOwnedBackend()
        {
            _ownedBackend?.Dispose();
            _ownedBackend = null;
        }
    }
}
