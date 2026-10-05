namespace SharpProof.Worker;

// Native allocation, purity and capability evidence; NativeEffectClaims publishes it.
internal static class NativeEffectSiteVerifier
{
    internal static Task<PassiveCallableCheckResult> VerifyAsync(CompilerCallablePreparation preparation,
        WorkerBudgets budgets, CancellationToken cancellationToken = default)
    { return VerifySitesAsync(preparation, budgets, WorkerEffectContractKind.ZeroAllocations, cancellationToken); }

    internal static Task<PassiveCallableCheckResult> VerifyPurityAsync(CompilerCallablePreparation preparation,
        WorkerBudgets budgets, CancellationToken cancellationToken = default)
    { return VerifySitesAsync(preparation, budgets, WorkerEffectContractKind.EnforcePure, cancellationToken); }

    // An AllowedCapabilities claim holds when no reachable lock or opaque call
    // uses a capability outside the allowed set.
    internal static async Task<PassiveCallableCheckResult> VerifyCapabilitiesAsync(CompilerCallablePreparation preparation,
        WorkerBudgets budgets, CancellationToken cancellationToken = default)
    {
        ArgumentNullGuard.NotNull(preparation, nameof(preparation));
        var claims = preparation.EffectClaims.Where(claim => claim.ContractKind == WorkerEffectContractKind.AllowedCapabilities).ToArray();
        return claims.Length != 1 ? Unknown(WorkerClaimReason.UnsupportedContract)
            : await VerifyCapabilityClaimAsync(preparation, claims[0].ClaimId, budgets, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PassiveCallableCheckResult> VerifyCapabilityClaimAsync(CompilerCallablePreparation preparation,
        string claimId, WorkerBudgets budgets, CancellationToken cancellationToken)
    {
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
        if (!preparation.Total.EffectsCompleteAtEntry)
        { return Unknown(WorkerClaimReason.UnsupportedBody); }
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        if (candidate == null)
        { return Unknown(WorkerClaimReason.UnsupportedBody); }
        return await VerifyCapabilityProjectionAsync(candidate, claims[0].Constraint.AllowedCapabilities, budgets, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<PassiveCallableCheckResult> VerifyCapabilityProjectionAsync(
        PassiveCallableCandidate candidate, WorkerEffectCapabilitySet allowed, WorkerBudgets budgets,
        CancellationToken cancellationToken = default)
    {
        if (candidate.Requires.Any(clause => IrTermAnalysis.GetDepth(clause.Value) > budgets.MaximumExpressionDepth ||
            IrTermAnalysis.GetDepth(clause.Safe) > budgets.MaximumExpressionDepth))
        { return Unknown(WorkerClaimReason.UnsupportedExpression); }
        if (!PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken))
        { return Unknown(failure); }
        using var solver = new PassiveCallableSolver(plan!, budgets.QueryRlimit, budgets.MethodRlimit);
        var entry = await solver.VerifyEntryAsync(cancellationToken).ConfigureAwait(false);
        if (entry.Outcome is not RefutedOutcome)
        { return entry; }
        return await solver.VerifyCapabilitiesAsync((allowed & WorkerEffectCapabilitySet.Synchronization) != 0,
            effects => (Capabilities(effects) & ~allowed) != 0, cancellationToken).ConfigureAwait(false);
    }

    // A trusted contract declares a call's capabilities; an unspecified call
    // may use any. Specified effects map as the compiler maps them:
    // input/output to IO and nondeterminism to Randomness.
    internal static WorkerEffectCapabilitySet Capabilities(IrOpaqueCallEffects effects)
    {
        if (IrOpaqueCallSite.Capabilities(effects) is { } declared)
        { return (WorkerEffectCapabilitySet)declared; }
        if (effects == IrOpaqueCallEffects.All)
        { return WorkerEffectCapabilitySet.AllKnown; }
        var capabilities = WorkerEffectCapabilitySet.None;
        if ((effects & IrOpaqueCallEffects.InputOutput) != 0)
        { capabilities |= WorkerEffectCapabilitySet.IO; }
        if ((effects & IrOpaqueCallEffects.Synchronizes) != 0)
        { capabilities |= WorkerEffectCapabilitySet.Synchronization; }
        if ((effects & IrOpaqueCallEffects.NativeCode) != 0)
        { capabilities |= WorkerEffectCapabilitySet.NativeInterop; }
        if ((effects & IrOpaqueCallEffects.Reflection) != 0)
        { capabilities |= WorkerEffectCapabilitySet.Reflection; }
        if ((effects & IrOpaqueCallEffects.Nondeterminism) != 0)
        { capabilities |= WorkerEffectCapabilitySet.Randomness; }
        return capabilities;
    }

    // An EffectContract summary bounds every effect of the body: exceptions
    // must be declared and listed, allocation needs Allocates, and writes,
    // reads, locks and opaque calls need their effects and capabilities. The
    // IR does not track which state an object belongs to, so a field or
    // element access needs every state flag of its kind; only a store through
    // a parameter is known to write argument state.
    internal static async Task<PassiveCallableCheckResult> VerifyEffectContractAsync(CompilerCallablePreparation preparation,
        WorkerBudgets budgets, CancellationToken cancellationToken = default)
    {
        ArgumentNullGuard.NotNull(preparation, nameof(preparation));
        ArgumentNullGuard.NotNull(budgets, nameof(budgets));
        var claims = preparation.EffectClaims.Where(claim => claim.ContractKind == WorkerEffectContractKind.EffectContract).ToArray();
        if (claims.Length != 1 || preparation.Total is not { } total || !total.ValidEffectClaimIds.Contains(claims[0].ClaimId) ||
            total.ExceptionConstraints.FirstOrDefault(constraint => constraint.ClaimId == claims[0].ClaimId) is not { } exceptions)
        { return Unknown(WorkerClaimReason.UnsupportedContract); }
        if (!total.EffectsCompleteAtEntry)
        { return Unknown(WorkerClaimReason.UnsupportedBody); }
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        if (candidate == null)
        { return Unknown(WorkerClaimReason.UnsupportedBody); }
        if (candidate.Requires.Any(clause => IrTermAnalysis.GetDepth(clause.Value) > budgets.MaximumExpressionDepth ||
            IrTermAnalysis.GetDepth(clause.Safe) > budgets.MaximumExpressionDepth))
        { return Unknown(WorkerClaimReason.UnsupportedExpression); }
        if (!PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken))
        { return Unknown(failure); }
        var constraint = claims[0].Constraint;
        var effects = constraint.AllowedEffects;
        var capabilities = constraint.AllowedCapabilities;
        bool Allows(WorkerEffectSet required)
        { return (required & ~effects) == 0; }
        bool WriteViolates(IrWriteRegion region)
        {
            return region switch
            {
                IrWriteRegion.Local => false,
                IrWriteRegion.Parameter => !Allows(WorkerEffectSet.WritesArgumentState),
                IrWriteRegion.Static => !Allows(WorkerEffectSet.WritesStaticState),
                _ => !Allows(AnyStateWrites)
            };
        }
        bool CallViolates(IrOpaqueCallEffects call)
        { return !Allows(CallEffects(call)) || (Capabilities(call) & ~capabilities) != 0; }
        var factory = candidate.Program.Factory;
        var allowedTypes = constraint.AllowedExceptionTypes.ToHashSet(StringComparer.Ordinal);
        var throwsDeclared = (effects & WorkerEffectSet.Throws) != 0;
        using var solver = new PassiveCallableSolver(plan!, budgets.QueryRlimit, budgets.MethodRlimit);
        var entry = await solver.VerifyEntryAsync(cancellationToken).ConfigureAwait(false);
        if (entry.Outcome is not RefutedOutcome)
        { return entry; }
        var thrown = await solver.VerifyExceptionsAsync([.. exceptions.AllowedKinds], !throwsDeclared || allowedTypes.Count == 0,
            site => throwsDeclared && ExplicitThrowSites.Types(factory, site).Any(allowedTypes.Contains),
            site => ExplicitThrowSites.IsExact(factory, site), cancellationToken).ConfigureAwait(false);
        if (thrown.Outcome is not ProvenOutcome)
        { return thrown; }
        if (!Allows(WorkerEffectSet.Allocates))
        {
            var allocated = await solver.VerifyAllocationsAsync(cancellationToken).ConfigureAwait(false);
            if (allocated.Outcome is not ProvenOutcome)
            { return allocated; }
        }
        var readFlags = AnyStateReads;
        var locksViolate = !Allows(WorkerEffectSet.Synchronizes) || (capabilities & WorkerEffectCapabilitySet.Synchronization) == 0;
        var sites = await solver.VerifyEffectSitesAsync(WriteViolates, !Allows(readFlags), locksViolate, CallViolates, cancellationToken)
            .ConfigureAwait(false);
        return sites with { EntryModel = sites.Outcome is ProvenOutcome ? entry.EntryModel : sites.EntryModel };
    }

    private const WorkerEffectSet AnyStateWrites = WorkerEffectSet.WritesReceiverState | WorkerEffectSet.WritesArgumentState |
        WorkerEffectSet.WritesCapturedState | WorkerEffectSet.WritesStaticState | WorkerEffectSet.WritesAmbientState;
    private const WorkerEffectSet AnyStateReads = WorkerEffectSet.ReadsReceiverState | WorkerEffectSet.ReadsArgumentState |
        WorkerEffectSet.ReadsCapturedState | WorkerEffectSet.ReadsStaticState | WorkerEffectSet.ReadsAmbientState;

    // An unspecified call may have every effect.
    private static WorkerEffectSet CallEffects(IrOpaqueCallEffects call)
    {
        if (call == IrOpaqueCallEffects.All)
        { return WorkerEffectSet.AllKnown; }
        var effects = WorkerEffectSet.None;
        if ((call & IrOpaqueCallEffects.Writes) != 0)
        { effects |= AnyStateWrites; }
        if ((call & IrOpaqueCallEffects.Reads) != 0)
        { effects |= AnyStateReads; }
        if ((call & IrOpaqueCallEffects.Allocates) != 0)
        { effects |= WorkerEffectSet.Allocates; }
        if ((call & IrOpaqueCallEffects.Throws) != 0)
        { effects |= WorkerEffectSet.Throws; }
        if ((call & IrOpaqueCallEffects.Synchronizes) != 0)
        { effects |= WorkerEffectSet.Synchronizes; }
        if ((call & IrOpaqueCallEffects.InputOutput) != 0)
        { effects |= WorkerEffectSet.ReadsAmbientState | WorkerEffectSet.WritesAmbientState; }
        if ((call & IrOpaqueCallEffects.Nondeterminism) != 0)
        { effects |= WorkerEffectSet.UsesNondeterminism; }
        if ((call & IrOpaqueCallEffects.NativeCode) != 0)
        { effects |= WorkerEffectSet.UsesNativeCode; }
        if ((call & IrOpaqueCallEffects.Reflection) != 0)
        { effects |= WorkerEffectSet.UsesReflection; }
        return effects;
    }

    // Synchronization-only masks, as measured by the synchronization shadow.
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
        return await VerifyCapabilityClaimAsync(preparation, claimId, budgets, cancellationToken).ConfigureAwait(false);
    }

    // Testable projection, without claim publication authority.
    internal static async Task<PassiveCallableCheckResult> VerifySynchronizationProjectionAsync(
        PassiveCallableCandidate candidate, WorkerEffectCapabilitySet allowed, WorkerBudgets budgets,
        CancellationToken cancellationToken = default)
    {
        return (allowed & ~WorkerEffectCapabilitySet.Synchronization) != 0 ? Unknown(WorkerClaimReason.UnsupportedContract)
            : await VerifyCapabilityProjectionAsync(candidate, allowed, budgets, cancellationToken).ConfigureAwait(false);
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
