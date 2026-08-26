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
        WorkerVersionSummary? versions = null, long elapsedMilliseconds = 0,
        WorkerCacheStatus cacheStatus = WorkerCacheStatus.Disabled)
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
                // This runs on the failure path, where the manifest may already be
                // malformed. A claim naming an absent callable must not turn a
                // reported failure into an unhandled exception.
                Assumptions = manifest.Callables.FirstOrDefault(callable =>
                    callable.CallableId == claim.CallableId)?.Assumptions ?? []
            }),
            budgets, cacheStatus, elapsedMilliseconds, errors, requestHash, versions);
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
        var callableReasons = callables?.OfType<WorkerCallableResult>()
            .Select(static result => result.Reason).ToArray() ?? [];
        var claimReasons = claims?.OfType<WorkerClaimResult>()
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
        var failure = claimFailure == WorkerRunFailureReason.BackendUnavailable
            ? claimFailure
            : callableFailure != WorkerRunFailureReason.None
                ? callableFailure
                : claimFailure;
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

    internal static bool TryProjectRunState(
        IEnumerable<WorkerCallableResult>? callables,
        IEnumerable<WorkerClaimResult>? claims,
        IEnumerable<WorkerProtocolError>? errors,
        out WorkerRunStatus status,
        out WorkerRunFailureReason failure)
    {
        var evidence = Classify(callables, claims);
        var errorStates = (errors ?? [])
            .Select(static error => ProjectError(error.Code))
            .ToArray();
        if (errorStates.Any(static state => state == null) ||
            errorStates.Select(static state => state!.Value).Distinct().Count() > 1)
        {
            status = WorkerRunStatus.Unspecified;
            failure = WorkerRunFailureReason.Unspecified;
            return false;
        }

        if (errorStates.Length != 0)
        {
            (status, failure) = errorStates[0]!.Value;
            return true;
        }

        status = evidence.Status;
        failure = evidence.Failure;
        return true;
    }

    internal static bool MatchesCallableProjection(
        WorkerCallableResult callable,
        WorkerClaimManifest manifest,
        IEnumerable<WorkerClaimResult> claims,
        WorkerRunStatus runStatus,
        WorkerRunFailureReason failureReason,
        bool hasErrors,
        IReadOnlyDictionary<string, WorkerClaimResult[]>? claimsByCallable = null)
    {
        var ownedIds = manifest.Callables.FirstOrDefault(entry =>
            entry.CallableId == callable.CallableId)?.ClaimIds ?? [];
        var owned = claimsByCallable != null
            ? claimsByCallable.TryGetValue(callable.CallableId, out var indexed)
                ? indexed
                : []
            : claims.Where(claim =>
                ownedIds.Contains(claim.ClaimId, StringComparer.Ordinal)).ToArray();
        WorkerCallableCoverageReason expected;
        if (runStatus == WorkerRunStatus.Failed && hasErrors)
        {
            expected = failureReason == WorkerRunFailureReason.MalformedResult
                ? WorkerCallableCoverageReason.MissingClaimResult
                : WorkerCallableCoverageReason.InfrastructureFailure;
        }
        else if (owned.Length == 0)
        {
            expected = runStatus switch
            {
                WorkerRunStatus.Canceled => WorkerCallableCoverageReason.Canceled,
                WorkerRunStatus.TimedOut => WorkerCallableCoverageReason.ProjectTimeout,
                _ => WorkerCallableCoverageReason.None
            };
        }
        else if (owned.All(static claim => claim.Outcome != WorkerClaimOutcome.Unknown))
        {
            expected = WorkerCallableCoverageReason.None;
        }
        else
        {
            var reasons = owned.Where(static claim =>
                    claim.Outcome == WorkerClaimOutcome.Unknown)
                .Select(static claim => claim.Reason)
                .ToArray();
            expected = reasons.All(static reason =>
                    reason == WorkerClaimReason.UnsupportedCallable)
                ? WorkerCallableCoverageReason.UnsupportedCallable
                : reasons.Any(static reason =>
                    reason == WorkerClaimReason.MethodTimeout)
                    ? WorkerCallableCoverageReason.MethodTimeout
                    : reasons.Any(static reason =>
                        reason == WorkerClaimReason.ProjectTimeout)
                        ? WorkerCallableCoverageReason.ProjectTimeout
                        : reasons.Any(static reason =>
                            reason == WorkerClaimReason.Canceled)
                            ? WorkerCallableCoverageReason.Canceled
                            : WorkerCallableCoverageReason.SemanticUnknown;
        }

        var matchesExpected = callable.Coverage ==
                (expected == WorkerCallableCoverageReason.None
                    ? WorkerCallableCoverage.Complete
                    : WorkerCallableCoverage.Incomplete) &&
            callable.Reason == expected;
        var directInfrastructureFailure =
            owned.Length != 0 &&
            owned.All(static claim =>
                claim.Outcome == WorkerClaimOutcome.Unknown &&
                claim.Reason is WorkerClaimReason.InfrastructureFailure or
                    WorkerClaimReason.BackendUnavailable) &&
            callable.Coverage == WorkerCallableCoverage.Incomplete &&
            callable.Reason == WorkerCallableCoverageReason.InfrastructureFailure;
        return matchesExpected || directInfrastructureFailure;
    }

    private static (WorkerRunStatus Status, WorkerRunFailureReason Failure)?
        ProjectError(string code)
    {
        if (code is "worker.timeout")
        {
            return (WorkerRunStatus.TimedOut, WorkerRunFailureReason.None);
        }
        if (code is "worker.canceled")
        {
            return (WorkerRunStatus.Canceled, WorkerRunFailureReason.None);
        }
        if (code is "request.malformed" or "launcher.timeout_overflow" ||
            HasPrefix(code, "protocol.") || HasPrefix(code, "project.") ||
            HasPrefix(code, "budgets.") || HasPrefix(code, "cache.") ||
            HasPrefix(code, "policy.") || code == "request.null")
        {
            return (WorkerRunStatus.Failed, WorkerRunFailureReason.InvalidRequest);
        }
        if (code is "compiler_manifest.unavailable" or "input.unavailable")
        {
            return (WorkerRunStatus.Failed, WorkerRunFailureReason.InputUnavailable);
        }
        if (WorkerProtocolJson.IsCompilerDiagnosticCode(code))
        {
            return (WorkerRunStatus.Failed, WorkerRunFailureReason.CompilationFailure);
        }
        if (code is "compiler_manifest.invalid" or "compiler_manifest.options" or
            "compiler_manifest.lowered_ir")
        {
            return (WorkerRunStatus.Failed, WorkerRunFailureReason.CompilerManifestMismatch);
        }
        if (code is "backend.unavailable")
        {
            return (WorkerRunStatus.Failed, WorkerRunFailureReason.BackendUnavailable);
        }
        if (code is "worker.infrastructure" or "launcher.infrastructure" or
            "worker.no_result")
        {
            return (WorkerRunStatus.Failed, WorkerRunFailureReason.InfrastructureFailure);
        }
        if (code is "worker.malformed_result" ||
            HasPrefix(code, "response.") || HasPrefix(code, "summary.") ||
            HasPrefix(code, "manifest."))
        {
            return (WorkerRunStatus.Failed, WorkerRunFailureReason.MalformedResult);
        }
        if (code is "containment.unsupported" or "containment.unavailable")
        {
            return (WorkerRunStatus.Failed, WorkerRunFailureReason.ContainmentFailure);
        }

        return null;
    }

    private static bool HasPrefix(string value, string prefix)
    {
        return value.StartsWith(prefix, StringComparison.Ordinal);
    }

}
