namespace SharpProof.Worker;

// Z3 decides every effect claim over the Total program.
// A proof is a complete may-effect summary; a refutation names the first
// replayed violating site.
internal static class NativeEffectClaims
{
    internal static bool IsNative(WorkerEffectContractKind kind)
    {
        return kind is WorkerEffectContractKind.DoesNotThrow or WorkerEffectContractKind.AllowedExceptions or
            WorkerEffectContractKind.ZeroAllocations or WorkerEffectContractKind.EnforcePure or
            WorkerEffectContractKind.AllowedCapabilities or WorkerEffectContractKind.EffectContract;
    }

    internal static async Task<ImmutableArray<WorkerClaimResult>> VerifyAsync(CompilerCallablePreparation target,
        CallableEntryFeasibility entry, WorkerBudgets budgets, CancellationToken cancellationToken)
    {
        // A trusted complete boundary is a declared assumption about a body
        // SharpProof does not see; it is published as declared.
        var claims = target.EffectClaims.Where(claim => IsNative(claim.ContractKind) &&
            claim.Certainty != WorkerEffectEvidenceCertainty.TrustedCompleteBoundary).ToArray();
        // An invalid contract has nothing to verify, whatever the entry.
        bool Invalid(CompilerEffectClaimArtifact claim)
        { return target.Total is { } total && !total.ValidEffectClaimIds.Contains(claim.ClaimId); }
        var invalid = claims.Where(Invalid).Select(claim => Unknown(target, claim.ClaimId, WorkerClaimReason.UnsupportedContract)).ToArray();
        claims = [.. claims.Where(claim => !Invalid(claim))];
        if (claims.Length == 0)
        { return [.. invalid]; }
        return [.. invalid, .. await VerifyValidAsync(target, claims, entry, budgets, cancellationToken).ConfigureAwait(false)];
    }

    private static async Task<ImmutableArray<WorkerClaimResult>> VerifyValidAsync(CompilerCallablePreparation target,
        CompilerEffectClaimArtifact[] claims, CallableEntryFeasibility entry, WorkerBudgets budgets, CancellationToken cancellationToken)
    {
        if (entry.IsContradictory)
        {
            return [.. claims.Select(claim => CallableClaimResultAssembler.Contradictory(target, claim.ClaimId,
                WorkerEffectEvidenceCertainty.VacuousEntry, entry.ProofCore, entry.UsedAssumptionIds))];
        }
        if (entry.IsUnknown)
        { return [.. claims.Select(claim => Unknown(target, claim.ClaimId, entry.Reason))]; }
        var exceptions = await NativeExceptionEffectVerifier.VerifyClaimsAsync(target, budgets, cancellationToken).ConfigureAwait(false);
        var results = ImmutableArray.CreateBuilder<WorkerClaimResult>(claims.Length);
        foreach (var claim in claims)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
        return CallableClaimResultAssembler.Create(target, claimId, WorkerClaimOutcome.Unknown,
            reason == WorkerClaimReason.None ? WorkerClaimReason.SolverIncomplete : reason, WorkerEffectEvidenceCertainty.Unavailable);
    }

    private static WorkerClaimResult Project(CompilerCallablePreparation target, string claimId, PassiveCallableCheckResult check)
    {
        if (check.Outcome is ProvenOutcome && check.Reason == WorkerClaimReason.None)
        {
            var record = CallableClaimResultAssembler.Create(target, claimId, WorkerClaimOutcome.Proven,
                WorkerClaimReason.None, WorkerEffectEvidenceCertainty.CompleteMayEffectSummary, projectAssumptions: false);
            record.ProofCore = ["native-effect:" + claimId];
            record.Assumptions = CallableClaimResultAssembler.MarkAssumptionsUsed(target, UsedRequires(target, check.Core));
            return record;
        }
        if (check.Outcome is RefutedOutcome && check.Reason == WorkerClaimReason.None && Witness(target, check) is { } witness)
        {
            var record = CallableClaimResultAssembler.Create(target, claimId, WorkerClaimOutcome.Refuted,
                WorkerClaimReason.None, WorkerEffectEvidenceCertainty.DefiniteViolation);
            record.EffectWitness = witness;
            return record;
        }
        return Unknown(target, claimId, check.Outcome is RefutedOutcome ? WorkerClaimReason.CounterexampleNotReplayable : check.Reason);
    }

    // Requires clauses are labelled by ordinal in the passive VC.
    private static HashSet<string> UsedRequires(CompilerCallablePreparation target, ImmutableArray<string> core)
    {
        var requires = target.Total!.Clauses.Where(clause => clause.Kind == CompilerContractKind.Requires).ToArray();
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (var ordinal = 0; ordinal < requires.Length; ordinal++)
        {
            if (core.Contains("requires:" + ordinal.ToString(CultureInfo.InvariantCulture)) && requires[ordinal].AssumptionId is { } id)
            { used.Add(id); }
        }
        return used;
    }

    // A witness names a source site; a site without a source position (an
    // inlined metadata body) leaves the claim Unknown.
    private static WorkerEffectViolationWitness? Witness(CompilerCallablePreparation target, PassiveCallableCheckResult check)
    {
        var program = target.Total!.Program;
        var factory = program.Factory;
        var (site, kind, detail, effects, capabilities) = check switch
        {
            { ExceptionWitness: { Kind: IrExceptionKind.Explicit, Site: { } thrown } } => (thrown, "explicit-throw",
                TypeName(ExplicitThrowSites.Types(factory, thrown).FirstOrDefault() ?? "System.Exception"), WorkerEffectSet.Throws, WorkerEffectCapabilitySet.None),
            { ExceptionWitness: { } exception } => (exception.Site, "implicit-throw", ExceptionName(exception.Kind),
                WorkerEffectSet.Throws, WorkerEffectCapabilitySet.None),
            { AllocationWitness: { } allocation } => (allocation, IsArray(program, allocation) ? "managed-array-allocation" : "managed-allocation",
                Describe(factory, allocation), WorkerEffectSet.Allocates, WorkerEffectCapabilitySet.None),
            { LockWitness: { } synchronization } => (synchronization, "synchronization-lock", Describe(factory, synchronization),
                WorkerEffectSet.Synchronizes, WorkerEffectCapabilitySet.Synchronization),
            { WriteWitness: { } write } => (write, "nonlocal-write", Describe(factory, write),
                WriteEffect(program, write), WorkerEffectCapabilitySet.None),
            _ => ((OperationId?)null, "", "", WorkerEffectSet.None, WorkerEffectCapabilitySet.None)
        };
        if (site == null || factory.GetOperationInfo(site.Value).SourceSpan is not { Line: > 0 } span)
        { return null; }
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

    // Identities are assembly-qualified as "assembly::type"; a witness shows the type.
    private static string TypeName(string identity)
    {
        var separator = identity.LastIndexOf("::", StringComparison.Ordinal);
        return separator < 0 ? identity : identity[(separator + 2)..];
    }

    private static string Describe(IrFactory factory, OperationId site)
    {
        return factory.GetOperationInfo(site).Description is { } description ? factory.GetString(description) : "operation";
    }

    private static bool IsArray(IrProgram program, OperationId site)
    {
        return program.Blocks.SelectMany(block => block.Instructions).OfType<IrAllocationInstruction>()
            .Any(allocation => allocation.Operation == site && allocation.Length != null);
    }

    private static WorkerEffectSet WriteEffect(IrProgram program, OperationId site)
    {
        var region = program.Blocks.SelectMany(block => block.Instructions).OfType<IrWriteInstruction>()
            .FirstOrDefault(write => write.Operation == site)?.Region;
        return region switch
        {
            IrWriteRegion.Parameter => WorkerEffectSet.WritesArgumentState,
            IrWriteRegion.Field => WorkerEffectSet.WritesReceiverState,
            IrWriteRegion.Static => WorkerEffectSet.WritesStaticState,
            _ => WorkerEffectSet.WritesAmbientState
        };
    }

    private static string ExceptionName(IrExceptionKind kind)
    {
        return kind switch
        {
            IrExceptionKind.NullReference => "System.NullReferenceException",
            IrExceptionKind.DivideByZero => "System.DivideByZeroException",
            IrExceptionKind.Overflow => "System.OverflowException",
            IrExceptionKind.IndexOutOfRange => "System.IndexOutOfRangeException",
            IrExceptionKind.InvalidCast => "System.InvalidCastException",
            IrExceptionKind.Argument => "System.ArgumentException",
            _ => "System.Exception"
        };
    }
}
