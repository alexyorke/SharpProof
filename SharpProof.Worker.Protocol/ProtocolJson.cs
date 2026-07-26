using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpProof.Worker.Protocol;

public static class WorkerProtocolJson {
    private static readonly HashSet<string> s_languageVersions =
        new(
            [
                "1",
                "1.0",
                "2",
                "2.0",
                "3",
                "3.0",
                "4",
                "4.0",
                "5",
                "5.0",
                "6",
                "6.0",
                "7",
                "7.0",
                "7.1",
                "7.2",
                "7.3",
                "8",
                "8.0",
                "9",
                "9.0",
                "10",
                "10.0",
                "11",
                "11.0",
                "12",
                "12.0",
                "13",
                "13.0"
            ],
            StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions s_options = CreateOptions();

    public static JsonSerializerOptions Options => new(s_options);

    public static WorkerVerifyRequest? DeserializeRequest(string json) {
        if (json == null) throw new ArgumentNullException(nameof(json));
        return JsonSerializer.Deserialize<WorkerVerifyRequest>(json, s_options);
    }

    public static WorkerVerifyResponse? DeserializeResponse(string json) {
        if (json == null) throw new ArgumentNullException(nameof(json));
        return JsonSerializer.Deserialize<WorkerVerifyResponse>(json, s_options);
    }

    public static string SerializeRequest(WorkerVerifyRequest request) {
        if (request == null) throw new ArgumentNullException(nameof(request));
        return JsonSerializer.Serialize(request, s_options);
    }

    public static string SerializeResponse(WorkerVerifyResponse response) {
        if (response == null) throw new ArgumentNullException(nameof(response));
        Canonicalize(response);
        return JsonSerializer.Serialize(response, s_options);
    }

    public static WorkerProtocolValidationResult Validate(
        WorkerVerifyRequest? request) {
        var errors = ImmutableArray.CreateBuilder<WorkerProtocolError>();
        if (request == null) {
            Add(errors, "request.null", "The request is required.");
            return new WorkerProtocolValidationResult(errors);
        }
        if (!string.Equals(
                request.ProtocolVersion,
                WorkerProtocolVersions.Current,
                StringComparison.Ordinal))
            Add(errors, "protocol.unsupported", "Protocol version 2 is required.");
        if (string.IsNullOrWhiteSpace(request.ProjectDirectory))
            Add(errors, "project.directory", "A project directory is required.");
        if (string.IsNullOrWhiteSpace(request.AssemblyName))
            Add(errors, "project.assembly_name", "An assembly name is required.");
        if (request.SourceFiles == null || request.SourceFiles.Length == 0)
            Add(errors, "project.sources", "At least one source file is required.");
        else if (request.SourceFiles.Any(string.IsNullOrWhiteSpace))
            Add(errors, "project.source_path", "Source file paths cannot be empty.");
        if (request.ReferenceAssemblies == null ||
            request.ReferenceAssemblies.Length == 0)
            Add(errors, "project.references", "At least one reference assembly is required.");
        else if (request.ReferenceAssemblies.Any(string.IsNullOrWhiteSpace))
            Add(errors, "project.reference_path", "Reference paths cannot be empty.");
        if (request.DefineConstants == null)
            Add(errors, "project.constants", "Define constants cannot be null.");
        else if (request.DefineConstants.Any(string.IsNullOrWhiteSpace))
            Add(errors, "project.constant", "Define constants cannot be blank.");
        ValidateCompilation(request.Compilation, errors);
        if (request.Budgets == null) {
            Add(errors, "budgets.null", "Budgets are required.");
        }
        else {
            if (request.Budgets.QueryRlimit == 0)
                Add(errors, "budgets.rlimit", "Query rlimit must be positive.");
            if (request.Budgets.MethodRlimit == 0)
                Add(errors, "budgets.method_rlimit", "Method rlimit must be positive.");
            if (request.Budgets.QueryRlimit >
                request.Budgets.MethodRlimit)
                Add(
                    errors,
                    "budgets.rlimit_order",
                    "Query rlimit cannot exceed method rlimit.");
            if (request.Budgets.MethodWallTimeMilliseconds <= 0)
                Add(errors, "budgets.method_wall", "Method wall time must be positive.");
            if (request.Budgets.ProjectWallTimeMilliseconds <= 0)
                Add(errors, "budgets.project_wall", "Project wall time must be positive.");
            if (request.Budgets.MaxParallelism is < 1 or >
                WorkerBudgets.MaximumParallelism)
                Add(errors, "budgets.parallelism", "Max parallelism must be between 1 and 4.");
            if (request.Budgets.MaximumExpressionDepth is < 1 or > 256)
                Add(errors, "budgets.expression_depth", "Expression depth must be between 1 and 256.");
            if (request.Budgets.ProcessMemoryLimitBytes <= 0)
                Add(errors, "budgets.process_memory", "Process memory limit must be positive.");
            if (request.Budgets.MaxWorkerProcesses is < 1 or >
                WorkerBudgets.MaximumParallelism)
                Add(errors, "budgets.worker_processes", "Worker process count must be between 1 and 4.");
            if (request.Budgets.MethodWallTimeMilliseconds >
                request.Budgets.ProjectWallTimeMilliseconds)
                Add(errors, "budgets.wall_order", "Method wall time cannot exceed project wall time.");
        }
        if (request.Cache == null) {
            Add(errors, "cache.null", "Cache options are required.");
        }
        else if (request.Cache.MaximumBytes <= 0 ||
                 request.Cache.MaximumBytes >
                 WorkerCacheOptions.DefaultMaximumBytes) {
            Add(errors, "cache.maximum_bytes", "Cache size must be between 1 byte and 512 MiB.");
        }
        return new WorkerProtocolValidationResult(errors);
    }

    private static void ValidateCompilation(
        WorkerCompilationOptions? options,
        ImmutableArray<WorkerProtocolError>.Builder errors) {
        if (options == null) {
            Add(
                errors,
                "compilation.null",
                "Compilation options are required.");
            return;
        }
        if (string.IsNullOrWhiteSpace(options.TargetFramework) ||
            options.TargetFramework.Length > 256 ||
            options.TargetFramework.Any(char.IsControl))
            Add(
                errors,
                "compilation.target_framework",
                "A valid target framework identity is required.");
        if (string.IsNullOrWhiteSpace(options.LanguageVersion) ||
            !s_languageVersions.Contains(options.LanguageVersion))
            Add(
                errors,
                "compilation.language_version",
                "A supported explicit C# language version is required.");
        ValidateDefined(
            options.NullableContext,
            WorkerNullableContext.Unspecified,
            "compilation.nullable",
            "An explicit nullable context is required.",
            errors);
        ValidateDefined(
            options.Optimization,
            WorkerOptimizationLevel.Unspecified,
            "compilation.optimization",
            "An explicit optimization level is required.",
            errors);
        if (!options.CheckOverflow.HasValue)
            Add(
                errors,
                "compilation.checked_overflow",
                "An explicit checked-overflow setting is required.");
        if (!options.AllowUnsafe.HasValue)
            Add(
                errors,
                "compilation.allow_unsafe",
                "An explicit unsafe-code setting is required.");
        if (!options.Deterministic.HasValue)
            Add(
                errors,
                "compilation.deterministic",
                "An explicit deterministic-build setting is required.");
        ValidateDefined(
            options.OutputKind,
            WorkerOutputKind.Unspecified,
            "compilation.output_kind",
            "An explicit output kind is required.",
            errors);
        ValidateDefined(
            options.Platform,
            WorkerPlatform.Unspecified,
            "compilation.platform",
            "An explicit target platform is required.",
            errors);
    }

    public static void Canonicalize(WorkerVerifyResponse response) {
        if (response == null) throw new ArgumentNullException(nameof(response));
        response.Records = [.. (response.Records ?? [])
            .OrderBy(static record => record.CallableId, StringComparer.Ordinal)
            .ThenBy(static record => record.ContractOrdinal)
            .ThenBy(static record => record.SourcePath, StringComparer.Ordinal)
            .ThenBy(static record => record.SourceStart)];
        foreach (var record in response.Records) {
            record.ProofCore = [.. (record.ProofCore ?? [])
                .OrderBy(static value => value, StringComparer.Ordinal)];
            record.Model = [.. (record.Model ?? [])
                .OrderBy(static value => value.Variable, StringComparer.Ordinal)
                .ThenBy(static value => value.Kind, StringComparer.Ordinal)
                .ThenBy(static value => value.Value, StringComparer.Ordinal)];
        }
        response.Errors = [.. (response.Errors ?? [])
            .OrderBy(static error => error.Code, StringComparer.Ordinal)
            .ThenBy(static error => error.Message, StringComparer.Ordinal)];
    }

    private static JsonSerializerOptions CreateOptions() {
        var options = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static void Add(
        ImmutableArray<WorkerProtocolError>.Builder errors,
        string code,
        string message) =>
        errors.Add(new WorkerProtocolError { Code = code, Message = message });

    private static void ValidateDefined<T>(
        T value,
        T unspecified,
        string code,
        string message,
        ImmutableArray<WorkerProtocolError>.Builder errors)
        where T : struct, Enum {
        if (!Enum.IsDefined(typeof(T), value) ||
            EqualityComparer<T>.Default.Equals(value, unspecified))
            Add(errors, code, message);
    }
}
