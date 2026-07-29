namespace SharpProof.Worker.Protocol;

internal static class WorkerResultAssembler
{
    internal const string EmptyInputHash = WorkerProtocolVersions.EmptySha256;
    internal static WorkerVerifyResponse Create(
        string inputHash, WorkerClaimManifest manifest, WorkerRunStatus runStatus, WorkerRunFailureReason failureReason,
        IEnumerable<WorkerCallableResult> callableResults, IEnumerable<WorkerClaimResult> claimResults,
        WorkerBudgets budgets, WorkerCacheStatus cacheStatus, long elapsedMilliseconds,
        IEnumerable<WorkerProtocolError>? errors = null, string? requestHash = null, WorkerVersionSummary? versions = null)
    {
        var callables = callableResults.ToArray();
        var claims = claimResults.ToArray();
        var response = new WorkerVerifyResponse
        {
            RequestHash = requestHash ?? EmptyInputHash,
            InputHash = inputHash,
            Manifest = manifest,
            RunStatus = runStatus,
            FailureReason = failureReason,
            CallableResults = callables,
            ClaimResults = claims,
            Summary = new WorkerVerificationSummary
            {
                CallableCount = manifest.Callables.Length,
                ClaimCount = manifest.Claims.Length,
                OutcomeCounts = [.. claims.GroupBy(static claim => claim.Outcome)
                    .Select(static group => new WorkerClaimOutcomeCount { Outcome = group.Key, Count = group.Count() })],
                ReasonCounts = [.. claims.GroupBy(static claim => claim.Reason)
                    .Select(static group => new WorkerClaimReasonCount { Reason = group.Key, Count = group.Count() })],
                Assumptions = SummarizeAssumptions(callables, claims, out _),
                CacheHit = cacheStatus == WorkerCacheStatus.Hit,
                CacheStatus = cacheStatus,
                Versions = versions ?? new WorkerVersionSummary { WorkerVersion = "unavailable", ApiSpecVersion = "unavailable" },
                Budgets = budgets,
                ElapsedMilliseconds = Math.Max(0, elapsedMilliseconds)
            },
            Errors = errors?.ToArray() ?? []
        };
        WorkerProtocolJson.Canonicalize(response);
        return response;
    }

    internal static WorkerVerifyResponse CreateIncomplete(
        string inputHash, string requestHash, WorkerClaimManifest manifest, WorkerBudgets budgets,
        WorkerRunStatus status, WorkerRunFailureReason failureReason, WorkerCallableCoverageReason callableReason,
        WorkerClaimReason claimReason, IEnumerable<WorkerProtocolError>? errors = null,
        WorkerVersionSummary? versions = null, long elapsedMilliseconds = 0)
    {
        return Create(inputHash, manifest, status, failureReason,
            manifest.Callables.Select(callable => new WorkerCallableResult
            {
                CallableId = callable.CallableId,
                Coverage = WorkerCallableCoverage.Incomplete,
                Reason = callableReason,
                Assumptions = callable.Assumptions
            }),
            manifest.Claims.Select(claim => new WorkerClaimResult
            {
                ClaimId = claim.ClaimId,
                Outcome = WorkerClaimOutcome.Unknown,
                Reason = claimReason,
                EffectCertainty = claim.Kind == WorkerClaimKind.Effect
                    ? WorkerEffectEvidenceCertainty.Unavailable
                    : WorkerEffectEvidenceCertainty.Unspecified,
                Assumptions = manifest.Callables.First(callable =>
                    callable.CallableId == claim.CallableId).Assumptions
            }),
            budgets, WorkerCacheStatus.Disabled, elapsedMilliseconds, errors, requestHash, versions);
    }

    internal static WorkerAssumptionSummary SummarizeAssumptions(WorkerCallableResult[] callables, WorkerClaimResult[] claims,
        out bool conflictingKinds)
    {
        var assumptions = callables.SelectMany(static callable => callable.Assumptions ?? [])
            .Concat(claims.SelectMany(static claim => claim.Assumptions ?? []))
            .Where(static value => value != null && !string.IsNullOrWhiteSpace(value.Id))
            .GroupBy(static value => value.Id, StringComparer.Ordinal).ToArray();
        conflictingKinds = assumptions.Any(static group => group.Select(static value => value.Kind).Distinct().Count() != 1);
        return new WorkerAssumptionSummary
        {
            Total = assumptions.Length,
            Used = assumptions.Count(static group => group.Any(static value => value.Used)),
            User = assumptions.Count(static group => group.First().Kind == WorkerAssumptionKind.UserAssume),
            Trusted = assumptions.Count(static group => group.First().Kind == WorkerAssumptionKind.TrustedBoundary)
        };
    }

    internal static WorkerClaimManifest EmptyManifest()
    {
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);
        return manifest;
    }

    internal static (WorkerRunStatus Status, WorkerRunFailureReason Failure,
        bool FatalCallable, bool FatalClaim, bool TimedOut, bool Canceled) Classify(
        IEnumerable<WorkerCallableResult>? callables, IEnumerable<WorkerClaimResult>? claims)
    {
        var callableReasons = callables?.Where(static result => result != null)
            .Select(static result => result.Reason).ToArray() ?? [];
        var claimReasons = claims?.Where(static result => result != null)
            .Select(static result => result.Reason).ToArray() ?? [];
        var callableFailure = callableReasons.Contains(WorkerCallableCoverageReason.InfrastructureFailure)
            ? WorkerRunFailureReason.InfrastructureFailure
            : callableReasons.Contains(WorkerCallableCoverageReason.MissingClaimResult)
                ? WorkerRunFailureReason.MalformedResult
                : WorkerRunFailureReason.None;
        var claimFailure = claimReasons.Contains(WorkerClaimReason.BackendUnavailable)
            ? WorkerRunFailureReason.BackendUnavailable
            : claimReasons.Contains(WorkerClaimReason.InfrastructureFailure)
                ? WorkerRunFailureReason.InfrastructureFailure
                : claimReasons.Contains(WorkerClaimReason.MalformedBackendResult)
                    ? WorkerRunFailureReason.MalformedResult
                    : claimReasons.Contains(WorkerClaimReason.CounterexampleReplayFailed)
                        ? WorkerRunFailureReason.CounterexampleReplayFailed
                        : WorkerRunFailureReason.None;
        var failure = callableFailure != WorkerRunFailureReason.None ? callableFailure : claimFailure;
        var canceled = callableReasons.Contains(WorkerCallableCoverageReason.Canceled) ||
            claimReasons.Contains(WorkerClaimReason.Canceled);
        var timedOut = callableReasons.Any(static reason => reason is
                WorkerCallableCoverageReason.MethodTimeout or WorkerCallableCoverageReason.ProjectTimeout) ||
            claimReasons.Any(static reason => reason is WorkerClaimReason.MethodTimeout or WorkerClaimReason.ProjectTimeout);
        var status = failure != WorkerRunFailureReason.None ? WorkerRunStatus.Failed
            : canceled ? WorkerRunStatus.Canceled
            : timedOut ? WorkerRunStatus.TimedOut
            : WorkerRunStatus.Complete;
        return (status, failure,
            callableFailure != WorkerRunFailureReason.None, claimFailure != WorkerRunFailureReason.None,
            timedOut, canceled);
    }
}
