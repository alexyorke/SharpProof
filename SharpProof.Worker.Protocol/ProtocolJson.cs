using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SharpProof.Worker.Protocol;

public static partial class WorkerProtocolJson
{
    private static readonly JsonSerializerOptions s_options = CreateOptions();

    public static JsonSerializerOptions Options => new(s_options);
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
        return JsonSerializer.Serialize(request ?? throw new ArgumentNullException(nameof(request)), s_options);
    }

    public static string ComputeRequestHash(WorkerVerifyRequest request)
    {
        return ComputeSha256(Encoding.UTF8.GetBytes(SerializeRequest(request)));
    }

    public static string SerializeResponse(WorkerVerifyResponse response)
    {
        Canonicalize(response ?? throw new ArgumentNullException(nameof(response)));
        return JsonSerializer.Serialize(response, s_options);
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
        return ValidateResponse(response, null, null, null, null);
    }

    public static WorkerProtocolValidationResult Validate(
        WorkerVerifyResponse? response, string expectedInputHash, WorkerClaimManifest? expectedManifest = null)
    {
        RequireSha256(expectedInputHash, nameof(expectedInputHash), "input");
        return ValidateResponse(response, expectedInputHash, expectedManifest, null, null);
    }
    public static WorkerProtocolValidationResult ValidateForRequest(
        WorkerVerifyResponse? response, string expectedRequestHash, string expectedInputHash,
        WorkerClaimManifest expectedManifest, WorkerBudgets expectedBudgets)
    {
        RequireSha256(expectedRequestHash, nameof(expectedRequestHash), "request");
        RequireSha256(expectedInputHash, nameof(expectedInputHash), "input");
        _ = expectedBudgets ?? throw new ArgumentNullException(nameof(expectedBudgets));
        return ValidateResponse(response, expectedInputHash, expectedManifest, expectedRequestHash, expectedBudgets);
    }

    public static void Canonicalize(WorkerVerifyResponse response)
    {
        _ = response ?? throw new ArgumentNullException(nameof(response));
        if (response.Manifest != null)
        {
            Canonicalize(response.Manifest);
        }

        response.CallableResults = SortOrdinal(response.CallableResults, static value => value?.CallableId);
        foreach (var result in response.CallableResults.Where(static value => value != null))
        {
            result.Assumptions = CanonicalizeAssumptions(result.Assumptions);
        }

        response.ClaimResults = [.. (response.ClaimResults ?? [])
            .OrderBy(value => FindClaimCallableId(response.Manifest, value?.ClaimId), StringComparer.Ordinal)
            .ThenBy(value => FindClaimOrdinal(response.Manifest, value?.ClaimId))
            .ThenBy(static value => value?.ClaimId, StringComparer.Ordinal)];
        foreach (var result in response.ClaimResults.Where(static value => value != null))
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
            .OrderBy(static value => value?.Code, StringComparer.Ordinal)
            .ThenBy(static value => value?.Message, StringComparer.Ordinal)];
    }
    private static void Canonicalize(WorkerClaimResult result)
    {
        result.ProofCore = SortOrdinal(result.ProofCore, static value => value);
        result.Model = [.. (result.Model ?? [])
            .OrderBy(static value => value?.Variable, StringComparer.Ordinal)
            .ThenBy(static value => value?.Kind, StringComparer.Ordinal)
            .ThenBy(static value => value?.Value, StringComparer.Ordinal)];
        result.Assumptions = CanonicalizeAssumptions(result.Assumptions);
        if (result.EffectWitness != null)
        {
            result.EffectWitness.ExactExceptionTypeHierarchy = SortOrdinal(
                result.EffectWitness.ExactExceptionTypeHierarchy, static value => value);
        }
    }

    private static WorkerProtocolValidationResult ValidateResponse(
        WorkerVerifyResponse? response, string? expectedInputHash, WorkerClaimManifest? expectedManifest,
        string? expectedRequestHash, WorkerBudgets? expectedBudgets)
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

        ValidateManifestCore(response.Manifest, "manifest", errors);
        ValidateExpectedManifest(response.Manifest, expectedManifest, errors);
        var protocolErrors = ValidateProtocolErrors(response.Errors, errors);
        ValidateRun(response, protocolErrors, errors);
        var callables = ValidateCallableResults(response.CallableResults, response.Manifest, errors);
        var claims = ValidateClaimResults(response.ClaimResults, response.Manifest, errors);
        ValidateUnknownCoverage(callables, claims, response.Manifest, errors);
        ValidateSummary(response.Summary, callables, claims, errors);
        if (expectedBudgets != null)
        {
            errors.Check(response.Summary?.Budgets != null &&
                JsonSerializer.Serialize(response.Summary.Budgets, s_options) ==
                JsonSerializer.Serialize(expectedBudgets, s_options), "response.budgets_mismatch");
        }

        return errors.Result;
    }
    private static void ValidateExpectedManifest(
        WorkerClaimManifest? actual, WorkerClaimManifest? expected, Validator errors)
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
        else if (actual != null)
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
            StringComparer.Ordinal);
        foreach (var callable in callables)
        {
            errors.Check(HasValidLocation(callable.Location), prefix + ".callable_location")
                .Rules(callable, WorkerProtocolMetadata.ManifestCallableRules, prefix + ".");
            ValidateClaimMembership(callable, claims, prefix, errors);
        }
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
            .ThenBy(static value => value.ClaimId, StringComparer.Ordinal).ToArray();
        errors.Check(expected.Select(static value => value.Ordinal)
                .SequenceEqual(Enumerable.Range(0, expected.Length)), prefix + ".dense_ordinals")
            .Check(callable.ClaimIds != null && callable.ClaimIds.SequenceEqual(
                expected.Select(static value => value.ClaimId), StringComparer.Ordinal),
                prefix + ".claim_membership");
    }
    private static WorkerCallableResult[] ValidateCallableResults(WorkerCallableResult[]? values,
        WorkerClaimManifest? manifest, Validator errors)
    {
        var valid = ValidateResultSet(values,
            manifest?.Callables?.Where(static value => value != null)
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
            manifest?.Claims?.Where(static value => value != null)
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
        errors.Check(effectClaim
                ? HasValidEffectCertainty(value.Outcome, value.Reason, value.EffectCertainty)
                : value.EffectCertainty == WorkerEffectEvidenceCertainty.Unspecified,
            "response.effect_certainty")
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
            .GroupBy(static value => value.ClaimId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().CallableId,
                StringComparer.Ordinal);
        var incomplete = new HashSet<string>(
            callables.Where(static value => value.Coverage == WorkerCallableCoverage.Incomplete)
                .Select(static value => value.CallableId),
            StringComparer.Ordinal);
        errors.Check(!claims.Any(value => value.Outcome == WorkerClaimOutcome.Unknown &&
            owners.TryGetValue(value.ClaimId, out var owner) && !incomplete.Contains(owner)),
            "response.unknown_coverage");
    }
    private static void ValidateRun(WorkerVerifyResponse response, WorkerProtocolError[] protocolErrors, Validator errors)
    {
        var failed = response.RunStatus == WorkerRunStatus.Failed;
        errors.Defined(response.RunStatus, WorkerRunStatus.Unspecified, "response.run_status")
            .Check(WorkerProtocolMetadata.MatchesRunFailure(
                response.RunStatus, response.FailureReason) &&
                (protocolErrors.Length == 0 || failed), "response.run_failure");
        var evidence = WorkerResultAssembler.Classify(response.CallableResults, response.ClaimResults);
        errors.Check(!evidence.FatalClaim || failed, "response.fatal_claim")
            .Check(!evidence.FatalCallable || failed, "response.fatal_callable")
            .Check(!evidence.TimedOut ||
                response.RunStatus is WorkerRunStatus.TimedOut or WorkerRunStatus.Failed,
                "response.timeout_status")
            .Check(!evidence.Canceled ||
                response.RunStatus is WorkerRunStatus.Canceled or WorkerRunStatus.Failed,
                "response.canceled_status");
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
        return actual != null && actual.Total == expected.Total && actual.Used == expected.Used &&
        actual.User == expected.User && actual.Trusted == expected.Trusted;
    }

    private static bool CountsMatch<TCount, TKind>(TCount[]? actual, IEnumerable<TKind> values,
        Func<TCount, TKind> kind, Func<TCount, int> count, TKind unspecified)
        where TCount : class where TKind : struct, Enum
    {
        var expected = values.GroupBy(static value => value)
            .ToDictionary(static group => group.Key, static group => group.Count());
        return actual != null &&
            actual.All(value => value != null && count(value) > 0 &&
                IsDefined(kind(value), unspecified)) &&
            actual.Length == expected.Count &&
            actual.All(value => expected.TryGetValue(kind(value), out var expectedCount) &&
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

    internal static bool HasKnownEffects(WorkerEffectSet effects, WorkerEffectCapabilitySet capabilities)
    {
        return WorkerProtocolMetadata.HasOnlyKnownFlags(effects) &&
            WorkerProtocolMetadata.HasOnlyKnownFlags(capabilities);
    }

    internal static bool AreDistinctNonblank(string[]? values)
    {
        return CompleteUnique(values, static value => !string.IsNullOrWhiteSpace(value), static value => value);
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
            items.Distinct(StringComparer.Ordinal).Count() == items.Length, code);
    }
    private static void ValidateExactIds(IEnumerable<string?> actual, IEnumerable<string?> expected, string code, Validator errors)
    {
        errors.Check(actual.OrderBy(static value => value, StringComparer.Ordinal)
            .SequenceEqual(expected.OrderBy(static value => value, StringComparer.Ordinal),
                StringComparer.Ordinal), code);
    }

    private static bool CompleteUnique<T>(T[]? values, Func<T, bool> complete, Func<T, string?> key) where T : class
    {
        return values != null && values.All(value => value != null && complete(value)) &&
            values.Select(key).Distinct(StringComparer.Ordinal).Count() == values.Length;
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
            .ThenBy(static value => value?.Id, StringComparer.Ordinal)];
    }

    private static T[] SortManifestEnums<T>(T[]? values) where T : struct, Enum
    {
        return [.. (values ?? []).OrderBy(ManifestName, StringComparer.Ordinal)];
    }

    private static T[] SortOrdinal<T>(T[]? values, Func<T, string?> identity)
    {
        return [.. (values ?? []).OrderBy(identity, StringComparer.Ordinal)];
    }

    private static bool SameAssumptionDeclarations(
            WorkerAssumptionEvidence[]? actual, WorkerAssumptionEvidence[]? expected)
    {
        return (actual ?? []).Where(static value => value != null)
                .OrderBy(static value => value.Id, StringComparer.Ordinal)
                .Select(static value => (value.Id, value.Kind))
                .SequenceEqual((expected ?? []).Where(static value => value != null)
                    .OrderBy(static value => value.Id, StringComparer.Ordinal)
                    .Select(static value => (value.Id, value.Kind)));
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
        EnsureRootProperties(json, requiredProperties);
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
