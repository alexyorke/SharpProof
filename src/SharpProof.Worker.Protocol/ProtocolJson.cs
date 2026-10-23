using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace SharpProof.Worker.Protocol;

internal static partial class WorkerProtocolJson
{
    internal static ImmutableArray<T> EmptyIfDefault<T>(
        ImmutableArray<T> values /* SPINT001: all values valid - default is the wire null sentinel */)
    {
        return values.IsDefault ? [] : values;
    }

    private static ImmutableArray<T> CopyArrayOwners<T>(
        ImmutableArray<T> values /* SPINT001: all values valid - default preserves malformed wire null */,
        Func<T, T> copy)
    {
        copy = RepositoryParameterGuard.NotNull(copy, nameof(copy));
        return values.IsDefault ? default : [.. values.Select(copy)];
    }
    internal const string CompilerDiagnosticCodePrefix = "compiler.";
    internal const int MaximumJsonBytes = 16 * 1024 * 1024;
    internal const int MaximumJsonDepth = 32;
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);
    private static readonly StringComparer s_ordinal = StringComparer.Ordinal;
    private static readonly JsonSerializerOptions s_options = CreateOptions();
    private static readonly ProtocolJsonContext s_context = new(new JsonSerializerOptions(s_options));

    public static JsonSerializerOptions Options => new(s_options);

    // Trusted in-process callers share the immutable configuration instead of
    // allocating a defensive copy for every serialization operation.
    internal static JsonSerializerOptions SharedOptions => s_options;

    internal static bool IsCompilerDiagnosticCode(string? value)
    {
        return value != null &&
            value.StartsWith(
                CompilerDiagnosticCodePrefix,
                StringComparison.Ordinal) &&
            value.Length > CompilerDiagnosticCodePrefix.Length &&
            value.Skip(CompilerDiagnosticCodePrefix.Length).All(
                static character => character is
                    >= 'A' and <= 'Z' or
                    >= 'a' and <= 'z' or
                    >= '0' and <= '9' or '_');
    }

    internal static string ReadUtf8File(string path)
    {
        path = RepositoryParameterGuard.NotNull(path, nameof(path));
        using var reader = OpenJsonReader(path);
        return reader.ReadToEnd();
    }

    internal static string ComputeFileSha256(string path)
    {
        path = RepositoryParameterGuard.NotNull(path, nameof(path));
        using var bounded = OpenBoundedJsonFile(
            path,
            $"The JSON file exceeds the {MaximumJsonBytes} byte limit.");
        using var buffer = new MemoryStream();
        bounded.CopyTo(buffer);
        // The exact-length stream rejects another byte itself. Keep the final
        // probe after CopyTo so a change at the completed read boundary fails.
        _ = bounded.ReadByte();

        buffer.Position = 0;
        return ProtocolHashEncoding.ComputeSha256Hex(buffer);
    }

    internal static async Task<string> ReadUtf8FileAsync(
        string path, CancellationToken cancellationToken)
    {
        path = RepositoryParameterGuard.NotNull(path, nameof(path));
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = OpenJsonReader(path);
        var text = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (read == 0)
            {
                return text.ToString();
            }
            text.Append(buffer, 0, read);
        }
    }

    public static WorkerVerifyRequest? DeserializeRequest(string json)
    {
        json = RepositoryParameterGuard.NotNull(json, nameof(json));
        return Deserialize<WorkerVerifyRequest>(json);
    }

    public static WorkerVerifyResponse? DeserializeResponse(string json)
    {
        json = RepositoryParameterGuard.NotNull(json, nameof(json));
        return Deserialize<WorkerVerifyResponse>(json);
    }

    public static string SerializeRequest(WorkerVerifyRequest request)
    {
        return SerializeBounded(
            ArgumentNullGuard.NotNull(request, nameof(request)));
    }

    public static string ComputeRequestHash(WorkerVerifyRequest request)
    {
        return ComputeBoundedUtf8Hash(
            ArgumentNullGuard.NotNull(request, nameof(request)));
    }

    private static StreamReader OpenJsonReader(string path)
    {
        path = RepositoryParameterGuard.NotNull(path, nameof(path));
        return new StreamReader(
            OpenBoundedJsonFile(
                path,
                "The JSON file must be a nonempty regular file."),
            s_strictUtf8,
            detectEncodingFromByteOrderMarks: false);
    }

    private static BoundedReadStream OpenBoundedJsonFile(
        string path,
        string emptyFileMessage)
    {
        // Inspect the directory entry before FileStream opens it. On Unix,
        // opening a FIFO for reading waits for a writer, so a zero-length
        // non-file must fail before the potentially blocking open.
        emptyFileMessage = RepositoryParameterGuard.NotNull(emptyFileMessage, nameof(emptyFileMessage));
        path = RepositoryParameterGuard.NotNull(path, nameof(path));
        var fileLength = new FileInfo(path).Length;
        if (fileLength <= 0)
        {
            throw new InvalidDataException(
                emptyFileMessage);
        }
        if (fileLength > MaximumJsonBytes)
        {
            throw new InvalidDataException(
                $"The JSON file exceeds the {MaximumJsonBytes} byte limit.");
        }

        FileStream? stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            options: FileOptions.SequentialScan);
        try
        {
            var bounded = BoundedReadStream.ForExactLength(
                stream,
                fileLength,
                "The JSON file changed while it was opened.",
                "The JSON file changed while it was read.");
            stream = null;
            return bounded;
        }
        finally
        {
            stream?.Dispose();
        }
    }

    public static string SerializeResponse(WorkerVerifyResponse response)
    {
        ArgumentNullGuard.NotNull(response, nameof(response));
        return SerializeCanonicalResponse(CanonicalizeCopy(response));
    }

    internal static WorkerVerifyResponse CanonicalizeCopy(WorkerVerifyResponse response)
    {
        response = RepositoryParameterGuard.NotNull(response, nameof(response));
        var manifest = response.Manifest;
        response = CopyCanonicalizationOwners(response);
        Canonicalize(response);
        if (manifest is not null && response.Manifest is not null && SameManifestCollections(manifest, response.Manifest))
        { response = response with { Manifest = manifest }; }
        return response;
    }
    // Retain the admitted manifest's identity when it is already canonical.
    // The other copied result owners still isolate sorting from their producers.
    private static bool SameManifestCollections(WorkerClaimManifest original, WorkerClaimManifest canonical)
    {
        original = RepositoryParameterGuard.NotNull(original, nameof(original));
        canonical = RepositoryParameterGuard.NotNull(canonical, nameof(canonical));
        if (original.Callables.IsDefault || canonical.Callables.IsDefault ||
            !SameArrayOrder(original.Claims, canonical.Claims) ||
            original.Callables.Length != canonical.Callables.Length)
        { return false; }
        for (var index = 0; index < original.Callables.Length; index++)
        {
            var source = original.Callables[index];
            var sorted = canonical.Callables[index];
            if (source is null || sorted is null || source.CallableId != sorted.CallableId ||
                !ReferenceEquals(source.Location, sorted.Location) ||
                !SameArrayOrder(source.SelectedFeatures, sorted.SelectedFeatures) ||
                !SameArrayOrder(source.SelectionReasons, sorted.SelectionReasons) ||
                !SameArrayOrder(source.ClaimIds, sorted.ClaimIds) ||
                !SameArrayOrder(source.Assumptions, sorted.Assumptions))
            { return false; }
        }
        return true;
    }

    private static bool SameArrayOrder<T>(ImmutableArray<T> original /* SPINT001: all values valid - default preserves wire null for validation */, ImmutableArray<T> canonical /* SPINT001: all values valid - default preserves wire null for validation */)
    {
        return !original.IsDefault && !canonical.IsDefault && original.SequenceEqual(canonical);
    }
    // Public Canonicalize remains an in-place API. Serialization copies each
    // owner whose collections canonicalization replaces before using that API.
    private static WorkerVerifyResponse CopyCanonicalizationOwners(WorkerVerifyResponse response)
    {
        response = RepositoryParameterGuard.NotNull(response, nameof(response));
        return response with
        {
            Manifest = response.Manifest is null ? null! : response.Manifest with
            {
                Callables = CopyArrayOwners(response.Manifest.Callables,
                    static (WorkerCallableManifestEntry /* SPINT001: all values valid - malformed null entries retain their wire shape */ entry) =>
                        entry is null ? null! : entry with { })
            },
            CallableResults = CopyArrayOwners(response.CallableResults,
                static (WorkerCallableResult /* SPINT001: all values valid - malformed null entries retain their wire shape */ result) =>
                    result is null ? null! : result with { }),
            ClaimResults = CopyArrayOwners(response.ClaimResults,
                static (WorkerClaimResult /* SPINT001: all values valid - malformed null entries retain their wire shape */ result) =>
                    result is null ? null! : result with
                    {
                        EffectWitness = result.EffectWitness is null ? null : result.EffectWitness with { }
                    })
        };
    }
    internal static string SerializeCanonicalResponse(WorkerVerifyResponse response)
    {
        ArgumentNullGuard.NotNull(response, nameof(response));
        return SerializeBounded(response);
    }

    private static string SerializeBounded<T>(T value /* SPINT001: all values valid - generic values are forwarded or serialized, including null when the type permits it */)
    {
        var json = JsonSerializer.Serialize(value, TypeInfo<T>());
        if (Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
        {
            throw new InvalidDataException(
                $"The JSON document exceeds the {MaximumJsonBytes} byte limit.");
        }

        return json;
    }

    private static string ComputeBoundedUtf8Hash<T>(T value /* SPINT001: all values valid - generic values are forwarded or serialized, including null when the type permits it */)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value, TypeInfo<T>()).ToImmutableArray();
        if (json.Length > MaximumJsonBytes)
        {
            throw new InvalidDataException(
                $"The JSON document exceeds the {MaximumJsonBytes} byte limit.");
        }

        return ComputeSha256(json);
    }

    public static WorkerProtocolValidationResult Validate(WorkerVerifyRequest? request)
    {
        var errors = new Validator();
        if (request == null)
        {
            return errors.Fail("request.null");
        }

        errors.Rules(request, WorkerProtocolMetadata.RequestRules.Take(2));
        ValidateBudgets(request.Budgets, "budgets", errors);
        errors.Rules(request, WorkerProtocolMetadata.RequestRules.Skip(2));
        return errors.Result;
    }
    public static WorkerProtocolValidationResult Validate(WorkerVerifyResponse? response)
    {
        return ValidateResponse(response, null, null, null, null, null);
    }

    public static WorkerProtocolValidationResult Validate(
        WorkerVerifyResponse? response, string expectedInputHash, WorkerClaimManifest? expectedManifest = null)
    {
        RequireSha256(expectedInputHash, nameof(expectedInputHash), "input");
        return ValidateResponse(response, expectedInputHash, expectedManifest, null, null, null);
    }

    internal static WorkerProtocolValidationResult ValidateKnownInputHash(
        WorkerVerifyResponse? response,
        string expectedInputHash,
        WorkerClaimManifest expectedManifest)
    {
        return ValidateResponse(
            response,
            expectedInputHash,
            expectedManifest,
            null,
            null,
            null);
    }

    public static WorkerProtocolValidationResult ValidateForRequest(
        WorkerVerifyResponse? response, string expectedRequestHash, string expectedInputHash,
        WorkerClaimManifest expectedManifest, WorkerVerifyRequest expectedRequest,
        WorkerVersionSummary expectedVersions)
    {
        return ValidateForRequestCore(
            response,
            expectedRequestHash,
            expectedInputHash,
            expectedManifest,
            expectedRequest,
            expectedVersions);
    }

    private static WorkerProtocolValidationResult ValidateForRequestCore(
        WorkerVerifyResponse? response, string expectedRequestHash, string expectedInputHash,
        WorkerClaimManifest expectedManifest, WorkerVerifyRequest expectedRequest,
        WorkerVersionSummary expectedVersions)
    {
        RequireSha256(expectedRequestHash, nameof(expectedRequestHash), "request");
        RequireSha256(expectedInputHash, nameof(expectedInputHash), "input");
        _ = ArgumentNullGuard.NotNull(expectedManifest, nameof(expectedManifest));
        _ = ArgumentNullGuard.NotNull(expectedRequest, nameof(expectedRequest));
        _ = ArgumentNullGuard.NotNull(expectedVersions, nameof(expectedVersions));
        if (!Validate(expectedRequest).IsValid ||
            ComputeRequestHash(expectedRequest) != expectedRequestHash)
        {
            throw new ArgumentException(
                "Expected request authority is invalid or does not match its hash.",
                nameof(expectedRequest));
        }
        if (!WorkerProtocolMetadata.IsVersionsValid(expectedVersions))
        {
            throw new ArgumentException(
                "Expected runtime provenance is invalid.",
                nameof(expectedVersions));
        }
        return ValidateResponse(
            response, expectedInputHash, expectedManifest,
            expectedRequestHash, expectedRequest, expectedVersions);
    }

    public static void Canonicalize(WorkerVerifyResponse response)
    {
        _ = ArgumentNullGuard.NotNull(response, nameof(response));
        var claimsById = response.Manifest == null
            ? CreateClaimIndex(null)
            : CanonicalizeManifest(response.Manifest);

        response.CallableResults = SortOrdinal(response.CallableResults, static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value?.CallableId));
        foreach (var result in response.CallableResults.OfType<WorkerCallableResult>())
        {
            result.Assumptions = CanonicalizeAssumptions(result.Assumptions);
        }

        response.ClaimResults = [.. (EmptyIfDefault(response.ClaimResults))
            .OrderBy(value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (FindClaimCallableId(claimsById, value?.ClaimId)), s_ordinal)
            .ThenBy(value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (FindClaimOrdinal(claimsById, value?.ClaimId)))
            .ThenBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value?.ClaimId), s_ordinal)];
        foreach (var result in response.ClaimResults.OfType<WorkerClaimResult>())
        {
            Canonicalize(result);
        }

        response.Errors = [.. (EmptyIfDefault(response.Errors))
            .OrderBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value?.Code), s_ordinal)
            .ThenBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value?.Message), s_ordinal)];
    }
    private static void Canonicalize(WorkerClaimResult result)
    {
        result = RepositoryParameterGuard.NotNull(result, nameof(result));
        result.ProofCore = SortOrdinal(result.ProofCore, static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value));
        result.Model = [.. (EmptyIfDefault(result.Model))
            .OrderBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value?.Variable), s_ordinal)
            .ThenBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value?.Kind), s_ordinal)
            .ThenBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value?.Value), s_ordinal)];
        result.Assumptions = CanonicalizeAssumptions(result.Assumptions);
        if (result.EffectWitness != null)
        {
            result.EffectWitness.ExactExceptionTypeHierarchy = SortOrdinal(
                result.EffectWitness.ExactExceptionTypeHierarchy, static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value));
        }
    }

    private static WorkerProtocolValidationResult ValidateResponse(
        WorkerVerifyResponse? response, string? expectedInputHash, WorkerClaimManifest? expectedManifest,
        string? expectedRequestHash, WorkerVerifyRequest? expectedRequest,
        WorkerVersionSummary? expectedVersions)
    {
        var errors = new Validator();
        if (response == null)
        {
            return errors.Fail("response.null");
        }

        var requestHashValid = IsSha256(response.RequestHash);
        errors.Check(response.ProtocolVersion == WorkerProtocolVersions.Current, "response.protocol")
            .Check(requestHashValid, "response.request_hash");
        if (requestHashValid && expectedRequestHash != null)
        {
            errors.Check(response.RequestHash == expectedRequestHash, "response.request_mismatch");
        }

        var inputHashValid = IsSha256(response.InputHash);
        errors.Check(inputHashValid, "response.input_hash");
        if (inputHashValid && expectedInputHash != null)
        {
            errors.Check(response.InputHash == expectedInputHash, "response.input_mismatch");
        }

        var manifestErrorCount = errors.Count;
        ValidateManifestCore(
            response.Manifest,
            "manifest",
            errors,
            out var allManifestClaimsPostconditions,
            out var manifestCallables,
            out var manifestClaims);
        ValidateExpectedManifest(
            response.Manifest,
            expectedManifest,
            errors.Count == manifestErrorCount,
            errors);
        var protocolErrors = ValidateProtocolErrors(response.Errors, errors);
        var manifestIndexes = new ManifestIdentityIndexes(
            manifestCallables,
            manifestClaims);
        var callables = ValidateCallableResults(
            response.CallableResults,
            manifestIndexes,
            errors,
            out var allCallableResultsComplete);
        var claims = ValidateClaimResults(
            response.ClaimResults,
            manifestIndexes,
            errors,
            out var allClaimResultsDecided);
        ValidateRun(
            response,
            callables,
            claims,
            protocolErrors,
            manifestIndexes,
            errors);
        ValidateUnknownCoverage(callables, claims, manifestIndexes, errors);
        ValidateSummary(response.Summary, callables, claims, errors);
        if (response.Summary != null)
        {
            errors.Check(
                response.Summary.ElapsedMilliseconds <=
                    WorkerExecutionEnvelope.MaximumProducerElapsedMilliseconds,
                "response.elapsed_unrepresentable");
        }
        if (expectedVersions != null)
        {
            errors.Check(
                response.Summary?.Versions != null &&
                VersionsEqual(response.Summary.Versions, expectedVersions),
                "response.versions_mismatch");
        }
        if (expectedRequest != null)
        {
            errors.Check(response.Summary?.Budgets != null &&
                BudgetsEqual(response.Summary.Budgets, expectedRequest.Budgets),
                "response.budgets_mismatch");
            ValidateCacheForRequest(
                response,
                expectedRequest,
                errors);
        }

        return errors.Result;
    }

    private static void ValidateCacheForRequest(
        WorkerVerifyResponse response,
        WorkerVerifyRequest request,
        Validator errors)
    {
        errors = RepositoryParameterGuard.NotNull(errors, nameof(errors));
        if (response.Summary == null)
        {
            return;
        }
        var status = response.Summary.CacheStatus;
        if (!request.Cache.Enabled)
        {
            errors.Check(status == WorkerCacheStatus.Disabled,
                "response.cache_request_mismatch");
            return;
        }

        var storableShape = !response.Errors.IsDefault && response is
        {
            RunStatus: WorkerRunStatus.Complete,
            FailureReason: WorkerRunFailureReason.None,
            Errors.Length: 0,
            CallableResults.IsDefault: false,
            ClaimResults.IsDefault: false
        };
        var valid = status switch
        {
            WorkerCacheStatus.Hit or WorkerCacheStatus.Written => storableShape,
            WorkerCacheStatus.Rejected =>
                response.RunStatus == WorkerRunStatus.Failed &&
                response.FailureReason == WorkerRunFailureReason.MalformedResult,
            WorkerCacheStatus.Miss or WorkerCacheStatus.Unavailable or
                WorkerCacheStatus.Disabled => true,
            _ => false
        };
        errors.Check(valid, "response.cache_request_mismatch");
    }
    private static bool VersionsEqual(
        WorkerVersionSummary actual,
        WorkerVersionSummary expected)
    {
        actual = RepositoryParameterGuard.NotNull(actual, nameof(actual));
        expected = RepositoryParameterGuard.NotNull(expected, nameof(expected));
        return actual.ProtocolVersion == expected.ProtocolVersion &&
            actual.ManifestSchemaVersion == expected.ManifestSchemaVersion &&
            actual.CacheSchemaVersion == expected.CacheSchemaVersion &&
            actual.WorkerVersion == expected.WorkerVersion &&
            actual.ApiSpecVersion == expected.ApiSpecVersion &&
            actual.WorkerBinarySha256 == expected.WorkerBinarySha256 &&
            actual.ApiSpecContentSha256 == expected.ApiSpecContentSha256;
    }
    private static bool BudgetsEqual(
        WorkerBudgets actual,
        WorkerBudgets expected)
    {
        actual = RepositoryParameterGuard.NotNull(actual, nameof(actual));
        expected = RepositoryParameterGuard.NotNull(expected, nameof(expected));
        return actual.QueryRlimit == expected.QueryRlimit &&
            actual.MethodRlimit == expected.MethodRlimit &&
            actual.MethodWallTimeMilliseconds == expected.MethodWallTimeMilliseconds &&
            actual.ProjectWallTimeMilliseconds == expected.ProjectWallTimeMilliseconds &&
            actual.MaxParallelism == expected.MaxParallelism &&
            actual.MaximumExpressionDepth == expected.MaximumExpressionDepth;
    }
    private static void ValidateExpectedManifest(
        WorkerClaimManifest? actual,
        WorkerClaimManifest? expected,
        bool actualIsValid,
        Validator errors)
    {
        errors = RepositoryParameterGuard.NotNull(errors, nameof(errors));
        if (expected == null)
        {
            return;
        }

        if (ReferenceEquals(actual, expected))
        {
            if (!actualIsValid)
            {
                errors.Add("response.expected_manifest");
            }

            return;
        }

        var expectedErrors = new Validator();
        ValidateManifestCore(
            expected,
            "expected_manifest",
            expectedErrors,
            out _,
            out _,
            out _);
        if (expectedErrors.Count != 0)
        {
            errors.Add("response.expected_manifest");
        }
        else if (actualIsValid && actual != null)
        {
            errors.Check(
            ManifestsEqual(actual, expected), "response.manifest_mismatch");
        }
    }
    private static void ValidateManifestCore(
        WorkerClaimManifest? manifest,
        string prefix,
        Validator errors,
        out bool allClaimsPostconditions,
        out ImmutableArray<WorkerCallableManifestEntry> callables,
        out ImmutableArray<WorkerClaimManifestEntry> claims)
    {
        allClaimsPostconditions = false;
        callables = [];
        claims = [];
        if (manifest == null)
        {
            errors.Add(prefix + ".null");
            return;
        }
        var initialErrors = errors.Count;
        errors.Check(manifest.SchemaVersion == WorkerManifestVersions.Current, prefix + ".schema");
        callables = Present(manifest.Callables, prefix + ".callables", errors);
        claims = Present(manifest.Claims, prefix + ".claims", errors);
        allClaimsPostconditions = !manifest.Claims.IsDefaultOrEmpty &&
            claims.Length == manifest.Claims.Length;
        var callableIdValues = ValidateUniqueIds(
            callables.Select(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.CallableId)),
            prefix + ".callable_id", errors);
        _ = ValidateUniqueIds(
            claims.Select(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.ClaimId)),
            prefix + ".claim_id", errors);
        var callableIds = callableIdValues
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToImmutableHashSet(s_ordinal);
        var claimsByCallable = claims.ToLookup(
            static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : ((string?)value.CallableId),
            s_ordinal);
        foreach (var callable in callables)
        {
            errors.Check(HasValidLocation(callable.Location), prefix + ".callable_location")
                .Rules(callable, WorkerProtocolMetadata.ManifestCallableRules, prefix + ".")
                .Check(HasProducerAssumptionKinds(callable.Assumptions),
                    prefix + ".assumption_kind");
            ValidateClaimMembership(
                callable,
                claimsByCallable[callable.CallableId],
                prefix,
                errors);
        }
        ValidateManifestAssumptionIdentity(callables, prefix, errors);
        foreach (var claim in claims)
        {
            allClaimsPostconditions &=
                claim.Kind == WorkerClaimKind.Postcondition;
            errors.Check(callableIds.Contains(claim.CallableId), prefix + ".claim_callable")
                .Rules(claim, WorkerProtocolMetadata.ManifestClaimRules, prefix + ".")
                .Check(HasValidLocation(claim.Location), prefix + ".claim_location");
        }
        if (errors.Count == initialErrors)
        {
            try
            {
                errors.Check(manifest.Hash == ComputeManifestHash(manifest), prefix + ".hash");
            }
            catch (InvalidDataException)
            {
                // Escaping can make the canonical hash payload exceed the byte
                // limit even when the received JSON document fits within it.
                errors.Add(prefix + ".hash_payload_size");
            }
        }
    }
    private static void ValidateClaimMembership(
        WorkerCallableManifestEntry callable,
        IEnumerable<WorkerClaimManifestEntry> claims,
        string prefix,
        Validator errors)
    {
        callable = RepositoryParameterGuard.NotNull(callable, nameof(callable));
        claims = RepositoryParameterGuard.NotNull(claims, nameof(claims));
        errors = RepositoryParameterGuard.NotNull(errors, nameof(errors));
        var expected = claims
            .OrderBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.Ordinal))
            .ThenBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.ClaimId), s_ordinal).ToImmutableArray();
        var denseOrdinals = true;
        var claimMembership = !callable.ClaimIds.IsDefault &&
            callable.ClaimIds.Length == expected.Length;
        var claimOrder = true;
        var effectSeen = false;
        for (var index = 0; index < expected.Length; index++)
        {
            var claim = expected[index];
            denseOrdinals &= claim.Ordinal == index;
            if (claimMembership &&
                !s_ordinal.Equals(callable.ClaimIds![index], claim.ClaimId))
            {
                claimMembership = false;
            }
            if (claim.Kind == WorkerClaimKind.Effect)
            {
                effectSeen = true;
            }
            else if (effectSeen && claim.Kind == WorkerClaimKind.Postcondition)
            {
                claimOrder = false;
            }
        }

        errors.Check(
                denseOrdinals,
                prefix + ".dense_ordinals")
            .Check(claimMembership, prefix + ".claim_membership")
            .Check(claimOrder, prefix + ".claim_order");
    }
    private static ImmutableArray<WorkerCallableResult> ValidateCallableResults(
        ImmutableArray<WorkerCallableResult> values /* SPINT001: all values valid - default preserves wire null for validation */,
        ManifestIdentityIndexes manifestIndexes,
        Validator errors,
        out bool allResultsComplete)
    {
        var valid = ValidateResultSet(values,
            manifestIndexes.Callables.Select(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.CallableId)),
            static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.CallableId), "response.callable_results",
            "response.callable_id", "response.callable_set", errors);
        allResultsComplete = !values.IsDefaultOrEmpty &&
            valid.Length == values.Length;
        foreach (var value in valid)
        {
            allResultsComplete &= value.Coverage == WorkerCallableCoverage.Complete &&
                value.Reason == WorkerCallableCoverageReason.None;
            errors.Rules(value, WorkerProtocolMetadata.CallableResultRules);
            var declared = manifestIndexes.CallablesById.Find(value.CallableId);
            errors.Check(declared != null &&
                SameAssumptionDeclarations(
                    value.Assumptions,
                    manifestIndexes.GetCallableAssumptions(declared.CallableId)),
                "response.callable_assumption_set");
        }
        return valid;
    }
    private static ImmutableArray<WorkerClaimResult> ValidateClaimResults(
        ImmutableArray<WorkerClaimResult> values /* SPINT001: all values valid - default preserves wire null for validation */,
        ManifestIdentityIndexes manifestIndexes,
        Validator errors,
        out bool allResultsDecided)
    {
        var valid = ValidateResultSet(values,
            manifestIndexes.Claims.Select(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.ClaimId)),
            static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.ClaimId), "response.claim_results",
            "response.result_claim_id", "response.claim_set", errors);
        allResultsDecided = !values.IsDefaultOrEmpty &&
            valid.Length == values.Length;
        foreach (var value in valid)
        {
            allResultsDecided &= value.Outcome is
                WorkerClaimOutcome.Proven or WorkerClaimOutcome.Refuted;
            ValidateClaimResult(value, manifestIndexes, errors);
        }

        return valid;
    }
    private static void ValidateClaimResult(
        WorkerClaimResult value,
        ManifestIdentityIndexes manifestIndexes,
        Validator errors)
    {
        errors = RepositoryParameterGuard.NotNull(errors, nameof(errors));
        errors.Rules(value, WorkerProtocolMetadata.ClaimResultRules);
        errors.Check(IsPresentationValid(value.Presentation), "response.claim_presentation");
        errors.Check(IsDiagnosticContextValid(value.Diagnostic) &&
            (value.Diagnostic is null || value.Outcome == WorkerClaimOutcome.Unknown), "response.claim_diagnostic");
        var claim = manifestIndexes.ClaimsById.Find(value.ClaimId);
        var effectClaim = claim?.Kind == WorkerClaimKind.Effect;
        var hasTrustedBoundary = false;
        var hasUsedTrustedBoundary = false;
        if (effectClaim)
        {
            foreach (var assumption in EmptyIfDefault(value.Assumptions))
            {
                if (assumption?.Kind != WorkerAssumptionKind.TrustedBoundary)
                {
                    continue;
                }

                hasTrustedBoundary = true;
                hasUsedTrustedBoundary |= assumption.Used;
            }
        }
        errors.Check(claim != null &&
                WorkerProtocolMetadata.MatchesClaimKindOutcome(
                    claim.Kind, value.Outcome, value.Reason),
            "response.claim_reason");
        errors.Check(effectClaim
                ? HasValidEffectCertainty(value.Outcome, value.Reason, value.EffectCertainty)
                : value.EffectCertainty == WorkerEffectEvidenceCertainty.Unspecified,
            "response.effect_certainty")
            .Check(!effectClaim || WorkerProtocolMetadata.MatchesEffectEvidenceTuple(
                value.Outcome,
                value.Reason,
                value.EffectCertainty,
                value.Vacuity,
                !value.ProofCore.IsDefaultOrEmpty,
                hasTrustedBoundary,
                hasUsedTrustedBoundary),
                "response.effect_evidence")
            .Check(effectClaim && value.Outcome == WorkerClaimOutcome.Refuted
                ? HasValidEffectWitness(claim!.EffectContractKind, value.EffectWitness)
                : value.EffectWitness == null, "response.effect_witness");
        if (value.EffectWitness != null)
        {
            errors.Check(HasValidLocation(value.EffectWitness.Location), "response.effect_witness_location");
        }

        errors.Check(WorkerProtocolMetadata.MatchesVacuity(
            claim?.Kind ?? WorkerClaimKind.Unspecified, value.Outcome, value.Vacuity),
            "response.vacuity");
        var owner = manifestIndexes.CallablesById.Find(claim?.CallableId);
        errors.Check(owner != null && SameAssumptionDeclarations(
            value.Assumptions,
            manifestIndexes.GetCallableAssumptions(owner.CallableId)),
            "response.claim_assumption_set");
    }
    internal static bool HasValidEffectCertainty(WorkerClaimOutcome outcome, WorkerClaimReason reason,
        WorkerEffectEvidenceCertainty certainty)
    {
        return WorkerProtocolMetadata.MatchesEffectCertainty(outcome, reason, certainty);
    }

    private static bool HasValidEffectWitness(WorkerEffectContractKind kind, WorkerEffectViolationWitness? witness)
    {
        if (witness == null || !WorkerProtocolMetadata.IsEffectWitnessValid(witness))
        {
            return false;
        }

        const WorkerEffectSet impureState = WorkerEffectSet.ReadsCapturedState | WorkerEffectSet.ReadsStaticState |
            WorkerEffectSet.ReadsAmbientState | WorkerEffectSet.WritesReceiverState | WorkerEffectSet.WritesArgumentState |
            WorkerEffectSet.WritesCapturedState | WorkerEffectSet.WritesStaticState | WorkerEffectSet.WritesAmbientState;
        return kind switch
        {
            WorkerEffectContractKind.EnforcePure => witness.Capabilities != WorkerEffectCapabilitySet.None ||
                (witness.Effects & impureState) != 0,
            WorkerEffectContractKind.ZeroAllocations => (witness.Effects & WorkerEffectSet.Allocates) != 0,
            WorkerEffectContractKind.AllowedCapabilities => witness.Capabilities != WorkerEffectCapabilitySet.None,
            WorkerEffectContractKind.DoesNotThrow or WorkerEffectContractKind.AllowedExceptions =>
                (witness.Effects & WorkerEffectSet.Throws) != 0,
            // Configured effect and capability allowances are not in the manifest.
            WorkerEffectContractKind.EffectContract => true,
            _ => false
        };
    }

    private static void ValidateUnknownCoverage(ImmutableArray<WorkerCallableResult> callables,
        ImmutableArray<WorkerClaimResult> claims, ManifestIdentityIndexes manifestIndexes,
        Validator errors)
    {
        RepositoryParameterGuard.RequireInitialized(callables.IsDefault, nameof(callables), "Array must be initialized.");
        errors = RepositoryParameterGuard.NotNull(errors, nameof(errors));
        var incomplete = callables.Where(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.Coverage == WorkerCallableCoverage.Incomplete))
            .Select(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.CallableId))
            .ToImmutableHashSet(s_ordinal);
        errors.Check(!claims.Any(value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.Outcome == WorkerClaimOutcome.Unknown &&
            !string.IsNullOrWhiteSpace(value.ClaimId) &&
            manifestIndexes.ClaimsById.Find(value.ClaimId) is { } claim &&
            !incomplete.Contains(claim.CallableId))),
            "response.unknown_coverage");
    }
    private static void ValidateRun(
        WorkerVerifyResponse response,
        ImmutableArray<WorkerCallableResult> callables,
        ImmutableArray<WorkerClaimResult> claims,
        ImmutableArray<WorkerProtocolError> protocolErrors,
        ManifestIdentityIndexes manifestIndexes,
        Validator errors)
    {
        RepositoryParameterGuard.RequireInitialized(callables.IsDefault, nameof(callables), "Array must be initialized.");
        RepositoryParameterGuard.RequireInitialized(claims.IsDefault, nameof(claims), "Array must be initialized.");
        errors = RepositoryParameterGuard.NotNull(errors, nameof(errors));
        manifestIndexes = RepositoryParameterGuard.NotNull(manifestIndexes, nameof(manifestIndexes));
        errors.Defined(response.RunStatus, WorkerRunStatus.Unspecified, "response.run_status")
            .Check(WorkerProtocolMetadata.MatchesRunFailure(
                response.RunStatus, response.FailureReason),
                "response.run_failure");
        var projected = WorkerResultAssembler.TryProjectRunState(
            callables,
            claims,
            protocolErrors,
            out var expectedStatus,
            out var expectedFailure);
        errors.Check(projected &&
                response.RunStatus == expectedStatus &&
                response.FailureReason == expectedFailure,
            "response.run_projection");
        if (response.Manifest != null && projected)
        {
            var claimsById = claims.ToLookup(
                static claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : ((string?)claim.ClaimId),
                s_ordinal);
            foreach (var callable in callables)
            {
                var declared = manifestIndexes.CallablesById.Find(callable.CallableId);
                var owned = GetOwnedClaimResults(declared, claimsById);
                errors.Check(WorkerResultAssembler.MatchesCallableProjection(
                        callable,
                        owned,
                        expectedStatus,
                        expectedFailure,
                        protocolErrors.Length != 0),
                    "response.callable_projection");
            }
        }
    }

    private static ImmutableArray<WorkerClaimResult> GetOwnedClaimResults(
        WorkerCallableManifestEntry? callable,
        ILookup<string?, WorkerClaimResult> claimsById)
    {
        claimsById = RepositoryParameterGuard.NotNull(claimsById, nameof(claimsById));
        if (callable is null || callable.ClaimIds.IsDefault)
        {
            return [];
        }

        var ownedIds = ImmutableHashSet.CreateBuilder<string?>(s_ordinal);
        var owned = ImmutableArray.CreateBuilder<WorkerClaimResult>();
        foreach (var claimId in callable.ClaimIds)
        {
            if (ownedIds.Add(claimId))
            {
                owned.AddRange(claimsById[claimId]);
            }
        }
        return owned.ToImmutable();
    }

    private static void ValidateSummary(WorkerVerificationSummary? summary,
        ImmutableArray<WorkerCallableResult> callables, ImmutableArray<WorkerClaimResult> claims, Validator errors)
    {
        if (summary == null)
        {
            errors.Add("response.summary");
            return;
        }
        errors.Check(!WorkerResultAssembler.HasConflictingAssumptionKinds(callables, claims),
                "summary.assumption_conflict")
            .Rules(summary, WorkerProtocolMetadata.SummaryRules);
        ValidateBudgets(summary.Budgets, "summary.budgets", errors);
    }
    private static ImmutableArray<WorkerProtocolError> ValidateProtocolErrors(ImmutableArray<WorkerProtocolError> values /* SPINT001: all values valid - default preserves wire null for validation */, Validator errors)
    {
        errors = RepositoryParameterGuard.NotNull(errors, nameof(errors));
        if (!values.IsDefault && values.All(static value => value != null &&
            WorkerProtocolMetadata.IsProtocolErrorValid(value)))
        {
            return values;
        }

        errors.Add("response.errors");
        return [];
    }
    private static void ValidateBudgets(WorkerBudgets? value, string prefix, Validator errors)
    {
        errors = RepositoryParameterGuard.NotNull(errors, nameof(errors));
        errors.Check(value != null, prefix + ".null");
        if (value != null)
        {
            errors.Rules(
            value, WorkerProtocolMetadata.BudgetsRules, prefix + ".");
        }
    }
    internal static bool HasValidLocation(WorkerSourceLocation? value)
    {
        return value != null && WorkerProtocolMetadata.IsSourceLocationValid(value);
    }

    internal static bool HasValidLocationOrNone(WorkerSourceLocation? value)
    {
        return value != null &&
            (WorkerProtocolMetadata.IsSourceLocationValid(value) ||
             IsNoneLocation(value));
    }

    internal static bool IsNoneLocation(WorkerSourceLocation? value)
    {
        return value is
        {
            Path.Length: 0,
            Start: 0,
            Length: 0,
            Line: 0,
            Column: 0
        };
    }

    internal static bool HasKnownEffects(WorkerEffectSet effects, WorkerEffectCapabilitySet capabilities)
    {
        return WorkerProtocolMetadata.HasOnlyKnownFlags(effects) &&
            WorkerProtocolMetadata.HasOnlyKnownFlags(capabilities);
    }

    internal static bool AreDistinctNonblank(ImmutableArray<string> values /* SPINT001: all values valid - default preserves wire null for validation */)
    {
        return CompleteUnique(values, static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (!string.IsNullOrWhiteSpace(value)), static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value));
    }

    internal static bool IsSingleLineText(string? value)
    {
        return value != null && value.All(static character =>
            !char.IsControl(character) &&
            character is not '\u2028' and not '\u2029');
    }

    internal static bool AreDefinedUnique<T>(ImmutableArray<T> values /* SPINT001: all values valid - default preserves wire null for validation */, T unspecified, bool nonEmpty)
            where T : struct, Enum
    {
        if (values.IsDefault || nonEmpty && values.Length == 0)
        {
            return false;
        }

        var seen = ImmutableHashSet.CreateBuilder<T>();
        foreach (var value in values)
        {
            if (!IsDefined(value, unspecified) || seen.Contains(value))
            {
                return false;
            }
            seen.Add(value);
        }

        return true;
    }

    internal static bool AreValidModel(ImmutableArray<WorkerModelValue> values /* SPINT001: all values valid - default preserves wire null for validation */)
    {
        return CompleteUnique(values, static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (!string.IsNullOrWhiteSpace(value.Variable) &&
                !string.IsNullOrWhiteSpace(value.Kind) && !string.IsNullOrWhiteSpace(value.Value)),
                static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.Variable));
    }

    internal static bool AreValidAssumptions(ImmutableArray<WorkerAssumptionEvidence> values /* SPINT001: all values valid - default preserves wire null for validation */)
    {
        return CompleteUnique(values,
                WorkerProtocolMetadata.IsAssumptionValid,
                static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.Id));
    }

    private static bool HasProducerAssumptionKinds(ImmutableArray<WorkerAssumptionEvidence> values /* SPINT001: all values valid - default preserves wire null for validation */)
    {
        return !values.IsDefault && values.All(static value =>
            value is null ? throw new System.ArgumentNullException(nameof(value)) : (value != null && WorkerProtocolMetadata.MatchesAssumptionKind(value.Kind)));
    }

    private static void ValidateManifestAssumptionIdentity(
        ImmutableArray<WorkerCallableManifestEntry> callables, string prefix, Validator errors)
    {
        RepositoryParameterGuard.RequireInitialized(callables.IsDefault, nameof(callables), "Array must be initialized.");
        errors = RepositoryParameterGuard.NotNull(errors, nameof(errors));
        var identities = ImmutableDictionary.CreateBuilder<string, (string? CallableId, WorkerAssumptionKind Kind)>(s_ordinal);
        var valid = true;
        foreach (var callable in callables)
        {
            if (callable == null)
            {
                continue;
            }
            foreach (var assumption in EmptyIfDefault(callable.Assumptions))
            {
                if (assumption == null ||
                    string.IsNullOrWhiteSpace(assumption.Id))
                {
                    continue;
                }
                var identity = (callable.CallableId, assumption.Kind);
                if (identities.TryGetValue(assumption.Id, out var existing))
                {
                    valid &= existing == identity;
                }
                else
                {
                    identities.Add(assumption.Id, identity);
                }
            }
        }
        errors.Check(valid, prefix + ".assumption_identity");
    }

    private static ImmutableArray<T> ValidateResultSet<T>(ImmutableArray<T> values /* SPINT001: all values valid - default preserves wire null for validation */, IEnumerable<string?> expectedIds,
        Func<T, string?> identity, string collectionCode, string identityCode, string setCode,
        Validator errors) where T : class
    {
        collectionCode = RepositoryParameterGuard.NotNull(collectionCode, nameof(collectionCode));
        var present = Present(values, collectionCode, errors);
        var identities = ValidateUniqueIds(present.Select(identity), identityCode, errors);
        ValidateExactIds(identities, expectedIds, setCode, errors);
        return present;
    }
    private static ImmutableArray<string?> ValidateUniqueIds(
        IEnumerable<string?> values, string code, Validator errors)
    {
        errors = RepositoryParameterGuard.NotNull(errors, nameof(errors));
        values = RepositoryParameterGuard.NotNull(values, nameof(values));
        var items = values.ToImmutableArray();
        var seen = ImmutableHashSet.CreateBuilder<string?>(s_ordinal);
        var valid = true;
        foreach (var value in items)
        {
            if (string.IsNullOrWhiteSpace(value) || seen.Contains(value))
            {
                valid = false;
            }
            else
            {
                seen.Add(value);
            }
        }
        errors.Check(valid, code);
        return items;
    }
    private static void ValidateExactIds(IEnumerable<string?> actual, IEnumerable<string?> expected, string code, Validator errors)
    {
        errors = RepositoryParameterGuard.NotNull(errors, nameof(errors));
        errors.Check(actual.OrderBy(static value => value, s_ordinal)
            .SequenceEqual(expected.OrderBy(static value => value, s_ordinal),
                s_ordinal), code);
    }

    private static bool CompleteUnique<T>(ImmutableArray<T> values /* SPINT001: all values valid - default preserves wire null for validation */, Func<T, bool> complete, Func<T, string?> key) where T : class
    {
        if (values.IsDefault)
        {
            return false;
        }

        var seen = ImmutableHashSet.CreateBuilder<string?>(s_ordinal);
        foreach (var value in values)
        {
            if (value == null || !complete(value))
            {
                return false;
            }
            var identity = key(value);
            if (seen.Contains(identity))
            { return false; }
            seen.Add(identity);
        }

        return true;
    }

    private static ImmutableArray<T> Present<T>(ImmutableArray<T> values /* SPINT001: all values valid - default preserves wire null for validation */, string code, Validator errors) where T : class
    {
        code = RepositoryParameterGuard.NotNull(code, nameof(code));
        errors = RepositoryParameterGuard.NotNull(errors, nameof(errors));
        if (!values.IsDefault && values.All(static value => value != null))
        {
            return values;
        }

        errors.Add(code);
        return [.. (EmptyIfDefault(values)).Where(static value => value != null)];
    }

    private static ImmutableArray<WorkerAssumptionEvidence> CanonicalizeAssumptions(ImmutableArray<WorkerAssumptionEvidence> values /* SPINT001: all values valid - default preserves wire null for validation */)
    {
        return [.. (EmptyIfDefault(values))
            .OrderBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (WorkerProtocolMetadata.GetAssumptionOrder(
                value?.Kind ?? WorkerAssumptionKind.Unspecified)))
            .ThenBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value?.Id), s_ordinal)];
    }

    private static ImmutableArray<T> SortManifestEnums<T>(ImmutableArray<T> values /* SPINT001: all values valid - default preserves wire null for validation */) where T : struct, Enum
    {
        return [.. (EmptyIfDefault(values)).OrderBy(ManifestName, s_ordinal)];
    }

    private static ImmutableArray<T> SortOrdinal<T>(ImmutableArray<T> values /* SPINT001: all values valid - default preserves wire null for validation */, Func<T, string?> identity)
    {
        identity = RepositoryParameterGuard.NotNull(identity, nameof(identity));
        return [.. (EmptyIfDefault(values)).OrderBy(identity, s_ordinal)];
    }

    internal static bool SameAssumptionDeclarations(
            ImmutableArray<WorkerAssumptionEvidence> actual /* SPINT001: all values valid - default preserves wire null for validation */,
            IEnumerable<(string Id, WorkerAssumptionKind Kind)> expected)
    {
        expected = RepositoryParameterGuard.NotNull(expected, nameof(expected));
        return NormalizeAssumptionDeclarations(actual).SequenceEqual(expected);
    }

    private static ImmutableArray<(string Id, WorkerAssumptionKind Kind)>
        NormalizeAssumptionDeclarations(ImmutableArray<WorkerAssumptionEvidence> values /* SPINT001: all values valid - default preserves wire null for validation */)
    {
        return [.. (EmptyIfDefault(values)).Where(static value => value != null)
            .OrderBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.Id), s_ordinal)
            .Select(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : ((value.Id, value.Kind)))];
    }

    private sealed class ManifestIdentityIndexes
    {
        internal ManifestIdentityIndexes(
            ImmutableArray<WorkerCallableManifestEntry> callables,
            ImmutableArray<WorkerClaimManifestEntry> claims)
        {
            RepositoryParameterGuard.RequireInitialized(callables.IsDefault, nameof(callables), "Array must be initialized.");
            RepositoryParameterGuard.RequireInitialized(claims.IsDefault, nameof(claims), "Array must be initialized.");
            Callables = callables;
            Claims = claims;
            CallablesById = new OrdinalIdentityIndex<
                WorkerCallableManifestEntry>(
                    Callables,
                    static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.CallableId));
            ClaimsById = new OrdinalIdentityIndex<WorkerClaimManifestEntry>(
                Claims,
                static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.ClaimId));
            CallableAssumptionsById = CreateCallableAssumptions(Callables);
        }

        internal ImmutableArray<WorkerCallableManifestEntry> Callables { get; }
        internal ImmutableArray<WorkerClaimManifestEntry> Claims { get; }
        internal OrdinalIdentityIndex<WorkerCallableManifestEntry> CallablesById { get; }
        internal OrdinalIdentityIndex<WorkerClaimManifestEntry> ClaimsById { get; }
        internal ImmutableDictionary<string, ImmutableArray<(string Id, WorkerAssumptionKind Kind)>> CallableAssumptionsById { get; }

        internal ImmutableArray<(string Id, WorkerAssumptionKind Kind)> GetCallableAssumptions(string? id)
        {
            return id != null && CallableAssumptionsById.TryGetValue(id, out var values)
                ? values
                : [];
        }

        private static ImmutableDictionary<string, ImmutableArray<(string Id, WorkerAssumptionKind Kind)>>
            CreateCallableAssumptions(ImmutableArray<WorkerCallableManifestEntry> callables)
        {
            RepositoryParameterGuard.RequireInitialized(callables.IsDefault, nameof(callables), "Array must be initialized.");
            var result = ImmutableDictionary.CreateBuilder<string, ImmutableArray<(string Id, WorkerAssumptionKind Kind)>>(s_ordinal);
            foreach (var callable in callables)
            {
                if (callable.CallableId != null && !result.ContainsKey(callable.CallableId))
                {
                    result.Add(
                        callable.CallableId,
                        NormalizeAssumptionDeclarations(callable.Assumptions));
                }
            }
            return result.ToImmutable();
        }
    }

    private sealed class OrdinalIdentityIndex<T>
        where T : class
    {
        private readonly ImmutableDictionary<string, T> _byId;
        private readonly bool _hasNull;
        private readonly T? _nullValue;

        internal OrdinalIdentityIndex(
            IEnumerable<T> values,
            Func<T, string?> identity)
        {
            identity = RepositoryParameterGuard.NotNull(identity, nameof(identity));
            values = RepositoryParameterGuard.NotNull(values, nameof(values));
            var byId = ImmutableDictionary.CreateBuilder<string, T>(s_ordinal);
            foreach (var value in values)
            {
                var id = identity(value);
                if (id == null)
                {
                    if (!_hasNull)
                    {
                        _nullValue = value;
                        _hasNull = true;
                    }
                }
                else if (!byId.ContainsKey(id))
                {
                    byId.Add(id, value);
                }
            }
            _byId = byId.ToImmutable();
        }

        internal T? Find(string? id)
        {
            if (id == null)
            {
                return _hasNull ? _nullValue : null;
            }
            return _byId.TryGetValue(id, out var value)
                ? value
                : null;
        }
    }

    private static string ManifestName<T>(T value) where T : struct, Enum
    {
        return WorkerProtocolMetadata.GetManifestName((Enum)(object)value) ??
            throw UnexpectedManifestEnum(value);
    }

    private static ArgumentOutOfRangeException UnexpectedManifestEnum<T>(T value /* SPINT001: all values valid - generic values are forwarded or serialized, including null when the type permits it */) where T : struct, Enum
    {
        return new(nameof(value), value, "The manifest contains an unknown enum value.");
    }

    private static T? Deserialize<T>(string json)
    {
        json = ArgumentNullGuard.NotNull(json, nameof(json));
        if (Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
        {
            throw new JsonException(
                $"The JSON document exceeds the {MaximumJsonBytes} byte limit.");
        }

        return JsonSerializer.Deserialize(json, TypeInfo<T>());
    }

    private static JsonTypeInfo<T> TypeInfo<T>()
    {
        return (s_context.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>) ??
            throw new InvalidOperationException("The protocol type has no JSON metadata.");
    }
    private static OrdinalIdentityIndex<WorkerClaimManifestEntry>
        CreateClaimIndex(WorkerClaimManifest? manifest)
    {
        return new(
            (EmptyIfDefault(manifest?.Claims ?? [])).OfType<WorkerClaimManifestEntry>(),
            static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.ClaimId));
    }

    private static int FindClaimOrdinal(
        OrdinalIdentityIndex<WorkerClaimManifestEntry> claimsById,
        string? id)
    {
        claimsById = RepositoryParameterGuard.NotNull(claimsById, nameof(claimsById));
        return claimsById.Find(id)?.Ordinal ?? int.MaxValue;
    }

    private static string FindClaimCallableId(
        OrdinalIdentityIndex<WorkerClaimManifestEntry> claimsById,
        string? id)
    {
        claimsById = RepositoryParameterGuard.NotNull(claimsById, nameof(claimsById));
        return claimsById.Find(id)?.CallableId ?? string.Empty;
    }

    internal static bool IsSha256(string? value)
    {
        return ProtocolHashEncoding.IsSha256(value);
    }

    internal static string ComputeSha256(ImmutableArray<byte> bytes)
    {
        RepositoryParameterGuard.ThrowIfMissing(bytes.IsDefault, nameof(bytes));
        return ProtocolHashEncoding.ComputeSha256Hex(bytes);
    }
    internal static bool IsDefined<T>(T value, T unspecified) where T : struct, Enum
    {
        return WorkerProtocolMetadata.IsKnown(value) &&
        !EqualityComparer<T>.Default.Equals(value, unspecified);
    }

    private static void RequireSha256(string value, string parameter, string kind)
    {
        kind = RepositoryParameterGuard.NotNull(kind, nameof(kind));
        parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter));
        if (!IsSha256(value))
        {
            throw new ArgumentException($"A lowercase SHA-256 {kind} hash is required.", parameter);
        }
    }
}
