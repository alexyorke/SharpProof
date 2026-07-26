namespace SharpProof.Worker;

internal sealed class WorkerInputSnapshot {
    private WorkerInputSnapshot(
        ImmutableArray<SourceInput> sources,
        ImmutableArray<ReferenceInput> references,
        string inputHash) {
        Sources = sources;
        References = references;
        InputHash = inputHash;
    }

    internal ImmutableArray<SourceInput> Sources { get; }
    internal ImmutableArray<ReferenceInput> References { get; }
    internal string InputHash { get; }

    internal static async Task<WorkerInputSnapshot> LoadAsync(
        WorkerVerifyRequest request,
        CancellationToken cancellationToken) =>
        await LoadAsync(
            request,
            WorkerCacheIdentity.Current,
            cancellationToken).ConfigureAwait(false);

    internal static async Task<WorkerInputSnapshot> LoadAsync(
        WorkerVerifyRequest request,
        WorkerCacheIdentity cacheIdentity,
        CancellationToken cancellationToken) {
        if (cacheIdentity == null)
            throw new ArgumentNullException(nameof(cacheIdentity));
        var projectDirectory = Path.GetFullPath(request.ProjectDirectory);
        var sourcePaths = request.SourceFiles
            .Select(path => ResolvePath(projectDirectory, path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToImmutableArray();
        var referencePaths = request.ReferenceAssemblies
            .Select(path => ResolvePath(projectDirectory, path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToImmutableArray();
        var sources = ImmutableArray.CreateBuilder<SourceInput>(
            sourcePaths.Length);
        var references = ImmutableArray.CreateBuilder<ReferenceInput>(
            referencePaths.Length);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AddNamedString(hash, "protocol", request.ProtocolVersion);
        AddNamedString(
            hash,
            "cache_schema",
            WorkerCacheVersions.Current.ToString(
                CultureInfo.InvariantCulture));
        AddNamedString(hash, "tool.identity", cacheIdentity.ToolIdentity);
        AddNamedString(hash, "tool.version", cacheIdentity.ToolVersion);
        AddNamedString(
            hash,
            "api_spec.identity",
            cacheIdentity.ApiSpecIdentity);
        AddNamedString(
            hash,
            "api_spec.version",
            cacheIdentity.ApiSpecVersion);
        AddNamedString(hash, "assembly_name", request.AssemblyName);
        AddNamedString(
            hash,
            "target_framework",
            request.Compilation.TargetFramework);
        AddNamedString(
            hash,
            "language_version",
            request.Compilation.LanguageVersion);
        AddNamedString(
            hash,
            "nullable",
            request.Compilation.NullableContext.ToString());
        AddNamedString(
            hash,
            "optimization",
            request.Compilation.Optimization.ToString());
        AddNamedString(
            hash,
            "checked_overflow",
            Boolean(request.Compilation.CheckOverflow!.Value));
        AddNamedString(
            hash,
            "allow_unsafe",
            Boolean(request.Compilation.AllowUnsafe!.Value));
        AddNamedString(
            hash,
            "deterministic",
            Boolean(request.Compilation.Deterministic!.Value));
        AddNamedString(
            hash,
            "output_kind",
            request.Compilation.OutputKind.ToString());
        AddNamedString(
            hash,
            "platform",
            request.Compilation.Platform.ToString());
        AddNamedString(
            hash,
            "budget.query_rlimit",
            request.Budgets.QueryRlimit.ToString(
                CultureInfo.InvariantCulture));
        AddNamedString(
            hash,
            "budget.method_rlimit",
            request.Budgets.MethodRlimit.ToString(
                CultureInfo.InvariantCulture));
        AddNamedString(
            hash,
            "budget.method_wall_ms",
            request.Budgets.MethodWallTimeMilliseconds.ToString(
                CultureInfo.InvariantCulture));
        AddNamedString(
            hash,
            "budget.project_wall_ms",
            request.Budgets.ProjectWallTimeMilliseconds.ToString(
                CultureInfo.InvariantCulture));
        AddNamedString(
            hash,
            "budget.max_parallelism",
            request.Budgets.MaxParallelism.ToString(
                CultureInfo.InvariantCulture));
        AddNamedString(
            hash,
            "budget.expression_depth",
            request.Budgets.MaximumExpressionDepth.ToString(
                CultureInfo.InvariantCulture));
        AddNamedString(
            hash,
            "budget.process_memory",
            request.Budgets.ProcessMemoryLimitBytes.ToString(
                CultureInfo.InvariantCulture));
        AddNamedString(
            hash,
            "budget.max_worker_processes",
            request.Budgets.MaxWorkerProcesses.ToString(
                CultureInfo.InvariantCulture));
        foreach (var symbol in request.DefineConstants
                     .OrderBy(static value => value, StringComparer.Ordinal))
            AddNamedString(hash, "define", symbol);
        foreach (var path in sourcePaths) {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken)
                .ConfigureAwait(false);
            AddString(hash, path);
            AddBytes(hash, bytes);
            sources.Add(new SourceInput(path, DecodeUtf8(bytes, path)));
        }
        foreach (var path in referencePaths) {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken)
                .ConfigureAwait(false);
            AddString(hash, path);
            AddBytes(hash, bytes);
            references.Add(new ReferenceInput(path, bytes));
        }
        return new WorkerInputSnapshot(
            sources.MoveToImmutable(),
            references.MoveToImmutable(),
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
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
            var offset = bytes.AsSpan().StartsWith(
                new byte[] { 0xEF, 0xBB, 0xBF })
                ? 3
                : 0;
            return new UTF8Encoding(false, true).GetString(
                bytes,
                offset,
                bytes.Length - offset);
        }
        catch (DecoderFallbackException exception) {
            throw new InvalidDataException(
                "Source files must be valid UTF-8: " + path,
                exception);
        }
    }

    private static void AddString(IncrementalHash hash, string value) {
        var bytes = Encoding.UTF8.GetBytes(value);
        AddBytes(hash, bytes);
    }

    private static void AddNamedString(
        IncrementalHash hash,
        string name,
        string value) {
        AddString(hash, name);
        AddString(hash, value);
    }

    private static string Boolean(bool value) =>
        value ? "true" : "false";

    private static void AddBytes(IncrementalHash hash, byte[] value) {
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            length,
            value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }

    internal readonly struct SourceInput(string path, string text) {
        internal string Path { get; } = path;
        internal string Text { get; } = text;
    }

    internal readonly struct ReferenceInput(string path, byte[] image) {
        internal string Path { get; } = path;
        internal byte[] Image { get; } = image;
    }
}

internal sealed class WorkerCacheIdentity {
    internal const string CurrentToolIdentity = "SharpProof.Worker";

    internal static WorkerCacheIdentity Current { get; } =
        new(
            CurrentToolIdentity,
            ReadToolVersion(),
            ApiSpecTable.DefaultTableIdentity,
            ApiSpecTable.DefaultTableVersion);

    internal WorkerCacheIdentity(
        string toolIdentity,
        string toolVersion,
        string apiSpecIdentity,
        string apiSpecVersion) {
        ToolIdentity = Required(toolIdentity, nameof(toolIdentity));
        ToolVersion = Required(toolVersion, nameof(toolVersion));
        ApiSpecIdentity = Required(
            apiSpecIdentity,
            nameof(apiSpecIdentity));
        ApiSpecVersion = Required(
            apiSpecVersion,
            nameof(apiSpecVersion));
    }

    internal string ToolIdentity { get; }
    internal string ToolVersion { get; }
    internal string ApiSpecIdentity { get; }
    internal string ApiSpecVersion { get; }

    private static string ReadToolVersion() {
        var attribute = typeof(SharpProofWorker).Assembly
            .GetCustomAttributes(
                typeof(
                    System.Reflection.AssemblyInformationalVersionAttribute),
                inherit: false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>()
            .SingleOrDefault();
        return attribute?.InformationalVersion ??
               throw new InvalidOperationException(
                   "The worker tool version is unavailable.");
    }

    private static string Required(string value, string parameterName) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException(
                "Cache identity values cannot be blank.",
                parameterName);
}
