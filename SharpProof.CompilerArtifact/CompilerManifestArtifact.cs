using System.Text.Json;
namespace SharpProof.CompilerArtifact;
internal static class CompilerManifestArtifactVersions {
    internal const string Schema = "SharpProof.CompilerManifest"; internal const int Current = 2;
}
internal sealed class CompilerManifestArtifact {
    public string Schema { get; set; } = CompilerManifestArtifactVersions.Schema;
    public int SchemaVersion { get; set; } = CompilerManifestArtifactVersions.Current;
    public string ProtocolVersion { get; set; } = WorkerProtocolVersions.Current; public WorkerFeatureSet Features { get; set; }
    public string CompilationSha256 { get; set; } = string.Empty; public CompilerCompilationSnapshot Compilation { get; set; } = new();
    public WorkerClaimManifest Manifest { get; set; } = new();
}
internal static class CompilerArtifactInputHash {
    internal static string Compute(
        WorkerVerifyRequest request, byte[] artifactBytes,
        string toolIdentity, string toolVersion, string apiSpecIdentity, string apiSpecVersion) {
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
    internal static CompilerManifestArtifact Create(
        CSharpCompilation compilation, string projectDirectory, string targetFramework, WorkerFeatureSet features,
        WorkerClaimManifest manifest, CancellationToken cancellationToken) {
        var snapshot = CompilationFingerprint.Capture(compilation, projectDirectory, targetFramework, cancellationToken);
        var artifact = new CompilerManifestArtifact();
        artifact.Features = features; artifact.CompilationSha256 = CompilationFingerprint.ComputeSha256(snapshot);
        artifact.Compilation = snapshot; artifact.Manifest = manifest;
        Validate(artifact); return artifact;
    }
    internal static string Serialize(CompilerManifestArtifact artifact) {
        if (artifact == null) throw new ArgumentNullException(nameof(artifact));
        WorkerProtocolJson.Canonicalize(artifact.Manifest); Validate(artifact);
        return JsonSerializer.Serialize(artifact, WorkerProtocolJson.Options) + "\n";
    }
    internal static CompilerManifestArtifact Deserialize(string json) {
        if (json == null) throw new ArgumentNullException(nameof(json));
        var artifact = JsonSerializer.Deserialize<CompilerManifestArtifact>(json, WorkerProtocolJson.Options) ??
            throw new JsonException("A compiler manifest artifact is required.");
        Validate(artifact); if (Serialize(artifact) != json) throw new JsonException("The compiler manifest artifact is not canonical.");
        return artifact;
    }
    internal static CSharpCompilation CreateCompilation(CompilerManifestArtifact artifact, CancellationToken cancellationToken) {
        Validate(artifact); if (!IsCompilerCompatible(artifact, out var message)) throw new InvalidOperationException(message);
        return CompilationFingerprint.Reconstruct(artifact.Compilation, cancellationToken);
    }
    internal static bool IsCompilerCompatible(CompilerManifestArtifact artifact, out string message) {
        if (artifact == null) throw new ArgumentNullException(nameof(artifact));
        var expected = artifact.Compilation;
        var compatible = expected.CompilerVersion == CompilationFingerprint.CurrentCompilerVersion && expected.CompilerMvid == CompilationFingerprint.CurrentCompilerMvid &&
            expected.CSharpCompilerVersion == CompilationFingerprint.CurrentCSharpCompilerVersion && expected.CSharpCompilerMvid == CompilationFingerprint.CurrentCSharpCompilerMvid;
        message = compatible ? string.Empty : $"The compiler manifest requires Roslyn Common {expected.CompilerVersion} and C# " +
            $"{expected.CSharpCompilerVersion} build identities that do not match the worker.";
        return compatible;
    }
    internal static bool ManifestsEqual(WorkerClaimManifest? left, WorkerClaimManifest? right) => WorkerProtocolJson.ManifestsEqual(left, right);
    private static void Validate(CompilerManifestArtifact value) {
        if (value == null || value.Schema != CompilerManifestArtifactVersions.Schema || value.SchemaVersion != CompilerManifestArtifactVersions.Current ||
            value.ProtocolVersion != WorkerProtocolVersions.Current ||
            !WorkerProtocolJson.IsDefined(value.Features, WorkerFeatureSet.Unspecified) || !WorkerProtocolJson.IsSha256(value.CompilationSha256) ||
            value.Compilation == null || value.CompilationSha256 != CompilationFingerprint.ComputeSha256(value.Compilation) ||
            !WorkerProtocolJson.ValidateManifest(value.Manifest).IsValid)
            throw new JsonException("The compiler manifest artifact is invalid.");
        CompilationFingerprint.ValidateShape(value.Compilation);
    }
}
