using System.Text.Json;
using System.Text.RegularExpressions;
using static System.IO.Path;

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
    internal const int MaximumComponentBytes = 32 * 1024 * 1024;
    internal const long MaximumClosureBytes = 64L * 1024 * 1024;
    internal const long MaximumDependenciesBytes = 1024L * 1024;
    internal const long MaximumRuntimeConfigBytes = 64L * 1024;

    internal static WorkerRuntimeClosureSnapshot CreateSnapshot(
        string workerPath)
    {
        var path = NormalizeWorkerPath(workerPath);
        var stagingDirectory = CreateStagingDirectory();
        FileStream[] stagedHandles = [];
        var stagedCount = 0;
        var ownershipTransferred = false;
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            WorkerCachePath.ValidateNoReparsePoints([
                stagingDirectory,
                path,
                ChangeExtension(path, ".deps.json")]);
            using var dependency = OpenRead(ChangeExtension(path, ".deps.json"));
            var components = RuntimeComponents(path, dependency);
            stagedHandles = new FileStream[components.Count];
            using var hash = new CanonicalHashWriter();
            hash.Add("SharpProof.WorkerBinarySet", 1);
            long totalBytes = 0;
#pragma warning disable CA2000 // Stream ownership transfers to the retained snapshot list.
            foreach (var component in components)
            {
                var sourceBytes = CompilerManifestArtifactFile.ReadAllBytes(
                    component.Value,
                    MaximumComponentBytes);
                var sourceLength = sourceBytes.LongLength;
                ValidateComponentLength(component.Key, sourceLength, ref totalBytes);
                var stagedPath = Combine(
                    stagingDirectory,
                    component.Key.Replace('/', DirectorySeparatorChar));
                Directory.CreateDirectory(GetDirectoryName(stagedPath)!);
                using (var staged = new FileStream(
                           stagedPath,
                           FileMode.CreateNew))
                {
                    staged.Write(sourceBytes, 0, sourceBytes.Length);
                }
                using (var stagedRead = OpenRead(stagedPath))
                {
                    var stagedTotalBytes = totalBytes - sourceLength;
                    ValidateComponentLength(
                        component.Key,
                        stagedRead.Length,
                        ref stagedTotalBytes);
                    EnsureStagedComponentConsistency(
                        component.Value,
                        stagedPath);
                    hash.Add(component.Key.ToUpperInvariant()).Add(stagedRead);
                }
                stagedHandles[stagedCount++] = OpenRead(stagedPath);
            }
#pragma warning restore CA2000
            var snapshot = new WorkerRuntimeClosureSnapshot(
                path,
                Combine(stagingDirectory, GetFileName(path)),
                components.Values.ToArray(),
                hash.Finish(),
                stagedHandles);
            ownershipTransferred = true;
            return snapshot;
        }
        finally
        {
            if (!ownershipTransferred)
            {
                for (var index = 0; index < stagedCount; index++)
                {
                    stagedHandles[index].Dispose();
                }
                DeleteStagingDirectory(stagingDirectory);
            }
        }
    }

    internal static string ComputeSha256(string workerPath)
    {
        using var snapshot = CreateSnapshot(workerPath);
        return snapshot.Sha256;
    }

    private static string NormalizeWorkerPath(string workerPath)
    {
        var path = GetFullPath(workerPath);
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

    internal static void EnsureStagedComponentConsistency(
        string sourcePath,
        string stagedPath)
    {
        if (!CompilerManifestArtifactFile.ReadAllBytes(
                    sourcePath,
                    MaximumComponentBytes).SequenceEqual(
                CompilerManifestArtifactFile.ReadAllBytes(
                    stagedPath,
                    MaximumComponentBytes)))
        {
            throw new InvalidDataException(
                "A worker runtime component changed during staging.");
        }
    }

    private static SortedDictionary<string, string> RuntimeComponents(
        string workerPath,
        FileStream dependencyStream)
    {
        var directory = GetDirectoryName(workerPath)!;
        long dependencyBytes = 0;
        ValidateComponentLength(
            "dependencies",
            dependencyStream.Length,
            ref dependencyBytes);
        using var document = JsonDocument.Parse(
            dependencyStream,
            new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            GetFileName(workerPath),
            GetFileName(ChangeExtension(workerPath, ".deps.json")),
            GetFileName(ChangeExtension(workerPath, ".runtimeconfig.json"))
        };
        foreach (Match match in Regex.Matches(
                     root.GetRawText(),
                     @"(?:runtimes/(?:win-x64|win)/[^""\r\n]+\.dll|(?<![A-Za-z0-9_./-])(?!runtimes/)(?:[A-Za-z0-9_.-]+/)*[A-Za-z0-9_.-]+\.dll)"))
        {
            var name = match.Value;
            names.Add(name.StartsWith(
                "runtimes/",
                StringComparison.OrdinalIgnoreCase) ? name : GetFileName(name));
        }
        var optionalFrameworkAssembly = GetFileName(
            typeof(ImmutableArray<>).Assembly.Location);
        if (File.Exists(Combine(directory, optionalFrameworkAssembly)))
        {
            names.Add(optionalFrameworkAssembly);
        }

        ValidateComponentCount(names.Count);
        var result = new SortedDictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            if (name.IndexOf('\\') >= 0 ||
                name.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                throw new InvalidDataException(
                    "A worker runtime component identity is invalid.");
            }

            result.Add(
                name,
                Combine(directory, name.Replace('/', DirectorySeparatorChar)));
        }

        WorkerCachePath.ValidateNoReparsePoints(result.Values);
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

    private static string CreateStagingDirectory()
    {
        return Combine(
            GetTempPath(),
            "SharpProof.Worker.Runtime." + GetRandomFileName());
    }

    internal static void DeleteStagingDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

}

internal sealed class WorkerRuntimeClosureSnapshot(
    string workerPath,
    string executionWorkerPath,
    IReadOnlyList<string> componentPaths,
    string sha256,
    IReadOnlyList<FileStream> stagedHandles) : IDisposable
{
    internal string WorkerPath { get; } = workerPath;
    internal string ExecutionWorkerPath { get; } = executionWorkerPath;
    internal IReadOnlyList<string> ComponentPaths { get; } =
        ImmutableArray.CreateRange(componentPaths);
    internal string Sha256 { get; } = sha256;
    private IReadOnlyList<FileStream> StagedHandles { get; } = stagedHandles;

    public void Dispose()
    {
        foreach (var handle in StagedHandles)
        {
            handle.Dispose();
        }
        WorkerBinaryIdentity.DeleteStagingDirectory(
            GetDirectoryName(ExecutionWorkerPath)!);
    }
}

internal static class CompilerManifestArtifactJson
{
    internal static string Serialize(CompilerManifestArtifact artifact)
    {
        artifact = ArgumentNullGuard.NotNull(artifact, nameof(artifact));

        if (!HasValidDiagnosticShapes(artifact.CompilerDiagnostics))
        {
            throw new JsonException("The compiler diagnostics are invalid.");
        }

        WorkerProtocolJson.Canonicalize(artifact.Manifest);
        artifact.CompilerDiagnostics =
            CompilerDiagnosticArtifactOrdering.Canonicalize(
                artifact.CompilerDiagnostics);
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
        if (!HasValidDiagnostics(value.CompilerDiagnostics) ||
            !HasValidEnvelope(value) ||
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
        value.CompilationSha256 == CompilationFingerprint.ComputeSha256(
            value.Compilation, value.CompilerDiagnostics) &&
        WorkerProtocolJson.ValidateManifest(value.Manifest).IsValid;
    }

    private static bool HasValidDiagnostics(CompilerDiagnosticArtifact[]? diagnostics)
    {
        return HasValidDiagnosticShapes(diagnostics) &&
        CompilerDiagnosticArtifactOrdering.IsCanonical(diagnostics!);
    }

    private static bool HasValidDiagnosticShapes(
        CompilerDiagnosticArtifact[]? diagnostics)
    {
        return diagnostics?.All(static item =>
            item != null &&
            !string.IsNullOrWhiteSpace(item.Code) &&
            !string.IsNullOrWhiteSpace(item.Message) &&
            item.Location is
            {
                Path: not null,
                Start: >= 0,
                Length: >= 0,
                Line: >= 0,
                Column: >= 0
            }) == true;
    }

    private static bool HasMatchingCallables(
        CompilerCallableArtifact[]? callables,
        WorkerClaimManifest manifest)
    {
        return callables?.Length == manifest.Callables.Length &&
        callables.Select(static item => item?.CallableId).SequenceEqual(
            manifest.Callables.Select(static item => item.CallableId),
            StringComparer.Ordinal);
    }

    private static bool HasValidEffectReplayTrees(
        CompilerCallableArtifact[]? callables,
        CompilerCompilationSnapshot? compilation)
    {
        if (callables is null || compilation is not { SyntaxTrees: not null })
        {
            return false;
        }

        foreach (var effectEvent in callables
                     .OfType<CompilerCallableArtifact>()
                     .SelectMany(static callable => callable.EffectClaims ?? [])
                     .OfType<CompilerEffectClaimArtifact>()
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

    internal static byte[] ReadAllBytes(
        string path,
        int maximumBytes = MaximumBytes)
    {
        using var stream = Open(path, out var length, maximumBytes);
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

    private static FileStream Open(
        string path,
        out int length,
        int maximumBytes)
    {
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        if (stream.Length > maximumBytes)
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
