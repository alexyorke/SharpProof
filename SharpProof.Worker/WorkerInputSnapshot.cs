namespace SharpProof.Worker;
internal sealed record WorkerInputSnapshot(
    ImmutableArray<WorkerInputSnapshot.SourceInput> Sources,
    ImmutableArray<WorkerInputSnapshot.ReferenceInput> References,
    string InputHash) {
    internal static async Task<WorkerInputSnapshot> LoadAsync(
        WorkerVerifyRequest request, CancellationToken cancellationToken) =>
        await LoadAsync(request, WorkerCacheIdentity.Current, cancellationToken).ConfigureAwait(false);
    internal static async Task<WorkerInputSnapshot> LoadAsync(
        WorkerVerifyRequest request, WorkerCacheIdentity cacheIdentity,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(cacheIdentity);
        var projectDirectory = Path.GetFullPath(request.ProjectDirectory);
        var sourcePaths = request.SourceFiles.Select(path => ResolvePath(projectDirectory, path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToImmutableArray();
        var referencePaths = request.ReferenceAssemblies.Select(path => ResolvePath(projectDirectory, path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToImmutableArray();
        var sources = ImmutableArray.CreateBuilder<SourceInput>(sourcePaths.Length);
        var references = ImmutableArray.CreateBuilder<ReferenceInput>(referencePaths.Length);
        using var hash = new CanonicalHashWriter();
        var invariant = CultureInfo.InvariantCulture;
        foreach (var (name, value) in new[] {
                     ("protocol", request.ProtocolVersion),
                     ("cache_schema", WorkerCacheVersions.Current.ToString(invariant)),
                     ("tool.identity", cacheIdentity.ToolIdentity),
                     ("tool.version", cacheIdentity.ToolVersion),
                     ("api_spec.identity", cacheIdentity.ApiSpecIdentity),
                     ("api_spec.version", cacheIdentity.ApiSpecVersion),
                     ("assembly_name", request.AssemblyName),
                     ("target_framework", request.Compilation.TargetFramework),
                     ("language_version", request.Compilation.LanguageVersion),
                     ("nullable", request.Compilation.NullableContext.ToString()),
                     ("optimization", request.Compilation.Optimization.ToString()),
                     ("checked_overflow", request.Compilation.CheckOverflow!.Value ? "true" : "false"),
                     ("allow_unsafe", request.Compilation.AllowUnsafe!.Value ? "true" : "false"),
                     ("deterministic", request.Compilation.Deterministic!.Value ? "true" : "false"),
                     ("output_kind", request.Compilation.OutputKind.ToString()),
                     ("platform", request.Compilation.Platform.ToString()),
                     ("features", request.Features.ToString()),
                     ("budget.query_rlimit", request.Budgets.QueryRlimit.ToString(invariant)),
                     ("budget.method_rlimit", request.Budgets.MethodRlimit.ToString(invariant)),
                     ("budget.method_wall_ms", request.Budgets.MethodWallTimeMilliseconds.ToString(invariant)),
                     ("budget.project_wall_ms", request.Budgets.ProjectWallTimeMilliseconds.ToString(invariant)),
                     ("budget.max_parallelism", request.Budgets.MaxParallelism.ToString(invariant)),
                     ("budget.expression_depth", request.Budgets.MaximumExpressionDepth.ToString(invariant)),
                     ("budget.process_memory", request.Budgets.ProcessMemoryLimitBytes.ToString(invariant)),
                     ("budget.max_worker_processes", request.Budgets.MaxWorkerProcesses.ToString(invariant))
                 })
            hash.Add(name).Add(value);
        foreach (var symbol in request.DefineConstants
                     .OrderBy(static value => value, StringComparer.Ordinal))
            hash.Add("define").Add(symbol);
        foreach (var path in sourcePaths) {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            hash.Add(path).Add(bytes);
            sources.Add(new SourceInput(path, DecodeUtf8(bytes, path)));
        }
        foreach (var path in referencePaths) {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            hash.Add(path).Add(bytes);
            references.Add(new ReferenceInput(path, bytes));
        }
        return new WorkerInputSnapshot(
            sources.MoveToImmutable(),
            references.MoveToImmutable(),
            hash.Finish());
    }
    private static string ResolvePath(string projectDirectory, string path) {
        var resolved = Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, projectDirectory);
        if (!File.Exists(resolved))
            throw new FileNotFoundException("A verifier input file was not found.", resolved);
        return resolved;
    }
    private static string DecodeUtf8(byte[] bytes, string path) {
        try {
            var offset = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
            return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException exception) {
            throw new InvalidDataException("Source files must be valid UTF-8: " + path, exception);
        }
    }
    internal readonly record struct SourceInput(string Path, string Text);
    internal readonly record struct ReferenceInput(string Path, byte[] Image);
}

internal sealed class WorkerCacheIdentity(
    string toolIdentity,
    string toolVersion,
    string apiSpecIdentity,
    string apiSpecVersion) {
    internal const string CurrentToolIdentity = "SharpProof.Worker";
    internal static WorkerCacheIdentity Current { get; } = new(
        CurrentToolIdentity, ReadToolVersion(),
        ApiSpecTable.DefaultTableIdentity, ApiSpecTable.DefaultTableVersion);
    internal string ToolIdentity { get; } = Required(toolIdentity, nameof(toolIdentity));
    internal string ToolVersion { get; } = Required(toolVersion, nameof(toolVersion));
    internal string ApiSpecIdentity { get; } = Required(apiSpecIdentity, nameof(apiSpecIdentity));
    internal string ApiSpecVersion { get; } = Required(apiSpecVersion, nameof(apiSpecVersion));
    private static string ReadToolVersion() =>
        typeof(SharpProofWorker).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ??
        throw new InvalidOperationException("The worker tool version is unavailable.");
    private static string Required(string value, string parameterName) =>
        !string.IsNullOrWhiteSpace(value) ? value :
        throw new ArgumentException("Cache identity values cannot be blank.", parameterName);
}
