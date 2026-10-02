namespace SharpProof.Worker;

internal static partial class CallableVerificationPolicy
{
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
        var hasRequires = target.Entry.Assumptions.Any(static assumption =>
            assumption.Kind == WorkerAssumptionKind.Precondition);
        var claims = target.Entry.ClaimIds.Select((claimId, index) =>
            effectClaims.TryGetValue(claimId, out var evidence)
                ? EffectClaimResultAssembler.Assemble(
                    target,
                    evidence,
                    // A failed lowering does not establish that a required
                    // entry is reachable. Keep compiler-proven summaries,
                    // but do not publish a replayed violation from an
                    // unverified entry.
                    hasRequires && evidence.Outcome == WorkerClaimOutcome.Refuted
                        ? CallableEntryFeasibility.Unknown(target.FailureReason)
                        : CallableEntryFeasibility.Feasible,
                    cancellationToken)
                : CallableClaimResultAssembler.Unknown(
                    target,
                    index,
                    target.FailureReason)).ToImmutableArray();
        var reason = claims.Length == 0
            ? WorkerCallableCoverageReason.SemanticUnknown
            : WorkerResultAssembler.ProjectCallableReasons(claims).Reason;
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
