using System.Text;
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
            "budget.expression_depth", request.Budgets.MaximumExpressionDepth);
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
                    hash.Add(component.Key).Add(stagedRead);
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
        artifact.LocationAuthorities = [
            .. (artifact.LocationAuthorities ?? [])
                .OrderBy(static item => item?.OwnerKind)
                .ThenBy(static item => item?.OwnerId, StringComparer.Ordinal)
        ];
        Validate(artifact);
        var json = JsonSerializer.Serialize(
                artifact,
                WorkerProtocolJson.Options) +
            "\n";
        if (Encoding.UTF8.GetByteCount(json) >
            CompilerManifestArtifactFile.MaximumBytes)
        {
            throw new JsonException(
                "The compiler manifest exceeds the worker input byte limit.");
        }

        return json;
    }

    internal static CompilerManifestArtifact Deserialize(string json)
    {
        json = ArgumentNullGuard.NotNull(json, nameof(json));
        RequireSpecificationPackAuthorityProperties(json);

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
        // DecodeCallables is also used by in-memory hydration probes that
        // deliberately mutate lowered evidence after the wire seal. Let the
        // existing lowerer report those malformed-body cases; wire reads and
        // writes still enforce the feature-scope seal below.
        Validate(artifact, validateFeatureScope: false);
        return CompilerLoweredArtifact.Decode(
            artifact.Callables,
            artifact.Manifest,
            artifact.Compilation);
    }

    internal static void Validate(CompilerManifestArtifact value)
    {
        Validate(value, validateFeatureScope: true);
    }

    private static void Validate(
        CompilerManifestArtifact value,
        bool validateFeatureScope)
    {
        if (!HasValidDiagnostics(value.CompilerDiagnostics, value.Compilation) ||
            !HasValidEnvelope(value) ||
            (validateFeatureScope && !HasValidFeatureScope(value)) ||
            !HasValidLocationAuthorities(value) ||
            !HasMatchingCallables(value.Callables, value.Manifest) ||
            !HasValidCallableStates(
                value.Callables,
                value.CompilerDiagnostics.Length != 0) ||
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
            RelationalSummarySchemaVersion:
                CompilerRelationalSummaryVersions.Current,
            SpecificationPackSchemaVersion:
                CompilerSpecificationPackVersions.Current,
            Compilation: not null,
            MaximumExpressionDepth: >= 1 and <= 256
        } &&
        CompilerSpecificationPackAuthorityValidation.Matches(value) &&
        WorkerProtocolJson.IsDefined(value.Features, WorkerFeatureSet.Unspecified) &&
        WorkerProtocolJson.IsSha256(value.CompilationSha256) &&
        value.CompilationSha256 == CompilationFingerprint.ComputeSha256(
            value.Compilation, value.CompilerDiagnostics) &&
        WorkerProtocolJson.ValidateManifest(value.Manifest).IsValid;
    }

    private static bool HasValidFeatureScope(CompilerManifestArtifact? value)
    {
        return value != null &&
            WorkerProtocolJson.IsSha256(value.FeatureScopeSha256) &&
            HasFeatureScopeParity(value) &&
            value.FeatureScopeSha256 ==
                CompilerFeatureScopeFingerprint.ComputeSha256(value);
    }

    private static bool HasFeatureScopeParity(CompilerManifestArtifact value)
    {
        if (value.Manifest?.Callables is not { } manifestCallables ||
            value.Manifest.Claims is not { } manifestClaims ||
            value.Callables is not { } loweredCallables)
        {
            return false;
        }

        var allowEffects = value.Features is WorkerFeatureSet.Effects or WorkerFeatureSet.All;
        var allowContracts = value.Features is WorkerFeatureSet.Contracts or WorkerFeatureSet.All;
        var loweredById = new Dictionary<string, CompilerCallableArtifact>(
            StringComparer.Ordinal);
        foreach (var lowered in loweredCallables)
        {
            if (lowered == null || loweredById.ContainsKey(lowered.CallableId))
            {
                return false;
            }

            loweredById.Add(lowered.CallableId, lowered);
        }

        foreach (var callable in manifestCallables)
        {
            if (callable == null ||
                !loweredById.TryGetValue(callable.CallableId, out var lowered) ||
                lowered == null)
            {
                return false;
            }

            var claims = manifestClaims
                .Where(claim => claim != null && claim.CallableId == callable.CallableId)
                .OrderBy(static claim => claim!.Ordinal)
                .ToArray();
            var postconditions = claims
                .Where(static claim => claim!.Kind == WorkerClaimKind.Postcondition)
                .ToArray();
            var effects = claims
                .Where(static claim => claim!.Kind == WorkerClaimKind.Effect)
                .ToArray();
            var selectedEffects = callable.SelectedFeatures.Contains(WorkerSelectedFeature.Effects);
            var selectedContracts = callable.SelectedFeatures.Contains(WorkerSelectedFeature.Contracts);

            if ((selectedEffects && !allowEffects) ||
                (selectedContracts && !allowContracts) ||
                (effects.Length != 0 && (!selectedEffects || !allowEffects)) ||
                (postconditions.Length != 0 && (!selectedContracts || !allowContracts)) ||
                callable.Assumptions.Any(assumption =>
                    assumption != null &&
                    assumption.Kind is (WorkerAssumptionKind.Precondition or WorkerAssumptionKind.UserAssume) &&
                    (!selectedContracts || !allowContracts)))
            {
                return false;
            }

            var hasExplicitReason = callable.SelectionReasons.Contains(
                WorkerSelectionReason.ExplicitAnnotation);
            var hasDiscoveredReason = callable.SelectionReasons.Contains(
                WorkerSelectionReason.DiscoveredPostcondition);
            var hasContractAssumptions = callable.Assumptions.Any(assumption =>
                assumption != null &&
                assumption.Kind is (WorkerAssumptionKind.Precondition or WorkerAssumptionKind.UserAssume));
            if (hasDiscoveredReason != (postconditions.Length != 0) ||
                (hasExplicitReason &&
                 callable.SelectedFeatures.Length == 0 &&
                 callable.Assumptions.Length == 0) ||
                (!hasContractAssumptions && callable.Assumptions.Length == 0 &&
                 !selectedContracts &&
                 !selectedEffects &&
                 !hasDiscoveredReason && callable.SelectionReasons.Length != 0))
            {
                return false;
            }

            var loweredEffects = lowered.EffectClaims;
            if (loweredEffects == null ||
                loweredEffects.Length != effects.Length ||
                !loweredEffects.Select(static effect => effect?.ClaimId)
                    .SequenceEqual(effects.Select(static claim => claim!.ClaimId), StringComparer.Ordinal) ||
                !loweredEffects.Select(static effect => effect!.ContractKind)
                    .SequenceEqual(effects.Select(static claim => claim!.EffectContractKind)))
            {
                return false;
            }

            foreach (var effect in loweredEffects)
            {
                if (effect == null)
                {
                    return false;
                }

                try
                {
                    CompilerEffectClaimArtifactCodec.Validate(effect);
                }
                catch (InvalidDataException)
                {
                    return false;
                }
            }

            if (lowered.FailureReason != CompilerCallableArtifactReasonCatalog.SuccessReason)
            {
                continue;
            }

            if (lowered.Clauses is not { } clauses)
            {
                return false;
            }

            var loweredPostconditions = clauses
                .Where(static clause => clause?.Kind == CompilerContractKind.Ensures)
                .ToArray();
            if (loweredPostconditions.Length != postconditions.Length ||
                !loweredPostconditions.Select(static clause => clause?.ClaimId)
                    .SequenceEqual(postconditions.Select(static claim => claim!.ClaimId), StringComparer.Ordinal) ||
                !loweredPostconditions.Select(static clause => ManifestEvidence(clause!.Evidence))
                    .SequenceEqual(postconditions.Select(static claim => claim!.Evidence)))
            {
                return false;
            }

            var declaredAssumptions = callable.Assumptions
                .Where(static assumption => assumption != null &&
                    assumption.Kind is WorkerAssumptionKind.Precondition or WorkerAssumptionKind.UserAssume)
                .Select(static assumption => (assumption!.Id, assumption.Kind))
                .OrderBy(static item => item.Id, StringComparer.Ordinal)
                .ToArray();
            var loweredAssumptions = clauses
                .Where(static clause => clause?.Kind != CompilerContractKind.Ensures)
                .Select(static clause => clause == null
                    ? (string.Empty, WorkerAssumptionKind.Unspecified)
                    : (clause.AssumptionId ?? string.Empty,
                        clause.Kind == CompilerContractKind.Requires
                            ? WorkerAssumptionKind.Precondition
                            : WorkerAssumptionKind.UserAssume))
                .OrderBy(static item => item.Item1, StringComparer.Ordinal)
                .ToArray();
            if (!declaredAssumptions.SequenceEqual(loweredAssumptions))
            {
                return false;
            }
        }

        return true;
    }

    private static WorkerClaimEvidence ManifestEvidence(
        CompilerContractEvidence value)
    {
        return value switch
        {
            CompilerContractEvidence.CompilerBoundInvocation => WorkerClaimEvidence.DirectClause,
            CompilerContractEvidence.ClosedAttribute => WorkerClaimEvidence.ReturnAttribute,
            CompilerContractEvidence.Companion => WorkerClaimEvidence.CompanionClause,
            _ => WorkerClaimEvidence.Unspecified
        };
    }

    private static void RequireSpecificationPackAuthorityProperties(string json)
    {
        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions { MaxDepth = WorkerProtocolJson.MaximumJsonDepth });
        var root = document.RootElement;
        RequireProperty(root, "specificationPackIds");
        RequireProperty(root, "specificationPackCatalogVersion");
        RequireProperty(root, "specificationPackCatalogSha256");
        var compilation = RequireProperty(root, "compilation");
        RequireProperty(compilation, "specificationPackIds");
        RequireProperty(compilation, "specificationPackCatalogVersion");
        RequireProperty(compilation, "specificationPackCatalogSha256");
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

    private static bool HasValidDiagnostics(
        CompilerDiagnosticArtifact[]? diagnostics,
        CompilerCompilationSnapshot? compilation)
    {
        return HasValidDiagnosticShapes(diagnostics) &&
            CompilerDiagnosticArtifactOrdering.IsCanonical(diagnostics!) &&
            diagnostics!.All(item => HasValidDiagnosticBinding(item, compilation));
    }

    private static bool HasValidDiagnosticShapes(
        CompilerDiagnosticArtifact[]? diagnostics)
    {
        return diagnostics?.All(static item =>
            item != null &&
            WorkerProtocolJson.IsCompilerDiagnosticCode(item.Code) &&
            !string.IsNullOrWhiteSpace(item.Message) &&
            item.Location is { Path: not null } location &&
            WorkerProtocolJson.HasValidLocationOrNone(location)) == true;
    }

    private static bool HasValidDiagnosticBinding(
        CompilerDiagnosticArtifact value,
        CompilerCompilationSnapshot? compilation)
    {
        if (value?.Location is not { } location)
        {
            return false;
        }

        if (CompilerSourceLocationAuthority.IsNone(location))
        {
            return value.SourceTreeOrdinal == -1 &&
                value.SourceTreePath.Length == 0 &&
                value.SourceTreeSha256.Length == 0 &&
                value.SourceLineMapSha256.Length == 0;
        }

        // Keep existing in-memory protocol-shape fixtures usable.  All
        // diagnostics emitted by the compiler collector carry this binding;
        // when a binding is present it is complete and independently checked.
        if (value.SourceTreeOrdinal == -1 &&
            value.SourceTreePath.Length == 0 &&
            value.SourceTreeSha256.Length == 0 &&
            value.SourceLineMapSha256.Length == 0)
        {
            return true;
        }

        return CompilerSourceLocationAuthority.IsBound(
            location,
            value.SourceTreeOrdinal,
            value.SourceTreePath,
            value.SourceTreeSha256,
            value.SourceLineMapSha256,
            compilation);
    }

    private static bool HasValidLocationAuthorities(
        CompilerManifestArtifact? value)
    {
        if (value?.LocationAuthorities is not { } authorities ||
            value.Manifest is not { } manifest ||
            value.Compilation is not { } compilation ||
            authorities.Length !=
                manifest.Callables.Length + manifest.Claims.Length)
        {
            return false;
        }

        if (authorities.Any(static authority => authority == null) ||
            !authorities.Zip(
                    authorities.Skip(1),
                    static (left, right) => CompareAuthorities(left, right) < 0)
                .All(static ordered => ordered))
        {
            return false;
        }

        var expected = manifest.Callables
            .Select(static entry => (
                Kind: CompilerSourceLocationOwnerKind.Callable,
                Id: entry.CallableId,
                Location: entry.Location))
            .Concat(manifest.Claims.Select(static entry => (
                Kind: CompilerSourceLocationOwnerKind.Claim,
                Id: entry.ClaimId,
                Location: entry.Location)))
            .OrderBy(static value => value.Kind)
            .ThenBy(static value => value.Id, StringComparer.Ordinal)
            .ToArray();
        if (expected.Any(static row => row.Location == null))
        {
            return false;
        }

        for (var index = 0; index < expected.Length; index++)
        {
            var authority = authorities[index];
            var row = expected[index];
            if (!Enum.IsDefined(
                    typeof(CompilerSourceLocationOwnerKind),
                    authority.OwnerKind) ||
                string.IsNullOrWhiteSpace(authority.OwnerId) ||
                authority.OwnerKind != row.Kind ||
                authority.OwnerId != row.Id ||
                !CompilerSourceLocationAuthority.LocationsEqual(
                    authority.Location,
                    row.Location) ||
                !CompilerSourceLocationAuthority.IsBound(
                    authority.Location,
                    authority.SourceTreeOrdinal,
                    authority.SourceTreePath,
                    authority.SourceTreeSha256,
                    authority.SourceLineMapSha256,
                    compilation,
                    allowNone: true))
            {
                return false;
            }
        }

        return true;
    }

    private static int CompareAuthorities(
        CompilerLocationAuthorityArtifact left,
        CompilerLocationAuthorityArtifact right)
    {
        var result = left.OwnerKind.CompareTo(right.OwnerKind);
        return result != 0
            ? result
            : StringComparer.Ordinal.Compare(left.OwnerId, right.OwnerId);
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
                effectEvent.SyntaxTreeSnapshotSha256 !=
                    CompilationFingerprint.ComputeSyntaxTreeSnapshotSha256(tree) ||
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
