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
        string artifactDigest,
        string toolIdentity,
        string toolVersion,
        string workerBinarySha256,
        string apiSpecIdentity,
        string apiSpecVersion,
        string apiSpecContentSha256)
    {
        request = ArgumentNullGuard.NotNull(request, nameof(request));
        artifactDigest = ArgumentNullGuard.NotNull(artifactDigest, nameof(artifactDigest));

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
        return hash.Add("compiler_manifest").Add(artifactDigest).Finish();
    }
}

// Cache identity of the worker runtime: the SharpProof and Z3 managed
// assemblies shipped beside the worker plus the native Z3 library digest.
internal static class WorkerBinaryIdentity
{
    internal static string ComputeSha256(
        string workerPath,
        string? z3LibrarySha256 = null)
    {
        var path = Path.GetFullPath(workerPath);
        if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "The managed worker binary must be an existing .dll.",
                path);
        }

        var directory = Path.GetDirectoryName(path)!;
        var files = Directory.EnumerateFiles(directory, "SharpProof.*.dll")
            .Append(Path.Combine(directory, "Microsoft.Z3.dll"))
            .Where(File.Exists)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static file => Path.GetFileName(file), StringComparer.Ordinal);
        using var hash = new CanonicalHashWriter();
        foreach (var file in files)
        {
            hash.Add(Path.GetFileName(file)).Add(File.ReadAllBytes(file));
        }
        return hash.Add("z3.native").Add(z3LibrarySha256 ?? string.Empty).Finish();
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

    // The compiler emits a closed artifact. Semantic validation and IR decoding
    // happen once at the worker boundary; this path only writes producer data.
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

        // Reject malformed containers before canonicalization dereferences them.
        if (validate && (artifact.Manifest == null ||
            artifact.Callables == null ||
            artifact.Callables.Any(static item => item == null)))
        {
            throw new JsonException("The compiler manifest artifact is invalid.");
        }

        if (validate && !HasValidDiagnosticShapes(artifact.CompilerDiagnostics))
        {
            throw new JsonException("The compiler diagnostics are invalid.");
        }

        if (canonicalize)
        {
            try
            { WorkerProtocolJson.Canonicalize(artifact.Manifest); }
            catch (ArgumentOutOfRangeException exception) when (exception.ActualValue is Enum)
            {
                throw new JsonException("The compiler manifest contains an unknown enum value.", exception);
            }
            artifact.CompilerDiagnostics =
                CompilerDiagnosticArtifactOrdering.Canonicalize(
                    artifact.CompilerDiagnostics);
            artifact.Callables = [
                .. artifact.Callables.OrderBy(
                    static item => item.CallableId,
                    StringComparer.Ordinal)
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
        return DeserializePrepared(json, out _, cancellationToken);
    }

    internal static CompilerManifestArtifact DeserializePrepared(
        string json,
        out ImmutableArray<CompilerCallablePreparation> callables,
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
        try
        {
            callables = DecodeCallables(artifact, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            throw new JsonException("The compiler manifest callable payload is invalid.", exception);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return artifact;
    }

    internal static ImmutableArray<CompilerCallablePreparation> DecodeCallables(
        CompilerManifestArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        Validate(
            artifact,
            validateDecodability: false,
            cancellationToken);
        artifact.Compilation.SpecificationPackIds = artifact.SpecificationPackIds;
        artifact.Compilation.SpecificationPackCatalogVersion = artifact.SpecificationPackCatalogVersion;
        artifact.Compilation.SpecificationPackCatalogSha256 = artifact.SpecificationPackCatalogSha256;
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
        RequireValid(HasValidDiagnosticShapes(value.CompilerDiagnostics));
        RequireValid(HasMatchingCallables(value.Callables, value.Manifest));
        RequireValid(HasValidCallableStates(
            value.Callables,
            value.CompilerDiagnostics.Length != 0));
        CompilerReachableSourceValidator.Validate(value, cancellationToken);
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
        RequireProperty(root, "compilation");
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
             (callable.FailureReason ==
                CompilerCallableArtifactReasonCatalog.DiagnosticFailureReason && callable.Total == null)) &&
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
