namespace SharpProof.Worker;

// Shadow qualification only; compiler evidence still controls publication.
internal static class NativeAllocationEffectVerifier
{
    internal static async Task<PassiveCallableCheckResult> VerifyAsync(CompilerCallablePreparation preparation,
        WorkerBudgets budgets, CancellationToken cancellationToken = default)
    {
        ArgumentNullGuard.NotNull(preparation, nameof(preparation));
        ArgumentNullGuard.NotNull(budgets, nameof(budgets));
        cancellationToken.ThrowIfCancellationRequested();
        var claims = preparation.EffectClaims.Where(claim => claim.ContractKind == WorkerEffectContractKind.ZeroAllocations).ToArray();
        if (claims.Length != 1)
        { return Unknown(WorkerClaimReason.UnsupportedContract); }
        if (preparation.Total == null)
        { return Unknown(WorkerClaimReason.UnsupportedBody); }
        if (!preparation.Total.ValidEffectClaimIds.Contains(claims[0].ClaimId))
        { return Unknown(WorkerClaimReason.UnsupportedContract); }
        if (preparation.Total?.EffectsCompleteAtEntry != true)
        { return Unknown(WorkerClaimReason.UnsupportedBody); }
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        if (candidate == null)
        { return Unknown(WorkerClaimReason.UnsupportedBody); }
        if (candidate.Requires.Any(clause => IrTermAnalysis.GetDepth(clause.Value) > budgets.MaximumExpressionDepth ||
            IrTermAnalysis.GetDepth(clause.Safe) > budgets.MaximumExpressionDepth))
        { return Unknown(WorkerClaimReason.UnsupportedExpression); }
        if (!PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken))
        { return Unknown(failure); }
        using var solver = new PassiveCallableSolver(plan!, budgets.QueryRlimit, budgets.MethodRlimit);
        var entry = await solver.VerifyEntryAsync(cancellationToken).ConfigureAwait(false);
        if (entry.Outcome is not RefutedOutcome)
        { return entry; }
        var result = await solver.VerifyAllocationsAsync(cancellationToken).ConfigureAwait(false);
        return result.Outcome is ProvenOutcome ? result with { EntryModel = entry.EntryModel } : result;
    }

    private static PassiveCallableCheckResult Unknown(WorkerClaimReason reason)
    { return new(null, reason, ImmutableDictionary<IrVarId, IrValue>.Empty, [], []); }
}
