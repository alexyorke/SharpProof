namespace SharpProof.Worker;
internal sealed record CacheableWorkerResponse(
    string InputHash,
    string Payload,
    WorkerCallableResult[] CallableResults,
    WorkerClaimResult[] ClaimResults) {

    internal static bool TryCreate(
        WorkerVerifyResponse? response,
        string expectedInputHash,
        WorkerClaimManifest expectedManifest,
        [NotNullWhen(true)]
        out CacheableWorkerResponse? cacheable) {
        cacheable = null;
        if (response == null ||
            response.RunStatus != WorkerRunStatus.Complete ||
            response.Errors is not { Length: 0 } ||
            response.CallableResults.Any(static result => result.Coverage != WorkerCallableCoverage.Complete ||
                result.Reason != WorkerCallableCoverageReason.None) ||
            response.ClaimResults.Any(static result => result.Outcome is not
                (WorkerClaimOutcome.Proven or WorkerClaimOutcome.Refuted)) ||
            !WorkerProtocolJson.Validate(response, expectedInputHash, expectedManifest).IsValid)
            return false;
        var payload = JsonSerializer.Serialize(new CachePayload(
            expectedManifest.Hash, response.CallableResults, response.ClaimResults), WorkerProtocolJson.Options);
        cacheable = new CacheableWorkerResponse(
            expectedInputHash,
            payload,
            response.CallableResults,
            response.ClaimResults);
        return true;
    }

    internal static bool TryParse(
        string? payload,
        string expectedInputHash,
        WorkerClaimManifest expectedManifest,
        WorkerBudgets budgets,
        [NotNullWhen(true)]
        out CacheableWorkerResponse? cacheable) {
        cacheable = null;
        CachePayload? decoded;
        try {
            decoded = JsonSerializer.Deserialize<CachePayload>(payload ?? string.Empty, WorkerProtocolJson.Options);
        }
        catch (JsonException) {
            return false;
        }
        if (decoded == null ||
            !string.Equals(decoded.ManifestHash, expectedManifest.Hash, StringComparison.Ordinal) ||
            decoded.CallableResults == null ||
            decoded.ClaimResults == null)
            return false;
        var response = WorkerResultAssembler.Create(
            expectedInputHash,
            expectedManifest,
            WorkerRunStatus.Complete,
            WorkerRunFailureReason.None,
            decoded.CallableResults,
            decoded.ClaimResults,
            budgets,
            WorkerCacheStatus.Hit,
            0);
        return TryCreate(
            response,
            expectedInputHash,
            expectedManifest,
            out cacheable);
    }

    private sealed record CachePayload(
        string ManifestHash,
        WorkerCallableResult[] CallableResults,
        WorkerClaimResult[] ClaimResults);
}
