using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpProof.CompilerArtifact;

internal static class CompilerArtifactInputHash
{
    internal static string Compute(
        WorkerVerifyRequest request,
        byte[] artifactBytes,
        string toolIdentity,
        string toolVersion,
        string workerBinarySha256,
        string apiSpecIdentity,
        string apiSpecVersion,
        string apiSpecContentSha256)
    {
        request = ArgumentNullGuard.NotNull(request, nameof(request));
        artifactBytes = ArgumentNullGuard.NotNull(artifactBytes, nameof(artifactBytes));

        using var hash = new CanonicalHashWriter();
        hash.Add(
            "protocol", request.ProtocolVersion,
            "cache_schema", WorkerCacheVersions.Current,
            "tool.identity", toolIdentity, "tool.version", toolVersion,
            "tool.binary_sha256", workerBinarySha256, "api_spec.identity", apiSpecIdentity,
            "api_spec.version", apiSpecVersion, "api_spec.content_sha256", apiSpecContentSha256,
            "budget.query_rlimit", request.Budgets.QueryRlimit, "budget.method_rlimit", request.Budgets.MethodRlimit,
            "budget.method_wall_ms", request.Budgets.MethodWallTimeMilliseconds,
            "budget.project_wall_ms", request.Budgets.ProjectWallTimeMilliseconds,
            "budget.max_parallelism", request.Budgets.MaxParallelism,
            "budget.expression_depth", request.Budgets.MaximumExpressionDepth,
            "budget.process_memory", request.Budgets.ProcessMemoryLimitBytes,
            "budget.max_worker_processes", request.Budgets.MaxWorkerProcesses);
        return hash.Add("compiler_manifest").Add(artifactBytes).Finish();
    }
}

internal static class WorkerBinaryIdentity
{
    internal const int MaximumComponentKeyCharacters = 256;
    internal const int MaximumRuntimeComponents = 64;
    internal const long MaximumComponentBytes = 32L * 1024 * 1024;
    internal const long MaximumClosureBytes = 64L * 1024 * 1024;
    internal const long MaximumDependenciesBytes = 1024L * 1024;
    internal const long MaximumRuntimeConfigBytes = 64L * 1024;

    internal static WorkerRuntimeClosureSnapshot CreateSnapshot(
        string workerPath)
    {
        var path = NormalizeWorkerPath(workerPath);
        var stagingDirectory = CreateStagingDirectory();
        try
        {
            using var dependency = OpenRead(Path.ChangeExtension(path, ".deps.json"));
            var components = RuntimeComponents(path, dependency);
            ValidateComponentCount(components.Count);
            using var hash = new CanonicalHashWriter();
            hash.Add("SharpProof.WorkerBinarySet", 1);
            long totalBytes = 0;
#pragma warning disable CA2000 // Stream ownership transfers to the retained snapshot list.
            foreach (var component in components)
            {
                var isDependency = component.Key == "dependencies";
                var stream = isDependency
                    ? dependency
                    : OpenRead(component.Value);
                try
                {
                    ValidateComponentLength(
                        component.Key,
                        stream.Length,
                        ref totalBytes);
                    stream.Position = 0;
                    hash.Add(component.Key).Add(stream);
                    StageComponent(stagingDirectory, path, component.Value);
                }
                finally
                {
                    if (!isDependency)
                    {
                        stream.Dispose();
                    }
                }
            }
#pragma warning restore CA2000
            return new WorkerRuntimeClosureSnapshot(
                path,
                Path.Combine(stagingDirectory, Path.GetFileName(path)),
                components.Values.ToArray(),
                hash.Finish());
        }
        catch
        {
            DeleteStagingDirectory(stagingDirectory);

            throw;
        }
    }

    internal static string ComputeSha256(string workerPath)
    {
        using var snapshot = CreateSnapshot(workerPath);
        return snapshot.Sha256;
    }

    private static string NormalizeWorkerPath(string workerPath)
    {
        var path = Path.GetFullPath(workerPath);
        if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException(
                "The managed worker binary must be a .dll.",
                path);
        }

        return path;
    }
    internal static void ValidateComponentCount(int count)
    {
        if (count > MaximumRuntimeComponents)
        {
            throw new InvalidDataException(
                "The worker runtime closure contains too many components.");
        }
    }

    internal static void ValidateComponentLength(
        string key,
        long length,
        ref long totalBytes)
    {
        if (key.Length > MaximumComponentKeyCharacters)
        {
            throw new InvalidDataException(
                "A worker runtime component identity is too long.");
        }

        var maximum = key switch
        {
            "dependencies" => MaximumDependenciesBytes,
            "runtimeConfig" => MaximumRuntimeConfigBytes,
            _ when key.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) =>
                MaximumDependenciesBytes,
            _ when key.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase) =>
                MaximumRuntimeConfigBytes,
            _ => MaximumComponentBytes
        };
        if (length > maximum || totalBytes > MaximumClosureBytes - length)
        {
            throw new InvalidDataException(
                "The worker runtime closure exceeds its byte limits.");
        }

        totalBytes += length;
    }

    private static SortedDictionary<string, string> RuntimeComponents(
        string workerPath,
        FileStream dependencyStream)
    {
        var directory = Path.GetDirectoryName(workerPath)!;
        long dependencyBytes = 0;
        ValidateComponentLength(
            "dependencies",
            dependencyStream.Length,
            ref dependencyBytes);
        using var document = JsonDocument.Parse(
            dependencyStream,
            new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        root.GetProperty("targets").GetProperty(
            root.GetProperty("runtimeTarget").GetProperty("name").GetString()!);
        var names = new HashSet<string>
        {
            Path.GetFileName(workerPath),
            Path.GetFileName(Path.ChangeExtension(workerPath, ".deps.json")),
            Path.GetFileName(Path.ChangeExtension(workerPath, ".runtimeconfig.json"))
        };
        foreach (Match match in Regex.Matches(
                     root.GetRawText(),
                     @"[A-Za-z0-9_.-]+\.dll"))
        {
            names.Add(match.Value);
        }
        var files = Directory.GetFiles(
            directory,
            "*",
            SearchOption.AllDirectories);
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in files.Where(path =>
                     names.Contains(Path.GetFileName(path)) ||
                     path.EndsWith(
                         Path.GetFileName(typeof(ImmutableArray<>).Assembly.Location),
                         StringComparison.OrdinalIgnoreCase)))
        {
            result.Add(
                path.Substring(directory.Length + 1)
                    .Replace(Path.DirectorySeparatorChar, '/'),
                path);
        }
        if (!names.IsSubsetOf(result.Values.Select(Path.GetFileName)))
        {
            throw new FileNotFoundException(
                "A trusted worker runtime component is unavailable.");
        }

        return result;
    }

    private static FileStream OpenRead(string path)
    {
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
    }

    private static void StageComponent(
        string stagingDirectory,
        string workerPath,
        string componentPath)
    {
        var stagedPath = componentPath.Replace(
            Path.GetDirectoryName(workerPath)!,
            stagingDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
        File.Copy(componentPath, stagedPath);
    }

    private static string CreateStagingDirectory()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "SharpProof.Worker.Runtime." + Path.GetRandomFileName());
    }

    internal static void DeleteStagingDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

}

internal sealed class WorkerRuntimeClosureSnapshot(
    string workerPath,
    string executionWorkerPath,
    IReadOnlyList<string> componentPaths,
    string sha256) : IDisposable
{
    internal string WorkerPath { get; } = workerPath;
    internal string ExecutionWorkerPath { get; } = executionWorkerPath;
    internal IReadOnlyList<string> ComponentPaths { get; } =
        ImmutableArray.CreateRange(componentPaths);
    internal string Sha256 { get; } = sha256;

    public void Dispose()
    {
        WorkerBinaryIdentity.DeleteStagingDirectory(
            Path.GetDirectoryName(ExecutionWorkerPath)!);
    }
}

internal static class CompilerManifestArtifactJson
{
    internal static string Serialize(CompilerManifestArtifact artifact)
    {
        artifact = ArgumentNullGuard.NotNull(artifact, nameof(artifact));

        WorkerProtocolJson.Canonicalize(artifact.Manifest);
        artifact.CompilerDiagnostics = [
            .. artifact.CompilerDiagnostics
                .OrderBy(static item => item.Location.Path, StringComparer.Ordinal)
                .ThenBy(static item => item.Location.Start)
                .ThenBy(static item => item.Code, StringComparer.Ordinal)
        ];
        artifact.Callables = [
            .. artifact.Callables.OrderBy(static item => item.CallableId, StringComparer.Ordinal)
        ];
        Validate(artifact);
        return JsonSerializer.Serialize(artifact, WorkerProtocolJson.Options) + "\n";
    }

    internal static CompilerManifestArtifact Deserialize(string json)
    {
        json = ArgumentNullGuard.NotNull(json, nameof(json));

        var artifact = JsonSerializer.Deserialize<CompilerManifestArtifact>(
            json, WorkerProtocolJson.Options) ??
            throw new JsonException("A compiler manifest artifact is required.");
        Validate(artifact);
        if (Serialize(artifact) != json)
        {
            throw new JsonException("The compiler manifest artifact is not canonical.");
        }

        return artifact;
    }

    internal static ImmutableArray<CompilerCallablePreparation> DecodeCallables(
        CompilerManifestArtifact artifact)
    {
        Validate(artifact);
        return CompilerLoweredArtifact.Decode(
            artifact.Callables,
            artifact.Manifest,
            artifact.Compilation);
    }

    internal static void Validate(CompilerManifestArtifact value)
    {
        if (!HasValidEnvelope(value) ||
            !HasValidDiagnostics(value.CompilerDiagnostics) ||
            !HasMatchingCallables(value.Callables, value.Manifest) ||
            !HasValidEffectReplayTrees(value.Callables, value.Compilation))
        {
            throw new JsonException("The compiler manifest artifact is invalid.");
        }

        CompilationFingerprint.ValidateShape(value.Compilation);
    }

    private static bool HasValidEnvelope(CompilerManifestArtifact? value)
    {
        return value is
        {
            Schema: CompilerManifestArtifactVersions.Schema,
            SchemaVersion: CompilerManifestArtifactVersions.Current,
            ProtocolVersion: WorkerProtocolVersions.Current,
            Compilation: not null,
            MaximumExpressionDepth: >= 1 and <= 256
        } &&
        WorkerProtocolJson.IsDefined(value.Features, WorkerFeatureSet.Unspecified) &&
        WorkerProtocolJson.IsSha256(value.CompilationSha256) &&
        value.CompilationSha256 == CompilationFingerprint.ComputeSha256(value.Compilation) &&
        WorkerProtocolJson.ValidateManifest(value.Manifest).IsValid;
    }

    private static bool HasValidDiagnostics(CompilerDiagnosticArtifact[]? diagnostics)
    {
        return diagnostics != null &&
        diagnostics.All(static item =>
            item != null &&
            !string.IsNullOrWhiteSpace(item.Code) &&
            !string.IsNullOrWhiteSpace(item.Message) &&
            item.Location is { Start: >= 0, Length: >= 0, Line: >= 0, Column: >= 0 });
    }

    private static bool HasMatchingCallables(
        CompilerCallableArtifact[]? callables,
        WorkerClaimManifest manifest)
    {
        return callables != null &&
        callables.Length == manifest.Callables.Length &&
        callables.Select(static item => item?.CallableId).SequenceEqual(
            manifest.Callables.Select(static item => item.CallableId),
            StringComparer.Ordinal);
    }

    private static bool HasValidEffectReplayTrees(
        CompilerCallableArtifact[]? callables,
        CompilerCompilationSnapshot? compilation)
    {
        if (callables == null || compilation?.SyntaxTrees == null)
        {
            return false;
        }

        foreach (var effectEvent in callables
                     .Where(static callable => callable != null)
                     .SelectMany(static callable => callable.EffectClaims ?? [])
                     .Where(static claim => claim != null)
                     .SelectMany(static claim => claim.Replay?.Events ?? []))
        {
            if (effectEvent == null ||
                effectEvent.SyntaxTreeOrdinal < 0 ||
                effectEvent.SyntaxTreeOrdinal >= compilation.SyntaxTrees.Length)
            {
                return false;
            }

            var tree = compilation.SyntaxTrees[effectEvent.SyntaxTreeOrdinal];
            if (tree == null ||
                effectEvent.SyntaxTreeSha256 != tree.Sha256 ||
                effectEvent.SyntaxStart < 0 ||
                effectEvent.SyntaxLength <= 0 ||
                effectEvent.SyntaxStart > tree.TextLength ||
                effectEvent.SyntaxLength > tree.TextLength - effectEvent.SyntaxStart)
            {
                return false;
            }
        }

        return true;
    }
}

internal static class CompilerManifestArtifactFile
{
    internal const int MaximumBytes = WorkerProtocolJson.MaximumJsonBytes;

    internal static byte[] ReadAllBytes(string path)
    {
        using var stream = Open(path, out var length);
        var bytes = new byte[length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0)
            {
                throw new InvalidDataException(
                    "The compiler manifest changed while it was read.");
            }

            offset += read;
        }
        EnsureEndOfFile(stream.ReadByte());
        return bytes;
    }

    private static FileStream Open(string path, out int length)
    {
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        if (stream.Length > MaximumBytes)
        {
            stream.Dispose();
            throw new InvalidDataException(
                "The compiler manifest exceeds the byte limit.");
        }

        length = checked((int)stream.Length);
        return stream;
    }

    private static void EnsureEndOfFile(int extraByte)
    {
        if (extraByte != -1)
        {
            throw new InvalidDataException(
                "The compiler manifest changed while it was read.");
        }
    }
}
