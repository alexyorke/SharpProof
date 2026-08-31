using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
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
        var expectedLength = new FileInfo(path).Length;
        if (expectedLength <= 0 || expectedLength > MaximumJsonBytes)
        {
            throw new InvalidDataException("The JSON file exceeds the size limit.");
        }

        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, options: FileOptions.SequentialScan);
        if (file.Length != expectedLength)
        {
            throw new InvalidDataException("The JSON file changed while it was opened.");
        }

        using var bounded = new BoundedReadStream(file, MaximumJsonBytes,
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
        return Deserialize<WorkerVerifyRequest>(json, WorkerProtocolMetadata.WorkerVerifyRequestJsonProperties);
    }

    public static WorkerVerifyResponse? DeserializeResponse(string json)
    {
        return Deserialize<WorkerVerifyResponse>(json, WorkerProtocolMetadata.WorkerVerifyResponseJsonProperties);
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
        // Inspect the directory entry before FileStream opens it. On Unix,
        // opening a FIFO for reading waits for a writer, so a zero-length
        // non-file must fail before the potentially blocking open.
        var fileLength = new FileInfo(path).Length;
        if (fileLength <= 0)
        {
            throw new InvalidDataException(
                "The JSON file must be a nonempty regular file.");
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

        return new StreamReader(
            new BoundedReadStream(
                stream,
                MaximumJsonBytes,
                $"The JSON file exceeds the {MaximumJsonBytes} byte limit."),
            s_strictUtf8,
            detectEncodingFromByteOrderMarks: false);
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

    internal static WorkerProtocolValidationResult Validate(
        WorkerVerifyResponse? response, string expectedInputHash,
        WorkerClaimManifest? expectedManifest,
        IWorkerResponseEvidenceAuthority evidenceAuthority)
    {
        RequireSha256(expectedInputHash, nameof(expectedInputHash), "input");
        _ = evidenceAuthority ??
            throw new ArgumentNullException(nameof(evidenceAuthority));
        return ValidateResponse(
            response, expectedInputHash, expectedManifest, null, null, null,
            evidenceAuthority: evidenceAuthority);
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
        int terminationGraceMilliseconds = WorkerLauncherDefaults.TerminationGraceMilliseconds)
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
            maximumElapsedMilliseconds, evidenceAuthority);
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

        response.ClaimResults = [.. (response.ClaimResults ?? [])
            .OrderBy(value => FindClaimCallableId(response.Manifest, value?.ClaimId), s_ordinal)
            .ThenBy(value => FindClaimOrdinal(response.Manifest, value?.ClaimId))
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
        IWorkerResponseEvidenceAuthority? evidenceAuthority = null)
    {
        var errors = new Validator();
        if (response == null)
        {
            return errors.Fail("response.null");
        }

        errors.Check(response.ProtocolVersion == WorkerProtocolVersions.Current, "response.protocol")
            .Check(IsSha256(response.RequestHash), "response.request_hash");
        if (IsSha256(response.RequestHash) && expectedRequestHash != null)
        {
            errors.Check(response.RequestHash == expectedRequestHash, "response.request_mismatch");
        }

        errors.Check(IsSha256(response.InputHash), "response.input_hash");
        if (IsSha256(response.InputHash) && expectedInputHash != null)
        {
            errors.Check(response.InputHash == expectedInputHash, "response.input_mismatch");
        }

        var manifestErrorCount = errors.Count;
        ValidateManifestCore(response.Manifest, "manifest", errors);
        ValidateExpectedManifest(
            response.Manifest,
            expectedManifest,
            errors.Count == manifestErrorCount,
            errors);
        var protocolErrors = ValidateProtocolErrors(response.Errors, errors);
        var callables = ValidateCallableResults(response.CallableResults, response.Manifest, errors);
        var claims = ValidateClaimResults(response.ClaimResults, response.Manifest, errors);
        ValidateRun(response, callables, claims, protocolErrors, errors);
        ValidateUnknownCoverage(callables, claims, response.Manifest, errors);
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
                JsonSerializer.Serialize(response.Summary.Budgets, s_options) ==
                JsonSerializer.Serialize(expectedRequest.Budgets, s_options), "response.budgets_mismatch");
            ValidateCacheForRequest(response, expectedRequest, errors);
        }

        if (evidenceAuthority != null)
        {
            try
            {
                foreach (var code in evidenceAuthority.Validate(response)
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
            CallableResults: { Length: > 0 } callables,
            ClaimResults: { Length: > 0 } claims,
            Manifest.Claims: { Length: > 0 } manifestClaims
        } &&
            callables.All(static result => result is
            {
                Coverage: WorkerCallableCoverage.Complete,
                Reason: WorkerCallableCoverageReason.None
            }) &&
            claims.All(static result => result?.Outcome == WorkerClaimOutcome.Refuted) &&
            manifestClaims.All(static claim =>
                claim?.Kind == WorkerClaimKind.Postcondition);
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
        ValidateManifestCore(expected, "expected_manifest", expectedErrors);
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
    private static void ValidateManifestCore(WorkerClaimManifest? manifest, string prefix, Validator errors)
    {
        if (manifest == null)
        {
            errors.Add(prefix + ".null");
            return;
        }
        var initialErrors = errors.Count;
        errors.Check(manifest.SchemaVersion == WorkerManifestVersions.Current, prefix + ".schema");
        var callables = Present(manifest.Callables, prefix + ".callables", errors);
        var claims = Present(manifest.Claims, prefix + ".claims", errors);
        ValidateUniqueIds(callables.Select(static value => value.CallableId), prefix + ".callable_id", errors);
        ValidateUniqueIds(claims.Select(static value => value.ClaimId), prefix + ".claim_id", errors);
        var callableIds = new HashSet<string>(
            callables.Where(static value => !string.IsNullOrWhiteSpace(value.CallableId))
                .Select(static value => value.CallableId),
            s_ordinal);
        foreach (var callable in callables)
        {
            errors.Check(HasValidLocation(callable.Location), prefix + ".callable_location")
                .Rules(callable, WorkerProtocolMetadata.ManifestCallableRules, prefix + ".")
                .Check(HasProducerAssumptionKinds(callable.Assumptions),
                    prefix + ".assumption_kind");
            ValidateClaimMembership(callable, claims, prefix, errors);
        }
        ValidateManifestAssumptionIdentity(callables, prefix, errors);
        foreach (var claim in claims)
        {
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
        WorkerCallableManifestEntry callable, WorkerClaimManifestEntry[] claims, string prefix, Validator errors)
    {
        var expected = claims.Where(value => value.CallableId == callable.CallableId)
            .OrderBy(static value => value.Ordinal)
            .ThenBy(static value => value.ClaimId, s_ordinal).ToArray();
        errors.Check(expected.Select(static value => value.Ordinal)
                .SequenceEqual(Enumerable.Range(0, expected.Length)), prefix + ".dense_ordinals")
            .Check(callable.ClaimIds != null && callable.ClaimIds.SequenceEqual(
                expected.Select(static value => value.ClaimId), s_ordinal),
                prefix + ".claim_membership")
            .Check(HasVerifierCompatibleClaimOrder(expected), prefix + ".claim_order");
    }
    private static bool HasVerifierCompatibleClaimOrder(WorkerClaimManifestEntry[] claims)
    {
        var effectSeen = false;
        foreach (var claim in claims)
        {
            if (claim.Kind == WorkerClaimKind.Effect)
            {
                effectSeen = true;
            }
            else if (effectSeen && claim.Kind == WorkerClaimKind.Postcondition)
            {
                return false;
            }
        }

        return true;
    }
    private static WorkerCallableResult[] ValidateCallableResults(WorkerCallableResult[]? values,
        WorkerClaimManifest? manifest, Validator errors)
    {
        var valid = ValidateResultSet(values,
            manifest?.Callables?.OfType<WorkerCallableManifestEntry>()
                .Select(static value => value.CallableId) ?? [],
            static value => value.CallableId, "response.callable_results",
            "response.callable_id", "response.callable_set", errors);
        foreach (var value in valid)
        {
            errors.Rules(value, WorkerProtocolMetadata.CallableResultRules);
            var declared = manifest?.Callables?.FirstOrDefault(
                item => item != null && item.CallableId == value.CallableId);
            errors.Check(declared != null &&
                SameAssumptionDeclarations(value.Assumptions, declared.Assumptions),
                "response.callable_assumption_set");
        }
        return valid;
    }
    private static WorkerClaimResult[] ValidateClaimResults(WorkerClaimResult[]? values, WorkerClaimManifest? manifest, Validator errors)
    {
        var valid = ValidateResultSet(values,
            manifest?.Claims?.OfType<WorkerClaimManifestEntry>()
                .Select(static value => value.ClaimId) ?? [],
            static value => value.ClaimId, "response.claim_results",
            "response.result_claim_id", "response.claim_set", errors);
        foreach (var value in valid)
        {
            ValidateClaimResult(value, manifest, errors);
        }

        return valid;
    }
    private static void ValidateClaimResult(WorkerClaimResult value, WorkerClaimManifest? manifest, Validator errors)
    {
        errors.Rules(value, WorkerProtocolMetadata.ClaimResultRules);
        var claim = manifest?.Claims?.FirstOrDefault(
            item => item != null && item.ClaimId == value.ClaimId);
        var effectClaim = claim?.Kind == WorkerClaimKind.Effect;
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
                (value.Assumptions ?? []).Any(static assumption =>
                    assumption != null &&
                    assumption.Kind == WorkerAssumptionKind.TrustedBoundary),
                (value.Assumptions ?? []).Any(static assumption =>
                    assumption != null &&
                    assumption.Kind == WorkerAssumptionKind.TrustedBoundary &&
                    assumption.Used)),
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
        var owner = manifest?.Callables?.FirstOrDefault(
            item => item != null && item.CallableId == claim?.CallableId);
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
        WorkerClaimResult[] claims, WorkerClaimManifest? manifest, Validator errors)
    {
        if (manifest?.Claims == null)
        {
            return;
        }

        var owners = manifest.Claims.Where(static value =>
                value != null && !string.IsNullOrWhiteSpace(value.ClaimId))
            .GroupBy(static value => value.ClaimId, s_ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().CallableId,
                s_ordinal);
        var incomplete = new HashSet<string>(
            callables.Where(static value => value.Coverage == WorkerCallableCoverage.Incomplete)
                .Select(static value => value.CallableId),
            s_ordinal);
        errors.Check(!claims.Any(value => value.Outcome == WorkerClaimOutcome.Unknown &&
            owners.TryGetValue(value.ClaimId, out var owner) && !incomplete.Contains(owner)),
            "response.unknown_coverage");
    }
    private static void ValidateRun(
        WorkerVerifyResponse response,
        WorkerCallableResult[] callables,
        WorkerClaimResult[] claims,
        WorkerProtocolError[] protocolErrors,
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
            foreach (var callable in callables)
            {
                errors.Check(WorkerResultAssembler.MatchesCallableProjection(
                        callable,
                        response.Manifest,
                        claims,
                        expectedStatus,
                        expectedFailure,
                        protocolErrors.Length != 0),
                    "response.callable_projection");
            }
        }
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
        return actual != null &&
            actual.Length == expected.Count &&
            actual.All(value => value != null && count(value) > 0 &&
                IsDefined(kind(value), unspecified) && seen.Add(kind(value)) &&
                expected.TryGetValue(kind(value), out var expectedCount) &&
                count(value) == expectedCount);
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
             (value.Path.Length == 0 &&
              value.Start == 0 &&
              value.Length == 0 &&
              value.Line == 0 &&
              value.Column == 0));
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
        return values != null && (!nonEmpty || values.Length > 0) &&
            values.All(value => IsDefined(value, unspecified)) &&
            values.Distinct().Count() == values.Length;
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
        ValidateUniqueIds(present.Select(identity), identityCode, errors);
        ValidateExactIds(present.Select(identity), expectedIds, setCode, errors);
        return present;
    }
    private static void ValidateUniqueIds(IEnumerable<string?> values, string code, Validator errors)
    {
        var items = values.ToArray();
        errors.Check(items.All(static value => !string.IsNullOrWhiteSpace(value)) &&
            items.Distinct(s_ordinal).Count() == items.Length, code);
    }
    private static void ValidateExactIds(IEnumerable<string?> actual, IEnumerable<string?> expected, string code, Validator errors)
    {
        errors.Check(actual.OrderBy(static value => value, s_ordinal)
            .SequenceEqual(expected.OrderBy(static value => value, s_ordinal),
                s_ordinal), code);
    }

    private static bool CompleteUnique<T>(T[]? values, Func<T, bool> complete, Func<T, string?> key) where T : class
    {
        return values != null && values.All(value => value != null && complete(value)) &&
            values.Select(key).Distinct(s_ordinal).Count() == values.Length;
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

    private static bool SameAssumptionDeclarations(
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

    private static string ManifestName<T>(T value) where T : struct, Enum
    {
        return WorkerProtocolMetadata.GetManifestName((Enum)(object)value) ??
            throw UnexpectedManifestEnum(value);
    }

    private static ArgumentOutOfRangeException UnexpectedManifestEnum<T>(T value) where T : struct, Enum
    {
        return new(nameof(value), value, "The manifest contains an unknown enum value.");
    }

    private static T? Deserialize<T>(string json, IEnumerable<string> requiredProperties)
    {
        _ = requiredProperties;
        EnsureJsonShape(json, typeof(T).Name);
        return JsonSerializer.Deserialize<T>(json, s_options);
    }
    private static int FindClaimOrdinal(WorkerClaimManifest? manifest, string? id)
    {
        return manifest?.Claims?.FirstOrDefault(value => value != null && value.ClaimId == id)?.Ordinal ??
        int.MaxValue;
    }

    private static string FindClaimCallableId(WorkerClaimManifest? manifest, string? id)
    {
        return manifest?.Claims?.FirstOrDefault(value => value != null && value.ClaimId == id)?.CallableId ??
            string.Empty;
    }

    internal static bool IsSha256(string? value)
    {
        return value is { Length: 64 } &&
            value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    internal static string ComputeSha256(byte[] bytes)
    {
        using var hash = SHA256.Create();
        return string.Concat(hash.ComputeHash(bytes)
            .Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
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
