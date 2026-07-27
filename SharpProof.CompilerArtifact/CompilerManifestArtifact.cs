using System.Text.Json;
namespace SharpProof.CompilerArtifact;
#pragma warning disable IDE0055 // Compact artifact transport preserves the size ratchet.
internal static class CompilerManifestArtifactVersions {
    internal const string Schema = "SharpProof.CompilerManifest"; internal const int Current = 3;
}
internal sealed class CompilerDiagnosticArtifact {
    public string Code { get; set; } = string.Empty; public string Message { get; set; } = string.Empty;
    public WorkerSourceLocation Location { get; set; } = new();
}
internal sealed class CompilerManifestArtifact {
    public string Schema { get; set; } = CompilerManifestArtifactVersions.Schema; public int SchemaVersion { get; set; } = CompilerManifestArtifactVersions.Current;
    public string ProtocolVersion { get; set; } = WorkerProtocolVersions.Current; public WorkerFeatureSet Features { get; set; }
    public string CompilationSha256 { get; set; } = string.Empty; public CompilerCompilationSnapshot Compilation { get; set; } = new();
    public WorkerClaimManifest Manifest { get; set; } = new(); public int MaximumExpressionDepth { get; set; } = WorkerBudgets.DefaultMaximumExpressionDepth;
    public CompilerDiagnosticArtifact[] CompilerDiagnostics { get; set; } = []; public CompilerCallableArtifact[] Callables { get; set; } = [];
}
internal static class CompilerArtifactInputHash {
    internal static string Compute(WorkerVerifyRequest request, byte[] artifactBytes, string toolIdentity,
        string toolVersion, string apiSpecIdentity, string apiSpecVersion) {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (artifactBytes == null) throw new ArgumentNullException(nameof(artifactBytes));
        using var hash = new CanonicalHashWriter();
        var invariant = CultureInfo.InvariantCulture;
        hash.Add(
            "protocol", request.ProtocolVersion, "cache_schema", WorkerCacheVersions.Current.ToString(invariant),
            "tool.identity", toolIdentity, "tool.version", toolVersion,
            "api_spec.identity", apiSpecIdentity, "api_spec.version", apiSpecVersion,
            "budget.query_rlimit", request.Budgets.QueryRlimit.ToString(invariant), "budget.method_rlimit", request.Budgets.MethodRlimit.ToString(invariant),
            "budget.method_wall_ms", request.Budgets.MethodWallTimeMilliseconds.ToString(invariant), "budget.project_wall_ms", request.Budgets.ProjectWallTimeMilliseconds.ToString(invariant),
            "budget.max_parallelism", request.Budgets.MaxParallelism.ToString(invariant), "budget.expression_depth", request.Budgets.MaximumExpressionDepth.ToString(invariant),
            "budget.process_memory", request.Budgets.ProcessMemoryLimitBytes.ToString(invariant), "budget.max_worker_processes", request.Budgets.MaxWorkerProcesses.ToString(invariant));
        return hash.Add("compiler_manifest").Add(artifactBytes).Finish();
    }
}
internal static class CompilerManifestArtifactJson {
    internal static string Serialize(CompilerManifestArtifact artifact) {
        if (artifact == null) throw new ArgumentNullException(nameof(artifact));
        WorkerProtocolJson.Canonicalize(artifact.Manifest);
        artifact.CompilerDiagnostics = [.. artifact.CompilerDiagnostics
            .OrderBy(static item => item.Location.Path, StringComparer.Ordinal)
            .ThenBy(static item => item.Location.Start)
            .ThenBy(static item => item.Code, StringComparer.Ordinal)];
        artifact.Callables = [.. artifact.Callables.OrderBy(static item => item.CallableId, StringComparer.Ordinal)];
        Validate(artifact); return JsonSerializer.Serialize(artifact, WorkerProtocolJson.Options) + "\n";
    }
    internal static CompilerManifestArtifact Deserialize(string json) {
        if (json == null) throw new ArgumentNullException(nameof(json));
        var artifact = JsonSerializer.Deserialize<CompilerManifestArtifact>(json, WorkerProtocolJson.Options) ??
            throw new JsonException("A compiler manifest artifact is required.");
        Validate(artifact); if (Serialize(artifact) != json) throw new JsonException("The compiler manifest artifact is not canonical.");
        return artifact;
    }
    internal static ImmutableArray<CompilerCallablePreparation> DecodeCallables(CompilerManifestArtifact artifact) {
        Validate(artifact);
        return CompilerLoweredArtifact.Decode(artifact.Callables, artifact.Manifest);
    }
    internal static void Validate(CompilerManifestArtifact value) {
        if (value == null || value.Schema != CompilerManifestArtifactVersions.Schema || value.SchemaVersion != CompilerManifestArtifactVersions.Current ||
            value.ProtocolVersion != WorkerProtocolVersions.Current ||
            !WorkerProtocolJson.IsDefined(value.Features, WorkerFeatureSet.Unspecified) || !WorkerProtocolJson.IsSha256(value.CompilationSha256) ||
            value.Compilation == null || value.CompilationSha256 != CompilationFingerprint.ComputeSha256(value.Compilation) ||
            value.MaximumExpressionDepth is < 1 or > 256 ||
            !WorkerProtocolJson.ValidateManifest(value.Manifest).IsValid ||
            value.CompilerDiagnostics == null ||
            value.CompilerDiagnostics.Any(static item => item == null ||
                string.IsNullOrWhiteSpace(item.Code) ||
                string.IsNullOrWhiteSpace(item.Message) ||
                item.Location == null || item.Location.Start < 0 ||
                item.Location.Length < 0 || item.Location.Line < 0 ||
                item.Location.Column < 0) ||
            value.Callables == null ||
            value.Callables.Length != value.Manifest.Callables.Length ||
            !value.Callables.Select(static item => item?.CallableId)
                .SequenceEqual(value.Manifest.Callables.Select(
                    static item => item.CallableId), StringComparer.Ordinal))
            throw new JsonException("The compiler manifest artifact is invalid.");
        CompilationFingerprint.ValidateShape(value.Compilation);
    }
}
