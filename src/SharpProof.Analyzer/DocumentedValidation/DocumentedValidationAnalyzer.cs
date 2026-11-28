namespace SharpProof.Analyzer;

// Compares the validation that documentation states with what the code does.
// For a method that is not an HTTP entry, the evidence on a parameter is its
// declarations (attributes and Contract.Requires) together with its runtime guards.
// For an HTTP entry handler, which no in-program caller checks, the evidence is
// the runtime guards together with the DataAnnotations and required-ness the
// framework applies. A property's evidence is its DataAnnotations Range. OpenAPI
// parameter constraints are compared with the handler's evidence and documentation.
// A sentence the claim grammar does not recognize is never reported, and evidence
// the readers cannot establish exactly leaves its subject silent.
internal static class DocumentedValidationAnalyzer
{
    private static readonly DeclaredValidation RequiredFact = new(true, null, null, false);

    private static readonly Reasons ParameterReasons = new(
        "neither a NonNull attribute, a Contract.Requires clause, nor a runtime guard makes it non-null",
        "neither a Positive or InRange attribute, a Contract.Requires clause, nor a runtime guard bounds it",
        "its declarations and runtime guards admit ");

    private static readonly Reasons ReturnReasons = new(
        "no [return: NonNull] attribute or Contract.Ensures makes the result non-null",
        "no [return: Positive], [return: InRange] attribute or Contract.Ensures bounds it",
        "its declared contract admits ");

    private static readonly Reasons PropertyReasons = new(
        string.Empty,
        string.Empty,
        "its DataAnnotations admit ");

    internal static void Validate(CompilationAnalysisContext context)
    {
        var compilation = context.Compilation;
        var cancellationToken = context.CancellationToken;
        var entries = HttpEntryCatalog.Collect(compilation, cancellationToken);
        var openApi = OpenApiDocumentReader.Find(context.Options.AdditionalFiles, cancellationToken);
        var sidecar = ClaimReviewSidecar.Read(context.Options.AdditionalFiles, cancellationToken);
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AnalyzerGeneratedCodePolicy.IsGenerated(tree, compilation, cancellationToken))
            {
                continue;
            }

            var model = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, tree);
            var root = tree.GetRoot(cancellationToken);
            foreach (var declaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration, cancellationToken) is not IMethodSymbol method ||
                    !IsCandidate(method))
                {
                    continue;
                }

                var routes = entries.TryGetValue(method, out var registered)
                    ? registered
                    : ImmutableArray<HttpEntryRoute>.Empty;
                var documentation = method.GetDocumentationCommentXml(cancellationToken: cancellationToken) ?? string.Empty;
                var documented = !string.IsNullOrWhiteSpace(documentation);
                if (!documented && routes.IsEmpty)
                {
                    continue;
                }

                var resolution = EffectiveContractSourceResolver
                    .ForCompilation(compilation, cancellationToken)
                    .Resolve(method, null, cancellationToken);

                // A companion's contract declares another method's clauses, and a
                // failed binding has no reliable contract. Neither is compared.
                if (resolution.UsesCompanion || resolution.Failure != ContractBindingFailure.None)
                {
                    continue;
                }

                AnalyzeMethod(
                    compilation,
                    method,
                    documented,
                    ApplyReview(method, documentation, DocumentedClaimParser.Parse(documentation), sidecar, context.ReportDiagnostic),
                    routes,
                    resolution,
                    openApi,
                    context.ReportDiagnostic,
                    cancellationToken);
            }

            foreach (var declaration in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration, cancellationToken) is IPropertySymbol property)
                {
                    AnalyzeProperty(property, context.ReportDiagnostic, cancellationToken);
                }
            }
        }
    }

    // Merges the claims a person accepted into their parameters, and reports the
    // proposals still awaiting review. Only a parameter whose documentation text
    // matches an entry's key is affected.
    private static ImmutableDictionary<string, ImmutableArray<DocumentedClaim>> ApplyReview(
        IMethodSymbol method,
        string documentation,
        ImmutableDictionary<string, ImmutableArray<DocumentedClaim>> claims,
        ClaimReviewSidecar sidecar,
        Action<Diagnostic> report)
    {
        if (sidecar.IsEmpty)
        {
            return claims;
        }

        var merged = claims.ToBuilder();
        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None ||
                DocumentedClaimParser.ParameterText(documentation, parameter.Name) is not { } text)
            {
                continue;
            }

            foreach (var entry in sidecar.For(parameter.Name, text))
            {
                switch (entry.Status)
                {
                    case ReviewStatus.Accepted when entry.Claim is { } accepted:
                        var existing = merged.TryGetValue(parameter.Name, out var found)
                            ? found
                            : ImmutableArray<DocumentedClaim>.Empty;

                        // A claim the grammar already reads from the same text is not
                        // added twice, so its bounds are not reported twice.
                        if (!existing.Any(claim => claim.Kind == accepted.Kind && claim.Lower == accepted.Lower && claim.Upper == accepted.Upper))
                        {
                            merged[parameter.Name] = existing.Add(accepted);
                        }

                        break;
                    case ReviewStatus.Proposed:
                        report(Diagnostic.Create(
                            DocumentedValidationDiagnosticDescriptors.ReviewRule,
                            FirstLocation(parameter.Locations),
                            $"Parameter '{parameter.Name}' has a validation claim proposed from its documentation that awaits review: '{entry.SourceText}'"));
                        break;
                    case ReviewStatus.Ambiguous:
                        report(Diagnostic.Create(
                            DocumentedValidationDiagnosticDescriptors.ReviewRule,
                            FirstLocation(parameter.Locations),
                            $"Parameter '{parameter.Name}' has documentation the local model could not read as a validation claim: {entry.Reason}"));
                        break;
                }
            }
        }

        return merged.ToImmutable();
    }

    private static void AnalyzeMethod(
        Compilation compilation,
        IMethodSymbol method,
        bool documented,
        ImmutableDictionary<string, ImmutableArray<DocumentedClaim>> claims,
        ImmutableArray<HttpEntryRoute> routes,
        EffectiveContractSourceResolution resolution,
        OpenApiDocument? openApi,
        Action<Diagnostic> report,
        CancellationToken cancellationToken)
    {
        var declared = new DeclaredValidationReader(compilation, method, resolution);
        var guards = RuntimeGuardReader.Read(compilation, method, cancellationToken);
        var isEntry = !routes.IsEmpty;
        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                continue;
            }

            var parameterClaims = claims.TryGetValue(parameter.Name, out var found)
                ? found
                : ImmutableArray<DocumentedClaim>.Empty;
            var declaredFacts = declared.ForParameter(parameter);
            var runtime = guards.TryGetValue(parameter, out var guard) ? guard : DeclaredValidation.None;
            var location = FirstLocation(parameter.Locations);
            if (isEntry)
            {
                // Required restates non-nullability, so it counts as evidence of the
                // entry but is not reported as undocumented validation. The bounds and
                // runtime guards are what SP0063 reports.
                var bounds = runtime.And(HttpEntryCatalog.DataAnnotationValidation(parameter.GetAttributes(), includeRequired: false));
                var evidence = runtime
                    .And(HttpEntryCatalog.DeclaredFrameworkValidation(parameter))
                    .And(HttpEntryCatalog.IsRequestBound(parameter) ? RequiredFact : DeclaredValidation.None);
                var declaredBounds = !declaredFacts.Unknown && (declaredFacts.Lower is not null || declaredFacts.Upper is not null);
                if (!parameterClaims.IsEmpty)
                {
                    CompareClaims(
                        $"Parameter '{parameter.Name}'",
                        parameter.Type,
                        parameterClaims,
                        evidence,
                        location,
                        report,
                        EntryReasons(declaredBounds));
                }

                ReportUndocumented(parameter, parameterClaims, bounds, location, report);
                if (openApi is not null)
                {
                    CompareOpenApi(parameter, routes, parameterClaims, evidence, openApi, location, report);
                }
            }
            else
            {
                if (!parameterClaims.IsEmpty)
                {
                    CompareClaims(
                        $"Parameter '{parameter.Name}'",
                        parameter.Type,
                        parameterClaims,
                        declaredFacts.And(runtime),
                        location,
                        report,
                        ParameterReasons);
                }

                if (documented)
                {
                    ReportUndocumented(parameter, parameterClaims, runtime, location, report);
                }
            }
        }

        if (documented &&
            !method.ReturnsVoid &&
            claims.TryGetValue(DocumentedClaimParser.ReturnKey, out var returnClaims))
        {
            CompareClaims(
                $"The return value of '{method.Name}'",
                method.ReturnType,
                returnClaims,
                declared.ForReturn(),
                FirstLocation(method.Locations),
                report,
                ReturnReasons);
        }
    }

    // A property is validated by its DataAnnotations Range, which model binding
    // applies. Its documentation is compared only where a Range exists, so prose
    // about validation the analyzer cannot see produces no finding.
    private static void AnalyzeProperty(
        IPropertySymbol property,
        Action<Diagnostic> report,
        CancellationToken cancellationToken)
    {
        var range = HttpEntryCatalog.DataAnnotationValidation(property.GetAttributes(), includeRequired: false);
        if (range.Unknown || !IntegralDomain.TryGet(property.Type, out var minimum, out var maximum))
        {
            return;
        }

        var documentation = property.GetDocumentationCommentXml(cancellationToken: cancellationToken) ?? string.Empty;
        var claims = DocumentedClaimParser.ParseDescription(documentation);
        var intervalClaims = claims.Where(static claim => claim.Kind == DocumentedClaimKind.Interval).ToImmutableArray();
        var hasBounds = range.Lower is not null || range.Upper is not null;
        if (!hasBounds && intervalClaims.IsEmpty)
        {
            return;
        }

        var subject = $"Property '{property.Name}'";
        var location = FirstLocation(property.Locations);
        if (hasBounds && !intervalClaims.IsEmpty)
        {
            CompareInterval(subject, property.Type, intervalClaims, range, location, report, PropertyReasons);
            return;
        }

        if (hasBounds)
        {
            var (lower, upper) = Admitted(range, minimum, maximum);
            if (lower <= upper && (lower > minimum || upper < maximum))
            {
                report(Diagnostic.Create(
                    DocumentedValidationDiagnosticDescriptors.UndocumentedRule,
                    location,
                    subject,
                    RangePhrase(lower, upper, minimum, maximum)));
            }
        }
    }

    private static void CompareClaims(
        string subject,
        ITypeSymbol type,
        ImmutableArray<DocumentedClaim> claims,
        DeclaredValidation evidence,
        Location location,
        Action<Diagnostic> report,
        Reasons reasons)
    {
        if (evidence.Unknown)
        {
            return;
        }

        var nonNullClaims = claims.Where(static claim => claim.Kind == DocumentedClaimKind.NonNull).ToImmutableArray();
        if (!nonNullClaims.IsEmpty && type.IsReferenceType && !evidence.NonNull)
        {
            report(Diagnostic.Create(
                DocumentedValidationDiagnosticDescriptors.MismatchRule,
                location,
                subject,
                JoinSentences(nonNullClaims),
                reasons.NonNullGap));
        }

        var intervalClaims = claims.Where(static claim => claim.Kind == DocumentedClaimKind.Interval).ToImmutableArray();
        if (!intervalClaims.IsEmpty)
        {
            CompareInterval(subject, type, intervalClaims, evidence, location, report, reasons);
        }
    }

    private static void CompareInterval(
        string subject,
        ITypeSymbol type,
        ImmutableArray<DocumentedClaim> intervalClaims,
        DeclaredValidation evidence,
        Location location,
        Action<Diagnostic> report,
        Reasons reasons)
    {
        if (evidence.Unknown || !IntegralDomain.TryGet(type, out var minimum, out var maximum))
        {
            return;
        }

        // Contradictory prose such as "between 5 and 1" admits nothing to compare,
        // so it is not reported as a mismatch.
        if (!TryDocumented(intervalClaims, minimum, maximum, out var documentedLower, out var documentedUpper))
        {
            return;
        }

        var (admittedLower, admittedUpper) = Admitted(evidence, minimum, maximum);
        if (admittedLower == documentedLower && admittedUpper == documentedUpper)
        {
            return;
        }

        var reason = evidence.Lower is null && evidence.Upper is null
            ? reasons.BoundsGap
            : reasons.AdmitsPrefix + RangePhrase(admittedLower, admittedUpper, minimum, maximum);
        report(Diagnostic.Create(
            DocumentedValidationDiagnosticDescriptors.MismatchRule,
            location,
            subject,
            JoinSentences(intervalClaims),
            reason));
    }

    // Reports what the runtime checks or DataAnnotations bounds enforce that the
    // documentation does not state. Declared contracts are not included here,
    // because a caller precondition is not runtime behavior of the method.
    private static void ReportUndocumented(
        IParameterSymbol parameter,
        ImmutableArray<DocumentedClaim> claims,
        DeclaredValidation explicitFacts,
        Location location,
        Action<Diagnostic> report)
    {
        if (explicitFacts.Unknown)
        {
            return;
        }

        var subject = $"Parameter '{parameter.Name}'";
        if (explicitFacts.NonNull && parameter.Type.IsReferenceType && !claims.Any(static claim => claim.Kind == DocumentedClaimKind.NonNull))
        {
            report(Diagnostic.Create(
                DocumentedValidationDiagnosticDescriptors.UndocumentedRule,
                location,
                subject,
                "non-null values"));
        }

        if (!IntegralDomain.TryGet(parameter.Type, out var minimum, out var maximum) ||
            (explicitFacts.Lower is null && explicitFacts.Upper is null) ||
            claims.Any(static claim => claim.Kind == DocumentedClaimKind.Interval))
        {
            return;
        }

        var (lower, upper) = Admitted(explicitFacts, minimum, maximum);
        if (lower <= upper && (lower > minimum || upper < maximum))
        {
            report(Diagnostic.Create(
                DocumentedValidationDiagnosticDescriptors.UndocumentedRule,
                location,
                subject,
                RangePhrase(lower, upper, minimum, maximum)));
        }
    }

    // Compares the OpenAPI constraints for each route the handler serves with the
    // runtime evidence and with the documented claims.
    private static void CompareOpenApi(
        IParameterSymbol parameter,
        ImmutableArray<HttpEntryRoute> routes,
        ImmutableArray<DocumentedClaim> claims,
        DeclaredValidation evidence,
        OpenApiDocument openApi,
        Location location,
        Action<Diagnostic> report)
    {
        foreach (var route in routes)
        {
            if (!openApi.TryGetParameter(route.Verb, route.Route, parameter.Name, out var schema) || schema.Unknown)
            {
                continue;
            }

            var operation = route.Verb + " " + route.Route;
            var name = parameter.Name;
            if (IntegralDomain.TryGet(parameter.Type, out var minimum, out var maximum))
            {
                var declaredLower = Math.Max(schema.Minimum ?? minimum, minimum);
                var declaredUpper = Math.Min(schema.Maximum ?? maximum, maximum);
                if (!evidence.Unknown)
                {
                    var (admittedLower, admittedUpper) = Admitted(evidence, minimum, maximum);
                    if (admittedLower != declaredLower || admittedUpper != declaredUpper)
                    {
                        report(Diagnostic.Create(
                            DocumentedValidationDiagnosticDescriptors.OpenApiRule,
                            location,
                            $"OpenAPI declares {RangePhrase(declaredLower, declaredUpper, minimum, maximum)} for '{name}' of {operation}, but the HTTP entry admits {RangePhrase(admittedLower, admittedUpper, minimum, maximum)}"));
                    }
                }

                var intervalClaims = claims.Where(static claim => claim.Kind == DocumentedClaimKind.Interval).ToImmutableArray();
                if (TryDocumented(intervalClaims, minimum, maximum, out var documentedLower, out var documentedUpper) &&
                    (documentedLower != declaredLower || documentedUpper != declaredUpper))
                {
                    report(Diagnostic.Create(
                        DocumentedValidationDiagnosticDescriptors.OpenApiRule,
                        location,
                        $"OpenAPI declares {RangePhrase(declaredLower, declaredUpper, minimum, maximum)} for '{name}' of {operation}, but its XML documentation says {RangePhrase(documentedLower, documentedUpper, minimum, maximum)}"));
                }
            }

            if (schema.Nullable && parameter.Type.IsReferenceType)
            {
                if (!evidence.Unknown && evidence.NonNull)
                {
                    report(Diagnostic.Create(
                        DocumentedValidationDiagnosticDescriptors.OpenApiRule,
                        location,
                        $"OpenAPI declares '{name}' of {operation} nullable, but the HTTP entry rejects null"));
                }

                if (claims.Any(static claim => claim.Kind == DocumentedClaimKind.NonNull))
                {
                    report(Diagnostic.Create(
                        DocumentedValidationDiagnosticDescriptors.OpenApiRule,
                        location,
                        $"OpenAPI declares '{name}' of {operation} nullable, but its XML documentation says it is non-null"));
                }
            }
        }
    }

    private static bool TryDocumented(
        ImmutableArray<DocumentedClaim> intervalClaims,
        long minimum,
        long maximum,
        out long lower,
        out long upper)
    {
        lower = minimum;
        upper = maximum;
        foreach (var claim in intervalClaims)
        {
            lower = Math.Max(lower, claim.Lower ?? minimum);
            upper = Math.Min(upper, claim.Upper ?? maximum);
        }

        return !intervalClaims.IsEmpty && lower <= upper;
    }

    private static (long Lower, long Upper) Admitted(DeclaredValidation facts, long minimum, long maximum)
    {
        return (Math.Max(facts.Lower ?? minimum, minimum), Math.Min(facts.Upper ?? maximum, maximum));
    }

    // How a closed interval reads in a diagnostic: "1 through 100", "at least 1",
    // "at most 100", or "no bound" when neither side is narrower than the type.
    internal static string RangePhrase(long lower, long upper, long minimum, long maximum)
    {
        var lowerBounded = lower > minimum;
        var upperBounded = upper < maximum;
        if (lowerBounded && upperBounded)
        {
            return Number(lower) + " through " + Number(upper);
        }

        if (lowerBounded)
        {
            return "at least " + Number(lower);
        }

        return upperBounded ? "at most " + Number(upper) : "no bound";
    }

    private static string Number(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static Reasons EntryReasons(bool declaredBounds)
    {
        return new Reasons(
            "no runtime guard, DataAnnotation, or framework requirement makes it non-null on this HTTP entry",
            "no runtime guard or DataAnnotation bounds it on this HTTP entry" +
                (declaredBounds ? "; a declared contract checks only in-program callers" : string.Empty),
            "the HTTP entry admits ");
    }

    private sealed class Reasons
    {
        public Reasons(string nonNullGap, string boundsGap, string admitsPrefix)
        {
            NonNullGap = RepositoryParameterGuard.NotNull(nonNullGap, nameof(nonNullGap));
            BoundsGap = RepositoryParameterGuard.NotNull(boundsGap, nameof(boundsGap));
            AdmitsPrefix = RepositoryParameterGuard.NotNull(admitsPrefix, nameof(admitsPrefix));
        }

        public string NonNullGap { get; }

        public string BoundsGap { get; }

        public string AdmitsPrefix { get; }
    }

    private static string JoinSentences(ImmutableArray<DocumentedClaim> claims)
    {
        return string.Join("; ", claims.Select(static claim => claim.Sentence));
    }

    private static bool IsCandidate(IMethodSymbol method)
    {
        return method.MethodKind == MethodKind.Ordinary &&
            !method.IsOverride &&
            !method.IsVirtual &&
            !method.IsAbstract &&
            !method.IsPartialDefinition &&
            method.PartialImplementationPart is null &&
            method.ExplicitInterfaceImplementations.IsEmpty &&
            method.ContainingType.TypeKind != TypeKind.Interface &&
            !method.DeclaringSyntaxReferences.IsDefaultOrEmpty;
    }

    private static Location FirstLocation(ImmutableArray<Location> locations)
    {
        return locations.IsDefaultOrEmpty ? Location.None : locations[0];
    }
}
