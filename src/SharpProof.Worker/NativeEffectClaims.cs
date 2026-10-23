namespace SharpProof.Worker;

// Native checkers decide effect claims over compiler-owned programs.
// A proof is a complete may-effect summary; a refutation names the first
// replayed violating site.
internal static class NativeEffectClaims
{
    internal static bool IsNative(WorkerEffectContractKind kind)
    {
        return kind is WorkerEffectContractKind.DoesNotThrow or WorkerEffectContractKind.AllowedExceptions or
            WorkerEffectContractKind.ZeroAllocations or WorkerEffectContractKind.EnforcePure or
            WorkerEffectContractKind.AllowedCapabilities or WorkerEffectContractKind.EffectContract or WorkerEffectContractKind.ResourceUpperBound;
    }

    internal static async Task<ImmutableArray<WorkerClaimResult>> VerifyAsync(CompilerCallablePreparation target,
        CallableEntryFeasibility entry, WorkerBudgets budgets, CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(budgets);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entry);
        // A trusted complete boundary is a declared assumption about a body
        // SharpProof does not see; it is published as declared.
        var claims = target.EffectClaims.Where(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (IsNative(claim.ContractKind) &&
            claim.Certainty != WorkerEffectEvidenceCertainty.TrustedCompleteBoundary)).ToImmutableArray();
        // An invalid contract has nothing to verify, whatever the entry.
        bool Invalid(CompilerEffectClaimArtifact claim)
        { System.ArgumentNullException.ThrowIfNull(claim, nameof(claim)); return target.Total is { } total && !total.ValidEffectClaimIds.Contains(claim.ClaimId); }
        var invalid = claims.Where(Invalid).Select(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (Unknown(target, claim.ClaimId, WorkerClaimReason.UnsupportedContract))).ToImmutableArray();
        var validClaims = claims.Where(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (!Invalid(claim))).ToImmutableArray();
        if (validClaims.Length == 0)
        { return [.. invalid]; }
        return [.. invalid, .. await VerifyValidAsync(target, validClaims, entry, budgets, cancellationToken).ConfigureAwait(false)];
    }

    private static async Task<ImmutableArray<WorkerClaimResult>> VerifyValidAsync(CompilerCallablePreparation target,
        ImmutableArray<CompilerEffectClaimArtifact> claims, CallableEntryFeasibility entry, WorkerBudgets budgets, CancellationToken cancellationToken)
    {
        RepositoryParameterGuard.RequireInitialized(claims.IsDefault, nameof(claims), "Array must be initialized.");
        System.ArgumentNullException.ThrowIfNull(budgets);
        System.ArgumentNullException.ThrowIfNull(target);
        if (entry.IsContradictory)
        {
            return [.. claims.Select(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (CallableClaimResultAssembler.Contradictory(target, claim.ClaimId,
                WorkerEffectEvidenceCertainty.VacuousEntry, entry.ProofCore, entry.UsedAssumptionIds)))];
        }
        if (entry.IsUnknown)
        { return [.. claims.Select(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (Unknown(target, claim.ClaimId, entry.Reason)))]; }
        var exceptions = await NativeExceptionEffectVerifier.VerifyClaimsAsync(target, budgets, cancellationToken).ConfigureAwait(false);
        var results = ImmutableArray.CreateBuilder<WorkerClaimResult>(claims.Length);
        foreach (var claim in claims)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (claim.ContractKind == WorkerEffectContractKind.ResourceUpperBound)
            {
                results.Add(await NativeResourceContractVerifier.VerifyAsync(target, claim, budgets, cancellationToken).ConfigureAwait(false));
                continue;
            }
            var check = claim.ContractKind switch
            {
                WorkerEffectContractKind.ZeroAllocations => await NativeEffectSiteVerifier.VerifyAsync(target, budgets, cancellationToken).ConfigureAwait(false),
                WorkerEffectContractKind.EnforcePure => await NativeEffectSiteVerifier.VerifyPurityAsync(target, budgets, cancellationToken).ConfigureAwait(false),
                WorkerEffectContractKind.AllowedCapabilities => await NativeEffectSiteVerifier.VerifyCapabilitiesAsync(target, budgets, cancellationToken).ConfigureAwait(false),
                WorkerEffectContractKind.EffectContract => await NativeEffectSiteVerifier.VerifyEffectContractAsync(target, budgets, cancellationToken).ConfigureAwait(false),
                _ => exceptions[claim.ClaimId]
            };
            results.Add(Project(target, claim.ClaimId, check));
        }
        return results.MoveToImmutable();
    }

    internal static WorkerClaimResult Unknown(CompilerCallablePreparation target, string claimId, WorkerClaimReason reason)
    {
        System.ArgumentNullException.ThrowIfNull(claimId);
        System.ArgumentNullException.ThrowIfNull(target);
        return CallableClaimResultAssembler.Create(target, claimId, WorkerClaimOutcome.Unknown,
            reason == WorkerClaimReason.None ? WorkerClaimReason.SolverIncomplete : reason, WorkerEffectEvidenceCertainty.Unavailable);
    }

    internal static WorkerClaimResult Project(CompilerCallablePreparation target, string claimId, PassiveCallableCheckResult check)
    {
        System.ArgumentNullException.ThrowIfNull(claimId);
        if (check.Outcome is ProvenOutcome && check.Reason == WorkerClaimReason.None)
        {
            var record = CallableClaimResultAssembler.Create(target, claimId, WorkerClaimOutcome.Proven,
                WorkerClaimReason.None, WorkerEffectEvidenceCertainty.CompleteMayEffectSummary, projectAssumptions: false);
            record = record with { ProofCore = ["native-effect:" + claimId] };
            record = record with { Assumptions = CallableClaimResultAssembler.MarkAssumptionsUsed(target, UsedRequires(target, check.Core)) };
            return record;
        }
        if (check.Outcome is RefutedOutcome && check.Reason == WorkerClaimReason.None && Witness(target, check) is { } witness)
        {
            var record = CallableClaimResultAssembler.Create(target, claimId, WorkerClaimOutcome.Refuted,
                WorkerClaimReason.None, WorkerEffectEvidenceCertainty.DefiniteViolation);
            record = record with { EffectWitness = witness };
            return record;
        }
        return Unknown(target, claimId, check.Outcome is RefutedOutcome ? WorkerClaimReason.CounterexampleNotReplayable : check.Reason);
    }

    // Requires clauses are labelled by ordinal in the passive VC.
    private static ImmutableHashSet<string> UsedRequires(CompilerCallablePreparation target, ImmutableArray<string> core)
    {

        RepositoryParameterGuard.RequireInitialized(core.IsDefault, nameof(core), "Array must be initialized.");
        var requires = (target.Total ?? throw new InvalidOperationException("A proven native effect claim requires a total callable.")).Clauses.Where(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == CompilerContractKind.Requires)).ToImmutableArray();
        var used = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        for (var ordinal = 0; ordinal < requires.Length; ordinal++)
        {
            if (core.Contains("requires:" + ordinal.ToString(CultureInfo.InvariantCulture)) && requires[ordinal].AssumptionId is { } id)
            { used.Add(id); }
        }
        return used.ToImmutable();
    }

    // A witness names a source site; a site without a source position (an
    // inlined metadata body) leaves the claim Unknown.
    private static WorkerEffectViolationWitness? Witness(CompilerCallablePreparation target, PassiveCallableCheckResult check)
    {
        System.ArgumentNullException.ThrowIfNull(check);
        var program = (target.Total ?? throw new InvalidOperationException("A refuted native effect claim requires a total callable.")).Program;
        var factory = program.Factory;
        var site = check switch
        {
            { ExceptionWitness: { } exception } => exception.Site,
            { AllocationWitness: { } allocation } => allocation,
            { LockWitness: { } synchronization } => synchronization,
            _ => check.WriteWitness
        };
        if (site == null || factory.GetOperationInfo(site.Value).SourceSpan is not { Line: > 0 } span)
        { return null; }
        var (kind, detail, effects, capabilities) = check switch
        {
            { ExceptionWitness: { Kind: IrExceptionKind.Explicit } } => ("explicit-throw",
                ExplicitThrowSites.TypeName(ExplicitThrowSites.Types(factory, site.Value).FirstOrDefault() ?? "System.Exception"), WorkerEffectSet.Throws, WorkerEffectCapabilitySet.None),
            { ExceptionWitness: { } exception } => ("implicit-throw", ExplicitThrowSites.TypeName(exception.Kind),
                WorkerEffectSet.Throws, WorkerEffectCapabilitySet.None),
            { AllocationWitness: { } allocation } => (IsArray(program, allocation) ? "managed-array-allocation" : "managed-allocation",
                Describe(factory, allocation), WorkerEffectSet.Allocates, WorkerEffectCapabilitySet.None),
            { LockWitness: { } synchronization } => ("synchronization-lock", Describe(factory, synchronization),
                WorkerEffectSet.Synchronizes, WorkerEffectCapabilitySet.Synchronization),
            _ => ("nonlocal-write", Describe(factory, site.Value),
                WriteEffect(program, site.Value, check.ReceiverWriteWitness), WorkerEffectCapabilitySet.None)
        };
        return new WorkerEffectViolationWitness
        {
            Kind = kind,
            Detail = detail,
            Effects = effects,
            Capabilities = capabilities,
            ExactExceptionTypeHierarchy = kind == "explicit-throw" && ExplicitThrowSites.IsExact(factory, site.Value)
                ? ExplicitThrowSites.Types(factory, site.Value) : [],
            Location = new WorkerSourceLocation { Path = span.Document, Start = span.Start, Length = span.Length, Line = span.Line, Column = span.Column }
        };
    }

    private static string Describe(IrFactory factory, OperationId site)
    {
        RepositoryParameterGuard.ThrowIfInvalid(site.IsDefault, nameof(site));
        System.ArgumentNullException.ThrowIfNull(factory);
        return factory.GetOperationInfo(site).Description is { } description ? factory.GetString(description) : "operation";
    }

    private static bool IsArray(IrProgram program, OperationId site)
    {
        RepositoryParameterGuard.ThrowIfInvalid(site.IsDefault, nameof(site));
        System.ArgumentNullException.ThrowIfNull(program);
        return program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).OfType<IrAllocationInstruction>()
            .Any(allocation => allocation is null ? throw new System.ArgumentNullException(nameof(allocation)) : (allocation.Operation == site && allocation.Length != null));
    }

    private static WorkerEffectSet WriteEffect(IrProgram program, OperationId site, bool receiver)
    {
        RepositoryParameterGuard.ThrowIfInvalid(site.IsDefault, nameof(site));
        System.ArgumentNullException.ThrowIfNull(program);
        if (receiver)
        { return WorkerEffectSet.WritesReceiverState; }
        var region = program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).OfType<IrWriteInstruction>()
            .FirstOrDefault(write => write is null ? throw new System.ArgumentNullException(nameof(write)) : (write.Operation == site))?.Region;
        return region switch
        {
            IrWriteRegion.Parameter => WorkerEffectSet.WritesArgumentState,
            IrWriteRegion.Field => WorkerEffectSet.WritesReceiverState,
            IrWriteRegion.Static => WorkerEffectSet.WritesStaticState,
            _ => WorkerEffectSet.WritesAmbientState
        };
    }

}
