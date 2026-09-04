using static SharpProof.Worker.CallableVerificationPolicy;
using SharpProof.Host;
using System.Threading.Channels;

namespace SharpProof.Worker;

public sealed class SharpProofWorker : IDisposable
{
    private readonly ISmtBackend? _backend;
    private readonly Func<ISmtBackend>? _backendFactory;
    private readonly uint? _configuredQueryRlimit;
    private readonly Func<long>? _readConsumedResourceCount;
    private readonly Channel<byte>? _injectedBackendRunGate;
    private bool _disposed;
    // An injected backend cannot be renewed after interruption.  Once a run
    // has timed out or been cancelled, fail closed rather than handing the
    // potentially poisoned instance to a later request.
    private bool _injectedBackendPoisoned;
    public SharpProofWorker(ISmtBackend backend) : this(
        backend, ReadResources(backend))
    {
    }
    internal SharpProofWorker(ISmtBackend backend, Func<long>? readConsumedResourceCount)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        _readConsumedResourceCount = readConsumedResourceCount;
        _injectedBackendRunGate = CreateInjectedBackendRunGate();
    }
    internal SharpProofWorker(Func<ISmtBackend> backendFactory)
    {
        ArgumentNullException.ThrowIfNull(backendFactory);
        _backendFactory = backendFactory;
    }
    private SharpProofWorker(Func<ISmtBackend> backendFactory, uint configuredQueryRlimit)
        : this(backendFactory)
    {
        _configuredQueryRlimit = configuredQueryRlimit;
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
            }, budgets.QueryRlimit);
    }
    public async Task<WorkerVerifyResponse> VerifyAsync(
        WorkerVerifyRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var started = Stopwatch.GetTimestamp();
        var validation = WorkerProtocolJson.Validate(request);
        if (!validation.IsValid)
        {
            return Failure(string.Empty, WorkerRunFailureReason.InvalidRequest, new WorkerBudgets(), started, validation.Errors);
        }

        if (_configuredQueryRlimit.HasValue &&
            request.Budgets.QueryRlimit != _configuredQueryRlimit.Value)
        {
            return Failure(string.Empty, WorkerRunFailureReason.InvalidRequest,
                request.Budgets, started,
                Error("budgets.query_rlimit_mismatch",
                    "The request query rlimit must match the worker creation limit."));
        }

        var requestHash = WorkerProtocolJson.ComputeRequestHash(request);
        using var projectBoundary =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        var remainingProjectMilliseconds =
            request.Budgets.ProjectWallTimeMilliseconds - Elapsed(started);
        var projectDeadlineExpired = remainingProjectMilliseconds <= 0;
        if (!projectDeadlineExpired)
        {
            projectBoundary.CancelAfter(
                checked((int)remainingProjectMilliseconds));
        }
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
                errors: input == null
                    ? Error(
                        canceled ? "worker.canceled" : "worker.timeout",
                        canceled
                            ? "The worker was canceled before loading the compiler manifest."
                            : "The project timed out before loading the compiler manifest.")
                    : null,
                versions: Versions(), elapsedMilliseconds: Elapsed(started));
        }
        if (projectDeadlineExpired)
        {
            return Interrupted();
        }
        WorkerInputSnapshot snapshot;
        VerificationLane[] solverLanes = [];
        var ownsInjectedBackendRunGate = false;
        try
        {
            snapshot = await WorkerInputSnapshot.LoadAsync(
                request,
                WorkerCacheIdentity.Current,
                projectBoundary.Token).ConfigureAwait(false);
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
                targets = CompilerManifestArtifactJson.DecodeCallables(
                    snapshot.CompilerManifest,
                    projectBoundary.Token);
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
                return assembled;
            }
            WorkerVerifyResponse Canceled(WorkerCacheStatus status)
            {
                var lanes = targets.Select(target => Unknown(
                    target, WorkerClaimReason.Canceled, WorkerCallableCoverageReason.Canceled)).ToArray();
                var projected = ProjectResults(lanes);
                return WorkerResultAssembler.Create(
                    snapshot.InputHash,
                    manifest,
                    WorkerRunStatus.Canceled,
                    WorkerRunFailureReason.None,
                    projected.Callables,
                    projected.Claims,
                    request.Budgets,
                    status,
                    Elapsed(started),
                    requestHash: requestHash,
                    versions: Versions());
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
                            responseAuthority,
                            projectBoundary.Token).IsValid)
                    {
                        return cachedResponse;
                    }
                }
                if (cache.LastReadUnavailable)
                {
                    cacheStatus = WorkerCacheStatus.Unavailable;
                }
            }
            if (_backend != null)
            {
                var injectedBackendRunGate = _injectedBackendRunGate ??
                    throw new InvalidOperationException(
                        "The injected backend run gate was not initialized.");
                await injectedBackendRunGate.Reader.ReadAsync(
                        projectBoundary.Token)
                    .ConfigureAwait(false);
                ownsInjectedBackendRunGate = true;
            }
            var orderedTargets = targets.OrderBy(
                static target => target.Entry.CallableId, StringComparer.Ordinal).ToArray();
            var laneCreation = TryCreateLanes(
                request.Budgets,
                CountSolverTargets(orderedTargets),
                out solverLanes,
                out var laneError);
            if (laneCreation != LaneCreationResult.Success)
            {
                var backendUnavailable =
                    laneCreation == LaneCreationResult.BackendUnavailable;
                return FailedAfterManifest(
                    backendUnavailable
                        ? WorkerRunFailureReason.BackendUnavailable
                        : WorkerRunFailureReason.InfrastructureFailure,
                    Error(
                        backendUnavailable
                            ? "backend.unavailable"
                            : "worker.infrastructure",
                        (backendUnavailable
                            ? "The native SMT backend is unavailable: "
                            : "The worker could not initialize verification lanes: ") +
                        laneError),
                    backendUnavailable
                        ? WorkerClaimReason.BackendUnavailable
                        : WorkerClaimReason.InfrastructureFailure);
            }
            var results = new CallableVerificationResult[orderedTargets.Length];
            for (var index = 0; index < orderedTargets.Length; index++)
            {
                if (!orderedTargets[index].IsSuccess)
                {
                    results[index] = CallableVerificationPolicy.FailedLowering(
                        orderedTargets[index], projectBoundary.Token);
                }
            }
            var nextTarget = -1;
            var retirementSynchronization = new object();
            var retirementCallableReason = WorkerCallableCoverageReason.InfrastructureFailure;
            var retirementClaimReason = WorkerClaimReason.InfrastructureFailure;
            var hasRetirementReason = false;
            var retirementRank = -1;
            void RecordRetirement(
                WorkerCallableCoverageReason callableReason,
                WorkerClaimReason claimReason)
            {
                lock (retirementSynchronization)
                {
                    // Several lanes can renew concurrently.  Select the
                    // strongest failure by a stable policy instead of letting
                    // scheduler lock acquisition decide the response reason.
                    var rank = claimReason switch
                    {
                        WorkerClaimReason.BackendUnavailable => 2,
                        WorkerClaimReason.InfrastructureFailure => 1,
                        _ => 0
                    };
                    if (hasRetirementReason && rank <= retirementRank)
                    {
                        return;
                    }
                    retirementCallableReason = callableReason;
                    retirementClaimReason = claimReason;
                    hasRetirementReason = true;
                    retirementRank = rank;
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

                    if (!orderedTargets[index].IsSuccess)
                    {
                        continue;
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
                            if (renewal == LaneRenewalResult.Unsupported && _backend != null)
                            {
                                _injectedBackendPoisoned = true;
                            }
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
            if (cancellationToken.IsCancellationRequested)
            {
                return Canceled(cacheStatus);
            }
            projectBoundary.Token.ThrowIfCancellationRequested();

            WorkerCallableCoverageReason completedRetirementCallableReason;
            WorkerClaimReason completedRetirementClaimReason;
            lock (retirementSynchronization)
            {
                completedRetirementCallableReason = retirementCallableReason;
                completedRetirementClaimReason = retirementClaimReason;
            }
            for (var index = 0; index < results.Length; index++)
            {
                projectBoundary.Token.ThrowIfCancellationRequested();
                if (results[index] == null)
                {
                    results[index] = Unknown(
                        orderedTargets[index],
                        completedRetirementClaimReason,
                        completedRetirementCallableReason);
                }
            }

            var projected = ProjectResults(results);
            var callableResults = projected.Callables;
            var claimResults = projected.Claims;
            projectBoundary.Token.ThrowIfCancellationRequested();
            var run = WorkerResultAssembler.Classify(callableResults, claimResults);
            var response = Assemble(run.Status, run.Failure, callableResults, claimResults, cacheStatus);
            var responseValidation = WorkerProtocolJson.Validate(
                response,
                snapshot.InputHash,
                manifest,
                responseAuthority,
                projectBoundary.Token);
            if (!responseValidation.IsValid)
            {
                var malformed = targets.Select(target => Unknown(target, WorkerClaimReason.InfrastructureFailure,
                    WorkerCallableCoverageReason.MissingClaimResult)).ToArray();
                var malformedProjection = ProjectResults(malformed);
                return Assemble(WorkerRunStatus.Failed, WorkerRunFailureReason.MalformedResult,
                    malformedProjection.Callables,
                    malformedProjection.Claims,
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
            projectBoundary.Token.ThrowIfCancellationRequested();
            return response;
        }
        catch (OperationCanceledException) { return Interrupted(snapshot); }
        finally
        {
            if (ownsInjectedBackendRunGate && projectBoundary.IsCancellationRequested)
            {
                _injectedBackendPoisoned = true;
            }
            foreach (var lane in solverLanes)
            {
                lane.DisposeOwnedBackend();
            }
            if (ownsInjectedBackendRunGate)
            {
                _ = _injectedBackendRunGate!.Writer.TryWrite(0);
            }
        }
    }
    public void Dispose()
    {
        _disposed = true;
    }

    private static Channel<byte> CreateInjectedBackendRunGate()
    {
        var gate = Channel.CreateBounded<byte>(1);
        if (!gate.Writer.TryWrite(0))
        {
            throw new InvalidOperationException(
                "The injected backend run gate could not be initialized.");
        }

        return gate;
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

    private LaneCreationResult TryCreateLanes(
        WorkerBudgets budgets,
        int targetCount,
        out VerificationLane[] lanes, out string? error)
    {
        lanes = [];
        error = null;
        if (targetCount == 0)
        {
            return LaneCreationResult.Success;
        }

        if (_backend != null)
        {
            if (_injectedBackendPoisoned)
            {
                error = "The injected SMT backend was interrupted and cannot be reused.";
                return LaneCreationResult.InfrastructureFailure;
            }
            lanes = [CreateLane(_backend, budgets.MaximumExpressionDepth, null, null,
                _readConsumedResourceCount)];
            return LaneCreationResult.Success;
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
            return LaneCreationResult.Success;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and
            not StackOverflowException and not OperationCanceledException)
        {
            foreach (var lane in created)
            {
                lane.DisposeOwnedBackend();
            }

            error = exception.GetBaseException().Message;
            return Program.IsBackendUnavailable(exception)
                ? LaneCreationResult.BackendUnavailable
                : LaneCreationResult.InfrastructureFailure;
        }
    }

    internal static int CountSolverTargets(
        IEnumerable<CompilerCallablePreparation> targets)
    {
        return targets.Count(static target => target.IsSuccess);
    }

    private static (WorkerCallableResult[] Callables, WorkerClaimResult[] Claims)
        ProjectResults(IReadOnlyList<CallableVerificationResult> results)
    {
        var callables = new WorkerCallableResult[results.Count];
        var claims = new List<WorkerClaimResult>();
        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];
            callables[index] = result.Callable;
            claims.AddRange(result.Claims);
        }

        return (callables, [.. claims]);
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
                    var replacement = _backendFactory() ??
                        throw new InvalidOperationException("The backend factory returned null.");
                    if (ReferenceEquals(replacement, prior) ||
                        lanes.Any(lane => !ReferenceEquals(lane, this) &&
                            ReferenceEquals(lane.Backend, replacement)))
                    {
                        (replacement as IDisposable)?.Dispose();
                        return LaneRenewalResult.BackendUnavailable;
                    }

                    // Do not tear down the currently healthy backend until the
                    // replacement has been accepted.  A factory can return an
                    // instance already owned by another lane; disposing the
                    // prior backend before this check can destroy live work.
                    var priorOwner = _ownedBackend;
                    _ownedBackend = null;
                    priorOwner?.Dispose();
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
            var ownedBackend = _ownedBackend;
            _ownedBackend = null;
            if (ownedBackend == null)
            {
                return;
            }

            try
            {
                ownedBackend.Dispose();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and
                not StackOverflowException and not OperationCanceledException)
            {
                // Backend cleanup is best-effort and cannot replace a
                // completed response or interrupt cleanup of later lanes.
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

    private enum LaneCreationResult
    {
        Success,
        BackendUnavailable,
        InfrastructureFailure
    }
}
