namespace SharpProof.Worker;

internal static class CallableVerificationPolicy
{
    internal static async Task<CallableVerificationResult> VerifyTargetAsync(
        CallableVerifier verifier, CompilerCallablePreparation target, WorkerBudgets budgets,
        Func<long>? readConsumedResourceCount, int methodWallTimeMilliseconds,
        CancellationTokenSource projectBoundary, CancellationToken callerCancellation)
    {
        if (callerCancellation.IsCancellationRequested)
        {
            return Unknown(target, WorkerClaimReason.Canceled, WorkerCallableCoverageReason.Canceled);
        }

        using var methodBoundary = CancellationTokenSource.CreateLinkedTokenSource(projectBoundary.Token);
        methodBoundary.CancelAfter(methodWallTimeMilliseconds);
        try
        {
            if (!target.IsSuccess)
            {
                return FailedLowering(target, methodBoundary.Token);
            }

            var proof = await verifier.VerifyWithEntryFeasibilityAsync(
                target,
                new MethodResourceBudget(readConsumedResourceCount, budgets.QueryRlimit, budgets.MethodRlimit),
                methodBoundary.Token).ConfigureAwait(false);
            var ordinal = target.Entry.ClaimIds
                .Select(static (claimId, index) => (claimId, index))
                .ToDictionary(static item => item.claimId, static item => item.index, StringComparer.Ordinal);
            var records = proof.Postconditions
                .Concat(target.EffectClaims.Select(evidence =>
                    EffectClaimResultAssembler.Assemble(
                        target,
                        evidence,
                        proof.EntryFeasibility,
                        methodBoundary.Token)))
                .OrderBy(result => ordinal[result.ClaimId])
                .ToImmutableArray();
            var reason = records.Any(static record => record.Outcome == WorkerClaimOutcome.Unknown)
                ? records.Any(static record =>
                    record.Reason is WorkerClaimReason.InfrastructureFailure or
                        WorkerClaimReason.MalformedBackendResult)
                    ? WorkerCallableCoverageReason.InfrastructureFailure
                    : WorkerCallableCoverageReason.SemanticUnknown
                : WorkerCallableCoverageReason.None;
            return Result(target, reason, records);
        }
        catch (OperationCanceledException)
        {
            if (callerCancellation.IsCancellationRequested)
            {
                return Unknown(target, WorkerClaimReason.Canceled,
                    WorkerCallableCoverageReason.Canceled);
            }

            if (projectBoundary.IsCancellationRequested)
            {
                return Unknown(target, WorkerClaimReason.ProjectTimeout,
                    WorkerCallableCoverageReason.ProjectTimeout);
            }

            if (methodBoundary.IsCancellationRequested)
            {
                return Unknown(target, WorkerClaimReason.MethodTimeout,
                    WorkerCallableCoverageReason.MethodTimeout);
            }

            return Unknown(target, WorkerClaimReason.InfrastructureFailure,
                WorkerCallableCoverageReason.InfrastructureFailure);
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException and not StackOverflowException)
        {
            return Unknown(target, WorkerClaimReason.InfrastructureFailure,
                WorkerCallableCoverageReason.InfrastructureFailure);
        }
    }

    internal static CallableVerificationResult Unknown(
        CompilerCallablePreparation target, WorkerClaimReason claimReason,
        WorkerCallableCoverageReason callableReason)
    {
        return Result(target, callableReason, CallableClaimResultAssembler.Unknowns(target, claimReason));
    }

    internal static CallableVerificationResult FailedLowering(
        CompilerCallablePreparation target,
        CancellationToken cancellationToken)
    {
        if (target.FailureReason == WorkerClaimReason.UnsupportedCallable)
        {
            return Unknown(
                target,
                WorkerClaimReason.UnsupportedCallable,
                WorkerCallableCoverageReason.UnsupportedCallable);
        }

        var effectClaims = target.EffectClaims.ToDictionary(
            static evidence => evidence.ClaimId,
            StringComparer.Ordinal);
        var claims = target.Entry.ClaimIds.Select((claimId, index) =>
            effectClaims.TryGetValue(claimId, out var evidence)
                ? EffectClaimResultAssembler.Assemble(
                    target,
                    evidence,
                    CallableEntryFeasibility.Feasible,
                    cancellationToken)
                : CallableClaimResultAssembler.Unknown(
                    target,
                    index,
                    target.FailureReason)).ToImmutableArray();
        var reason = claims.Length > 0 &&
            claims.All(static claim =>
                claim.Outcome != WorkerClaimOutcome.Unknown)
                    ? WorkerCallableCoverageReason.None
                    : WorkerCallableCoverageReason.SemanticUnknown;
        return Result(target, reason, claims);
    }

    private static CallableVerificationResult Result(
        CompilerCallablePreparation target, WorkerCallableCoverageReason reason,
        ImmutableArray<WorkerClaimResult> claims)
    {
        return new(
            new WorkerCallableResult
            {
                CallableId = target.Entry.CallableId,
                Coverage = reason == WorkerCallableCoverageReason.None
                    ? WorkerCallableCoverage.Complete
                    : WorkerCallableCoverage.Incomplete,
                Reason = reason,
                Assumptions = [.. target.Entry.Assumptions]
            },
            claims);
    }
}
