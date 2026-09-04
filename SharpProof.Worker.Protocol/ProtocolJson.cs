using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace SharpProof.Worker.Protocol;

public static partial class WorkerProtocolJson
{
    internal const string CompilerDiagnosticCodePrefix = "compiler.";
    internal const int MaximumJsonBytes = 16 * 1024 * 1024;
    internal const int MaximumJsonDepth = 32;
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);
    private static readonly StringComparer s_ordinal = StringComparer.Ordinal;
    private static readonly JsonSerializerOptions s_options = CreateOptions();

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
                static character =>
                    (character >= 'A' && character <= 'Z') ||
                    (character >= 'a' && character <= 'z') ||
                    (character >= '0' && character <= '9') ||
                    character == '_');
    }

    internal static string ReadUtf8File(string path)
    {
        using var reader = OpenJsonReader(path);
        return reader.ReadToEnd().TrimStart('\uFEFF');
    }

    internal static string ComputeFileSha256(string path)
    {
        using var bounded = OpenBoundedJsonFile(
            path,
            $"The JSON file exceeds the {MaximumJsonBytes} byte limit.");
        using var buffer = new MemoryStream();
        bounded.CopyTo(buffer);
        if (bounded.ReadByte() != -1)
        {
            throw new InvalidDataException("The JSON file changed while it was read.");
        }

        return ComputeSha256(buffer.ToArray());
    }

    internal static async Task<string> ReadUtf8FileAsync(
        string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = OpenJsonReader(path);
        var text = await reader.ReadToEndAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return text.TrimStart('\uFEFF');
    }

    public static WorkerVerifyRequest? DeserializeRequest(string json)
    {
        return Deserialize<WorkerVerifyRequest>(json);
    }

    public static WorkerVerifyResponse? DeserializeResponse(string json)
    {
        return Deserialize<WorkerVerifyResponse>(json);
    }

    public static string SerializeRequest(WorkerVerifyRequest request)
    {
        return SerializeBounded(
            request ?? throw new ArgumentNullException(nameof(request)));
    }

    public static string ComputeRequestHash(WorkerVerifyRequest request)
    {
        return ComputeSha256(Encoding.UTF8.GetBytes(SerializeRequest(request)));
    }

    private static StreamReader OpenJsonReader(string path)
    {
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

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            options: FileOptions.SequentialScan);
        if (stream.Length != fileLength)
        {
            stream.Dispose();
            throw new InvalidDataException(
                "The JSON file changed while it was opened.");
        }

        return new BoundedReadStream(
            stream,
            MaximumJsonBytes,
            $"The JSON file exceeds the {MaximumJsonBytes} byte limit.");
    }

    public static string SerializeResponse(WorkerVerifyResponse response)
    {
        Canonicalize(response ?? throw new ArgumentNullException(nameof(response)));
        return SerializeBounded(response);
    }

    private static string SerializeBounded<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, s_options);
        if (Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
        {
            throw new InvalidDataException(
                $"The JSON document exceeds the {MaximumJsonBytes} byte limit.");
        }

        return json;
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

    internal static WorkerProtocolValidationResult Validate(
        WorkerVerifyResponse? response, string expectedInputHash,
        WorkerClaimManifest? expectedManifest,
        IWorkerResponseEvidenceAuthority evidenceAuthority,
        CancellationToken cancellationToken = default)
    {
        RequireSha256(expectedInputHash, nameof(expectedInputHash), "input");
        _ = evidenceAuthority ??
            throw new ArgumentNullException(nameof(evidenceAuthority));
        return ValidateResponse(
            response, expectedInputHash, expectedManifest, null, null, null,
            evidenceAuthority: evidenceAuthority,
            cancellationToken: cancellationToken);
    }
    public static WorkerProtocolValidationResult ValidateForRequest(
        WorkerVerifyResponse? response, string expectedRequestHash, string expectedInputHash,
        WorkerClaimManifest expectedManifest, WorkerVerifyRequest expectedRequest,
        WorkerVersionSummary expectedVersions,
        int terminationGraceMilliseconds = WorkerLauncherDefaults.TerminationGraceMilliseconds)
    {
        RequireSha256(expectedRequestHash, nameof(expectedRequestHash), "request");
        RequireSha256(expectedInputHash, nameof(expectedInputHash), "input");
        _ = expectedManifest ??
            throw new ArgumentNullException(nameof(expectedManifest));
        _ = expectedRequest ?? throw new ArgumentNullException(nameof(expectedRequest));
        _ = expectedVersions ?? throw new ArgumentNullException(nameof(expectedVersions));
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
        var maximumElapsedMilliseconds = WorkerExecutionEnvelope.MaximumElapsedMilliseconds(
            expectedRequest, terminationGraceMilliseconds);
        return ValidateResponse(response, expectedInputHash, expectedManifest,
            expectedRequestHash, expectedRequest, expectedVersions,
            maximumElapsedMilliseconds);
    }

    internal static WorkerProtocolValidationResult ValidateForRequest(
        WorkerVerifyResponse? response, string expectedRequestHash, string expectedInputHash,
        WorkerClaimManifest expectedManifest, WorkerVerifyRequest expectedRequest,
        WorkerVersionSummary expectedVersions,
        IWorkerResponseEvidenceAuthority evidenceAuthority,
        int terminationGraceMilliseconds = WorkerLauncherDefaults.TerminationGraceMilliseconds,
        CancellationToken cancellationToken = default)
    {
        RequireSha256(expectedRequestHash, nameof(expectedRequestHash), "request");
        RequireSha256(expectedInputHash, nameof(expectedInputHash), "input");
        _ = expectedManifest ??
            throw new ArgumentNullException(nameof(expectedManifest));
        _ = expectedRequest ??
            throw new ArgumentNullException(nameof(expectedRequest));
        _ = expectedVersions ??
            throw new ArgumentNullException(nameof(expectedVersions));
        _ = evidenceAuthority ??
            throw new ArgumentNullException(nameof(evidenceAuthority));
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
        var maximumElapsedMilliseconds = WorkerExecutionEnvelope.MaximumElapsedMilliseconds(
            expectedRequest, terminationGraceMilliseconds);
        return ValidateResponse(
            response, expectedInputHash, expectedManifest,
            expectedRequestHash, expectedRequest, expectedVersions,
            maximumElapsedMilliseconds, evidenceAuthority, cancellationToken);
    }

    public static void Canonicalize(WorkerVerifyResponse response)
    {
        _ = response ?? throw new ArgumentNullException(nameof(response));
        if (response.Manifest != null)
        {
            Canonicalize(response.Manifest);
        }

        response.CallableResults = SortOrdinal(response.CallableResults, static value => value?.CallableId);
        foreach (var result in response.CallableResults.OfType<WorkerCallableResult>())
        {
            result.Assumptions = CanonicalizeAssumptions(result.Assumptions);
        }

        var claimsById = CreateClaimIndex(response.Manifest);
        response.ClaimResults = [.. (response.ClaimResults ?? [])
            .OrderBy(value => FindClaimCallableId(claimsById, value?.ClaimId), s_ordinal)
            .ThenBy(value => FindClaimOrdinal(claimsById, value?.ClaimId))
            .ThenBy(static value => value?.ClaimId, s_ordinal)];
        foreach (var result in response.ClaimResults.OfType<WorkerClaimResult>())
        {
            Canonicalize(result);
        }

        if (response.Summary != null)
        {
            response.Summary.OutcomeCounts = SortOrdinal(response.Summary.OutcomeCounts,
                static value => value?.Outcome.ToString());
            response.Summary.ReasonCounts = SortOrdinal(response.Summary.ReasonCounts,
                static value => value?.Reason.ToString());
        }
        response.Errors = [.. (response.Errors ?? [])
            .OrderBy(static value => value?.Code, s_ordinal)
            .ThenBy(static value => value?.Message, s_ordinal)];
    }
    private static void Canonicalize(WorkerClaimResult result)
    {
        result.ProofCore = SortOrdinal(result.ProofCore, static value => value);
        result.Model = [.. (result.Model ?? [])
            .OrderBy(static value => value?.Variable, s_ordinal)
            .ThenBy(static value => value?.Kind, s_ordinal)
            .ThenBy(static value => value?.Value, s_ordinal)];
        result.Assumptions = CanonicalizeAssumptions(result.Assumptions);
        if (result.EffectWitness != null)
        {
            result.EffectWitness.ExactExceptionTypeHierarchy = SortOrdinal(
                result.EffectWitness.ExactExceptionTypeHierarchy, static value => value);
        }
    }

    private static WorkerProtocolValidationResult ValidateResponse(
        WorkerVerifyResponse? response, string? expectedInputHash, WorkerClaimManifest? expectedManifest,
        string? expectedRequestHash, WorkerVerifyRequest? expectedRequest,
        WorkerVersionSummary? expectedVersions,
        long? maximumElapsedMilliseconds = null,
        IWorkerResponseEvidenceAuthority? evidenceAuthority = null,
        CancellationToken cancellationToken = default)
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
            out var allManifestClaimsPostconditions);
        ValidateExpectedManifest(
            response.Manifest,
            expectedManifest,
            errors.Count == manifestErrorCount,
            errors);
        var protocolErrors = ValidateProtocolErrors(response.Errors, errors);
        var manifestIndexes = new ManifestIdentityIndexes(response.Manifest);
        var callables = ValidateCallableResults(
            response.CallableResults,
            manifestIndexes,
            errors,
            out var allCallableResultsComplete);
        var claims = ValidateClaimResults(
            response.ClaimResults,
            manifestIndexes,
            errors,
            out var allClaimResultsRefuted);
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
            if (maximumElapsedMilliseconds.HasValue)
            {
                errors.Check(
                    response.Summary.ElapsedMilliseconds <= maximumElapsedMilliseconds.Value,
                    "response.elapsed_request_envelope");
            }
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
                allCallableResultsComplete,
                allClaimResultsRefuted,
                allManifestClaimsPostconditions,
                errors);
        }

        if (evidenceAuthority != null)
        {
            try
            {
                foreach (var code in evidenceAuthority.Validate(response, cancellationToken)
                             .Where(static code => !string.IsNullOrWhiteSpace(code))
                             .Distinct(s_ordinal))
                {
                    errors.Add(code);
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidDataException or
                InvalidOperationException or KeyNotFoundException or
                NullReferenceException)
            {
                errors.Add("response.evidence_authority");
            }
        }

        return errors.Result;
    }

    private static void ValidateCacheForRequest(
        WorkerVerifyResponse response,
        WorkerVerifyRequest request,
        bool allCallableResultsComplete,
        bool allClaimResultsRefuted,
        bool allManifestClaimsPostconditions,
        Validator errors)
    {
        if (response.Summary == null)
        {
            return;
        }
        var status = response.Summary.CacheStatus;
        var inactive = !request.Cache.Enabled ||
            request.VerifyPolicy == WorkerVerifyPolicy.RequireProven;
        if (inactive)
        {
            errors.Check(status == WorkerCacheStatus.Disabled,
                "response.cache_request_mismatch");
            return;
        }

        var storableShape = response is
        {
            RunStatus: WorkerRunStatus.Complete,
            FailureReason: WorkerRunFailureReason.None,
            Errors.Length: 0,
            CallableResults: { Length: > 0 },
            ClaimResults: { Length: > 0 }
        } &&
            allCallableResultsComplete &&
            allClaimResultsRefuted &&
            allManifestClaimsPostconditions;
        var valid = status switch
        {
            WorkerCacheStatus.Hit or WorkerCacheStatus.Written => storableShape,
            WorkerCacheStatus.Rejected =>
                response.RunStatus == WorkerRunStatus.Failed &&
                response.FailureReason == WorkerRunFailureReason.MalformedResult,
            WorkerCacheStatus.Miss => !storableShape,
            WorkerCacheStatus.Unavailable => true,
            WorkerCacheStatus.Disabled =>
                response.RunStatus != WorkerRunStatus.Complete,
            _ => false
        };
        errors.Check(valid, "response.cache_request_mismatch");
    }
    private static bool VersionsEqual(
        WorkerVersionSummary actual,
        WorkerVersionSummary expected)
    {
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
        if (expected == null)
        {
            return;
        }

        var expectedErrors = new Validator();
        ValidateManifestCore(
            expected,
            "expected_manifest",
            expectedErrors,
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
        out bool allClaimsPostconditions)
    {
        allClaimsPostconditions = false;
        if (manifest == null)
        {
            errors.Add(prefix + ".null");
            return;
        }
        var initialErrors = errors.Count;
        errors.Check(manifest.SchemaVersion == WorkerManifestVersions.Current, prefix + ".schema");
        var callables = Present(manifest.Callables, prefix + ".callables", errors);
        var claims = Present(manifest.Claims, prefix + ".claims", errors);
        allClaimsPostconditions = manifest.Claims is { Length: > 0 } &&
            claims.Length == manifest.Claims.Length;
        var callableIdValues = ValidateUniqueIds(
            callables.Select(static value => value.CallableId),
            prefix + ".callable_id", errors);
        _ = ValidateUniqueIds(
            claims.Select(static value => value.ClaimId),
            prefix + ".claim_id", errors);
        var callableIds = new HashSet<string>(
            callableIdValues
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!),
            s_ordinal);
        var claimsByCallable = claims.ToLookup(
            static value => (string?)value.CallableId,
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
            errors.Check(manifest.Hash == ComputeManifestHash(manifest), prefix + ".hash");
        }
    }
    private static void ValidateClaimMembership(
        WorkerCallableManifestEntry callable,
        IEnumerable<WorkerClaimManifestEntry> claims,
        string prefix,
        Validator errors)
    {
        var expected = claims
            .OrderBy(static value => value.Ordinal)
            .ThenBy(static value => value.ClaimId, s_ordinal).ToArray();
        var denseOrdinals = true;
        var claimMembership = callable.ClaimIds is
        { Length: var claimIdCount } &&
                claimIdCount == expected.Length;
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
    private static WorkerCallableResult[] ValidateCallableResults(
        WorkerCallableResult[]? values,
        ManifestIdentityIndexes manifestIndexes,
        Validator errors,
        out bool allResultsComplete)
    {
        var valid = ValidateResultSet(values,
            manifestIndexes.Callables.Select(static value => value.CallableId),
            static value => value.CallableId, "response.callable_results",
            "response.callable_id", "response.callable_set", errors);
        allResultsComplete = values is { Length: > 0 } &&
            valid.Length == values.Length;
        foreach (var value in valid)
        {
            allResultsComplete &= value.Coverage == WorkerCallableCoverage.Complete &&
                value.Reason == WorkerCallableCoverageReason.None;
            errors.Rules(value, WorkerProtocolMetadata.CallableResultRules);
            var declared = manifestIndexes.CallablesById.Find(value.CallableId);
            errors.Check(declared != null &&
                SameAssumptionDeclarations(value.Assumptions, declared.Assumptions),
                "response.callable_assumption_set");
        }
        return valid;
    }
    private static WorkerClaimResult[] ValidateClaimResults(
        WorkerClaimResult[]? values,
        ManifestIdentityIndexes manifestIndexes,
        Validator errors,
        out bool allResultsRefuted)
    {
        var valid = ValidateResultSet(values,
            manifestIndexes.Claims.Select(static value => value.ClaimId),
            static value => value.ClaimId, "response.claim_results",
            "response.result_claim_id", "response.claim_set", errors);
        allResultsRefuted = values is { Length: > 0 } &&
            valid.Length == values.Length;
        foreach (var value in valid)
        {
            allResultsRefuted &= value.Outcome == WorkerClaimOutcome.Refuted;
            ValidateClaimResult(value, manifestIndexes, errors);
        }

        return valid;
    }
    private static void ValidateClaimResult(
        WorkerClaimResult value,
        ManifestIdentityIndexes manifestIndexes,
        Validator errors)
    {
        errors.Rules(value, WorkerProtocolMetadata.ClaimResultRules);
        var claim = manifestIndexes.ClaimsById.Find(value.ClaimId);
        var effectClaim = claim?.Kind == WorkerClaimKind.Effect;
        var hasTrustedBoundary = false;
        var hasUsedTrustedBoundary = false;
        if (effectClaim)
        {
            foreach (var assumption in value.Assumptions ?? [])
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
                value.ProofCore is { Length: > 0 },
                hasTrustedBoundary,
                hasUsedTrustedBoundary),
                "response.effect_evidence")
            .Check(effectClaim && value.Outcome == WorkerClaimOutcome.Refuted
                ? HasValidEffectWitness(value.EffectWitness)
                : value.EffectWitness == null, "response.effect_witness");
        if (value.EffectWitness != null)
        {
            errors.Check(HasValidLocation(value.EffectWitness.Location), "response.effect_witness_location");
        }

        errors.Check(WorkerProtocolMetadata.MatchesVacuity(
            claim?.Kind ?? WorkerClaimKind.Unspecified, value.Outcome, value.Vacuity),
            "response.vacuity");
        var owner = manifestIndexes.CallablesById.Find(claim?.CallableId);
        errors.Check(owner != null && SameAssumptionDeclarations(value.Assumptions, owner.Assumptions),
            "response.claim_assumption_set");
    }
    internal static bool HasValidEffectCertainty(WorkerClaimOutcome outcome, WorkerClaimReason reason,
        WorkerEffectEvidenceCertainty certainty)
    {
        return WorkerProtocolMetadata.MatchesEffectCertainty(outcome, reason, certainty);
    }

    internal static bool HasValidEffectWitness(WorkerEffectViolationWitness? witness)
    {
        return witness != null && WorkerProtocolMetadata.IsEffectWitnessValid(witness);
    }

    private static void ValidateUnknownCoverage(WorkerCallableResult[] callables,
        WorkerClaimResult[] claims, ManifestIdentityIndexes manifestIndexes,
        Validator errors)
    {
        var incomplete = new HashSet<string>(
            callables.Where(static value => value.Coverage == WorkerCallableCoverage.Incomplete)
                .Select(static value => value.CallableId),
            s_ordinal);
        errors.Check(!claims.Any(value => value.Outcome == WorkerClaimOutcome.Unknown &&
            !string.IsNullOrWhiteSpace(value.ClaimId) &&
            manifestIndexes.ClaimsById.Find(value.ClaimId) is { } claim &&
            !incomplete.Contains(claim.CallableId)),
            "response.unknown_coverage");
    }
    private static void ValidateRun(
        WorkerVerifyResponse response,
        WorkerCallableResult[] callables,
        WorkerClaimResult[] claims,
        WorkerProtocolError[] protocolErrors,
        ManifestIdentityIndexes manifestIndexes,
        Validator errors)
    {
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
                static claim => (string?)claim.ClaimId,
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

    private static WorkerClaimResult[] GetOwnedClaimResults(
        WorkerCallableManifestEntry? callable,
        ILookup<string?, WorkerClaimResult> claimsById)
    {
        if (callable?.ClaimIds == null)
        {
            return [];
        }

        var ownedIds = new HashSet<string?>(s_ordinal);
        var owned = new List<WorkerClaimResult>();
        foreach (var claimId in callable.ClaimIds)
        {
            if (ownedIds.Add(claimId))
            {
                owned.AddRange(claimsById[claimId]);
            }
        }
        return [.. owned];
    }

    private static void ValidateSummary(WorkerVerificationSummary? summary,
        WorkerCallableResult[] callables, WorkerClaimResult[] claims, Validator errors)
    {
        if (summary == null)
        {
            errors.Add("response.summary");
            return;
        }
        errors.Check(summary.CallableCount == callables.Length &&
                summary.ClaimCount == claims.Length, "summary.totals")
            .Check(CountsMatch(summary.OutcomeCounts, claims.Select(static value => value.Outcome),
                static value => value.Outcome, static value => value.Count,
                WorkerClaimOutcome.Unspecified), "summary.outcomes")
            .Check(CountsMatch(summary.ReasonCounts, claims.Select(static value => value.Reason),
                static value => value.Reason, static value => value.Count,
                WorkerClaimReason.Unspecified), "summary.reasons");
        var assumptions = WorkerResultAssembler.SummarizeAssumptions(
            callables, claims, out var conflictingKinds);
        errors.Check(!conflictingKinds, "summary.assumption_conflict")
            .Check(SummaryAssumptionsMatch(summary.Assumptions, assumptions), "summary.assumptions")
            .Rules(summary, WorkerProtocolMetadata.SummaryRules.Take(2));
        ValidateBudgets(summary.Budgets, "summary.budgets", errors);
        errors.Rules(summary, WorkerProtocolMetadata.SummaryRules.Skip(2));
    }
    private static bool SummaryAssumptionsMatch(WorkerAssumptionSummary? actual, WorkerAssumptionSummary expected)
    {
        if (actual == null)
        {
            return false;
        }

        return (actual.Total, actual.Used, actual.User, actual.Trusted) ==
               (expected.Total, expected.Used, expected.User, expected.Trusted);
    }

    private static bool CountsMatch<TCount, TKind>(TCount[]? actual, IEnumerable<TKind> values,
        Func<TCount, TKind> kind, Func<TCount, int> count, TKind unspecified)
        where TCount : class where TKind : struct, Enum
    {
        var expected = values.GroupBy(static value => value)
            .ToDictionary(static group => group.Key, static group => group.Count());
        var seen = new HashSet<TKind>();
        if (actual == null || actual.Length != expected.Count)
        {
            return false;
        }

        foreach (var value in actual)
        {
            if (value == null)
            {
                return false;
            }
            var itemCount = count(value);
            if (itemCount <= 0)
            {
                return false;
            }
            var itemKind = kind(value);
            if (!IsDefined(itemKind, unspecified) ||
                !seen.Add(itemKind) ||
                !expected.TryGetValue(itemKind, out var expectedCount) ||
                itemCount != expectedCount)
            {
                return false;
            }
        }

        return true;
    }
    private static WorkerProtocolError[] ValidateProtocolErrors(WorkerProtocolError[]? values, Validator errors)
    {
        if (values != null && values.All(static value => value != null &&
            WorkerProtocolMetadata.IsProtocolErrorValid(value)))
        {
            return values;
        }

        errors.Add("response.errors");
        return [];
    }
    private static void ValidateBudgets(WorkerBudgets? value, string prefix, Validator errors)
    {
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

    internal static bool AreDistinctNonblank(string[]? values)
    {
        return CompleteUnique(values, static value => !string.IsNullOrWhiteSpace(value), static value => value);
    }

    internal static bool IsSingleLineText(string? value)
    {
        return value != null && value.All(static character =>
            !char.IsControl(character) &&
            character is not '\u2028' and not '\u2029');
    }

    internal static bool AreDefinedUnique<T>(T[]? values, T unspecified, bool nonEmpty)
            where T : struct, Enum
    {
        if (values == null || nonEmpty && values.Length == 0)
        {
            return false;
        }

        var seen = new HashSet<T>();
        foreach (var value in values)
        {
            if (!IsDefined(value, unspecified) || !seen.Add(value))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool AreValidModel(WorkerModelValue[]? values)
    {
        return CompleteUnique(values, static value => !string.IsNullOrWhiteSpace(value.Variable) &&
                !string.IsNullOrWhiteSpace(value.Kind) && !string.IsNullOrWhiteSpace(value.Value),
                static value => value.Variable);
    }

    internal static bool AreValidAssumptions(WorkerAssumptionEvidence[]? values)
    {
        return CompleteUnique(values,
                WorkerProtocolMetadata.IsAssumptionValid,
                static value => value.Id);
    }

    private static bool HasProducerAssumptionKinds(WorkerAssumptionEvidence[]? values)
    {
        return values != null && values.All(static value =>
            value != null && WorkerProtocolMetadata.MatchesAssumptionKind(value.Kind));
    }

    private static void ValidateManifestAssumptionIdentity(
        WorkerCallableManifestEntry[] callables, string prefix, Validator errors)
    {
        var groups = callables
            .Where(static callable => callable != null)
            .SelectMany(static callable => (callable.Assumptions ?? [])
                .Where(static assumption => assumption != null &&
                    !string.IsNullOrWhiteSpace(assumption.Id))
                .Select(assumption => (CallableId: callable.CallableId, Assumption: assumption)))
            .GroupBy(static item => item.Assumption.Id, s_ordinal);
        errors.Check(groups.All(static group => group
                .Select(static item => (item.CallableId, item.Assumption.Kind))
                .Distinct()
                .Count() == 1), prefix + ".assumption_identity");
    }

    private static T[] ValidateResultSet<T>(T[]? values, IEnumerable<string?> expectedIds,
        Func<T, string?> identity, string collectionCode, string identityCode, string setCode,
        Validator errors) where T : class
    {
        var present = Present(values, collectionCode, errors);
        var identities = ValidateUniqueIds(present.Select(identity), identityCode, errors);
        ValidateExactIds(identities, expectedIds, setCode, errors);
        return present;
    }
    private static string?[] ValidateUniqueIds(
        IEnumerable<string?> values, string code, Validator errors)
    {
        var items = values.ToArray();
        var seen = new HashSet<string?>(s_ordinal);
        var valid = true;
        foreach (var value in items)
        {
            if (string.IsNullOrWhiteSpace(value) || !seen.Add(value))
            {
                valid = false;
            }
        }
        errors.Check(valid, code);
        return items;
    }
    private static void ValidateExactIds(IEnumerable<string?> actual, IEnumerable<string?> expected, string code, Validator errors)
    {
        errors.Check(actual.OrderBy(static value => value, s_ordinal)
            .SequenceEqual(expected.OrderBy(static value => value, s_ordinal),
                s_ordinal), code);
    }

    private static bool CompleteUnique<T>(T[]? values, Func<T, bool> complete, Func<T, string?> key) where T : class
    {
        if (values == null)
        {
            return false;
        }

        var seen = new HashSet<string?>(s_ordinal);
        foreach (var value in values)
        {
            if (value == null || !complete(value) || !seen.Add(key(value)))
            {
                return false;
            }
        }

        return true;
    }

    private static T[] Present<T>(T[]? values, string code, Validator errors) where T : class
    {
        if (values != null && values.All(static value => value != null))
        {
            return values;
        }

        errors.Add(code);
        return [.. (values ?? []).Where(static value => value != null)];
    }

    private static WorkerAssumptionEvidence[] CanonicalizeAssumptions(WorkerAssumptionEvidence[]? values)
    {
        return [.. (values ?? [])
            .OrderBy(static value => WorkerProtocolMetadata.GetAssumptionOrder(
                value?.Kind ?? WorkerAssumptionKind.Unspecified))
            .ThenBy(static value => value?.Id, s_ordinal)];
    }

    private static T[] SortManifestEnums<T>(T[]? values) where T : struct, Enum
    {
        return [.. (values ?? []).OrderBy(ManifestName, s_ordinal)];
    }

    private static T[] SortOrdinal<T>(T[]? values, Func<T, string?> identity)
    {
        return [.. (values ?? []).OrderBy(identity, s_ordinal)];
    }

    internal static bool SameAssumptionDeclarations(
            WorkerAssumptionEvidence[]? actual, WorkerAssumptionEvidence[]? expected)
    {
        static IEnumerable<(string Id, WorkerAssumptionKind Kind)> Normalize(
            WorkerAssumptionEvidence[]? values)
        {
            return (values ?? []).Where(static value => value != null)
                .OrderBy(static value => value.Id, s_ordinal)
                .Select(static value => (value.Id, value.Kind));
        }

        return Normalize(actual).SequenceEqual(Normalize(expected));
    }

    private sealed class ManifestIdentityIndexes
    {
        internal ManifestIdentityIndexes(WorkerClaimManifest? manifest)
        {
            Callables = [.. manifest?.Callables
                ?.OfType<WorkerCallableManifestEntry>() ?? []];
            Claims = [.. manifest?.Claims
                ?.OfType<WorkerClaimManifestEntry>() ?? []];
            CallablesById = new OrdinalIdentityIndex<
                WorkerCallableManifestEntry>(
                    Callables,
                    static item => item.CallableId);
            ClaimsById = new OrdinalIdentityIndex<WorkerClaimManifestEntry>(
                Claims,
                static item => item.ClaimId);
        }

        internal WorkerCallableManifestEntry[] Callables { get; }
        internal WorkerClaimManifestEntry[] Claims { get; }
        internal OrdinalIdentityIndex<WorkerCallableManifestEntry> CallablesById { get; }
        internal OrdinalIdentityIndex<WorkerClaimManifestEntry> ClaimsById { get; }
    }

    private sealed class OrdinalIdentityIndex<T>
        where T : class
    {
        private readonly Dictionary<string, T> _byId = new(s_ordinal);
        private bool _hasNull;
        private T? _nullValue;

        internal OrdinalIdentityIndex(
            IEnumerable<T> values,
            Func<T, string?> identity)
        {
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
                else if (!_byId.ContainsKey(id))
                {
                    _byId.Add(id, value);
                }
            }
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

    private static ArgumentOutOfRangeException UnexpectedManifestEnum<T>(T value) where T : struct, Enum
    {
        return new(nameof(value), value, "The manifest contains an unknown enum value.");
    }

    private static T? Deserialize<T>(string json)
    {
        EnsureJsonShape(json, typeof(T).Name);
        return JsonSerializer.Deserialize<T>(json, s_options);
    }
    private static OrdinalIdentityIndex<WorkerClaimManifestEntry>
        CreateClaimIndex(WorkerClaimManifest? manifest)
    {
        return new(
            (manifest?.Claims ?? []).OfType<WorkerClaimManifestEntry>(),
            static value => value.ClaimId);
    }

    private static int FindClaimOrdinal(
        OrdinalIdentityIndex<WorkerClaimManifestEntry> claimsById,
        string? id)
    {
        return claimsById.Find(id)?.Ordinal ?? int.MaxValue;
    }

    private static string FindClaimCallableId(
        OrdinalIdentityIndex<WorkerClaimManifestEntry> claimsById,
        string? id)
    {
        return claimsById.Find(id)?.CallableId ?? string.Empty;
    }

    internal static bool IsSha256(string? value)
    {
        return ProtocolHashEncoding.IsSha256(value);
    }

    internal static string ComputeSha256(byte[] bytes)
    {
        return ProtocolHashEncoding.ComputeSha256Hex(bytes);
    }
    internal static bool IsDefined<T>(T value, T unspecified) where T : struct, Enum
    {
        return WorkerProtocolMetadata.IsKnown(value) &&
        !EqualityComparer<T>.Default.Equals(value, unspecified);
    }

    private static void RequireSha256(string value, string parameter, string kind)
    {
        if (!IsSha256(value))
        {
            throw new ArgumentException($"A lowercase SHA-256 {kind} hash is required.", parameter);
        }
    }
}
