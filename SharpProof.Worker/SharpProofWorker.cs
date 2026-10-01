using static SharpProof.Worker.CallableVerificationPolicy;
using SharpProof.Host;
using System.Threading.Channels;

namespace SharpProof.Worker;

public sealed class SharpProofWorker : IDisposable
{
    private readonly ISmtBackend? _backend;
    private readonly Func<ISmtBackend>? _backendFactory;
    private readonly uint? _configuredQueryRlimit;
    private readonly bool _nativeAuthority;
    private readonly Func<long>? _readConsumedResourceCount;
    private readonly Channel<byte>? _injectedBackendRunGate;
    private bool _disposed;
    // An injected backend cannot be renewed after interruption.  Once a run
    // has timed out or been cancelled, fail closed rather than handing the
    // potentially poisoned instance to a later request.
    private bool _injectedBackendPoisoned;
    internal Action<WorkerVcShadowReport>? ShadowReportSink { get; set; }
    public SharpProofWorker(ISmtBackend backend) : this(
        backend, ReadResources(backend))
    {
    }
    internal SharpProofWorker(ISmtBackend backend, Func<long>? readConsumedResourceCount, bool nativeAuthority = false)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        _readConsumedResourceCount = readConsumedResourceCount;
        _nativeAuthority = nativeAuthority;
        _injectedBackendRunGate = CreateInjectedBackendRunGate();
    }
    internal SharpProofWorker(Func<ISmtBackend> backendFactory, bool nativeAuthority = false)
    {
        ArgumentNullException.ThrowIfNull(backendFactory);
        _backendFactory = backendFactory;
        _nativeAuthority = nativeAuthority;
    }
    private SharpProofWorker(Func<ISmtBackend> backendFactory, uint configuredQueryRlimit, bool nativeAuthority = false)
        : this(backendFactory, nativeAuthority)
    {
        _configuredQueryRlimit = configuredQueryRlimit;
    }
    public static SharpProofWorker Create(WorkerBudgets budgets)
    {
        budgets = budgets ?? throw new ArgumentNullException(nameof(budgets));
        var queryRlimit = budgets.QueryRlimit;
        return new SharpProofWorker(
            () =>
            {
                ContainerNativeLibrary.InstallZ3ResolverRequired(
                    typeof(Microsoft.Z3.Context).Assembly);
                return new IrSmtBackend(
                    new IrSmtBackendOptions(queryRlimit));
            }, queryRlimit);
    }
    internal static SharpProofWorker CreateNative(WorkerBudgets budgets)
    {
        ArgumentNullException.ThrowIfNull(budgets);
        var queryRlimit = budgets.QueryRlimit;
        return new SharpProofWorker(() =>
        {
            ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
            return new NativeCallableBackend(new IrSmtBackendOptions(queryRlimit));
        }, queryRlimit, nativeAuthority: true);
    }

    public Task<WorkerVerifyResponse> VerifyAsync(
        WorkerVerifyRequest request, CancellationToken cancellationToken = default)
    {
        return VerifyAsync(request, null, cancellationToken);
    }

    internal async Task<WorkerVerifyResponse> VerifyAsync(
        WorkerVerifyRequest request, WorkerInputSnapshot? preparedInput,
        CancellationToken cancellationToken, long? operationStarted = null)
    {
        request = request ?? throw new ArgumentNullException(nameof(request));
        var shadow = string.Equals(Environment.GetEnvironmentVariable("SHARPPROOF_VC"), "shadow", StringComparison.Ordinal);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var started = operationStarted ?? Stopwatch.GetTimestamp();
        var validation = WorkerProtocolJson.Validate(request);
        if (!validation.IsValid)
        {
            return Failure(string.Empty, WorkerRunFailureReason.InvalidRequest, new WorkerBudgets(), started, validation.Errors);
        }

        var budgets = request.Budgets;
        if (_configuredQueryRlimit.HasValue &&
            budgets.QueryRlimit != _configuredQueryRlimit.Value)
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
        void ThrowIfProjectInterrupted()
        {
            if (Elapsed(started) >= request.Budgets.ProjectWallTimeMilliseconds)
            {
                projectBoundary.Cancel();
            }
            projectBoundary.Token.ThrowIfCancellationRequested();
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
                        canceled ? "worker.canceled" : WorkerProtocolErrorCodes.WorkerTimeout,
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
        var authoritativeResponseCompleted = false;
        try
        {
            snapshot = preparedInput == null
                ? WorkerInputSnapshot.Load(request, WorkerCacheIdentity.Current, projectBoundary.Token)
                : ArtifactValidator.Bind(request,
                    new ValidatedArtifact(preparedInput.CompilerManifest,
                        preparedInput.Callables, preparedInput.ArtifactDigest),
                    WorkerCacheIdentity.Current);
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
        catch (InvalidDataException)
        {
            return Failed(WorkerRunFailureReason.CompilerManifestMismatch, "compiler_manifest.invalid",
                "The prepared artifact does not match the request.");
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

        async Task<WorkerVerifyResponse> ObserveShadow(WorkerVerifyResponse authoritative)
        {
            if (!shadow)
            { return authoritative; }
            try
            {
                var report = await WorkerVcShadowObserver.ObserveAsync(authoritative, snapshot.Callables, budgets,
                    projectBoundary.Token, cancellationToken).ConfigureAwait(false);
                if (ShadowReportSink is { } sink)
                { sink(report); }
                else
                { await Console.Error.WriteLineAsync(report.Serialize()).ConfigureAwait(false); }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                // Optional observation and diagnostic I/O cannot replace an
                // already validated authoritative response or cache hit.
            }
            return authoritative;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Interrupted(snapshot);
        }

        try
        {
            ThrowIfProjectInterrupted();
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

            var targets = snapshot.Callables;
            ThrowIfProjectInterrupted();
            var manifest = snapshot.CompilerManifest.Manifest;
            WorkerVerifyResponse Assemble(WorkerRunStatus status, WorkerRunFailureReason reason,
                IEnumerable<WorkerCallableResult> callables, IEnumerable<WorkerClaimResult> claims,
                WorkerCacheStatus resultCacheStatus, IEnumerable<WorkerProtocolError>? errors = null)
            {
                ThrowIfProjectInterrupted();
                var assembled = WorkerResultAssembler.Create(snapshot.InputHash, manifest, status, reason, callables, claims,
                    request.Budgets, resultCacheStatus, Elapsed(started), errors,
                    requestHash, Versions());
                return assembled;
            }
            var cache = CreateCacheIfEnabled(request,
                snapshot.CompilerManifest.Compilation.ProjectDirectory, out var cacheStatus);
            if (cache != null)
            {
                var cached = await cache.TryReadAsync(
                    snapshot.InputHash,
                    manifest,
                    request.Budgets,
                    projectBoundary.Token).ConfigureAwait(false);
                ThrowIfProjectInterrupted();
                if (cached != null)
                {
                    var cachedResponse = WorkerResultAssembler.ApplyRequestContext(
                        cached,
                        requestHash,
                        request.Budgets,
                        Versions(),
                        WorkerCacheStatus.Hit,
                        Elapsed(started));
                    if (WorkerProtocolJson.Validate(
                            cachedResponse,
                            snapshot.InputHash,
                            manifest).IsValid)
                    {
                        ThrowIfProjectInterrupted();
                        return await ObserveShadow(cachedResponse).ConfigureAwait(false);
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
            ThrowIfProjectInterrupted();
            var orderedTargets = targets.OrderBy(
                static target => target.Entry.CallableId, StringComparer.Ordinal).ToArray();
            var laneCreation = TryCreateLanes(
                request.Budgets,
                CountSolverTargets(orderedTargets, _nativeAuthority),
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
                if (!CanVerifyTarget(orderedTargets[index], _nativeAuthority))
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

                    if (!CanVerifyTarget(orderedTargets[index], _nativeAuthority))
                    {
                        continue;
                    }

                    ThrowIfProjectInterrupted();
                    var result = _nativeAuthority
                        ? await VerifyNativeTargetAsync(lane.Backend, orderedTargets[index], request.Budgets,
                            lane.ReadConsumedResourceCount, request.Budgets.MethodWallTimeMilliseconds,
                            projectBoundary, cancellationToken).ConfigureAwait(false)
                        : await VerifyTargetAsync(lane.Verifier, orderedTargets[index], request.Budgets,
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
                return Interrupted(snapshot);
            }
            ThrowIfProjectInterrupted();

            WorkerCallableCoverageReason completedRetirementCallableReason;
            WorkerClaimReason completedRetirementClaimReason;
            lock (retirementSynchronization)
            {
                completedRetirementCallableReason = retirementCallableReason;
                completedRetirementClaimReason = retirementClaimReason;
            }
            for (var index = 0; index < results.Length; index++)
            {
                ThrowIfProjectInterrupted();
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
            ThrowIfProjectInterrupted();
            var run = WorkerResultAssembler.Classify(callableResults, claimResults);
            var response = Assemble(run.Status, run.Failure, callableResults, claimResults, cacheStatus);
            var responseValidation = WorkerProtocolJson.Validate(
                response,
                snapshot.InputHash,
                manifest);
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
                    manifest))
            {
                var written = await cache.TryWriteAsync(
                    response, snapshot.InputHash, manifest, projectBoundary.Token).ConfigureAwait(false);
                ThrowIfProjectInterrupted();
                response = WorkerResultAssembler.ApplyRequestContext(
                    response,
                    requestHash,
                    request.Budgets,
                    Versions(),
                    written ? WorkerCacheStatus.Written : WorkerCacheStatus.Unavailable,
                    Elapsed(started));
            }
            ThrowIfProjectInterrupted();
            authoritativeResponseCompleted = true;
            return await ObserveShadow(response).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return Interrupted(snapshot); }
        finally
        {
            if (ownsInjectedBackendRunGate && !authoritativeResponseCompleted && projectBoundary.IsCancellationRequested)
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
        if (!request.Cache.Enabled)
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
            return new VerificationCache(
                directory,
                request.Cache.MaximumBytes);
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
            lanes = [new VerificationLane(_backend, budgets.MaximumExpressionDepth, null, null,
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

                created.Add(new VerificationLane(backend, budgets.MaximumExpressionDepth,
                    backend as IDisposable, _backendFactory));
            }
            lanes = [.. created];
            return LaneCreationResult.Success;
        }
        catch (AggregateException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and
            not StackOverflowException and not OperationCanceledException)
        {
            foreach (var lane in created)
            {
                lane.DisposeOwnedBackend();
            }

            error = exception.GetBaseException().Message;
            return WorkerHost.IsBackendUnavailable(exception)
                ? LaneCreationResult.BackendUnavailable
                : LaneCreationResult.InfrastructureFailure;
        }
    }

    internal static int CountSolverTargets(
        IEnumerable<CompilerCallablePreparation> targets, bool nativeAuthority = false)
    {
        return targets.Count(target => CanVerifyTarget(target, nativeAuthority));
    }

    private static bool CanVerifyTarget(CompilerCallablePreparation target, bool nativeAuthority)
    { return target.IsSuccess || nativeAuthority && (target.Total != null || target.TotalEntry != null); }

    private static (WorkerCallableResult[] Callables, WorkerClaimResult[] Claims)
        ProjectResults(CallableVerificationResult[] results)
    {
        var callables = new WorkerCallableResult[results.Length];
        var claims = new List<WorkerClaimResult>();
        for (var index = 0; index < results.Length; index++)
        {
            var result = results[index];
            callables[index] = result.Callable;
            claims.AddRange(result.Claims);
        }

        return (callables, [.. claims]);
    }

    private static Func<long>? ReadResources(ISmtBackend backend)
    {
        return backend switch
        {
            IrSmtBackend concrete => () => concrete.ConsumedResourceCount,
            NativeCallableBackend native => () => native.ConsumedResourceCount,
            _ => null
        };
    }

    private sealed class VerificationLane(
        ISmtBackend backend, int maximumExpressionDepth,
        IDisposable? ownedBackend, Func<ISmtBackend>? backendFactory, Func<long>? resourceReader = null)
    {
        private readonly Func<ISmtBackend>? _backendFactory = backendFactory;
        private IDisposable? _ownedBackend = ownedBackend;
        private (ISmtBackend Backend, CallableVerifier Verifier, Func<long>? ResourceReader)
            _backend = ProjectBackend(backend, maximumExpressionDepth, resourceReader);
        internal ISmtBackend Backend => _backend.Backend;
        internal CallableVerifier Verifier => _backend.Verifier;
        internal Func<long>? ReadConsumedResourceCount => _backend.ResourceReader;
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
                        return LaneRenewalResult.BackendUnavailable;
                    }

                    // Do not tear down the currently healthy backend until the
                    // replacement has been accepted.  A factory can return an
                    // instance already owned by another lane; disposing the
                    // prior backend before this check can destroy live work.
                    var priorOwner = _ownedBackend;
                    replacementOwner = replacement as IDisposable;
                    _ownedBackend = null;
                    priorOwner?.Dispose();
                    _backend = ProjectBackend(replacement, maximumExpressionDepth);
                    _ownedBackend = replacementOwner;
                    replacementOwner = null;
                    return LaneRenewalResult.Success;
                }
                catch (AggregateException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException and
                    not StackOverflowException and not OperationCanceledException)
                {
                    return WorkerHost.IsBackendUnavailable(exception)
                        ? LaneRenewalResult.BackendUnavailable
                        : LaneRenewalResult.InfrastructureFailure;
                }
                finally
                {
                    DisposeBackend(replacementOwner);
                }
            }
        }
        private static (ISmtBackend, CallableVerifier, Func<long>?) ProjectBackend(
            ISmtBackend backend, int maximumExpressionDepth,
            Func<long>? resourceReader = null)
        {
            return (backend, new CallableVerifier(backend, maximumExpressionDepth),
                resourceReader ?? ReadResources(backend));
        }
        internal void DisposeOwnedBackend()
        {
            var ownedBackend = _ownedBackend;
            _ownedBackend = null;
            DisposeBackend(ownedBackend);
        }

        private static void DisposeBackend(IDisposable? ownedBackend)
        {
            if (ownedBackend == null)
            {
                return;
            }

            try
            {
                ownedBackend.Dispose();
            }
            catch (AggregateException)
            {
                throw;
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
