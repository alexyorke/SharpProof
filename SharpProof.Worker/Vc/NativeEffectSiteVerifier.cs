namespace SharpProof.Worker;

// Shadow qualification only; compiler evidence still controls publication.
internal static class NativeEffectSiteVerifier
{
    internal static Task<PassiveCallableCheckResult> VerifyAsync(CompilerCallablePreparation preparation,
        WorkerBudgets budgets, CancellationToken cancellationToken = default)
    { return VerifySitesAsync(preparation, budgets, WorkerEffectContractKind.ZeroAllocations, cancellationToken); }

    internal static Task<PassiveCallableCheckResult> VerifyPurityAsync(CompilerCallablePreparation preparation,
        WorkerBudgets budgets, CancellationToken cancellationToken = default)
    { return VerifySitesAsync(preparation, budgets, WorkerEffectContractKind.EnforcePure, cancellationToken); }

    // Synchronization projection only; never complete capability authority.
    internal static async Task<PassiveCallableCheckResult> VerifySynchronizationShadowAsync(
        CompilerCallablePreparation preparation, string claimId, WorkerEffectCapabilitySet expectedMask,
        WorkerBudgets budgets, CancellationToken cancellationToken = default)
    {
        ArgumentNullGuard.NotNull(preparation, nameof(preparation));
        ArgumentNullGuard.NotNull(budgets, nameof(budgets));
        cancellationToken.ThrowIfCancellationRequested();
        var claims = preparation.EffectClaims.Where(claim => claim.ContractKind == WorkerEffectContractKind.AllowedCapabilities).ToArray();
        if (claims.Length != 1 || claims[0].ClaimId != claimId ||
            !preparation.Entry.ClaimIds.Contains(claimId) ||
            preparation.Total == null || !preparation.Total.ValidEffectClaimIds.Contains(claimId))
        { return Unknown(WorkerClaimReason.UnsupportedContract); }
        try
        { CompilerEffectClaimArtifactCodec.Validate(claims[0]); }
        catch (InvalidDataException) { return Unknown(WorkerClaimReason.UnsupportedContract); }
        var allowed = claims[0].Constraint.AllowedCapabilities;
        if (allowed != expectedMask || (allowed & ~WorkerEffectCapabilitySet.Synchronization) != 0)
        { return Unknown(WorkerClaimReason.UnsupportedContract); }
        if (!preparation.Total.EffectsCompleteAtEntry)
        { return Unknown(WorkerClaimReason.UnsupportedBody); }
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        if (candidate == null)
        { return Unknown(WorkerClaimReason.UnsupportedBody); }
        return await VerifySynchronizationProjectionAsync(candidate, allowed, budgets, cancellationToken).ConfigureAwait(false);
    }

    // Testable projection, without claim publication authority.
    internal static async Task<PassiveCallableCheckResult> VerifySynchronizationProjectionAsync(
        PassiveCallableCandidate candidate, WorkerEffectCapabilitySet allowed, WorkerBudgets budgets,
        CancellationToken cancellationToken = default)
    {
        if ((allowed & ~WorkerEffectCapabilitySet.Synchronization) != 0)
        { return Unknown(WorkerClaimReason.UnsupportedContract); }
        if (candidate.Requires.Any(clause => IrTermAnalysis.GetDepth(clause.Value) > budgets.MaximumExpressionDepth ||
            IrTermAnalysis.GetDepth(clause.Safe) > budgets.MaximumExpressionDepth))
        { return Unknown(WorkerClaimReason.UnsupportedExpression); }
        if (!PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken))
        { return Unknown(failure); }
        using var solver = new PassiveCallableSolver(plan!, budgets.QueryRlimit, budgets.MethodRlimit);
        var entry = await solver.VerifyEntryAsync(cancellationToken).ConfigureAwait(false);
        if (entry.Outcome is not RefutedOutcome)
        { return entry; }
        return await solver.VerifySynchronizationShadowAsync(
            (allowed & WorkerEffectCapabilitySet.Synchronization) != 0, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PassiveCallableCheckResult> VerifySitesAsync(CompilerCallablePreparation preparation,
        WorkerBudgets budgets, WorkerEffectContractKind contract, CancellationToken cancellationToken)
    {
        ArgumentNullGuard.NotNull(preparation, nameof(preparation));
        ArgumentNullGuard.NotNull(budgets, nameof(budgets));
        cancellationToken.ThrowIfCancellationRequested();
        var claims = preparation.EffectClaims.Where(claim => claim.ContractKind == contract).ToArray();
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
        var result = contract == WorkerEffectContractKind.EnforcePure
            ? await solver.VerifyPurityAsync(cancellationToken).ConfigureAwait(false)
            : await solver.VerifyAllocationsAsync(cancellationToken).ConfigureAwait(false);
        return result with
        {
            EntryModel = result.Outcome is ProvenOutcome ? entry.EntryModel : result.EntryModel,
            HasFeasibleEntryWitness = result.Outcome is ProvenOutcome or RefutedOutcome
        };
    }

    private static PassiveCallableCheckResult Unknown(WorkerClaimReason reason)
    { return new(null, reason, ImmutableDictionary<IrVarId, IrValue>.Empty, [], []); }
}
