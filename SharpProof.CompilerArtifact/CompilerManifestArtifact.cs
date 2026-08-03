using System.Text.Json;

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
        var streams = new List<FileStream>();
        try
        {
            using var dependency = OpenRead(Path.ChangeExtension(path, ".deps.json"));
            var components = RuntimeComponents(path, dependency);
            ValidateComponentCount(components.Count);
            using var hash = new CanonicalHashWriter();
            hash.Add("SharpProof.WorkerBinarySet", 1);
            long totalBytes = 0;
            foreach (var component in components)
            {
                var stream = OpenRead(component.Value);
                try
                {
                    ValidateComponentLength(
                        component.Key,
                        stream.Length,
                        ref totalBytes);
                    stream.Position = 0;
                    hash.Add(component.Key).Add(stream);
                    streams.Add(stream);
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }

            return new WorkerRuntimeClosureSnapshot(
                path,
                streams,
                hash.Finish());
        }
        catch
        {
            foreach (var stream in streams)
            {
                stream.Dispose();
            }

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
        var dependencies = Path.ChangeExtension(workerPath, ".deps.json");
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["worker"] = workerPath,
            ["dependencies"] = dependencies,
            ["runtimeConfig"] = Path.ChangeExtension(workerPath, ".runtimeconfig.json")
        };
        var immutableName = typeof(ImmutableArray<>).Assembly.GetName().Name + ".dll";
        var immutable = Path.Combine(directory, immutableName);
        if (File.Exists(immutable))
        {
            result.Add("app-local/" + immutableName, immutable);
        }
        long dependencyBytes = 0;
        ValidateComponentLength(
            "dependencies",
            dependencyStream.Length,
            ref dependencyBytes);
        using var document = JsonDocument.Parse(
            dependencyStream,
            new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        var targetName = root.GetProperty("runtimeTarget").GetProperty("name").GetString()!;
        var target = root.GetProperty("targets").GetProperty(targetName);
        foreach (var library in target.EnumerateObject())
        {
            AddLibraryAssets(result, directory, library.Value);
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

    private static void AddLibraryAssets(
        SortedDictionary<string, string> result,
        string directory,
        JsonElement library)
    {
        foreach (var group in new[] { "runtime", "native", "runtimeTargets" })
        {
            if (library.TryGetProperty(group, out var assets))
            {
                foreach (var asset in assets.EnumerateObject())
                {
                    if (group == "runtimeTargets" &&
                        asset.Value.GetProperty("rid").GetString() is not ("win" or "win-x64"))
                    {
                        continue;
                    }

                    result.Add(group + "/" + asset.Name, ResolveAsset(directory, asset.Name));
                }
            }
        }
    }

    private static string ResolveAsset(string directory, string relativePath)
    {
        var nested = Path.GetFullPath(Path.Combine(
            directory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var root = directory + Path.DirectorySeparatorChar;
        if (!nested.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("A worker runtime asset escapes its package.");
        }

        if (File.Exists(nested))
        {
            return nested;
        }

        var flattened = Path.Combine(directory, Path.GetFileName(relativePath));
        return File.Exists(flattened)
            ? flattened
            : throw new FileNotFoundException("A trusted worker runtime component is unavailable.", nested);
    }

}

internal sealed class WorkerRuntimeClosureSnapshot(
    string workerPath,
    IReadOnlyList<FileStream> components,
    string sha256) : IDisposable
{
    internal string WorkerPath { get; } = workerPath;
    internal string Sha256 { get; } = sha256;

    public void Dispose()
    {
        foreach (var stream in components)
        {
            stream.Dispose();
        }
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
