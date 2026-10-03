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

    // The effect claims a callable declares, without analyzing its body: Z3
    // decides them in the worker. A complete EffectContract on a bodyless
    // declaration is a trusted boundary: it is established as declared, and
    // it establishes every other claim it satisfies.
    internal static ImmutableArray<EffectClaimEvaluation> Declare(
        IMethodSymbol method, Location location, AnalyzerSession session, CancellationToken cancellationToken)
    {
        var attributes = ContractSelectionInventory.GetCallableAttributes(method).ToImmutableArray();
        var summaryContracts = Select(attributes, session.Attributes.EffectContract);
        var capabilitiesAttributes = Select(attributes, session.Attributes.AllowedCapabilities);
        var allowedExceptions = Select(attributes, session.Attributes.AllowedExceptions);
        var capabilities = DecodeCapabilities(capabilitiesAttributes, location, session, static _ => { });
        var exceptions = DecodeAllowedExceptions(allowedExceptions, session.Compilation, location, session, static _ => { });
        cancellationToken.ThrowIfCancellationRequested();
        var contract = summaryContracts.IsDefaultOrEmpty
            ? new EffectContractResolution(EffectContractResolutionKind.Missing, EffectSummary.Bottom)
            : session.ResolveEffectContract(method);
        var declared = summaryContracts.IsDefaultOrEmpty ? default : EffectSummaryProjector.Project(contract.Summary);
        var bodyless = method is { IsAbstract: true } or { IsExtern: true };
        var trusted = bodyless && contract.Kind == EffectContractResolutionKind.Valid && declared.IsComplete;
        var throws = contract.Summary.Throws;
        var evaluations = ImmutableArray.CreateBuilder<EffectClaimEvaluation>(6);
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
            !throws.IncludesUnknown && throws.Types.All(type => exceptions.Types.Any(allowed => EffectTypeFacts.IsDerivedFrom(type, allowed))));
        Add(summaryContracts, EffectEvaluationContractKind.EffectContract,
            new EffectClaimConstraint(declared.Effects, declared.Capabilities, contract.Summary.Throws.Types),
            contract.Kind != EffectContractResolutionKind.Invalid, true);
        return evaluations.ToImmutable();

        void Add(ImmutableArray<AttributeData> selected, EffectEvaluationContractKind kind, EffectClaimConstraint constraint,
            bool valid = true, bool established = false)
        {
            if (selected.IsDefaultOrEmpty)
            { return; }
            established &= trusted && valid;
            var projected = EffectEvaluationProjections.Classify(
                established, false, valid, trusted, trusted, EffectEvaluationReason.EffectSummaryIncomplete);
            var (outcome, reason, certainty) = EffectEvaluationProducerTupleCatalog.Require(
                projected.Outcome, projected.Reason, projected.Certainty);
            evaluations.Add(new EffectClaimEvaluation(kind, selected, outcome, reason, certainty,
                trusted ? "trusted-boundary" : "native", null, constraint, null, Location.None, []));
        }
    }

    // Advisory feedback for the effect claims a callable declares. The
    // analyzer never proves a claim: it reports the first site that could
    // violate each one in the same Total IR body the worker verifies, and
    // Z3 decides the claim in the build.
    internal static ImmutableArray<EffectClaimEvaluation> Evaluate(
        IMethodSymbol method, Location location, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic, CancellationToken cancellationToken)
    {
        var attributes = ContractSelectionInventory.GetCallableAttributes(method).ToImmutableArray();
        _ = DecodeCapabilities(Select(attributes, session.Attributes.AllowedCapabilities), location, session, reportDiagnostic);
        _ = DecodeAllowedExceptions(Select(attributes, session.Attributes.AllowedExceptions), session.Compilation, location, session,
            reportDiagnostic);
        var evaluations = Declare(method, location, session, cancellationToken);
        if (evaluations.All(evaluation => evaluation.Outcome == EffectEvaluationOutcome.Proven) ||
            session.Compilation is not CSharpCompilation compilation ||
            method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) is not { } declaration)
        { return evaluations; }
        // A bodyless declaration has only its trusted contract to report on.
        if (method is { IsAbstract: true } or { IsExtern: true })
        {
            return [.. evaluations.Select(evaluation => evaluation.Outcome != EffectEvaluationOutcome.Proven &&
                evaluation.Certainty == EffectEvaluationCertainty.TrustedCompleteBoundary
                ? Report(evaluation, method.Name, "its trusted effect contract does not establish it") : evaluation)];
        }
        var analysis = AdvisoryEffectSites.Analyze(compilation, method, declaration, cancellationToken);
        if (analysis.Gap != null)
        {
            reportDiagnostic(Diagnostic.Create(GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule, location,
                method.Name, "Advisory:" + analysis.Gap));
            return evaluations;
        }
        return [.. evaluations.Select(evaluation =>
            evaluation.Outcome != EffectEvaluationOutcome.Proven &&
            AdvisoryEffectClaims.FindViolation(evaluation.Kind, evaluation.Constraint, analysis.Sites, compilation) is { } violation
                ? Report(evaluation, SiteText(violation.Site.Location), violation.Detail) : evaluation)];

        EffectClaimEvaluation Report(EffectClaimEvaluation evaluation, string subject, string detail)
        {
            var described = "'" + subject + "' " + detail;
            var (rule, arguments) = evaluation.Kind switch
            {
                EffectEvaluationContractKind.EnforcePure => (GeneratedDiagnosticDescriptors.PurityNotVerifiedRule, new object[] { method.Name }),
                EffectEvaluationContractKind.ZeroAllocations => (GeneratedDiagnosticDescriptors.ZeroAllocationsNotVerifiedRule,
                    new object[] { method.Name, described }),
                EffectEvaluationContractKind.AllowedCapabilities => (GeneratedDiagnosticDescriptors.CapabilityUnknownRule,
                    new object[] { subject, method.Name, detail }),
                EffectEvaluationContractKind.DoesNotThrow => (GeneratedDiagnosticDescriptors.ExceptionContractNotVerifiedRule,
                    new object[] { method.Name, "[DoesNotThrow]", described }),
                EffectEvaluationContractKind.AllowedExceptions => (GeneratedDiagnosticDescriptors.ExceptionContractNotVerifiedRule,
                    new object[] { method.Name, "[AllowedExceptions]", described }),
                _ => (GeneratedDiagnosticDescriptors.EffectContractNotProvenRule, new object[] { method.Name, described })
            };
            return evaluation with { Diagnostic = rule, DiagnosticLocation = location, DiagnosticArguments = arguments };
        }
    }

    private static string SiteText(Location location)
    {
        var text = location.SourceTree?.GetText().ToString(location.SourceSpan) ?? "";
        text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= 80 ? text : text.Substring(0, 77) + "...";
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

    internal static string FormatTypes(IEnumerable<INamedTypeSymbol> types)
    {
        return string.Join(",", types
            .Select(FormatExceptionType)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal));
    }

    private static string FormatExceptionType(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Error ||
            string.IsNullOrEmpty(
                DocumentationCommentId.CreateReferenceId(type)))
        {
            return "<error-type>";
        }

        return CompilerExceptionTypeIdentity.Encode(type);
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
