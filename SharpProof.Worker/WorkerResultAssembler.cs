namespace SharpProof.Worker;
internal static class WorkerResultAssembler {
    internal const string EmptyInputHash = "e3b0c44298fc1c149afbf4c8996fb924" +
        "27ae41e4649b934ca495991b7852b855";

    internal static WorkerVerifyResponse Create(
        string inputHash,
        WorkerClaimManifest manifest,
        WorkerRunStatus runStatus,
        WorkerRunFailureReason failureReason,
        IEnumerable<WorkerCallableResult> callableResults,
        IEnumerable<WorkerClaimResult> claimResults,
        WorkerBudgets budgets,
        WorkerCacheStatus cacheStatus,
        long elapsedMilliseconds,
        IEnumerable<WorkerProtocolError>? errors = null) {
        var callables = callableResults.ToArray();
        var claims = claimResults.ToArray();
        var assumptions = callables.SelectMany(static callable => callable.Assumptions ?? [])
            .Concat(claims.SelectMany(static claim => claim.Assumptions ?? []))
            .Where(static evidence => evidence != null && !string.IsNullOrWhiteSpace(evidence.Id))
            .GroupBy(static evidence => evidence.Id, StringComparer.Ordinal)
            .Select(static group => new WorkerAssumptionEvidence {
                Id = group.Key,
                Kind = group.Select(static item => item.Kind).First(),
                Used = group.Any(static item => item.Used)
            })
            .OrderBy(static evidence => evidence.Id, StringComparer.Ordinal)
            .ToArray();
        var response = new WorkerVerifyResponse {
            ProtocolVersion = WorkerProtocolVersions.Current,
            InputHash = inputHash,
            Manifest = manifest,
            RunStatus = runStatus,
            FailureReason = failureReason,
            CallableResults = callables,
            ClaimResults = claims,
            Summary = new WorkerVerificationSummary {
                CallableCount = manifest.Callables.Length,
                ClaimCount = manifest.Claims.Length,
                OutcomeCounts = [.. claims
                    .GroupBy(static claim => claim.Outcome)
                    .OrderBy(static group => group.Key)
                    .Select(static group => new WorkerClaimOutcomeCount { Outcome = group.Key, Count = group.Count() })],
                ReasonCounts = [.. claims
                    .GroupBy(static claim => claim.Reason)
                    .OrderBy(static group => group.Key)
                    .Select(static group => new WorkerClaimReasonCount { Reason = group.Key, Count = group.Count() })],
                Assumptions = new WorkerAssumptionSummary {
                    Total = assumptions.Length,
                    Used = assumptions.Count(static item => item.Used),
                    User = assumptions.Count(static item => item.Kind == WorkerAssumptionKind.UserAssume),
                    Trusted = assumptions.Count(static item => item.Kind == WorkerAssumptionKind.TrustedBoundary)
                },
                CacheHit = cacheStatus == WorkerCacheStatus.Hit,
                CacheStatus = cacheStatus,
                Versions = new WorkerVersionSummary {
                    WorkerVersion = WorkerCacheIdentity.Current.ToolVersion,
                    ApiSpecVersion = WorkerCacheIdentity.Current.ApiSpecVersion
                },
                Budgets = budgets,
                ElapsedMilliseconds = Math.Max(0, elapsedMilliseconds)
            },
            Errors = errors?.ToArray() ?? []
        };
        WorkerProtocolJson.Canonicalize(response);
        return response;
    }

    internal static WorkerClaimManifest EmptyManifest() {
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.Canonicalize(manifest);
        manifest.Hash = WorkerProtocolJson.ComputeManifestHash(manifest);
        return manifest;
    }
}
