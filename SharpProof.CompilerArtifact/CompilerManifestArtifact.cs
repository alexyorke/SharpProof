using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;
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
        hash.Add("protocol")
            .Add(request.ProtocolVersion)
            .Add("cache_schema")
            .Add(WorkerCacheVersions.Current)
            .Add("tool.identity")
            .Add(toolIdentity)
            .Add("tool.version")
            .Add(toolVersion)
            .Add("tool.binary_sha256")
            .Add(workerBinarySha256)
            .Add("api_spec.identity")
            .Add(apiSpecIdentity)
            .Add("api_spec.version")
            .Add(apiSpecVersion)
            .Add("api_spec.content_sha256")
            .Add(apiSpecContentSha256)
            .Add("budget.query_rlimit")
            .Add(request.Budgets.QueryRlimit)
            .Add("budget.method_rlimit")
            .Add(request.Budgets.MethodRlimit)
            .Add("budget.method_wall_ms")
            .Add(request.Budgets.MethodWallTimeMilliseconds)
            .Add("budget.project_wall_ms")
            .Add(request.Budgets.ProjectWallTimeMilliseconds)
            .Add("budget.max_parallelism")
            .Add(request.Budgets.MaxParallelism)
            .Add("budget.expression_depth")
            .Add(request.Budgets.MaximumExpressionDepth);
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
        string workerPath,
        string? z3LibrarySha256 = null)
    {
        if (z3LibrarySha256 is not null &&
            !WorkerProtocolJson.IsSha256(z3LibrarySha256))
        {
            throw new InvalidDataException(
                "The Z3 native library identity is not a SHA-256 digest.");
        }
        var path = NormalizeWorkerPath(workerPath);
        var stagingDirectory = CreateStagingDirectory();
        FileStream[] stagedHandles = [];
        var stagedCount = 0;
        var ownershipTransferred = false;
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            using var dependency = OpenRead(ChangeExtension(path, ".deps.json"));
            var dependencyBytes = ReadSnapshotBytes(
                dependency,
                MaximumDependenciesBytes);
            var components = RuntimeComponents(path, dependencyBytes);
            ValidateSnapshotBytes(dependencyBytes);
            stagedHandles = new FileStream[components.Count];
            using var hash = new CanonicalHashWriter();
            hash.Add("SharpProof.WorkerBinarySet")
                .Add(z3LibrarySha256 is null ? 1 : 2);
            long totalBytes = 0;
#pragma warning disable CA2000 // Stream ownership transfers to the retained snapshot list.
            foreach (var component in components)
            {
                var sourceBytes = string.Equals(
                        component.Key,
                        GetFileName(ChangeExtension(path, ".deps.json")),
                        StringComparison.Ordinal)
                    ? dependencyBytes
                    : CompilerManifestArtifactFile.ReadAllBytes(
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
                    if (!ReferenceEquals(sourceBytes, dependencyBytes))
                    {
                        EnsureStagedComponentConsistency(
                            component.Value,
                            stagedPath);
                    }
                    hash.Add(component.Key).Add(stagedRead);
                }
                stagedHandles[stagedCount++] = OpenRead(stagedPath);
            }
            if (z3LibrarySha256 is not null)
            {
                hash.Add("z3-native-sha256").Add(z3LibrarySha256);
            }
#pragma warning restore CA2000
            var snapshot = new WorkerRuntimeClosureSnapshot(
                path,
                Combine(stagingDirectory, GetFileName(path)),
                components.Values,
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

    internal static string ComputeSha256(
        string workerPath,
        string? z3LibrarySha256 = null)
    {
        using var snapshot = CreateSnapshot(workerPath, z3LibrarySha256);
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

    private static byte[] ReadSnapshotBytes(FileStream stream, long maximumBytes)
    {
        if (stream.Length > maximumBytes)
        {
            throw new InvalidDataException(
                "The worker runtime component exceeds the byte limit.");
        }

        return CompilerManifestArtifactFile.ReadExact(
            stream,
            checked((int)stream.Length),
            "A worker runtime component changed while it was read.");
    }

    private static void ValidateSnapshotBytes(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            throw new InvalidDataException(
                "The worker runtime component exceeds the byte limit.");
        }
    }

    private static SortedDictionary<string, string> RuntimeComponents(
        string workerPath,
        byte[] dependencyBytes)
    {
        var directory = GetDirectoryName(workerPath)!;
        using var document = JsonDocument.Parse(
            dependencyBytes,
            new JsonDocumentOptions { MaxDepth = 32 });
        var root = document.RootElement;
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            GetFileName(workerPath),
            GetFileName(ChangeExtension(workerPath, ".deps.json")),
            GetFileName(ChangeExtension(workerPath, ".runtimeconfig.json"))
        };
        foreach (Match match in Regex.Matches(
                     root.GetRawText(),
                     @"(?<![A-Za-z0-9_./-])(?!runtimes/)(?:[A-Za-z0-9_.-]+/)*[A-Za-z0-9_.-]+\.dll"))
        {
            var name = match.Value;
            names.Add(GetFileName(name));
        }
        var optionalFrameworkAssembly = GetFileName(
            typeof(ImmutableArray<>).Assembly.Location);
        if (File.Exists(Combine(directory, optionalFrameworkAssembly)))
        {
            names.Add(optionalFrameworkAssembly);
        }

        ValidateComponentCount(names.Count);
        var result = new SortedDictionary<string, string>(
            StringComparer.Ordinal);
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
    IEnumerable<string> componentPaths,
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
    private sealed class ClaimPartitions
    {
        internal ClaimPartitions(IEnumerable<WorkerClaimManifestEntry> claims)
        {
            var postconditions = new List<WorkerClaimManifestEntry>();
            var effects = new List<WorkerClaimManifestEntry>();
            foreach (var claim in claims.OrderBy(static claim => claim.Ordinal))
            {
                if (claim.Kind == WorkerClaimKind.Postcondition)
                {
                    postconditions.Add(claim);
                }
                else if (claim.Kind == WorkerClaimKind.Effect)
                {
                    effects.Add(claim);
                }
            }
            Postconditions = [.. postconditions];
            Effects = [.. effects];
        }

        internal WorkerClaimManifestEntry[] Postconditions { get; }
        internal WorkerClaimManifestEntry[] Effects { get; }
    }

    internal static string Serialize(
        CompilerManifestArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        return SerializeCore(
            artifact,
            validate: true,
            canonicalize: true,
            cancellationToken: cancellationToken);
    }

    internal static string SerializeValidated(
        CompilerManifestArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        return SerializeCore(
            artifact,
            validate: false,
            canonicalize: true,
            cancellationToken: cancellationToken);
    }

    // CompilerManifestArtifactProducer validates and canonicalizes the
    // artifact before handing it directly to one of these writers. Keep this
    // precondition explicit so ordinary callers still receive the defensive
    // canonicalization performed by SerializeValidated.
    internal static string SerializeProducerValidated(
        CompilerManifestArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        return SerializeCore(
            artifact,
            validate: false,
            canonicalize: false,
            cancellationToken: cancellationToken);
    }

    private static string SerializeCore(
        CompilerManifestArtifact artifact,
        bool validate,
        bool canonicalize,
        CancellationToken cancellationToken)
    {
        artifact = ArgumentNullGuard.NotNull(artifact, nameof(artifact));
        cancellationToken.ThrowIfCancellationRequested();

        if (validate && !HasValidDiagnosticShapes(artifact.CompilerDiagnostics))
        {
            throw new JsonException("The compiler diagnostics are invalid.");
        }

        if (canonicalize)
        {
            WorkerProtocolJson.Canonicalize(artifact.Manifest);
            artifact.CompilerDiagnostics =
                CompilerDiagnosticArtifactOrdering.Canonicalize(
                    artifact.CompilerDiagnostics);
            artifact.Callables = [
                .. artifact.Callables.OrderBy(
                    static item => item.CallableId,
                    StringComparer.Ordinal)
            ];
            artifact.LocationAuthorities = [
                .. (artifact.LocationAuthorities ?? [])
                    .OrderBy(static item => item?.OwnerKind)
                    .ThenBy(static item => item?.OwnerId, StringComparer.Ordinal)
            ];
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (validate)
        {
            Validate(
                artifact,
                validateDecodability: true,
                cancellationToken);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var json = JsonSerializer.Serialize(
                artifact,
                WorkerProtocolJson.SharedOptions) +
            "\n";
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWithinByteLimit(json);

        return json;
    }

    private static void EnsureWithinByteLimit(string json)
    {
        if (Encoding.UTF8.GetByteCount(json) >
            CompilerManifestArtifactFile.MaximumBytes)
        {
            throw new JsonException(
                "The compiler manifest exceeds the worker input byte limit.");
        }
    }

    internal static CompilerManifestArtifact Deserialize(
        string json,
        CancellationToken cancellationToken = default)
    {
        json = ArgumentNullGuard.NotNull(json, nameof(json));
        cancellationToken.ThrowIfCancellationRequested();
        EnsureWithinByteLimit(json);
        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions { MaxDepth = WorkerProtocolJson.MaximumJsonDepth });
        RequireCompatibilityProperties(document.RootElement, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var artifact = JsonSerializer.Deserialize<CompilerManifestArtifact>(
            document.RootElement, WorkerProtocolJson.SharedOptions) ??
            throw new JsonException("A compiler manifest artifact is required.");
        cancellationToken.ThrowIfCancellationRequested();
        Validate(artifact, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (SerializeValidated(artifact, cancellationToken) != json)
        {
            throw new JsonException("The compiler manifest artifact is not canonical.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return artifact;
    }

    internal static ImmutableArray<CompilerCallablePreparation> DecodeCallables(
        CompilerManifestArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        // DecodeCallables is also used by in-memory hydration probes that
        // deliberately mutate lowered evidence after the wire seal. Let the
        // existing lowerer report those malformed-body cases; wire reads and
        // writes still enforce the feature-scope seal below.
        Validate(
            artifact,
            validateDecodability: false,
            cancellationToken);
        return CompilerLoweredArtifact.Decode(
            artifact.Callables,
            artifact.Manifest,
            artifact.Compilation,
            cancellationToken);
    }

    internal static void Validate(
        CompilerManifestArtifact value,
        CancellationToken cancellationToken = default)
    {
        Validate(
            value,
            validateDecodability: true,
            cancellationToken);
    }

    private static void Validate(
        CompilerManifestArtifact value,
        bool validateDecodability,
        CancellationToken cancellationToken)
    {
        // The artifact is trusted build output: validate only what the worker
        // needs to decode and account for it, not producer-side fingerprints.
        cancellationToken.ThrowIfCancellationRequested();
        RequireValid(HasValidEnvelope(value));
        RequireValid(HasMatchingCallables(value.Callables, value.Manifest));
        RequireValid(HasValidCallableStates(
            value.Callables,
            value.CompilerDiagnostics.Length != 0));
        cancellationToken.ThrowIfCancellationRequested();
        if (validateDecodability &&
            !HasDecodableCallables(value, cancellationToken))
        {
            throw new JsonException(
                "The compiler manifest callable payload is invalid.");
        }

        static void RequireValid(bool condition)
        {
            if (!condition)
            {
                throw new JsonException(
                    "The compiler manifest artifact is invalid.");
            }
        }
    }

    private static bool HasDecodableCallables(
        CompilerManifestArtifact value,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = CompilerLoweredArtifact.Decode(
                value.Callables,
                value.Manifest,
                value.Compilation,
                cancellationToken);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool HasValidEnvelope(CompilerManifestArtifact? value)
    {
        return value is
        {
            Schema: CompilerManifestArtifactVersions.Schema,
            SchemaVersion: CompilerManifestArtifactVersions.Current,
            ProtocolVersion: WorkerProtocolVersions.Current,
            RelationalSummarySchemaVersion:
                CompilerRelationalSummaryVersions.Current,
            SpecificationPackSchemaVersion:
                CompilerSpecificationPackVersions.Current,
            Compilation: not null,
            MaximumExpressionDepth: >= 1 and <= 256
        } &&
        WorkerProtocolJson.IsDefined(value.Features, WorkerFeatureSet.Unspecified) &&
        WorkerProtocolJson.ValidateManifest(value.Manifest).IsValid;
    }

    private static void RequireCompatibilityProperties(
        JsonElement root,
        CancellationToken cancellationToken)
    {
        RequireProperty(root, "specificationPackIds");
        RequireProperty(root, "specificationPackCatalogVersion");
        RequireProperty(root, "specificationPackCatalogSha256");
        var compilation = RequireProperty(root, "compilation");
        RequireProperty(compilation, "specificationPackIds");
        RequireProperty(compilation, "specificationPackCatalogVersion");
        RequireProperty(compilation, "specificationPackCatalogSha256");
        cancellationToken.ThrowIfCancellationRequested();
        if (!root.TryGetProperty("compilerDiagnostics", out var diagnostics) ||
            diagnostics.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var diagnostic in diagnostics.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireProperty(diagnostic, "isSource");
        }
    }

    private static JsonElement RequireProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var value))
        {
            throw new JsonException(
                "The compiler manifest is missing '" + name + "'.");
        }

        return value;
    }

    private static bool HasValidDiagnosticShapes(
        CompilerDiagnosticArtifact[]? diagnostics)
    {
        return diagnostics?.All(HasValidDiagnosticShape) == true;
    }

    private static bool HasValidDiagnosticShape(
        CompilerDiagnosticArtifact? item)
    {
        return item != null &&
            WorkerProtocolJson.IsCompilerDiagnosticCode(item.Code) &&
            !string.IsNullOrWhiteSpace(item.Message) &&
            item.SourceTreePath != null &&
            item.SourceTreeSha256 != null &&
            item.SourceLineMapSha256 != null &&
            item.Location is { Path: not null } location &&
            WorkerProtocolJson.HasValidLocationOrNone(location);
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

    private static bool HasValidCallableStates(
        CompilerCallableArtifact[]? callables,
        bool hasCompilerDiagnostics)
    {
        return callables?.All(callable =>
            callable != null &&
            (!hasCompilerDiagnostics ||
             callable.FailureReason ==
                CompilerCallableArtifactReasonCatalog.DiagnosticFailureReason) &&
            (callable.FailureReason ==
                CompilerCallableArtifactReasonCatalog.SuccessReason ||
             CompilerCallableArtifactReasonCatalog.IsFailureReason(
                 callable.FailureReason))) == true;
    }

}

internal static class CompilerManifestArtifactFile
{
    // Compiler manifests contain lowered evidence for every annotated method,
    // so they need a larger bounded envelope than ordinary worker JSON files.
    internal const int MaximumBytes = 32 * 1024 * 1024;

    internal static byte[] ReadAllBytes(
        string path,
        int maximumBytes = MaximumBytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = Open(path, out var length, maximumBytes);
        cancellationToken.ThrowIfCancellationRequested();
        return ReadExact(
            stream,
            length,
            "The compiler manifest changed while it was read.",
            cancellationToken);
    }

    internal static byte[] ReadExact(
        FileStream stream,
        int length,
        string changedMessage,
        CancellationToken cancellationToken = default)
    {
        var bytes = new byte[length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(
                bytes,
                offset,
                Math.Min(64 * 1024, bytes.Length - offset));
            if (read == 0)
            {
                throw new InvalidDataException(changedMessage);
            }

            offset += read;
        }
        cancellationToken.ThrowIfCancellationRequested();
        EnsureEndOfFile(stream.ReadByte());
        cancellationToken.ThrowIfCancellationRequested();
        return bytes;
    }

    private static FileStream Open(
        string path,
        out int length,
        int maximumBytes)
    {
        // Inspect the directory entry before opening it. On Unix, opening a
        // FIFO for reading blocks until a writer arrives, so the regular-file
        // and byte-limit checks must happen before FileStream opens the path.
        var fileInfo = new FileInfo(path);
        var fileLength = fileInfo.Length;
        if (fileLength <= 0)
        {
            throw new InvalidDataException(
                "The compiler manifest must be a nonempty regular file.");
        }
        if (fileLength > maximumBytes)
        {
            throw new InvalidDataException(
                "The compiler manifest exceeds the byte limit.");
        }

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        if (stream.Length != fileLength)
        {
            stream.Dispose();
            throw new InvalidDataException(
                "The compiler manifest changed while it was opened.");
        }

        length = checked((int)fileLength);
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
