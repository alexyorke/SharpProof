namespace SharpProof.Worker;

public sealed class SharpProofWorker : IDisposable {
    private readonly ISmtBackend _backend;
    private readonly Func<long>? _readConsumedResourceCount;
    private readonly SemaphoreSlim _methodResourceGate = new(1, 1);
    private readonly IDisposable? _ownedBackend;
    private bool _disposed;

    public SharpProofWorker(ISmtBackend backend)
        : this(
            backend,
            backend is IrSmtBackend concrete
                ? () => concrete.ConsumedResourceCount
                : null) {
    }

    internal SharpProofWorker(
        ISmtBackend backend,
        Func<long>? readConsumedResourceCount) {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _readConsumedResourceCount = readConsumedResourceCount;
    }

    private SharpProofWorker(IrSmtBackend backend)
        : this(backend, () => backend.ConsumedResourceCount) =>
        _ownedBackend = backend;

    public static SharpProofWorker Create(WorkerBudgets budgets) {
        if (budgets == null) throw new ArgumentNullException(nameof(budgets));
        return new SharpProofWorker(
            new IrSmtBackend(
                new IrSmtBackendOptions(budgets.QueryRlimit)));
    }

    public async Task<WorkerVerifyResponse> VerifyAsync(
        WorkerVerifyRequest request,
        CancellationToken cancellationToken = default) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var validation = WorkerProtocolJson.Validate(request);
        if (!validation.IsValid)
            return ErrorResponse(string.Empty, validation.Errors);

        WorkerInputSnapshot snapshot;
        try {
            snapshot = await WorkerInputSnapshot.LoadAsync(
                request,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            throw;
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            ArgumentException) {
            return ErrorResponse(
                string.Empty,
                [new WorkerProtocolError {
                    Code = "input.unavailable",
                    Message = "A source or reference input could not be loaded."
                }]);
        }

        VerificationCache? cache = null;
        if (request.Cache.Enabled) {
            try {
                cache = CreateCache(request);
            }
            catch (Exception exception) when (exception is
                ArgumentException or
                NotSupportedException) {
                // An invalid cache location disables caching without
                // changing the verification result.
            }
        }
        if (cache != null) {
            var cached = await cache.TryReadAsync(
                snapshot.InputHash,
                cancellationToken).ConfigureAwait(false);
            if (cached != null) {
                cancellationToken.ThrowIfCancellationRequested();
                return cached;
            }
        }

        CSharpCompilation compilation;
        try {
            compilation = WorkerCompilation.Create(request, snapshot);
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            BadImageFormatException) {
            return ErrorResponse(
                snapshot.InputHash,
                [new WorkerProtocolError {
                    Code = "project.invalid_input",
                    Message = "A parse option or reference assembly is invalid."
                }]);
        }
        var compilerErrors = compilation.GetDiagnostics(cancellationToken)
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .OrderBy(static diagnostic =>
                diagnostic.Location.SourceTree?.FilePath,
                StringComparer.Ordinal)
            .ThenBy(static diagnostic =>
                diagnostic.Location.SourceSpan.Start)
            .ThenBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .Select(static diagnostic => new WorkerProtocolError {
                Code = "compiler." + diagnostic.Id,
                Message = diagnostic.GetMessage(CultureInfo.InvariantCulture)
            })
            .ToArray();
        if (compilerErrors.Length != 0)
            return ErrorResponse(snapshot.InputHash, compilerErrors);

        var verifier = new CallableVerifier(
            compilation,
            _backend,
            request.Budgets.MaximumExpressionDepth);
        var targets = verifier.Discover();
        using var projectBoundary = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        projectBoundary.CancelAfter(
            request.Budgets.ProjectWallTimeMilliseconds);
        using var parallelism = new SemaphoreSlim(
            request.Budgets.MaxParallelism,
            request.Budgets.MaxParallelism);
        var tasks = targets.Select(target => VerifyTargetAsync(
            verifier,
            target,
            request.Budgets,
            parallelism,
            _methodResourceGate,
            _readConsumedResourceCount,
            projectBoundary,
            cancellationToken)).ToArray();
        var methodRecords = await Task.WhenAll(tasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var response = new WorkerVerifyResponse {
            ProtocolVersion = WorkerProtocolVersions.Current,
            InputHash = snapshot.InputHash,
            Records = [.. methodRecords
                .SelectMany(static records => records)
                .OrderBy(static record => record.CallableId, StringComparer.Ordinal)
                .ThenBy(static record => record.ContractOrdinal)],
            Errors = []
        };
        WorkerProtocolJson.Canonicalize(response);
        if (cache != null)
            await cache.TryWriteAsync(response, cancellationToken)
                .ConfigureAwait(false);
        return response;
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _ownedBackend?.Dispose();
        _methodResourceGate.Dispose();
    }

    private static async Task<ImmutableArray<WorkerVerificationRecord>>
        VerifyTargetAsync(
            CallableVerifier verifier,
            CallableTarget target,
            WorkerBudgets budgets,
            SemaphoreSlim parallelism,
            SemaphoreSlim methodResourceGate,
            Func<long>? readConsumedResourceCount,
            CancellationTokenSource projectBoundary,
            CancellationToken callerCancellation) {
        try {
            await parallelism.WaitAsync(projectBoundary.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            callerCancellation.ThrowIfCancellationRequested();
            return [CreateTimeout(target, project: true)];
        }
        try {
            using var methodBoundary = CancellationTokenSource
                .CreateLinkedTokenSource(projectBoundary.Token);
            methodBoundary.CancelAfter(
                budgets.MethodWallTimeMilliseconds);
            var ownsResourceGate = false;
            try {
                if (readConsumedResourceCount != null) {
                    await methodResourceGate.WaitAsync(methodBoundary.Token)
                        .ConfigureAwait(false);
                    ownsResourceGate = true;
                }
                var resourceBudget = new MethodResourceBudget(
                    readConsumedResourceCount,
                    budgets.QueryRlimit,
                    budgets.MethodRlimit);
                return await verifier.VerifyAsync(
                    target,
                    resourceBudget,
                    methodBoundary.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                callerCancellation.ThrowIfCancellationRequested();
                return [CreateTimeout(
                    target,
                    projectBoundary.IsCancellationRequested)];
            }
            catch (Exception exception) when (exception is not
                OutOfMemoryException and not StackOverflowException) {
                return [CreateInfrastructureFailure(target)];
            }
            finally {
                if (ownsResourceGate) methodResourceGate.Release();
            }
        }
        finally {
            parallelism.Release();
        }
    }

    private static WorkerVerificationRecord CreateTimeout(
        CallableTarget target,
        bool project) =>
        new() {
            CallableId = target.CallableId,
            ContractOrdinal = 0,
            SourcePath = target.Declaration.SyntaxTree.FilePath,
            SourceStart = target.Declaration.SpanStart,
            Status = WorkerVerificationStatus.Unknown,
            Reason = project
                ? WorkerVerificationReason.ProjectTimeout
                : WorkerVerificationReason.MethodTimeout
        };

    private static WorkerVerificationRecord CreateInfrastructureFailure(
        CallableTarget target) =>
        new() {
            CallableId = target.CallableId,
            ContractOrdinal = 0,
            SourcePath = target.Declaration.SyntaxTree.FilePath,
            SourceStart = target.Declaration.SpanStart,
            Status = WorkerVerificationStatus.Unknown,
            Reason = WorkerVerificationReason.InfrastructureFailure
        };

    private static VerificationCache CreateCache(
        WorkerVerifyRequest request) {
        var projectDirectory = Path.GetFullPath(request.ProjectDirectory);
        var directory = string.IsNullOrWhiteSpace(request.Cache.Directory)
            ? Path.Combine(
                projectDirectory,
                "obj",
                "SharpProof",
                "cache",
                "v2")
            : Path.IsPathFullyQualified(request.Cache.Directory)
                ? request.Cache.Directory
                : Path.GetFullPath(
                    request.Cache.Directory,
                    projectDirectory);
        return new VerificationCache(
            directory,
            request.Cache.MaximumBytes);
    }

    private static WorkerVerifyResponse ErrorResponse(
        string inputHash,
        IEnumerable<WorkerProtocolError> errors) {
        var response = new WorkerVerifyResponse {
            ProtocolVersion = WorkerProtocolVersions.Current,
            InputHash = inputHash,
            Records = [],
            Errors = [.. errors]
        };
        WorkerProtocolJson.Canonicalize(response);
        return response;
    }
}
