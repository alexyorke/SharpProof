using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorBuilder = System.Collections.Immutable.ImmutableArray<SharpProof.Worker.Protocol.WorkerProtocolError>.Builder;

namespace SharpProof.Worker.Protocol;

public static class WorkerProtocolJson {
    private static readonly JsonSerializerOptions s_options = CreateOptions();
    private static readonly string[] s_requestProperties = [
        "protocolVersion", "projectDirectory", "assemblyName", "sourceFiles",
        "referenceAssemblies", "defineConstants", "compilation", "budgets", "cache", "features", "verifyPolicy", "assumptionPolicy"];
    private static readonly string[] s_responseProperties = [
        "protocolVersion", "inputHash", "manifest", "runStatus", "failureReason", "callableResults", "claimResults", "summary", "errors"];

    public static JsonSerializerOptions Options => new(s_options);
    public static WorkerVerifyRequest? DeserializeRequest(string json) {
        EnsureRootProperties(json, s_requestProperties);
        return JsonSerializer.Deserialize<WorkerVerifyRequest>(json, s_options);
    }
    public static WorkerVerifyResponse? DeserializeResponse(string json) {
        EnsureRootProperties(json, s_responseProperties);
        return JsonSerializer.Deserialize<WorkerVerifyResponse>(json, s_options);
    }
    public static string SerializeRequest(WorkerVerifyRequest request) {
        if (request == null) throw new ArgumentNullException(nameof(request));
        return JsonSerializer.Serialize(request, s_options);
    }
    public static string SerializeResponse(WorkerVerifyResponse response) {
        if (response == null) throw new ArgumentNullException(nameof(response));
        Canonicalize(response);
        return JsonSerializer.Serialize(response, s_options);
    }
    public static WorkerProtocolValidationResult Validate(WorkerVerifyRequest? request) {
        var validation = new Validator();
        if (request == null) {
            validation.Add("request.null", "The request is required.");
            return validation.Result;
        }
        validation.Check(request.ProtocolVersion == WorkerProtocolVersions.Current, "protocol.unsupported",
            $"Protocol version {WorkerProtocolVersions.Current} is required.");
        validation.Check(!string.IsNullOrWhiteSpace(request.ProjectDirectory), "project.directory", "A project directory is required.");
        validation.Check(!string.IsNullOrWhiteSpace(request.AssemblyName), "project.assembly_name", "An assembly name is required.");
        ValidateStrings(request.SourceFiles, false, "project.sources", "project.source_path", validation);
        ValidateStrings(request.ReferenceAssemblies, false, "project.references", "project.reference_path", validation);
        ValidateStrings(request.DefineConstants, true, "project.constants", "project.constant", validation);
        ValidateCompilation(request.Compilation, validation);
        ValidateBudgets(request.Budgets, "budgets", validation);
        if (request.Cache == null)
            validation.Add("cache.null", "Cache options are required.");
        else
            validation.Check(request.Cache.MaximumBytes is > 0 and <= WorkerCacheOptions.DefaultMaximumBytes,
                "cache.maximum_bytes", "Cache size must be between 1 byte and 512 MiB.");
        validation.Defined(request.Features, WorkerFeatureSet.Unspecified, "policy.features", "An analysis feature set is required.");
        validation.Defined(request.VerifyPolicy, WorkerVerifyPolicy.Unspecified, "policy.verify", "A verification policy is required.");
        validation.Defined(request.AssumptionPolicy, WorkerAssumptionPolicy.Unspecified, "policy.assumption",
            "An assumption policy is required.");
        return validation.Result;
    }
    public static WorkerProtocolValidationResult Validate(WorkerVerifyResponse? response) => ValidateResponse(response, null, null);
    public static WorkerProtocolValidationResult Validate(WorkerVerifyResponse? response, string expectedInputHash,
        WorkerClaimManifest? expectedManifest = null) {
        if (!IsSha256(expectedInputHash))
            throw new ArgumentException("A lowercase SHA-256 input hash is required.", nameof(expectedInputHash));
        return ValidateResponse(response, expectedInputHash, expectedManifest);
    }
    public static void Canonicalize(WorkerClaimManifest manifest) {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        manifest.Claims = [.. (manifest.Claims ?? [])
            .OrderBy(static claim => claim?.CallableId, StringComparer.Ordinal).ThenBy(static claim => claim?.Ordinal ?? int.MinValue)
            .ThenBy(static claim => claim?.ClaimId, StringComparer.Ordinal)];
        manifest.Callables = [.. (manifest.Callables ?? []).OrderBy(static callable => callable?.CallableId, StringComparer.Ordinal)];
        foreach (var callable in manifest.Callables.Where(static callable => callable != null)) {
            callable.SelectedFeatures = [.. (callable.SelectedFeatures ?? []).OrderBy(static value => value)];
            callable.SelectionReasons = [.. (callable.SelectionReasons ?? []).OrderBy(static value => value)];
            callable.ClaimIds = [.. (callable.ClaimIds ?? [])
                .OrderBy(id => FindClaimOrdinal(manifest, id)).ThenBy(static id => id, StringComparer.Ordinal)];
        }
    }
    public static string ComputeManifestHash(WorkerClaimManifest manifest) {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(CreateManifestPayload(manifest)));
        return string.Concat(bytes.Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }
    public static void SealManifest(WorkerClaimManifest manifest) {
        if (manifest == null) throw new ArgumentNullException(nameof(manifest));
        Canonicalize(manifest);
        manifest.Hash = ComputeManifestHash(manifest);
    }
    public static void Canonicalize(WorkerVerifyResponse response) {
        if (response == null) throw new ArgumentNullException(nameof(response));
        if (response.Manifest != null) Canonicalize(response.Manifest);
        response.CallableResults = [.. (response.CallableResults ?? []).OrderBy(
            static result => result?.CallableId, StringComparer.Ordinal)];
        foreach (var result in response.CallableResults.Where(static result => result != null))
            result.Assumptions = CanonicalizeAssumptions(result.Assumptions);
        response.ClaimResults = [.. (response.ClaimResults ?? [])
            .OrderBy(result => FindClaimCallableId(response.Manifest, result?.ClaimId), StringComparer.Ordinal)
            .ThenBy(result => FindClaimOrdinal(response.Manifest, result?.ClaimId))
            .ThenBy(static result => result?.ClaimId, StringComparer.Ordinal)];
        foreach (var result in response.ClaimResults.Where(static result => result != null)) {
            result.ProofCore = [.. (result.ProofCore ?? []).OrderBy(static value => value, StringComparer.Ordinal)];
            result.Model = [.. (result.Model ?? [])
                .OrderBy(static value => value?.Variable, StringComparer.Ordinal).ThenBy(static value => value?.Kind, StringComparer.Ordinal)
                .ThenBy(static value => value?.Value, StringComparer.Ordinal)];
            result.Assumptions = CanonicalizeAssumptions(result.Assumptions);
        }
        if (response.Summary != null) {
            response.Summary.OutcomeCounts = [.. (response.Summary.OutcomeCounts ?? []).OrderBy(static count => count?.Outcome)];
            response.Summary.ReasonCounts = [.. (response.Summary.ReasonCounts ?? []).OrderBy(static count => count?.Reason)];
        }
        response.Errors = [.. (response.Errors ?? []).OrderBy(static error => error?.Code, StringComparer.Ordinal)
            .ThenBy(static error => error?.Message, StringComparer.Ordinal)];
    }
    private static WorkerProtocolValidationResult ValidateResponse(
        WorkerVerifyResponse? response, string? expectedInputHash, WorkerClaimManifest? expectedManifest) {
        var validation = new Validator();
        if (response == null) {
            validation.Add("response.null", "The response is required.");
            return validation.Result;
        }
        validation.Check(response.ProtocolVersion == WorkerProtocolVersions.Current,
            "response.protocol", "The response protocol is invalid.");
        validation.Check(IsSha256(response.InputHash), "response.input_hash", "The response input hash is invalid.");
        if (IsSha256(response.InputHash) && expectedInputHash != null)
            validation.Check(response.InputHash == expectedInputHash,
                "response.input_mismatch", "The response input hash does not match the request.");
        ValidateManifest(response.Manifest, "manifest", validation);
        if (expectedManifest != null) {
            var expectedValidation = new Validator();
            ValidateManifest(expectedManifest, "expected_manifest", expectedValidation);
            if (expectedValidation.Count != 0)
                validation.Add("response.expected_manifest", "The expected manifest is invalid.");
            else if (response.Manifest != null)
                validation.Check(CreateManifestPayload(response.Manifest) == CreateManifestPayload(expectedManifest),
                    "response.manifest_mismatch", "The response manifest does not match the current manifest.");
        }
        var protocolErrors = ValidateProtocolErrors(response.Errors, validation);
        ValidateRun(response, protocolErrors, validation);
        var callables = ValidateCallableResults(response.CallableResults, response.Manifest, validation);
        var claims = ValidateClaimResults(response.ClaimResults, response.Manifest, validation);
        ValidateUnknownCoverage(callables, claims, response.Manifest, validation);
        ValidateSummary(response.Summary, callables, claims, validation);
        return validation.Result;
    }
    private static void ValidateManifest(WorkerClaimManifest? manifest, string prefix, Validator validation) {
        if (manifest == null) {
            validation.Add(prefix + ".null", "The claim manifest is required.");
            return;
        }
        var initialErrors = validation.Count;
        validation.Check(manifest.SchemaVersion == WorkerManifestVersions.Current, prefix + ".schema", "The manifest schema is invalid.");
        validation.Check(manifest.Callables != null && manifest.Callables.All(static value => value != null),
            prefix + ".callables", "Manifest callables cannot be null.");
        validation.Check(manifest.Claims != null && manifest.Claims.All(static value => value != null),
            prefix + ".claims", "Manifest claims cannot be null.");
        var callables = (manifest.Callables ?? []).Where(static value => value != null).ToArray();
        var claims = (manifest.Claims ?? []).Where(static value => value != null).ToArray();
        ValidateUniqueIds(callables.Select(static value => value.CallableId), prefix + ".callable_id", validation);
        ValidateUniqueIds(claims.Select(static value => value.ClaimId), prefix + ".claim_id", validation);
        var callableIds = new HashSet<string>(
            callables.Where(static value => !string.IsNullOrWhiteSpace(value.CallableId))
                .Select(static value => value.CallableId), StringComparer.Ordinal);
        foreach (var callable in callables) {
            ValidateLocation(callable.Location, prefix + ".callable_location", validation);
            ValidateEnumArray(callable.SelectedFeatures, WorkerSelectedFeature.Unspecified, prefix + ".selected_features", validation);
            ValidateEnumArray(callable.SelectionReasons, WorkerSelectionReason.Unspecified, prefix + ".selection_reasons", validation);
            validation.Check(callable.ClaimIds != null &&
                    callable.ClaimIds.All(static id => !string.IsNullOrWhiteSpace(id)) &&
                    callable.ClaimIds.Distinct(StringComparer.Ordinal).Count() == callable.ClaimIds.Length,
                prefix + ".callable_claim_ids", "Callable claim IDs must be nonblank and unique.");
        }
        foreach (var claim in claims) {
            validation.Check(callableIds.Contains(claim.CallableId), prefix + ".claim_callable",
                "Every claim must reference a manifest callable.");
            validation.Check(claim.Ordinal >= 0, prefix + ".claim_ordinal", "Claim ordinals cannot be negative.");
            validation.Defined(claim.Kind, WorkerClaimKind.Unspecified, prefix + ".claim_kind", "A claim kind is required.");
            validation.Defined(
                claim.Evidence, WorkerClaimEvidence.Unspecified, prefix + ".claim_evidence", "Claim evidence is required.");
            ValidateLocation(claim.Location, prefix + ".claim_location", validation);
        }
        foreach (var callable in callables) {
            var expected = claims.Where(claim => claim.CallableId == callable.CallableId)
                .OrderBy(static claim => claim.Ordinal).ThenBy(static claim => claim.ClaimId, StringComparer.Ordinal).ToArray();
            validation.Check(expected.Select(static claim => claim.Ordinal).SequenceEqual(Enumerable.Range(0, expected.Length)),
                prefix + ".dense_ordinals", "Claim ordinals must be dense within each callable.");
            validation.Check(callable.ClaimIds != null && callable.ClaimIds.SequenceEqual(
                    expected.Select(static claim => claim.ClaimId), StringComparer.Ordinal),
                prefix + ".claim_membership", "Callable claim IDs must exactly match its claims.");
        }
        if (validation.Count == initialErrors)
            validation.Check(manifest.Hash == ComputeManifestHash(manifest), prefix + ".hash", "The manifest hash is invalid.");
    }
    private static WorkerCallableResult[] ValidateCallableResults(
        WorkerCallableResult[]? values, WorkerClaimManifest? manifest, Validator validation) {
        if (values == null || values.Any(static value => value == null)) {
            validation.Add("response.callable_results", "Callable results cannot be null.");
            values ??= [];
        }
        var valid = values.Where(static value => value != null).ToArray();
        ValidateUniqueIds(valid.Select(static value => value.CallableId), "response.callable_id", validation);
        foreach (var value in valid) {
            validation.Defined(value.Coverage, WorkerCallableCoverage.Unspecified, "response.callable_coverage",
                "Callable coverage is required.");
            if (value.Coverage == WorkerCallableCoverage.Complete && value.Reason != WorkerCallableCoverageReason.None ||
                value.Coverage == WorkerCallableCoverage.Incomplete &&
                    value.Reason is WorkerCallableCoverageReason.Unspecified or WorkerCallableCoverageReason.None ||
                !Enum.IsDefined(typeof(WorkerCallableCoverageReason), value.Reason))
                validation.Add("response.callable_reason", "Callable coverage and reason are inconsistent.");
            ValidateAssumptions(value.Assumptions, "response.callable_assumptions", validation);
        }
        ValidateExactIds(valid.Select(static value => value.CallableId),
            manifest?.Callables?.Where(static value => value != null).Select(static value => value.CallableId) ?? [],
            "response.callable_set", validation);
        return valid;
    }
    private static WorkerClaimResult[] ValidateClaimResults(
        WorkerClaimResult[]? values, WorkerClaimManifest? manifest, Validator validation) {
        if (values == null || values.Any(static value => value == null)) {
            validation.Add("response.claim_results", "Claim results cannot be null.");
            values ??= [];
        }
        var valid = values.Where(static value => value != null).ToArray();
        ValidateUniqueIds(valid.Select(static value => value.ClaimId), "response.result_claim_id", validation);
        foreach (var value in valid) {
            validation.Defined(
                value.Outcome, WorkerClaimOutcome.Unspecified, "response.claim_outcome", "A claim outcome is required.");
            if (value.Outcome == WorkerClaimOutcome.Unknown &&
                    value.Reason is WorkerClaimReason.Unspecified or WorkerClaimReason.None ||
                value.Outcome is WorkerClaimOutcome.Proven or WorkerClaimOutcome.Refuted && value.Reason != WorkerClaimReason.None ||
                !Enum.IsDefined(typeof(WorkerClaimReason), value.Reason))
                validation.Add("response.claim_reason", "Claim outcome and reason are inconsistent.");
            ValidateEvidence(value, validation);
        }
        ValidateExactIds(valid.Select(static value => value.ClaimId),
            manifest?.Claims?.Where(static value => value != null).Select(static value => value.ClaimId) ?? [],
            "response.claim_set", validation);
        return valid;
    }
    private static void ValidateEvidence(WorkerClaimResult value, Validator validation) {
        validation.Check(AreCompleteAndUnique(
                value.ProofCore, static item => !string.IsNullOrWhiteSpace(item), static item => item),
            "response.proof_core", "Proof-core entries must be nonblank and unique.");
        validation.Check(AreCompleteAndUnique(value.Model, static model => !string.IsNullOrWhiteSpace(model.Variable) &&
                    !string.IsNullOrWhiteSpace(model.Kind) &&
                    !string.IsNullOrWhiteSpace(model.Value), static model => model.Variable),
            "response.model", "Model values must be complete and unique.");
        ValidateAssumptions(value.Assumptions, "response.assumptions", validation);
        if (value.Outcome != WorkerClaimOutcome.Proven && value.ProofCore is { Length: > 0 } ||
            value.Outcome != WorkerClaimOutcome.Refuted && value.Model is { Length: > 0 })
            validation.Add("response.claim_payload", "Proof cores and models must match the claim outcome.");
    }
    private static void ValidateUnknownCoverage(WorkerCallableResult[] callables, WorkerClaimResult[] claims,
        WorkerClaimManifest? manifest, Validator validation) {
        if (manifest?.Claims == null) return;
        var claimOwners = manifest.Claims.Where(static claim => claim != null && !string.IsNullOrWhiteSpace(claim.ClaimId))
            .GroupBy(static claim => claim.ClaimId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().CallableId, StringComparer.Ordinal);
        var incomplete = new HashSet<string>(
            callables.Where(static result => result.Coverage == WorkerCallableCoverage.Incomplete)
                .Select(static result => result.CallableId), StringComparer.Ordinal);
        validation.Check(!claims.Any(result => result.Outcome == WorkerClaimOutcome.Unknown &&
                claimOwners.TryGetValue(result.ClaimId, out var callableId) && !incomplete.Contains(callableId)),
            "response.unknown_coverage", "A callable with an Unknown claim must be Incomplete.");
    }
    private static void ValidateRun(WorkerVerifyResponse response, WorkerProtocolError[] protocolErrors, Validator validation) {
        validation.Defined(
            response.RunStatus, WorkerRunStatus.Unspecified, "response.run_status", "A run status is required.");
        if (!Enum.IsDefined(typeof(WorkerRunFailureReason), response.FailureReason) ||
            response.RunStatus == WorkerRunStatus.Failed &&
                response.FailureReason is WorkerRunFailureReason.Unspecified or WorkerRunFailureReason.None ||
            response.RunStatus != WorkerRunStatus.Failed &&
                response.FailureReason != WorkerRunFailureReason.None ||
            protocolErrors.Length != 0 && response.RunStatus != WorkerRunStatus.Failed)
            validation.Add("response.run_failure", "Run status, failure reason, and errors are inconsistent.");
        var fatalClaim = response.ClaimResults?.Where(static result => result != null).Any(static result => result.Reason is
                WorkerClaimReason.BackendUnavailable or WorkerClaimReason.InfrastructureFailure or
                WorkerClaimReason.MalformedBackendResult or WorkerClaimReason.CounterexampleReplayFailed) == true;
        validation.Check(!fatalClaim || response.RunStatus == WorkerRunStatus.Failed,
            "response.fatal_claim", "Fatal claim failures require a Failed run.");
        var callableReasons = response.CallableResults?.Where(static result => result != null)
            .Select(static result => result.Reason).ToArray() ?? [];
        var claimReasons = response.ClaimResults?.Where(static result => result != null)
            .Select(static result => result.Reason).ToArray() ?? [];
        var fatalCallable = callableReasons.Any(static reason => reason is
            WorkerCallableCoverageReason.InfrastructureFailure or WorkerCallableCoverageReason.MissingClaimResult);
        validation.Check(!fatalCallable || response.RunStatus == WorkerRunStatus.Failed,
            "response.fatal_callable", "Fatal callable failures require a Failed run.");
        var timedOut = callableReasons.Any(static reason => reason is
                WorkerCallableCoverageReason.MethodTimeout or WorkerCallableCoverageReason.ProjectTimeout) ||
            claimReasons.Any(static reason => reason is WorkerClaimReason.MethodTimeout or WorkerClaimReason.ProjectTimeout);
        validation.Check(
            !timedOut || response.RunStatus is WorkerRunStatus.TimedOut or WorkerRunStatus.Failed,
            "response.timeout_status", "Timeout evidence requires a TimedOut or Failed run.");
        var canceled = callableReasons.Contains(WorkerCallableCoverageReason.Canceled) || claimReasons.Contains(WorkerClaimReason.Canceled);
        validation.Check(
            !canceled || response.RunStatus is WorkerRunStatus.Canceled or WorkerRunStatus.Failed,
            "response.canceled_status", "Cancellation evidence requires a Canceled or Failed run.");
    }
    private static void ValidateSummary(WorkerVerificationSummary? summary, WorkerCallableResult[] callables,
        WorkerClaimResult[] claims, Validator validation) {
        if (summary == null) {
            validation.Add("response.summary", "A verification summary is required.");
            return;
        }
        validation.Check(summary.CallableCount == callables.Length && summary.ClaimCount == claims.Length,
            "summary.totals", "Summary totals do not match the results.");
        validation.Check(CountsMatch(summary.OutcomeCounts, claims.Select(static value => value.Outcome),
                static value => value.Outcome, static value => value.Count, WorkerClaimOutcome.Unspecified),
            "summary.outcomes", "Summary outcome counts do not match the claim results.");
        validation.Check(CountsMatch(summary.ReasonCounts, claims.Select(static value => value.Reason),
                static value => value.Reason, static value => value.Count, WorkerClaimReason.Unspecified),
            "summary.reasons", "Summary reason counts do not match the claim results.");
        var assumptions = callables.SelectMany(static callable => callable.Assumptions ?? [])
            .Concat(claims.SelectMany(static claim => claim.Assumptions ?? [])).Where(static assumption => assumption != null)
            .GroupBy(static assumption => assumption.Id, StringComparer.Ordinal).ToArray();
        validation.Check(!assumptions.Any(
                static group => group.Select(static value => value.Kind).Distinct().Count() != 1),
            "summary.assumption_conflict", "An assumption ID cannot have conflicting kinds.");
        validation.Check(summary.Assumptions != null &&
                summary.Assumptions.Total == assumptions.Length &&
                summary.Assumptions.Used == assumptions.Count(static group =>
                    group.Any(static value => value.Used)) &&
                summary.Assumptions.User == assumptions.Count(static group =>
                    group.First().Kind == WorkerAssumptionKind.UserAssume) &&
                summary.Assumptions.Trusted == assumptions.Count(static group =>
                    group.First().Kind == WorkerAssumptionKind.TrustedBoundary),
            "summary.assumptions", "Summary assumption counts do not match the claim results.");
        validation.Check(Enum.IsDefined(typeof(WorkerCacheStatus), summary.CacheStatus) &&
                summary.CacheStatus != WorkerCacheStatus.Unspecified &&
                summary.CacheHit == (summary.CacheStatus == WorkerCacheStatus.Hit),
            "summary.cache", "Summary cache state is invalid.");
        validation.Check(summary.Versions != null && summary.Versions.ProtocolVersion == WorkerProtocolVersions.Current &&
                summary.Versions.ManifestSchemaVersion == WorkerManifestVersions.Current &&
                summary.Versions.CacheSchemaVersion == WorkerCacheVersions.Current &&
                !string.IsNullOrWhiteSpace(summary.Versions.WorkerVersion) &&
                !string.IsNullOrWhiteSpace(summary.Versions.ApiSpecVersion),
            "summary.versions", "Summary versions are invalid.");
        ValidateBudgets(summary.Budgets, "summary.budgets", validation);
        validation.Check(summary.ElapsedMilliseconds >= 0, "summary.elapsed", "Elapsed time cannot be negative.");
    }
    private static bool CountsMatch<TCount, TKind>(TCount[]? actual, IEnumerable<TKind> values, Func<TCount, TKind> kind,
        Func<TCount, int> count, TKind unspecified) where TCount : class where TKind : struct, Enum {
        var expected = values.GroupBy(static value => value).OrderBy(static group => group.Key)
            .Select(static group => (group.Key, Count: group.Count())).ToArray();
        return actual != null && actual.All(value => value != null && count(value) > 0 &&
                IsDefined(kind(value), unspecified)) && actual.Length == expected.Length &&
            actual.OrderBy(kind).Select(value => (kind(value), count(value))).SequenceEqual(expected);
    }
    private static WorkerProtocolError[] ValidateProtocolErrors(WorkerProtocolError[]? values, Validator validation) {
        if (values == null || values.Any(static value => value == null ||
                string.IsNullOrWhiteSpace(value.Code) || string.IsNullOrWhiteSpace(value.Message))) {
            validation.Add("response.errors", "Protocol errors must be complete.");
            return [];
        }
        return values;
    }
    private static void ValidateCompilation(WorkerCompilationOptions? options, Validator validation) {
        if (options == null) {
            validation.Add("compilation.null", "Compilation options are required.");
            return;
        }
        validation.Check(IsValidText(options.TargetFramework, 256),
            "compilation.target_framework", "A valid target framework identity is required.");
        validation.Check(IsValidText(options.LanguageVersion, 32),
            "compilation.language_version", "An explicit C# language version is required.");
        validation.Defined(options.NullableContext, WorkerNullableContext.Unspecified,
            "compilation.nullable", "An explicit nullable context is required.");
        validation.Defined(options.Optimization, WorkerOptimizationLevel.Unspecified,
            "compilation.optimization", "An explicit optimization level is required.");
        validation.Check(options.CheckOverflow.HasValue,
            "compilation.checked_overflow", "An explicit checked-overflow setting is required.");
        validation.Check(options.AllowUnsafe.HasValue, "compilation.allow_unsafe", "An explicit unsafe-code setting is required.");
        validation.Check(options.Deterministic.HasValue,
            "compilation.deterministic", "An explicit deterministic-build setting is required.");
        validation.Defined(options.OutputKind, WorkerOutputKind.Unspecified,
            "compilation.output_kind", "An explicit output kind is required.");
        validation.Defined(options.Platform, WorkerPlatform.Unspecified,
            "compilation.platform", "An explicit target platform is required.");
    }
    private static void ValidateBudgets(WorkerBudgets? budgets, string prefix, Validator validation) {
        if (budgets == null) {
            validation.Add(prefix + ".null", "Budgets are required.");
            return;
        }
        validation.Check(budgets.QueryRlimit > 0, prefix + ".rlimit", "Query rlimit must be positive.");
        validation.Check(budgets.MethodRlimit > 0, prefix + ".method_rlimit", "Method rlimit must be positive.");
        validation.Check(budgets.QueryRlimit <= budgets.MethodRlimit,
            prefix + ".rlimit_order", "Query rlimit cannot exceed method rlimit.");
        validation.Check(budgets.MethodWallTimeMilliseconds > 0, prefix + ".method_wall", "Method wall time must be positive.");
        validation.Check(budgets.ProjectWallTimeMilliseconds > 0,
            prefix + ".project_wall", "Project wall time must be positive.");
        validation.Check(budgets.MaxParallelism is >= 1 and <= WorkerBudgets.MaximumParallelism,
            prefix + ".parallelism", "Max parallelism must be between 1 and 4.");
        validation.Check(budgets.MaximumExpressionDepth is >= 1 and <= 256,
            prefix + ".expression_depth", "Expression depth must be between 1 and 256.");
        validation.Check(budgets.ProcessMemoryLimitBytes > 0, prefix + ".process_memory", "Process memory limit must be positive.");
        validation.Check(budgets.MaxWorkerProcesses is >= 1 and <= WorkerBudgets.MaximumParallelism,
            prefix + ".worker_processes", "Worker process count must be between 1 and 4.");
        validation.Check(budgets.MethodWallTimeMilliseconds <= budgets.ProjectWallTimeMilliseconds,
            prefix + ".wall_order", "Method wall time cannot exceed project wall time.");
    }
    private static void ValidateLocation(WorkerSourceLocation? location, string code, Validator validation) =>
        validation.Check(location != null && !string.IsNullOrWhiteSpace(location.Path) &&
                location.Start >= 0 && location.Length >= 0 && location.Line >= 1 && location.Column >= 1,
            code, "A complete source location is required.");
    private static void ValidateStrings(string[]? values, bool allowEmpty, string nullCode, string valueCode, Validator validation) {
        if (values == null || !allowEmpty && values.Length == 0)
            validation.Add(nullCode, "A non-null value collection is required.");
        else if (values.Any(string.IsNullOrWhiteSpace))
            validation.Add(valueCode, "Values cannot be blank.");
    }
    private static void ValidateUniqueIds(IEnumerable<string?> values, string code, Validator validation) {
        var array = values.ToArray();
        validation.Check(array.All(static value => !string.IsNullOrWhiteSpace(value)) &&
                array.Where(static value => value != null).Distinct(StringComparer.Ordinal).Count() == array.Length,
            code, "IDs must be nonblank and unique.");
    }
    private static void ValidateExactIds(IEnumerable<string?> actual, IEnumerable<string?> expected, string code, Validator validation) =>
        validation.Check(actual.OrderBy(static value => value, StringComparer.Ordinal)
                .SequenceEqual(expected.OrderBy(static value => value, StringComparer.Ordinal), StringComparer.Ordinal),
            code, "Result IDs must exactly match the manifest.");
    private static void ValidateEnumArray<T>(T[]? values, T unspecified, string code, Validator validation) where T : struct, Enum =>
        validation.Check(values != null && values.Length > 0 && values.All(value => IsDefined(value, unspecified)) &&
                values.Distinct().Count() == values.Length,
            code, "Typed selections must be nonempty and unique.");
    private static void ValidateAssumptions(WorkerAssumptionEvidence[]? values, string code, Validator validation) =>
        validation.Check(AreCompleteAndUnique(values,
                static value => !string.IsNullOrWhiteSpace(value.Id) &&
                    IsDefined(value.Kind, WorkerAssumptionKind.Unspecified), static value => value.Id),
            code, "Assumption evidence must be complete and unique.");
    private static bool AreCompleteAndUnique<T>(T[]? values, Func<T, bool> complete, Func<T, string?> key) where T : class =>
        values != null && values.All(value => value != null && complete(value)) &&
        values.Select(key).Distinct(StringComparer.Ordinal).Count() == values.Length;
    private static WorkerAssumptionEvidence[] CanonicalizeAssumptions(WorkerAssumptionEvidence[]? values) =>
        [.. (values ?? []).OrderBy(static value => value?.Kind).ThenBy(static value => value?.Id, StringComparer.Ordinal)];
    private static string CreateManifestPayload(WorkerClaimManifest manifest) {
        var builder = new StringBuilder();
        Append(builder, "SharpProof.Worker.ManifestHash"); Append(builder, 1);
        Append(builder, "manifest.schemaVersion"); Append(builder, manifest.SchemaVersion);
        Append(builder, "manifest.callables"); Append(builder, manifest.Callables?.Length ?? -1);
        foreach (var callable in (manifest.Callables ?? [])
                     .OrderBy(static value => value?.CallableId, StringComparer.Ordinal)) {
            Append(builder, "callable"); Append(builder, "callable.id"); Append(builder, callable?.CallableId);
            Append(builder, "callable.selectedFeatures"); Append(builder, callable?.SelectedFeatures?.Length ?? -1);
            foreach (var feature in (callable?.SelectedFeatures ?? []).OrderBy(static value => value))
                Append(builder, (int)feature);
            Append(builder, "callable.selectionReasons"); Append(builder, callable?.SelectionReasons?.Length ?? -1);
            foreach (var reason in (callable?.SelectionReasons ?? []).OrderBy(static value => value))
                Append(builder, (int)reason);
            Append(builder, "callable.location", callable?.Location);
            Append(builder, "callable.claimIds"); Append(builder, callable?.ClaimIds?.Length ?? -1);
            foreach (var claimId in (callable?.ClaimIds ?? []).OrderBy(static value => value, StringComparer.Ordinal))
                Append(builder, claimId);
        }
        Append(builder, "manifest.claims"); Append(builder, manifest.Claims?.Length ?? -1);
        foreach (var claim in (manifest.Claims ?? [])
                     .OrderBy(static value => value?.CallableId, StringComparer.Ordinal)
                     .ThenBy(static value => value?.Ordinal ?? int.MinValue)
                     .ThenBy(static value => value?.ClaimId, StringComparer.Ordinal)) {
            Append(builder, "claim");
            Append(builder, "claim.id"); Append(builder, claim?.ClaimId);
            Append(builder, "claim.callableId"); Append(builder, claim?.CallableId); Append(builder, "claim.ordinal");
            Append(builder, claim?.Ordinal ?? -1); Append(builder, "claim.kind");
            Append(builder, (int)(claim?.Kind ?? WorkerClaimKind.Unspecified)); Append(builder, "claim.evidence");
            Append(builder, (int)(claim?.Evidence ?? WorkerClaimEvidence.Unspecified));
            Append(builder, "claim.location", claim?.Location);
        }
        return builder.ToString();
    }
    private static void Append(StringBuilder builder, string domain, WorkerSourceLocation? value) {
        Append(builder, domain); Append(builder, value == null ? -1 : 5);
        Append(builder, "location.path"); Append(builder, value?.Path); Append(builder, "location.start");
        Append(builder, value?.Start ?? -1); Append(builder, "location.length"); Append(builder, value?.Length ?? -1);
        Append(builder, "location.line"); Append(builder, value?.Line ?? -1); Append(builder, "location.column");
        Append(builder, value?.Column ?? -1);
    }
    private static void Append(StringBuilder builder, int value) => Append(builder, value.ToString(CultureInfo.InvariantCulture));
    private static void Append(StringBuilder builder, string? value) {
        if (value == null) { builder.Append("-1:;"); return; }
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');
    }
    private static int FindClaimOrdinal(WorkerClaimManifest? manifest, string? id) =>
        manifest?.Claims?.FirstOrDefault(claim => claim != null && claim.ClaimId == id)?.Ordinal ?? int.MaxValue;
    private static string FindClaimCallableId(WorkerClaimManifest? manifest, string? id) =>
        manifest?.Claims?.FirstOrDefault(claim => claim != null && claim.ClaimId == id)?.CallableId ?? string.Empty;
    private static bool IsSha256(string? value) => value is { Length: 64 } &&
        value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool IsValidText(string? value, int maximumLength) =>
        value != null && !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength && !value.Any(char.IsControl);
    private static bool IsDefined<T>(T value, T unspecified) where T : struct, Enum =>
        Enum.IsDefined(typeof(T), value) && !EqualityComparer<T>.Default.Equals(value, unspecified);
    private static void EnsureRootProperties(string json, IEnumerable<string> required) {
        if (json == null) throw new ArgumentNullException(nameof(json));
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("A JSON object is required.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            if (!names.Add(property.Name))
                throw new JsonException("Duplicate JSON properties are not permitted.");
        if (required.Any(name => !names.Contains(name)))
            throw new JsonException("A required JSON property is missing.");
    }
    private static JsonSerializerOptions CreateOptions() {
        var options = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        return options;
    }
    private sealed class Validator {
        private readonly ErrorBuilder _errors = ImmutableArray.CreateBuilder<WorkerProtocolError>();
        internal int Count => _errors.Count;
        internal WorkerProtocolValidationResult Result => new(_errors);
        internal void Add(string code, string message) =>
            _errors.Add(new WorkerProtocolError { Code = code, Message = message });
        internal void Check(bool valid, string code, string message) {
            if (!valid) Add(code, message);
        }
        internal void Defined<T>(
            T value, T unspecified, string code, string message) where T : struct, Enum =>
            Check(IsDefined(value, unspecified), code, message);
    }
}
