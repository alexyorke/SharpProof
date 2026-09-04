namespace SharpProof.Analyzer;

internal static class EffectContractDiagnostics
{
    internal static void ValidateArguments(
        IMethodSymbol method, AnalyzerSession session, Action<Diagnostic> reportDiagnostic)
    {
        var attributes = ContractSelectionInventory.GetCallableAttributes(method).ToImmutableArray();
        var location = method.Locations.FirstOrDefault() ?? Location.None;
        _ = DecodeCapabilities(
            Select(attributes, session.Attributes.AllowedCapabilities), location, session, reportDiagnostic);
        _ = DecodeAllowedExceptions(
            Select(attributes, session.Attributes.AllowedExceptions), session.Compilation, location, session, reportDiagnostic);
        if (!attributes.Any(attribute =>
                ContractSelectionInventory.Is(attribute, session.Attributes.EffectContract)))
        {
            return;
        }

        var contract = session.ResolveEffectContract(method);
        if (contract is not { Kind: EffectContractResolutionKind.Invalid } ||
            contract.InvalidAttributes.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var invalid in contract.InvalidAttributes)
        {
            ReportInvalidOnce(
                invalid.Attribute, "[EffectContract]", invalid.Reason,
                location, session, reportDiagnostic);
        }
    }

    internal static AnalyzerSemanticOutcome Analyze(
        IMethodSymbol method, SyntaxNode declaration, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic, CancellationToken cancellationToken)
    {
        var evaluations = Evaluate(
            method, AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(declaration),
            session, reportDiagnostic, cancellationToken);
        if (evaluations.IsDefaultOrEmpty)
        {
            return AnalyzerSemanticOutcome.NotApplicable;
        }

        var hasRefuted = false;
        var allProven = true;
        foreach (var evaluation in evaluations)
        {
            if (evaluation.Diagnostic != null)
            {
                reportDiagnostic(Diagnostic.Create(
                    evaluation.Diagnostic,
                    evaluation.DiagnosticLocation,
                    evaluation.DiagnosticArguments));
            }

            if (evaluation.Outcome == EffectEvaluationOutcome.Refuted)
            {
                hasRefuted = true;
            }
            else if (evaluation.Outcome != EffectEvaluationOutcome.Proven)
            {
                allProven = false;
            }
        }

        if (hasRefuted)
        {
            return AnalyzerSemanticOutcome.Refuted;
        }

        return allProven
            ? AnalyzerSemanticOutcome.Proven
            : AnalyzerSemanticOutcome.Unknown;
    }

    internal static ImmutableArray<EffectClaimEvaluation> Evaluate(
        IMethodSymbol method, Location location, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic, CancellationToken cancellationToken)
    {
        var attributes = ContractSelectionInventory.GetCallableAttributes(method).ToImmutableArray();
        var pure = Select(attributes, session.Attributes.EnforcePure);
        var zeroAllocations = Select(attributes, session.Attributes.ZeroAllocations);
        var allowedCapabilities = Select(attributes, session.Attributes.AllowedCapabilities);
        var noThrow = Select(attributes, session.Attributes.DoesNotThrow);
        var allowedExceptions = Select(attributes, session.Attributes.AllowedExceptions);
        var summaryContracts = Select(attributes, session.Attributes.EffectContract);
        if (pure.IsDefaultOrEmpty &&
            zeroAllocations.IsDefaultOrEmpty &&
            allowedCapabilities.IsDefaultOrEmpty &&
            noThrow.IsDefaultOrEmpty &&
            allowedExceptions.IsDefaultOrEmpty &&
            summaryContracts.IsDefaultOrEmpty)
        {
            return [];
        }

        var capabilities = DecodeCapabilities(allowedCapabilities, location, session, reportDiagnostic);
        var exceptions = DecodeAllowedExceptions(
            allowedExceptions, session.Compilation, location, session, reportDiagnostic);
        cancellationToken.ThrowIfCancellationRequested();
        var contract = new EffectContractResolution(
            EffectContractResolutionKind.Missing,
            EffectSummary.Bottom);
        if (!summaryContracts.IsDefaultOrEmpty)
        {
            contract = session.ResolveEffectContract(method);
        }
        var bodyless = method is { IsAbstract: true } or { IsExtern: true };
        var bodylessTrusted = bodyless && contract.Kind == EffectContractResolutionKind.Valid;
        var result = session.AnalyzeEffects(method, cancellationToken);
        var summary = result.Summary;
        var projection = result.Projection;
        var entryIsBottom = ManagedAbstractFlow
            .ForCompilation(session.Compilation)
            .CreateEntryState(method)
            .IsBottom;
        if (summary is
            { AnalysisIncompleteReason: not EffectAnalysisIncompleteReason.None } &&
            summaryContracts.IsDefaultOrEmpty)
        {
            reportDiagnostic(Diagnostic.Create(
                GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule,
                location,
                method.Name,
                "ManagedAbstractFlow:" +
                EffectContractMappings.EvidenceName(summary.AnalysisIncompleteReason)));
        }

        var requires = session.BindRequires(method);
        var direct = requires.IsSuccess && requires.Contracts!.Clauses.IsDefaultOrEmpty
            ? result.DirectWitnesses
            : [];
        var summaryEvidence = CreateSummaryEvidence(summary);
        var flowComplete = summary is
        { AnalysisIncompleteReason: EffectAnalysisIncompleteReason.None };
        var entrySummaryReachable = !entryIsBottom && !summary.IsBottom;
        var facetComplete = flowComplete && entrySummaryReachable;
        var purityComplete = facetComplete && !summary.Reads.IsUnknown &&
            !summary.Writes.IsUnknown && !summary.Capabilities.IsUnknown;
        var allocationComplete = facetComplete &&
            summary.Allocation != EffectAllocationKind.Unknown;
        var capabilityComplete =
            facetComplete && !summary.Capabilities.IsUnknown;
        var exceptionComplete =
            facetComplete && !summary.Throws.IncludesUnknown;
        var disallowedCapabilities = projection.Capabilities & ~capabilities.Value;
        var disallowedExceptions = exceptions.IsValid
            ? summary.Throws.Types.Where(type => !IsAllowed(type, exceptions.Types)).ToImmutableArray()
            : [];
        var declaredProjection = summaryContracts.IsDefaultOrEmpty
            ? default
            : EffectSummaryProjector.Project(contract.Summary);
        var declaredValid = contract.Kind != EffectContractResolutionKind.Invalid;
        var declaredComplete = entrySummaryReachable && projection.IsComplete &&
            contract.Kind is not (EffectContractResolutionKind.Incomplete or EffectContractResolutionKind.Missing);
        var incompleteReason = MapIncompleteReason(summary);
        EffectDirectWitness? purityViolation = null;
        EffectDirectWitness? allocationViolation = null;
        EffectDirectWitness? capabilityViolation = null;
        EffectDirectWitness? noThrowViolation = null;
        EffectDirectWitness? exceptionViolation = null;
        EffectDirectWitness? declaredViolation = null;
        var declaredViolationApplicable = declaredValid && contract.Kind is not (
            EffectContractResolutionKind.Incomplete or EffectContractResolutionKind.Missing);
        foreach (var witness in direct)
        {
            if (purityViolation == null &&
                EffectContractMappings.IsPurityViolation(witness))
            {
                purityViolation = witness;
            }
            if (allocationViolation == null &&
                (witness.Effects & EffectContractKind.Allocates) != 0)
            {
                allocationViolation = witness;
            }
            if (capabilityViolation == null &&
                (witness.Capabilities & ~capabilities.Value) != 0)
            {
                capabilityViolation = witness;
            }
            if (noThrowViolation == null &&
                (witness.Effects & EffectContractKind.Throws) != 0)
            {
                noThrowViolation = witness;
            }
            if (exceptionViolation == null &&
                witness.ExceptionType != null &&
                !IsAllowed(witness.ExceptionType, exceptions.Types))
            {
                exceptionViolation = witness;
            }
            if (declaredViolationApplicable && declaredViolation == null &&
                EffectContractMappings.Violates(witness, contract.Summary))
            {
                declaredViolation = witness;
            }
        }

        var evaluations = ImmutableArray.CreateBuilder<EffectClaimEvaluation>(6);
        Add(pure, EffectEvaluationContractKind.EnforcePure, purityComplete,
            EffectContractMappings.IsObservablePure(summary),
            GeneratedDiagnosticDescriptors.PurityNotVerifiedRule, [method.Name],
            "constraint=observable-pure",
            purityViolation, EffectClaimConstraint.Empty);
        Add(zeroAllocations, EffectEvaluationContractKind.ZeroAllocations, allocationComplete,
            summary.Allocation == EffectAllocationKind.None,
            GeneratedDiagnosticDescriptors.ZeroAllocationsNotVerifiedRule,
            [method.Name, allocationComplete
                ? "may-effect summary includes allocation: " + summary.Allocation
                : FormatUnknown(summary, "AllocationUnknown")],
            "constraint=allocation:none",
            allocationViolation,
            EffectClaimConstraint.Empty);
        Add(allowedCapabilities, EffectEvaluationContractKind.AllowedCapabilities, capabilityComplete,
            disallowedCapabilities == EffectContractCapabilityKind.None,
            GeneratedDiagnosticDescriptors.CapabilityUnknownRule,
            ["method summary", method.Name, capabilityComplete
                ? "may-effect summary includes disallowed capabilities: " + disallowedCapabilities
                : FormatUnknown(summary, "CapabilitySetUnknown")],
            "allowed.capabilities=" + EffectContractMappings.EvidenceName(capabilities.Value),
            capabilityViolation,
            new EffectClaimConstraint(EffectContractKind.None, capabilities.Value, []),
            capabilities.IsValid);
        Add(noThrow, EffectEvaluationContractKind.DoesNotThrow, exceptionComplete, summary.Throws.IsEmpty,
            GeneratedDiagnosticDescriptors.ExceptionContractNotVerifiedRule,
            [method.Name, "[DoesNotThrow]", exceptionComplete
                ? "may-effect summary includes disallowed exceptions: " +
                  FormatDiagnosticTypes(summary.Throws.Types)
                : FormatUnknown(summary, "ExceptionSetUnknown")],
            "allowed.exceptions=[]",
            noThrowViolation,
            EffectClaimConstraint.Empty);
        Add(allowedExceptions, EffectEvaluationContractKind.AllowedExceptions, exceptionComplete,
            disallowedExceptions.IsDefaultOrEmpty,
            GeneratedDiagnosticDescriptors.ExceptionContractNotVerifiedRule,
            [method.Name, "[AllowedExceptions]", exceptionComplete
                ? "may-effect summary includes disallowed exceptions: " +
                  FormatDiagnosticTypes(disallowedExceptions)
                : FormatUnknown(summary, "ExceptionSetUnknown")],
            "allowed.exceptions=[" + FormatTypes(exceptions.Types) + "]",
            exceptionViolation,
            new EffectClaimConstraint(
                EffectContractKind.None, EffectContractCapabilityKind.None, exceptions.Types),
            exceptions.IsValid);
        Add(summaryContracts, EffectEvaluationContractKind.EffectContract, declaredComplete,
            bodyless
                ? contract.Kind == EffectContractResolutionKind.Valid && declaredProjection.IsComplete
                : contract.Kind is not (
                    EffectContractResolutionKind.Incomplete or EffectContractResolutionKind.Missing) &&
                  EffectContractMappings.Covers(summary, contract.Summary),
            !bodyless || contract.Kind == EffectContractResolutionKind.Valid
                ? GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule
                : null,
            [method.Name, contract.Kind == EffectContractResolutionKind.Incomplete
                ? "IncompleteEffectContract"
                : summary is
                    { AnalysisIncompleteReason: not EffectAnalysisIncompleteReason.None }
                    ? "ManagedAbstractFlow:" +
                      EffectContractMappings.EvidenceName(summary.AnalysisIncompleteReason)
                    : "EffectContractDoesNotCoverBodySummary"],
            summaryEvidence + ";declared=" + CreateSummaryEvidence(contract.Summary),
            declaredViolationApplicable ? declaredViolation : null,
            new EffectClaimConstraint(declaredProjection.Effects, declaredProjection.Capabilities,
                contract.Summary.Throws.Types),
            declaredValid,
            bodylessTrusted);
        return evaluations.ToImmutable();

        void Add(
            ImmutableArray<AttributeData> selected, EffectEvaluationContractKind kind,
            bool complete, bool isEstablished, DiagnosticDescriptor? diagnostic,
            object[] arguments, string evidence, EffectDirectWitness? candidateViolation,
            EffectClaimConstraint constraint, bool valid = true, bool trusted = false)
        {
            if (selected.IsDefaultOrEmpty)
            {
                return;
            }

            if (kind != EffectEvaluationContractKind.EffectContract)
            {
                evidence = SummaryFacetEvidence(summaryEvidence, complete, evidence);
            }

            var established = valid && complete && isEstablished;
            var violation = valid && !established ? candidateViolation : null;
            var projected = EffectEvaluationProjections.Classify(
                established, violation != null, valid, complete, trusted, incompleteReason);
            var (outcome, reason, certainty) =
                EffectEvaluationProducerTupleCatalog.Require(
                    projected.Outcome, projected.Reason, projected.Certainty);
            var claimDiagnostic =
                summary is
                { AnalysisIncompleteReason: not EffectAnalysisIncompleteReason.None } &&
                violation == null &&
                diagnostic != GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule
                    ? null
                    : diagnostic;
            evaluations.Add(new EffectClaimEvaluation(
                kind, selected, outcome,
                reason, certainty, AddWitnessEvidence(evidence, violation), violation, constraint,
                valid && !established ? claimDiagnostic : null, location, arguments));
        }
    }

    private static EffectEvaluationReason MapIncompleteReason(
        EffectSummary summary)
    {
        return EffectEvaluationProjections.MapIncompleteReason(
            summary.AnalysisIncompleteReason);
    }

    private static (EffectContractCapabilityKind Value, bool IsValid) DecodeCapabilities(
        ImmutableArray<AttributeData> attributes, Location fallbackLocation,
        AnalyzerSession session, Action<Diagnostic> reportDiagnostic)
    {
        var value = EffectContractCapabilityKind.None;
        foreach (var attribute in attributes)
        {
            if (attribute.ConstructorArguments.Length == 1 &&
                EffectContractMetadata.TryConvertInt64(
                    attribute.ConstructorArguments[0].Value, out var raw) &&
                raw >= 0 &&
                ((EffectContractCapabilityKind)raw & ~EffectContractMetadata.AllCapabilities) == 0)
            {
                value |= (EffectContractCapabilityKind)raw;
                continue;
            }
            ReportInvalidOnce(
                attribute, "[AllowedCapabilities]",
                "expected a defined SharpProofCapability flags value",
                fallbackLocation, session, reportDiagnostic);
            return (EffectContractCapabilityKind.None, false);
        }
        return (value, true);
    }

    private static (ImmutableArray<INamedTypeSymbol> Types, bool IsValid) DecodeAllowedExceptions(
        ImmutableArray<AttributeData> attributes, Compilation compilation, Location fallbackLocation,
        AnalyzerSession session, Action<Diagnostic> reportDiagnostic)
    {
        var exceptionType = compilation.GetTypeByMetadataName(FrameworkTypeMetadataNames.Exception);
        var types = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var valid = true;
        foreach (var attribute in attributes)
        {
            var arguments = attribute.ConstructorArguments;
            var values = arguments.Length == 1 && arguments[0].Kind == TypedConstantKind.Array
                ? arguments[0].Values
                : default;
            if (exceptionType != null &&
                !values.IsDefault &&
                values.All(argument => argument.Value is INamedTypeSymbol type &&
                    !type.IsUnboundGenericType &&
                    EffectTypeFacts.IsDerivedFrom(type, exceptionType)))
            {
                types.AddRange(values.Select(static argument => (INamedTypeSymbol)argument.Value!));
                continue;
            }
            ReportInvalidOnce(
                attribute, "[AllowedExceptions]",
                "expected only closed System.Exception-derived types",
                fallbackLocation, session, reportDiagnostic);
            valid = false;
        }
        return valid ? (types.ToImmutable(), true) : ([], false);
    }

    private static void ReportInvalidOnce(
        AttributeData attribute, string contract, string reason, Location fallbackLocation,
        AnalyzerSession session, Action<Diagnostic> reportDiagnostic)
    {
        if (session.TryMarkAttributeValidated(attribute))
        {
            reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                contract, "<invalid>", reason, GetLocation(attribute, fallbackLocation)));
        }
    }

    private static string SummaryFacetEvidence(string summary, bool complete, string constraint)
    {
        return summary + ";facet.complete=" +
        complete.ToString(CultureInfo.InvariantCulture) + ";" + constraint;
    }

    private static string AddWitnessEvidence(string evidence, EffectDirectWitness? witness)
    {
        return witness == null
            ? evidence
            : evidence + ";witness.kind=" + witness.Kind +
              ";witness.detail=" + witness.Detail +
              ";witness.start=" + witness.Location.SourceSpan.Start.ToString(CultureInfo.InvariantCulture) +
              ";witness.length=" + witness.Location.SourceSpan.Length.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatUnknown(EffectSummary summary, string facet)
    {
        return facet + ": " +
        (summary is { AnalysisIncompleteReason: not EffectAnalysisIncompleteReason.None }
            ? EffectContractMappings.EvidenceName(summary.AnalysisIncompleteReason)
            : summary.Uncertainty != EffectUncertainty.None
            ? EffectContractMappings.EvidenceName(summary.Uncertainty)
            : summary.Completeness != EffectCompleteness.Complete
                ? "IncompleteSummary"
                : "UnknownFacet");
    }

    private static bool IsAllowed(
        INamedTypeSymbol thrown, ImmutableArray<INamedTypeSymbol> allowed)
    {
        return allowed.Any(candidate => EffectTypeFacts.IsDerivedFrom(thrown, candidate));
    }

    private static string CreateSummaryEvidence(EffectSummary summary)
    {
        var projection = EffectSummaryProjector.Project(summary);
        return string.Join(";", [
            "actual.effects=" + EffectContractMappings.EvidenceName(projection.Effects),
            "actual.capabilities=" + EffectContractMappings.EvidenceName(projection.Capabilities),
            "actual.exceptions=[" + FormatTypes(summary.Throws.Types) + "]",
            "actual.exceptionsUnknown=" + summary.Throws.IncludesUnknown.ToString(CultureInfo.InvariantCulture),
            "actual.complete=" + projection.IsComplete.ToString(CultureInfo.InvariantCulture),
            "actual.allocation=" + EffectContractMappings.EvidenceName(summary.Allocation),
            "actual.completeness=" + EffectContractMappings.EvidenceName(summary.Completeness),
            "actual.uncertainty=" + EffectContractMappings.EvidenceName(summary.Uncertainty),
            "actual.analysisIncompleteReason=" +
            EffectContractMappings.EvidenceName(summary.AnalysisIncompleteReason)
        ]);
    }

    private static string FormatTypes(IEnumerable<INamedTypeSymbol> types)
    {
        return string.Join(",", types
            .Select(CompilerExceptionTypeIdentity.Encode)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal));
    }

    private static string FormatDiagnosticTypes(
        IEnumerable<INamedTypeSymbol> types)
    {
        return string.Join(", ", types.Select(static type =>
                CompilerIdentityBridge.CreateTypeDisplay(type))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal));
    }

    private static Location GetLocation(AttributeData attribute, Location fallback)
    {
        return attribute.ApplicationSyntaxReference?.SyntaxTree.GetLocation(
            attribute.ApplicationSyntaxReference.Span) ?? fallback;
    }

    private static ImmutableArray<AttributeData> Select(
        ImmutableArray<AttributeData> attributes, INamedTypeSymbol? expected)
    {
        return [.. attributes.Where(attribute => ContractSelectionInventory.Is(attribute, expected))];
    }
}

internal sealed partial record EffectClaimConstraint
{
    internal static EffectClaimConstraint Empty
    {
        get;
    } =
        new(EffectContractKind.None, EffectContractCapabilityKind.None, []);
}
