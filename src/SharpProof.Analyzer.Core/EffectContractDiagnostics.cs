namespace SharpProof.Analyzer;

internal static class EffectContractDiagnostics
{
    internal static void ValidateArguments(
        IMethodSymbol method, AnalyzerSession session, Action<Diagnostic> reportDiagnostic)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        session = ArgumentNullGuard.NotNull(session, nameof(session));
        reportDiagnostic = ArgumentNullGuard.NotNull(reportDiagnostic, nameof(reportDiagnostic));
        var attributes = ContractSelectionInventory.GetCallableAttributes(method).ToImmutableArray();
        var location = method.Locations.FirstOrDefault() ?? Location.None;
        _ = DecodeCapabilities(
            Select(attributes, session.Attributes.AllowedCapabilities), location, session, reportDiagnostic);
        _ = DecodeAllowedExceptions(
            Select(attributes, session.Attributes.AllowedExceptions), session.Compilation, location, session, reportDiagnostic);
        if (!attributes.Any(attribute =>
                attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (ContractSelectionInventory.Is(attribute, session.Attributes.EffectContract))))
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
        declaration = RepositoryParameterGuard.NotNull(declaration, nameof(declaration));
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        reportDiagnostic = RepositoryParameterGuard.NotNull(reportDiagnostic, nameof(reportDiagnostic));
        session = RepositoryParameterGuard.NotNull(session, nameof(session));
        var evaluations = Evaluate(
            method, AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(declaration),
            session, reportDiagnostic, cancellationToken, out var abstained);
        if (evaluations.IsDefaultOrEmpty)
        {
            return AnalyzerSemanticOutcome.NotApplicable;
        }
        if (abstained)
        {
            return AnalyzerSemanticOutcome.Abstained;
        }

        var allProven = true;
        foreach (var evaluation in evaluations)
        {
            if (evaluation.Diagnostic != null)
            {
                reportDiagnostic(Diagnostic.Create(
                    evaluation.Diagnostic,
                    evaluation.DiagnosticLocation,
                    evaluation.DiagnosticArguments.ToArray()));
            }

            // Declare establishes only trusted boundaries, and advisory body
            // feedback keeps every other outcome Unknown.
            if (evaluation.Outcome != EffectEvaluationOutcome.Proven)
            {
                allProven = false;
            }
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
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        session = ArgumentNullGuard.NotNull(session, nameof(session));
        ArgumentNullGuard.NotNull(location, nameof(location));
        return [.. EffectClaimDeclarations.Declare(method, session.Semantics, cancellationToken)
            .Select(static claim => claim is null ? throw new ArgumentNullException(nameof(claim)) : new EffectClaimEvaluation(claim.Kind, claim.Attributes,
                claim.Outcome, claim.Reason, claim.Certainty, claim.Evidence, null,
                claim.Constraint, null, Location.None, []))];
    }

    // Advisory feedback for the effect claims a callable declares. The
    // analyzer never proves a claim: it reports the first site that could
    // violate each one in the same Total IR body the worker verifies, and
    // Z3 decides the claim in the build.
    internal static ImmutableArray<EffectClaimEvaluation> Evaluate(
        IMethodSymbol method, Location location, AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic, CancellationToken cancellationToken, out bool abstained)
    {
        location = RepositoryParameterGuard.NotNull(location, nameof(location));
        abstained = false;
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        session = ArgumentNullGuard.NotNull(session, nameof(session));
        reportDiagnostic = ArgumentNullGuard.NotNull(reportDiagnostic, nameof(reportDiagnostic));
        var attributes = ContractSelectionInventory.GetCallableAttributes(method).ToImmutableArray();
        _ = DecodeCapabilities(Select(attributes, session.Attributes.AllowedCapabilities), location, session, reportDiagnostic);
        _ = DecodeAllowedExceptions(Select(attributes, session.Attributes.AllowedExceptions), session.Compilation, location, session,
            reportDiagnostic);
        var evaluations = Declare(method, location, session, cancellationToken);
        if (evaluations.All(evaluation => evaluation is null ? throw new System.ArgumentNullException(nameof(evaluation)) : (evaluation.Outcome == EffectEvaluationOutcome.Proven)) ||
            session.Compilation is not CSharpCompilation compilation ||
            method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken) is not { } declaration)
        { return evaluations; }
        // A bodyless declaration has only its trusted contract to report on.
        if (method is { IsAbstract: true } or { IsExtern: true })
        {
            return [.. evaluations.Select(evaluation => evaluation is null ? throw new System.ArgumentNullException(nameof(evaluation)) : (evaluation.Outcome != EffectEvaluationOutcome.Proven &&
                evaluation.Certainty == EffectEvaluationCertainty.TrustedCompleteBoundary
                ? Report(evaluation, method.Name, "its trusted effect contract does not establish it") : evaluation))];
        }
        var analysis = AdvisoryEffectSites.Analyze(compilation, method, declaration, cancellationToken);
        if (analysis.Gap != null)
        {
            reportDiagnostic(Diagnostic.Create(GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule, location,
                method.Name, "Advisory:" + analysis.Gap));
            abstained = true;
            return evaluations;
        }
        return [.. evaluations.Select(evaluation =>
            evaluation is null ? throw new System.ArgumentNullException(nameof(evaluation)) : (evaluation.Outcome != EffectEvaluationOutcome.Proven &&
            AdvisoryEffectClaims.FindViolation(evaluation.Kind, evaluation.Constraint, analysis.Sites, compilation) is { } violation
                ? Report(evaluation, SiteText(violation.Site.Location), violation.Detail) : evaluation))];

        EffectClaimEvaluation Report(EffectClaimEvaluation evaluation, string subject, string detail)
        {
            detail = RepositoryParameterGuard.NotNull(detail, nameof(detail));
            evaluation = RepositoryParameterGuard.NotNull(evaluation, nameof(evaluation));
            subject = RepositoryParameterGuard.NotNull(subject, nameof(subject));
            var described = "'" + subject + "' " + detail;
            (DiagnosticDescriptor Rule, ImmutableArray<object?> Arguments) diagnostic = evaluation.Kind switch
            {
                EffectEvaluationContractKind.EnforcePure => (GeneratedDiagnosticDescriptors.PurityNotVerifiedRule, [method.Name]),
                EffectEvaluationContractKind.ZeroAllocations => (GeneratedDiagnosticDescriptors.ZeroAllocationsNotVerifiedRule,
                    [method.Name, described]),
                EffectEvaluationContractKind.AllowedCapabilities => (GeneratedDiagnosticDescriptors.CapabilityUnknownRule,
                    [subject, method.Name, detail]),
                EffectEvaluationContractKind.DoesNotThrow => (GeneratedDiagnosticDescriptors.ExceptionContractNotVerifiedRule,
                    [method.Name, "[DoesNotThrow]", described]),
                EffectEvaluationContractKind.AllowedExceptions => (GeneratedDiagnosticDescriptors.ExceptionContractNotVerifiedRule,
                    [method.Name, "[AllowedExceptions]", described]),
                _ => (GeneratedDiagnosticDescriptors.EffectContractNotProvenRule, [method.Name, described])
            };
            return evaluation with { Diagnostic = diagnostic.Rule, DiagnosticLocation = location, DiagnosticArguments = diagnostic.Arguments };
        }
    }

    private static string SiteText(Location location)
    {
        location = RepositoryParameterGuard.NotNull(location, nameof(location));
        var text = location.SourceTree?.GetText().ToString(location.SourceSpan) ?? "";
        text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= 80 ? text : text.Substring(0, 77) + "...";
    }

    private static (EffectContractCapabilityKind Value, bool IsValid) DecodeCapabilities(
        ImmutableArray<AttributeData> attributes, Location fallbackLocation,
        AnalyzerSession session, Action<Diagnostic> reportDiagnostic)
    {
        RepositoryParameterGuard.ThrowIfInvalid(attributes.IsDefault, nameof(attributes));
        fallbackLocation = RepositoryParameterGuard.NotNull(fallbackLocation, nameof(fallbackLocation));
        reportDiagnostic = RepositoryParameterGuard.NotNull(reportDiagnostic, nameof(reportDiagnostic));
        session = RepositoryParameterGuard.NotNull(session, nameof(session));
        return EffectContractArguments.DecodeCapabilities(attributes,
            (attribute, contract, reason) =>
            {
                attribute = ArgumentNullGuard.NotNull(attribute, nameof(attribute));
                contract = ArgumentNullGuard.NotNull(contract, nameof(contract));
                reason = ArgumentNullGuard.NotNull(reason, nameof(reason));
                ReportInvalidOnce(attribute, contract, reason, fallbackLocation, session, reportDiagnostic);
            });
    }

    private static (ImmutableArray<INamedTypeSymbol> Types, bool IsValid) DecodeAllowedExceptions(
        ImmutableArray<AttributeData> attributes, Compilation compilation, Location fallbackLocation,
        AnalyzerSession session, Action<Diagnostic> reportDiagnostic)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        fallbackLocation = RepositoryParameterGuard.NotNull(fallbackLocation, nameof(fallbackLocation));
        reportDiagnostic = RepositoryParameterGuard.NotNull(reportDiagnostic, nameof(reportDiagnostic));
        session = RepositoryParameterGuard.NotNull(session, nameof(session));
        RepositoryParameterGuard.RequireInitialized(attributes.IsDefault, nameof(attributes), "Array must be initialized.");
        return EffectContractArguments.DecodeAllowedExceptions(attributes, compilation,
            (attribute, contract, reason) =>
            {
                attribute = ArgumentNullGuard.NotNull(attribute, nameof(attribute));
                contract = ArgumentNullGuard.NotNull(contract, nameof(contract));
                reason = ArgumentNullGuard.NotNull(reason, nameof(reason));
                ReportInvalidOnce(attribute, contract, reason, fallbackLocation, session, reportDiagnostic);
            });
    }

    private static void ReportInvalidOnce(
        AttributeData attribute, string contract, string reason, Location fallbackLocation,
        AnalyzerSession session, Action<Diagnostic> reportDiagnostic)
    {
        attribute = RepositoryParameterGuard.NotNull(attribute, nameof(attribute));
        contract = RepositoryParameterGuard.NotNull(contract, nameof(contract));
        fallbackLocation = RepositoryParameterGuard.NotNull(fallbackLocation, nameof(fallbackLocation));
        reason = RepositoryParameterGuard.NotNull(reason, nameof(reason));
        reportDiagnostic = RepositoryParameterGuard.NotNull(reportDiagnostic, nameof(reportDiagnostic));
        session = RepositoryParameterGuard.NotNull(session, nameof(session));
        if (session.TryMarkAttributeValidated(attribute))
        {
            reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                contract, "<invalid>", reason, GetLocation(attribute, fallbackLocation)));
        }
    }

    internal static string FormatTypes(IEnumerable<INamedTypeSymbol> types)
    {
        types = ArgumentNullGuard.NotNull(types, nameof(types));
        return string.Join(",", types
            .Select(FormatExceptionType)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value), StringComparer.Ordinal));
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
        attribute = RepositoryParameterGuard.NotNull(attribute, nameof(attribute));
        fallback = RepositoryParameterGuard.NotNull(fallback, nameof(fallback));
        return attribute.ApplicationSyntaxReference?.SyntaxTree.GetLocation(
            attribute.ApplicationSyntaxReference.Span) ?? fallback;
    }

    private static ImmutableArray<AttributeData> Select(
        ImmutableArray<AttributeData> attributes, INamedTypeSymbol? expected)
    {

        RepositoryParameterGuard.RequireInitialized(attributes.IsDefault, nameof(attributes), "Array must be initialized.");
        return [.. attributes.Where(attribute => attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (ContractSelectionInventory.Is(attribute, expected)))];
    }
}
