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
            // The method wall-time budget applies to proof verification. Result
            // assembly is a separate, bounded phase and must not inherit an
            // already-expired timer that can turn a completed proof into a
            // spurious timeout while rows are being projected.
            await methodBoundary.CancelAsync().ConfigureAwait(false);
            using var postProcessingBoundary =
                CancellationTokenSource.CreateLinkedTokenSource(
                    projectBoundary.Token, callerCancellation);
            var postProcessingToken = postProcessingBoundary.Token;
            var ordinal = target.Entry.ClaimIds
                .Select(static (claimId, index) => (claimId, index))
                .ToDictionary(static item => item.claimId, static item => item.index, StringComparer.Ordinal);
            var records = proof.Postconditions
                .Concat(target.EffectClaims.Select(evidence =>
                    AssembleEffectClaim(
                        target,
                        evidence,
                        proof.EntryFeasibility,
                        postProcessingToken)))
                .OrderBy(result => ordinal[result.ClaimId])
                .ToImmutableArray();
            var reason = GetCallableCoverageReason(records);
            return Result(target, reason, records);
        }
        catch (OperationCanceledException)
        {
            if (callerCancellation.IsCancellationRequested)
            {
                return Unknown(target, WorkerClaimReason.Canceled,
                    WorkerCallableCoverageReason.Canceled);
            }

            var timeout = projectBoundary.IsCancellationRequested
                ? (WorkerClaimReason.ProjectTimeout, WorkerCallableCoverageReason.ProjectTimeout)
                : (WorkerClaimReason.MethodTimeout, WorkerCallableCoverageReason.MethodTimeout);
            return Unknown(target, timeout.Item1, timeout.Item2);
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException and not StackOverflowException)
        {
            return Unknown(target, WorkerClaimReason.InfrastructureFailure,
                WorkerCallableCoverageReason.InfrastructureFailure);
        }
    }

    internal static WorkerClaimResult AssembleEffectClaim(
        CompilerCallablePreparation target,
        CompilerEffectClaimArtifact evidence,
        CallableEntryFeasibility entryFeasibility,
        CancellationToken cancellationToken)
    {
        try
        {
            return EffectClaimResultAssembler.Assemble(
                target, evidence, entryFeasibility, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is not OutOfMemoryException and
            not StackOverflowException)
        {
            return CallableClaimResultAssembler.Create(
                target,
                evidence.ClaimId,
                WorkerClaimOutcome.Unknown,
                WorkerClaimReason.MalformedBackendResult,
                WorkerEffectEvidenceCertainty.Unavailable);
        }
    }

    internal static CallableVerificationResult Unknown(
        CompilerCallablePreparation target, WorkerClaimReason claimReason,
        WorkerCallableCoverageReason callableReason)
    {
        return Result(target, callableReason, CallableClaimResultAssembler.Unknowns(target, claimReason));
    }

    internal static CallableVerificationResult Complete(
        CompilerCallablePreparation target)
    {
        return Result(target, WorkerCallableCoverageReason.None, []);
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

    private static WorkerCallableCoverageReason GetCallableCoverageReason(
        ImmutableArray<WorkerClaimResult> records)
    {
        var unknown = records
            .Where(static record => record.Outcome == WorkerClaimOutcome.Unknown)
            .ToArray();
        if (unknown.Length == 0)
        {
            return WorkerCallableCoverageReason.None;
        }

        if (unknown.All(static record =>
                record.Reason == WorkerClaimReason.UnsupportedCallable))
        {
            return WorkerCallableCoverageReason.UnsupportedCallable;
        }

        if (unknown.Any(static record =>
                record.Reason == WorkerClaimReason.MethodTimeout))
        {
            return WorkerCallableCoverageReason.MethodTimeout;
        }

        if (unknown.Any(static record =>
                record.Reason == WorkerClaimReason.ProjectTimeout))
        {
            return WorkerCallableCoverageReason.ProjectTimeout;
        }

        if (unknown.Any(static record =>
                record.Reason == WorkerClaimReason.Canceled))
        {
            return WorkerCallableCoverageReason.Canceled;
        }

        if (unknown.All(static record =>
                record.Reason is WorkerClaimReason.InfrastructureFailure or
                    WorkerClaimReason.BackendUnavailable))
        {
            return WorkerCallableCoverageReason.InfrastructureFailure;
        }

        return WorkerCallableCoverageReason.SemanticUnknown;
    }
}
