namespace SharpProof.Compiler.Semantics;

// Declared claims and trusted boundaries, independent of diagnostics or advisory execution.
internal static class EffectClaimDeclarations
{
    internal static ImmutableArray<EffectClaimDeclaration> Declare(
        IMethodSymbol method, CompilerSemanticSession session, CancellationToken cancellationToken)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        session = ArgumentNullGuard.NotNull(session, nameof(session));
        var attributes = ContractSelectionInventory.GetCallableAttributes(method).ToImmutableArray();
        var summaryContracts = Select(attributes, session.Attributes.EffectContract);
        var capabilitiesAttributes = Select(attributes, session.Attributes.AllowedCapabilities);
        var allowedExceptions = Select(attributes, session.Attributes.AllowedExceptions);
        var capabilities = EffectContractArguments.DecodeCapabilities(capabilitiesAttributes, static (_, _, _) => { });
        var exceptions = EffectContractArguments.DecodeAllowedExceptions(allowedExceptions, session.Compilation, static (_, _, _) => { });
        cancellationToken.ThrowIfCancellationRequested();
        var contract = summaryContracts.IsDefaultOrEmpty
            ? new EffectContractResolution(EffectContractResolutionKind.Missing, EffectSummary.Bottom)
            : session.ResolveEffectContract(method);
        var declared = summaryContracts.IsDefaultOrEmpty ? default : EffectSummaryProjector.Project(contract.Summary);
        var bodyless = method is { IsAbstract: true } or { IsExtern: true };
        var trusted = bodyless && contract.Kind == EffectContractResolutionKind.Valid && declared.IsComplete;
        var throws = contract.Summary.Throws;
        var evaluations = ImmutableArray.CreateBuilder<EffectClaimDeclaration>(6);
        Add(Select(attributes, session.Attributes.EnforcePure), EffectEvaluationContractKind.EnforcePure, EffectClaimConstraint.Empty,
            established: EffectContractMappings.IsObservablePure(contract.Summary));
        Add(Select(attributes, session.Attributes.ZeroAllocations), EffectEvaluationContractKind.ZeroAllocations, EffectClaimConstraint.Empty,
            established: (declared.Effects & EffectContractKind.Allocates) == 0);
        Add(capabilitiesAttributes, EffectEvaluationContractKind.AllowedCapabilities,
            new EffectClaimConstraint(EffectContractKind.None, capabilities.Value, []), capabilities.IsValid,
            (declared.Capabilities & ~capabilities.Value) == 0);
        Add(Select(attributes, session.Attributes.DoesNotThrow), EffectEvaluationContractKind.DoesNotThrow, EffectClaimConstraint.Empty,
            established: throws.IsEmpty);
        Add(allowedExceptions, EffectEvaluationContractKind.AllowedExceptions,
            new EffectClaimConstraint(EffectContractKind.None, EffectContractCapabilityKind.None, exceptions.Types), exceptions.IsValid,
            !throws.IncludesUnknown && throws.Types.All(type => type is null ? throw new ArgumentNullException(nameof(type)) : exceptions.Types.Any(allowed => EffectTypeFacts.IsDerivedFrom(
                type ?? throw new ArgumentNullException(nameof(type)), allowed ?? throw new ArgumentNullException(nameof(allowed))))));
        Add(summaryContracts, EffectEvaluationContractKind.EffectContract,
            new EffectClaimConstraint(declared.Effects, declared.Capabilities, contract.Summary.Throws.Types),
            contract.Kind != EffectContractResolutionKind.Invalid, true);
        foreach (var attribute in Select(attributes, session.Attributes.ResourceUpperBound))
        {
            var resource = ResourceUpperBoundContract.Decode(method, attribute);
            var constraint = EffectClaimConstraint.Empty with
            {
                ResourceUpperBound = resource ?? new ResourceBoundConstraint(ResourceCostMetric.LoopBodyEntries, ResourceExpression.Zero)
            };
            var valid = resource is not null;
            evaluations.Add(new(EffectEvaluationContractKind.ResourceUpperBound, [attribute], EffectEvaluationOutcome.Unknown,
                valid ? EffectEvaluationReason.EffectSummaryIncomplete : EffectEvaluationReason.UnsupportedContract,
                valid ? EffectEvaluationCertainty.IncompleteMayEffectSummary : EffectEvaluationCertainty.Unavailable,
                "native-resource", constraint));
        }
        return evaluations.ToImmutable();

        void Add(ImmutableArray<AttributeData> selected, EffectEvaluationContractKind kind, EffectClaimConstraint constraint,
            bool valid = true, bool established = false)
        {
            constraint = RepositoryParameterGuard.NotNull(constraint, nameof(constraint));
            if (selected.IsDefaultOrEmpty)
            { return; }
            established &= trusted && valid;
            var projected = EffectEvaluationProjections.Classify(
                established, false, valid, trusted, trusted, EffectEvaluationReason.EffectSummaryIncomplete);
            var (outcome, reason, certainty) = EffectEvaluationProducerTupleCatalog.Require(
                projected.Outcome, projected.Reason, projected.Certainty);
            evaluations.Add(new EffectClaimDeclaration(kind, selected, outcome, reason, certainty,
                trusted ? "trusted-boundary" : "native", constraint));
        }
    }
    private static ImmutableArray<AttributeData> Select(
        ImmutableArray<AttributeData> attributes, INamedTypeSymbol? expected)
    {
        RepositoryParameterGuard.RequireInitialized(attributes.IsDefault, nameof(attributes), "Array must be initialized.");
        return [.. attributes.Where(attribute => ContractSelectionInventory.Is(
            attribute ?? throw new ArgumentNullException(nameof(attribute)), expected))];
    }
}
