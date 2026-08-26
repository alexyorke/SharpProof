using static SharpProof.Worker.CallableVerificationPolicy;
using SharpProof.Host;

namespace SharpProof.Worker;

public sealed class SharpProofWorker : IDisposable
{
    internal static Action? CachedResponseAssemblyOverride;
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
            () =>
            {
                ContainerNativeLibrary.InstallZ3ResolverRequired(
                    typeof(Microsoft.Z3.Context).Assembly);
                return new IrSmtBackend(
                    new IrSmtBackendOptions(budgets.QueryRlimit));
            });
    }
    public async Task<WorkerVerifyResponse> VerifyAsync(
        WorkerVerifyRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var started = Stopwatch.GetTimestamp();
        ArgumentNullException.ThrowIfNull(request);
        var validation = WorkerProtocolJson.Validate(request);
        if (!validation.IsValid)
        {
            return Failure(string.Empty, WorkerRunFailureReason.InvalidRequest, new WorkerBudgets(), started, validation.Errors);
        }

        const int CallerCancellation = 1;
        const int ProjectDeadline = 2;
        var interruptionCause = 0;
        var interruptionCacheStatus = WorkerCacheStatus.Disabled;
        using var projectDeadline = new CancellationTokenSource();
        using var projectBoundary = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            projectDeadline.Token);
        using var callerCauseRegistration = cancellationToken.Register(
            () => Interlocked.CompareExchange(
                ref interruptionCause,
                CallerCancellation,
                0));
        using var deadlineCauseRegistration = projectDeadline.Token.Register(
            () => Interlocked.CompareExchange(
                ref interruptionCause,
                ProjectDeadline,
                0));
        var remainingMilliseconds = request.Budgets.ProjectWallTimeMilliseconds -
            Elapsed(started);
        if (remainingMilliseconds <= 0)
        {
            await projectDeadline.CancelAsync().ConfigureAwait(false);
        }
        else
        {
            projectDeadline.CancelAfter((int)Math.Min(
                remainingMilliseconds,
                int.MaxValue));
        }
        var requestHash = WorkerProtocolJson.ComputeRequestHash(request);
        bool CallerCancellationWon()
        {
            var cause = Volatile.Read(ref interruptionCause);
            return cause == CallerCancellation ||
                cause == 0 && cancellationToken.IsCancellationRequested &&
                !projectDeadline.IsCancellationRequested;
        }

        WorkerVerifyResponse Failed(WorkerRunFailureReason reason, string code, string message, string inputHash = "")
        {
            return Failure(inputHash, reason, request.Budgets, started, Error(code, message), requestHash);
        }

        WorkerVerifyResponse Interrupted(WorkerInputSnapshot? input = null)
        {
            var canceled = CallerCancellationWon();
            return WorkerResultAssembler.CreateIncomplete(
                input?.InputHash ?? WorkerResultAssembler.EmptyInputHash, requestHash,
                input?.CompilerManifest.Manifest ?? WorkerResultAssembler.EmptyManifest(), request.Budgets,
                canceled ? WorkerRunStatus.Canceled : WorkerRunStatus.TimedOut, WorkerRunFailureReason.None,
                canceled ? WorkerCallableCoverageReason.Canceled : WorkerCallableCoverageReason.ProjectTimeout,
                canceled ? WorkerClaimReason.Canceled : WorkerClaimReason.ProjectTimeout,
                errors: input == null
                    ? Error(
                        canceled ? "worker.canceled" : "worker.timeout",
                        canceled
                            ? "The worker was canceled before loading the compiler manifest."
                            : "The project timed out before loading the compiler manifest.")
                    : null,
                versions: Versions(), elapsedMilliseconds: Elapsed(started),
                cacheStatus: interruptionCacheStatus);
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

        if (CallerCancellationWon())
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
            var responseAuthority = new CompilerResponseEvidenceAuthority(targets);
            WorkerVerifyResponse Assemble(WorkerRunStatus status, WorkerRunFailureReason reason,
                IEnumerable<WorkerCallableResult> callables, IEnumerable<WorkerClaimResult> claims,
                WorkerCacheStatus resultCacheStatus, IEnumerable<WorkerProtocolError>? errors = null)
            {
                projectBoundary.Token.ThrowIfCancellationRequested();
                var assembled = WorkerResultAssembler.Create(snapshot.InputHash, manifest, status, reason, callables, claims,
                    request.Budgets, resultCacheStatus, Elapsed(started), errors,
                    requestHash, Versions());
                projectBoundary.Token.ThrowIfCancellationRequested();
                return assembled;
            }
            WorkerVerifyResponse Canceled(WorkerCacheStatus status)
            {
                var lanes = targets.Select(target => Unknown(
                    target, WorkerClaimReason.Canceled, WorkerCallableCoverageReason.Canceled)).ToArray();
                return WorkerResultAssembler.Create(
                    snapshot.InputHash,
                    manifest,
                    WorkerRunStatus.Canceled,
                    WorkerRunFailureReason.None,
                    lanes.Select(static lane => lane.Callable),
                    lanes.SelectMany(static lane => lane.Claims),
                    request.Budgets,
                    status,
                    Elapsed(started),
                    requestHash: requestHash,
                    versions: Versions());
            }

            var cache = CreateCacheIfEnabled(request,
                snapshot.CompilerManifest.Compilation.ProjectDirectory, out var cacheStatus);
            interruptionCacheStatus = cacheStatus;
            if (cache != null)
            {
                var cached = await cache.TryReadAsync(
                    snapshot.InputHash,
                    manifest,
                    targets,
                    request.Budgets,
                    projectBoundary.Token).ConfigureAwait(false);
                if (cache.LastReadUnavailable)
                {
                    interruptionCacheStatus = WorkerCacheStatus.Unavailable;
                }
                projectBoundary.Token.ThrowIfCancellationRequested();
                if (cached != null)
                {
                    CachedResponseAssemblyOverride?.Invoke();
                    var cachedResponse = Assemble(
                        WorkerRunStatus.Complete,
                        WorkerRunFailureReason.None,
                        cached.CallableResults,
                        cached.ClaimResults,
                        WorkerCacheStatus.Hit);
                    if (WorkerProtocolJson.Validate(
                            cachedResponse,
                            snapshot.InputHash,
                            manifest,
                        responseAuthority).IsValid)
                    {
                        projectBoundary.Token.ThrowIfCancellationRequested();
                        interruptionCacheStatus = WorkerCacheStatus.Hit;
                        return cachedResponse;
                    }
                }
            }
            if (!TryCreateLanes(request.Budgets, targets.Length, out solverLanes, out var backendError))
            {
                projectBoundary.Token.ThrowIfCancellationRequested();
                return FailedAfterManifest(WorkerRunFailureReason.BackendUnavailable,
                    Error("backend.unavailable", "The native SMT backend is unavailable: " + backendError),
                    WorkerClaimReason.BackendUnavailable);
            }
            var orderedTargets = targets.OrderBy(
                static target => target.Entry.CallableId, StringComparer.Ordinal).ToArray();
            var results = new CallableVerificationResult[orderedTargets.Length];
            var nextTarget = -1;
            var retirementSynchronization = new object();
            var retirementCallableReason = WorkerCallableCoverageReason.InfrastructureFailure;
            var retirementClaimReason = WorkerClaimReason.InfrastructureFailure;
            var hasRetirementReason = false;
            void RecordRetirement(
                WorkerCallableCoverageReason callableReason,
                WorkerClaimReason claimReason)
            {
                lock (retirementSynchronization)
                {
                    if (hasRetirementReason)
                    {
                        return;
                    }
                    retirementCallableReason = callableReason;
                    retirementClaimReason = claimReason;
                    hasRetirementReason = true;
                }
            }
            async Task RunLane(VerificationLane lane)
            {
                while (true)
                {
                    lock (retirementSynchronization)
                    {
                        if (hasRetirementReason)
                        {
                            return;
                        }
                    }
                    var index = Interlocked.Increment(ref nextTarget);
                    if (index >= orderedTargets.Length)
                    {
                        return;
                    }

                    var result = await VerifyTargetAsync(lane.Verifier, orderedTargets[index], request.Budgets,
                        lane.ReadConsumedResourceCount, request.Budgets.MethodWallTimeMilliseconds,
                        projectBoundary, cancellationToken).ConfigureAwait(false);
                    results[index] = result;
                    if (result.Callable.Reason ==
                            WorkerCallableCoverageReason.MethodTimeout &&
                        !projectBoundary.IsCancellationRequested)
                    {
                        var renewal = lane.Renew(
                            solverLanes,
                            request.Budgets.MaximumExpressionDepth);
                        if (renewal != LaneRenewalResult.Success)
                        {
                            RecordRetirement(
                                renewal == LaneRenewalResult.Unsupported
                                    ? WorkerCallableCoverageReason.MethodTimeout
                                    : WorkerCallableCoverageReason.InfrastructureFailure,
                                renewal switch
                                {
                                    LaneRenewalResult.Unsupported =>
                                        WorkerClaimReason.MethodTimeout,
                                    LaneRenewalResult.BackendUnavailable =>
                                        WorkerClaimReason.BackendUnavailable,
                                    _ => WorkerClaimReason.InfrastructureFailure
                                });
                            return;
                        }
                    }
                }
            }
            await Task.WhenAll(solverLanes.Select(RunLane)).ConfigureAwait(false);
            if (CallerCancellationWon())
            {
                return Canceled(interruptionCacheStatus);
            }
            projectBoundary.Token.ThrowIfCancellationRequested();

            for (var index = 0; index < results.Length; index++)
            {
                projectBoundary.Token.ThrowIfCancellationRequested();
                if (results[index] == null)
                {
                    lock (retirementSynchronization)
                    {
                        results[index] = Unknown(
                            orderedTargets[index],
                            retirementClaimReason,
                            retirementCallableReason);
                    }
                }
            }

            var callableResults = results.Select(static result => result.Callable).ToArray();
            var claimResults = results.SelectMany(static result => result.Claims).ToArray();
            projectBoundary.Token.ThrowIfCancellationRequested();
            var run = WorkerResultAssembler.Classify(callableResults, claimResults);
            var response = Assemble(run.Status, run.Failure, callableResults, claimResults, cacheStatus);
            var responseValidation = WorkerProtocolJson.Validate(
                response,
                snapshot.InputHash,
                manifest,
                responseAuthority);
            if (!responseValidation.IsValid)
            {
                var malformed = targets.Select(target => Unknown(target, WorkerClaimReason.InfrastructureFailure,
                    WorkerCallableCoverageReason.MissingClaimResult)).ToArray();
                return Assemble(WorkerRunStatus.Failed, WorkerRunFailureReason.MalformedResult,
                    malformed.Select(static lane => lane.Callable),
                    malformed.SelectMany(static lane => lane.Claims),
                    WorkerCacheStatus.Rejected, responseValidation.Errors);
            }
            if (cache != null)
            {
                if (VerificationCache.IsCacheable(
                        response,
                        snapshot.InputHash,
                        manifest,
                        targets,
                        projectBoundary.Token))
                {
                    var written = await cache.TryWriteAsync(
                        response, snapshot.InputHash, manifest, projectBoundary.Token).ConfigureAwait(false);
                    if (written)
                    {
                        interruptionCacheStatus = WorkerCacheStatus.Written;
                        // A successful cache commit is the terminal winner for
                        // this invocation. Do not let a cancellation racing the
                        // commit turn the same run into a contradictory response.
                        return WorkerResultAssembler.Create(
                            snapshot.InputHash,
                            manifest,
                            run.Status,
                            run.Failure,
                            callableResults,
                            claimResults,
                            request.Budgets,
                            WorkerCacheStatus.Written,
                            Elapsed(started),
                            requestHash: requestHash,
                            versions: Versions());
                    }
                    interruptionCacheStatus = WorkerCacheStatus.Unavailable;
                    response = Assemble(run.Status, run.Failure, callableResults, claimResults,
                        WorkerCacheStatus.Unavailable);
                }
                else if (VerificationCache.IsStorable(response, manifest))
                {
                    response = Assemble(run.Status, run.Failure, callableResults, claimResults,
                        WorkerCacheStatus.Rejected);
                }
            }
            projectBoundary.Token.ThrowIfCancellationRequested();
            return response;
        }
        catch (OperationCanceledException) { return Interrupted(snapshot); }
        finally
        {
            foreach (var lane in solverLanes)
            {
                try
                {
                    lane.DisposeOwnedBackend();
                }
                catch (Exception exception) when (
                    exception is not OutOfMemoryException and
                    not StackOverflowException and
                    not OperationCanceledException)
                {
                    // Cleanup must not replace an already assembled verifier
                    // response. Every lane is still attempted below.
                }
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
            var directory = WorkerCachePath.Resolve(
                request.Cache.Directory,
                projectDirectory);
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
                try
                {
                    lane.DisposeOwnedBackend();
                }
                catch (Exception cleanupException) when (
                    cleanupException is not OutOfMemoryException and
                    not StackOverflowException and
                    not OperationCanceledException)
                {
                }
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
        internal LaneRenewalResult Renew(
            VerificationLane[] lanes,
            int maximumExpressionDepth)
        {
            if (_backendFactory == null)
            {
                return LaneRenewalResult.Unsupported;
            }

            lock (lanes)
            {
                var prior = Backend;
                IDisposable? replacementOwner = null;
                try
                {
                    var priorOwner = _ownedBackend;
                    _ownedBackend = null;
                    priorOwner?.Dispose();
                    var replacement = _backendFactory() ??
                        throw new InvalidOperationException("The backend factory returned null.");
                    if (ReferenceEquals(replacement, prior) ||
                        lanes.Any(lane => !ReferenceEquals(lane, this) &&
                            ReferenceEquals(lane.Backend, replacement)))
                    {
                        (replacement as IDisposable)?.Dispose();
                        return LaneRenewalResult.BackendUnavailable;
                    }

                    replacementOwner = replacement as IDisposable;
                    Backend = replacement;
                    Verifier = new CallableVerifier(replacement, maximumExpressionDepth);
                    ReadConsumedResourceCount = ReadResources(replacement);
                    _ownedBackend = replacementOwner;
                    return LaneRenewalResult.Success;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException and
                    not StackOverflowException and not OperationCanceledException)
                {
                    replacementOwner?.Dispose();
                    return Program.IsBackendUnavailable(exception)
                        ? LaneRenewalResult.BackendUnavailable
                        : LaneRenewalResult.InfrastructureFailure;
                }
            }
        }
        internal void DisposeOwnedBackend()
        {
            try
            {
                _ownedBackend?.Dispose();
            }
            finally
            {
                _ownedBackend = null;
            }
        }
    }

    private enum LaneRenewalResult
    {
        Success,
        Unsupported,
        BackendUnavailable,
        InfrastructureFailure
    }
}
