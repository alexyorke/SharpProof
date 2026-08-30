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

        if (!target.IsSuccess)
        {
            return Unknown(target, target.FailureReason,
                target.FailureReason == WorkerClaimReason.UnsupportedCallable
                    ? WorkerCallableCoverageReason.UnsupportedCallable
                    : WorkerCallableCoverageReason.SemanticUnknown);
        }

        using var methodBoundary = CancellationTokenSource.CreateLinkedTokenSource(projectBoundary.Token);
        methodBoundary.CancelAfter(methodWallTimeMilliseconds);
        try
        {
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
                ? WorkerCallableCoverageReason.SemanticUnknown
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
