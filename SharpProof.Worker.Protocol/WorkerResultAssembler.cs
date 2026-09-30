namespace SharpProof.Worker.Protocol;

internal static class WorkerResultAssembler
{
    internal const string EmptyInputHash = WorkerProtocolVersions.EmptySha256;

    internal static WorkerVerifyResponse ApplyRequestContext(
        WorkerVerifyResponse response,
        string requestHash,
        WorkerBudgets budgets,
        WorkerVersionSummary versions,
        WorkerCacheStatus cacheStatus,
        long elapsedMilliseconds)
    {
        ArgumentNullGuard.NotNull(response, nameof(response));
        ArgumentNullGuard.NotNull(requestHash, nameof(requestHash));
        ArgumentNullGuard.NotNull(budgets, nameof(budgets));
        ArgumentNullGuard.NotNull(versions, nameof(versions));
        response.RequestHash = requestHash;
        response.Summary.CacheStatus = cacheStatus;
        response.Summary.CacheHit = cacheStatus == WorkerCacheStatus.Hit;
        response.Summary.Budgets = CloneBudgets(budgets);
        response.Summary.Versions = versions;
        response.Summary.ElapsedMilliseconds = Math.Max(0, elapsedMilliseconds);
        return response;
    }

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
                CacheHit = cacheStatus == WorkerCacheStatus.Hit,
                CacheStatus = cacheStatus,
                Versions = versions ?? new WorkerVersionSummary { WorkerVersion = "unavailable", ApiSpecVersion = "unavailable" },
                Budgets = CloneBudgets(budgets),
                ElapsedMilliseconds = Math.Max(0, elapsedMilliseconds)
            },
            Errors = errors?.ToArray() ?? []
        };
        WorkerProtocolJson.Canonicalize(response);
        return response;
    }

    private static WorkerBudgets CloneBudgets(WorkerBudgets value)
    {
        ArgumentNullGuard.NotNull(value, nameof(value));

        return new WorkerBudgets
        {
            QueryRlimit = value.QueryRlimit,
            MethodRlimit = value.MethodRlimit,
            MethodWallTimeMilliseconds = value.MethodWallTimeMilliseconds,
            ProjectWallTimeMilliseconds = value.ProjectWallTimeMilliseconds,
            MaxParallelism = value.MaxParallelism,
            MaximumExpressionDepth = value.MaximumExpressionDepth
        };
    }

    internal static WorkerVerifyResponse CreateIncomplete(
        string inputHash, string requestHash, WorkerClaimManifest manifest, WorkerBudgets budgets,
        WorkerRunStatus status, WorkerRunFailureReason failureReason, WorkerCallableCoverageReason callableReason,
        WorkerClaimReason claimReason, IEnumerable<WorkerProtocolError>? errors = null,
        WorkerVersionSummary? versions = null, long elapsedMilliseconds = 0)
    {
        var callables = (manifest.Callables ?? [])
            .OfType<WorkerCallableManifestEntry>()
            .ToArray();
        var claims = (manifest.Claims ?? [])
            .OfType<WorkerClaimManifestEntry>()
            .ToArray();
        var runErrors = errors?.ToArray() ?? [];
        if (callables.Length == 0 && claims.Length == 0 && runErrors.Length == 0 &&
            failureReason == WorkerRunFailureReason.None &&
            status is WorkerRunStatus.TimedOut or WorkerRunStatus.Canceled)
        {
            runErrors = [new WorkerProtocolError
            {
                Code = status == WorkerRunStatus.TimedOut ? WorkerProtocolErrorCodes.WorkerTimeout : "worker.canceled",
                Message = status == WorkerRunStatus.TimedOut
                    ? "The project timed out before selected callable evidence was published."
                    : "The worker was canceled before selected callable evidence was published."
            }];
        }
        var assumptionsByCallable = new Dictionary<
            string, WorkerAssumptionEvidence[]>(StringComparer.Ordinal);
        foreach (var callable in callables)
        {
            if (!string.IsNullOrWhiteSpace(callable.CallableId))
            {
                if (!assumptionsByCallable.ContainsKey(callable.CallableId))
                {
                    assumptionsByCallable.Add(
                        callable.CallableId,
                        callable.Assumptions ?? []);
                }
            }
        }
        return Create(inputHash, manifest, status, failureReason,
            callables.Select(callable => new WorkerCallableResult
            {
                CallableId = callable.CallableId,
                Coverage = WorkerCallableCoverage.Incomplete,
                Reason = callableReason,
                Assumptions = callable.Assumptions ?? []
            }),
            claims.Select(claim => new WorkerClaimResult
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
                Assumptions = string.IsNullOrWhiteSpace(claim.CallableId) ||
                    !assumptionsByCallable.TryGetValue(
                        claim.CallableId,
                        out var assumptions)
                    ? []
                    : assumptions
            }),
            budgets, WorkerCacheStatus.Disabled, elapsedMilliseconds, runErrors, requestHash, versions);
    }

    internal static bool HasConflictingAssumptionKinds(
        WorkerCallableResult[] callables, WorkerClaimResult[] claims)
    {
        var kinds = new Dictionary<string, WorkerAssumptionKind>(StringComparer.Ordinal);
        foreach (var assumption in callables.SelectMany(static value => value.Assumptions ?? [])
            .Concat(claims.SelectMany(static value => value.Assumptions ?? [])))
        {
            if (assumption == null || string.IsNullOrWhiteSpace(assumption.Id))
            {
                continue;
            }
            if (kinds.TryGetValue(assumption.Id, out var kind) && kind != assumption.Kind)
            {
                return true;
            }
            kinds[assumption.Id] = assumption.Kind;
        }
        return false;
    }
    internal static WorkerClaimManifest EmptyManifest()
    {
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);
        return manifest;
    }

    internal static (WorkerRunStatus Status, WorkerRunFailureReason Failure) Classify(
        IEnumerable<WorkerCallableResult>? callables, IEnumerable<WorkerClaimResult>? claims)
    {
        var callableSummary = SummarizeCallableReasons(callables);
        var claimSummary = SummarizeClaimReasons(claims);
        var callableFailure = callableSummary.Failure;
        var claimFailure = claimSummary.Failure;
        var failure = claimFailure is WorkerRunFailureReason.BackendUnavailable or
                WorkerRunFailureReason.MalformedResult
            ? claimFailure
            : callableFailure != WorkerRunFailureReason.None
                ? callableFailure
                : claimFailure;
        var canceled = callableSummary.Canceled || claimSummary.Canceled;
        var timedOut = callableSummary.TimedOut || claimSummary.TimedOut;
        var status = failure != WorkerRunFailureReason.None ? WorkerRunStatus.Failed
            : canceled ? WorkerRunStatus.Canceled
            : timedOut ? WorkerRunStatus.TimedOut
            : WorkerRunStatus.Complete;
        return (status, failure);
    }

    private static (WorkerRunFailureReason Failure, bool Canceled, bool TimedOut) SummarizeCallableReasons(
        IEnumerable<WorkerCallableResult>? callables)
    {
        var infrastructureFailure = false;
        var missingClaimResult = false;
        var canceled = false;
        var timedOut = false;
        foreach (var callable in callables ?? [])
        {
            if (callable == null)
            {
                continue;
            }
            var reason = callable.Reason;
            infrastructureFailure |= reason == WorkerCallableCoverageReason.InfrastructureFailure;
            missingClaimResult |= reason == WorkerCallableCoverageReason.MissingClaimResult;
            canceled |= reason == WorkerCallableCoverageReason.Canceled;
            timedOut |= reason is WorkerCallableCoverageReason.MethodTimeout or
                WorkerCallableCoverageReason.ProjectTimeout;
        }

        var failure = infrastructureFailure
            ? WorkerRunFailureReason.InfrastructureFailure
            : missingClaimResult
                ? WorkerRunFailureReason.MalformedResult
                : WorkerRunFailureReason.None;
        return (failure, canceled, timedOut);
    }

    private static (WorkerRunFailureReason Failure, bool Canceled, bool TimedOut) SummarizeClaimReasons(
        IEnumerable<WorkerClaimResult>? claims)
    {
        var backendUnavailable = false;
        var infrastructureFailure = false;
        var malformedBackendResult = false;
        var counterexampleReplayFailed = false;
        var canceled = false;
        var timedOut = false;
        foreach (var claim in claims ?? [])
        {
            if (claim == null)
            {
                continue;
            }
            var reason = claim.Reason;
            backendUnavailable |= reason == WorkerClaimReason.BackendUnavailable;
            infrastructureFailure |= reason == WorkerClaimReason.InfrastructureFailure;
            malformedBackendResult |= reason == WorkerClaimReason.MalformedBackendResult;
            counterexampleReplayFailed |= reason == WorkerClaimReason.CounterexampleReplayFailed;
            canceled |= reason == WorkerClaimReason.Canceled;
            timedOut |= reason is WorkerClaimReason.MethodTimeout or WorkerClaimReason.ProjectTimeout;
        }

        var failure = backendUnavailable
            ? WorkerRunFailureReason.BackendUnavailable
            : infrastructureFailure
                ? WorkerRunFailureReason.InfrastructureFailure
                : malformedBackendResult
                    ? WorkerRunFailureReason.MalformedResult
                    : counterexampleReplayFailed
                        ? WorkerRunFailureReason.CounterexampleReplayFailed
                        : WorkerRunFailureReason.None;
        return (failure, canceled, timedOut);
    }

    internal static bool TryProjectRunState(
        IEnumerable<WorkerCallableResult>? callables,
        IEnumerable<WorkerClaimResult>? claims,
        IEnumerable<WorkerProtocolError>? errors,
        out WorkerRunStatus status,
        out WorkerRunFailureReason failure)
    {
        var errorCount = 0;
        var errorStatus = WorkerRunStatus.Unspecified;
        var errorFailure = WorkerRunFailureReason.Unspecified;
        foreach (var error in errors ?? [])
        {
            var projected = ProjectError(error.Code);
            if (projected is null)
            {
                status = WorkerRunStatus.Unspecified;
                failure = WorkerRunFailureReason.Unspecified;
                return false;
            }
            if (errorCount == 0)
            {
                errorStatus = projected.Value.Status;
                errorFailure = projected.Value.Failure;
            }
            else if (projected.Value.Status != errorStatus ||
                projected.Value.Failure != errorFailure)
            {
                status = WorkerRunStatus.Unspecified;
                failure = WorkerRunFailureReason.Unspecified;
                return false;
            }
            errorCount++;
        }

        if (errorCount != 0)
        {
            status = errorStatus;
            failure = errorFailure;
            return true;
        }

        var evidence = Classify(callables, claims);
        status = evidence.Status;
        failure = evidence.Failure;
        return true;
    }

    internal static bool MatchesCallableProjection(
        WorkerCallableResult callable,
        WorkerClaimResult[] owned,
        WorkerRunStatus runStatus,
        WorkerRunFailureReason failureReason,
        bool hasErrors)
    {
        if (owned.Length == 0 &&
            !(runStatus == WorkerRunStatus.Failed && hasErrors))
        {
            if (callable.Coverage == WorkerCallableCoverage.Incomplete &&
                ((runStatus == WorkerRunStatus.TimedOut && callable.Reason == WorkerCallableCoverageReason.ProjectTimeout) ||
                 (runStatus == WorkerRunStatus.Canceled && callable.Reason == WorkerCallableCoverageReason.Canceled)))
            {
                return true;
            }
            return (callable.Coverage, callable.Reason) is
                (WorkerCallableCoverage.Complete,
                    WorkerCallableCoverageReason.None)
                or (WorkerCallableCoverage.Incomplete,
                    WorkerCallableCoverageReason.UnsupportedCallable)
                or (WorkerCallableCoverage.Incomplete,
                    WorkerCallableCoverageReason.UnsupportedContract)
                or (WorkerCallableCoverage.Incomplete,
                    WorkerCallableCoverageReason.SemanticUnknown)
                or (WorkerCallableCoverage.Incomplete,
                    WorkerCallableCoverageReason.InfrastructureFailure);
        }

        WorkerCallableCoverageReason expected;
        if (runStatus == WorkerRunStatus.Failed && hasErrors)
        {
            expected = failureReason == WorkerRunFailureReason.MalformedResult
                ? WorkerCallableCoverageReason.MissingClaimResult
                : WorkerCallableCoverageReason.InfrastructureFailure;
        }
        else
        {
            var projection = ProjectCallableReasons(owned);
            expected = projection.Reason is
                WorkerCallableCoverageReason.None or
                WorkerCallableCoverageReason.UnsupportedCallable or
                WorkerCallableCoverageReason.UnsupportedContract or
                WorkerCallableCoverageReason.InfrastructureFailure
                ? projection.Reason
                : projection.HasMethodTimeout
                    ? WorkerCallableCoverageReason.MethodTimeout
                    : projection.HasProjectTimeout
                        ? WorkerCallableCoverageReason.ProjectTimeout
                        : projection.HasCanceled
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
        var compatibleSemanticFallback =
            expected is WorkerCallableCoverageReason.UnsupportedCallable or
                WorkerCallableCoverageReason.UnsupportedContract or
                WorkerCallableCoverageReason.InfrastructureFailure &&
            callable.Coverage == WorkerCallableCoverage.Incomplete &&
            callable.Reason == WorkerCallableCoverageReason.SemanticUnknown;
        return matchesExpected || directInfrastructureFailure ||
            compatibleSemanticFallback;
    }

    internal static (
        WorkerCallableCoverageReason Reason,
        bool HasMethodTimeout,
        bool HasProjectTimeout,
        bool HasCanceled) ProjectCallableReasons(
        IEnumerable<WorkerClaimResult> claims)
    {
        var hasUnknown = false;
        var allUnsupportedCallable = true;
        var allUnsupportedContract = true;
        var hasInfrastructureFailure = false;
        var hasMethodTimeout = false;
        var hasProjectTimeout = false;
        var hasCanceled = false;
        foreach (var claim in claims)
        {
            if (claim.Outcome != WorkerClaimOutcome.Unknown)
            {
                continue;
            }

            hasUnknown = true;
            allUnsupportedCallable &=
                claim.Reason == WorkerClaimReason.UnsupportedCallable;
            allUnsupportedContract &=
                claim.Reason == WorkerClaimReason.UnsupportedContract;
            hasInfrastructureFailure |= claim.Reason is
                WorkerClaimReason.InfrastructureFailure or
                WorkerClaimReason.BackendUnavailable or
                WorkerClaimReason.MalformedBackendResult;
            hasMethodTimeout |= claim.Reason == WorkerClaimReason.MethodTimeout;
            hasProjectTimeout |= claim.Reason == WorkerClaimReason.ProjectTimeout;
            hasCanceled |= claim.Reason == WorkerClaimReason.Canceled;
        }

        var reason = !hasUnknown
            ? WorkerCallableCoverageReason.None
            : allUnsupportedCallable
                ? WorkerCallableCoverageReason.UnsupportedCallable
                : allUnsupportedContract
                    ? WorkerCallableCoverageReason.UnsupportedContract
                    : hasInfrastructureFailure
                        ? WorkerCallableCoverageReason.InfrastructureFailure
                        : WorkerCallableCoverageReason.SemanticUnknown;
        return (reason, hasMethodTimeout, hasProjectTimeout, hasCanceled);
    }

    private static (WorkerRunStatus Status, WorkerRunFailureReason Failure)?
        ProjectError(string code)
    {
        if (code is WorkerProtocolErrorCodes.WorkerTimeout)
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
        if (code is "worker.infrastructure" or "launcher.infrastructure")
        {
            return (WorkerRunStatus.Failed, WorkerRunFailureReason.InfrastructureFailure);
        }
        if (code is "worker.malformed_result" or "worker.no_result" ||
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
