namespace SharpProof.Analyzer;

// Compares the validation sentences in a method's XML documentation with the
// declared SharpProof contract on the same parameter or return value. This is a
// consistency check over declarations, not a proof of the implementation: it
// reports a documented claim that no declaration backs, and a declaration whose
// bounds disagree with the documented bounds. Anything the reader cannot
// establish is left silent.
internal static class DocumentedValidationAnalyzer
{
    internal static void Validate(SymbolAnalysisContext context, AnalyzerSession session)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        var method = (IMethodSymbol)context.Symbol;
        if (!IsCandidate(method) ||
            AnalyzerGeneratedCodePolicy.IsGenerated(
                method,
                method.DeclaringSyntaxReferences[0].SyntaxTree,
                context.Compilation,
                context.CancellationToken))
        {
            return;
        }

        var documentation = method.GetDocumentationCommentXml(cancellationToken: context.CancellationToken);
        var claims = DocumentedClaimParser.Parse(documentation ?? string.Empty);
        if (claims.IsEmpty)
        {
            return;
        }

        var resolution = session.ResolveContractSource(method);

        // A companion's contract declares another method's clauses, and a failed
        // binding has no reliable contract. Neither is compared.
        if (resolution.UsesCompanion || resolution.Failure != ContractBindingFailure.None)
        {
            return;
        }

        var reader = new DeclaredValidationReader(context.Compilation, method, resolution);
        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None ||
                !claims.TryGetValue(parameter.Name, out var parameterClaims))
            {
                continue;
            }

            Compare(
                subject: $"Parameter '{parameter.Name}'",
                type: parameter.Type,
                claims: parameterClaims,
                declared: reader.ForParameter(parameter),
                location: FirstLocation(parameter.Locations),
                report: context.ReportDiagnostic,
                nonNullGap: "no NonNull attribute or Contract.Requires(... != null) makes it non-null",
                boundsGap: "no Positive, InRange attribute or Contract.Requires bounds it");
        }

        if (!method.ReturnsVoid &&
            claims.TryGetValue(DocumentedClaimParser.ReturnKey, out var returnClaims))
        {
            Compare(
                subject: $"The return value of '{method.Name}'",
                type: method.ReturnType,
                claims: returnClaims,
                declared: reader.ForReturn(),
                location: FirstLocation(method.Locations),
                report: context.ReportDiagnostic,
                nonNullGap: "no [return: NonNull] attribute or Contract.Ensures makes the result non-null",
                boundsGap: "no [return: Positive], [return: InRange] attribute or Contract.Ensures bounds it");
        }
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

    private static void Compare(
        string subject,
        ITypeSymbol type,
        ImmutableArray<DocumentedClaim> claims,
        DeclaredValidation declared,
        Location location,
        Action<Diagnostic> report,
        string nonNullGap,
        string boundsGap)
    {
        if (declared.Unknown)
        {
            return;
        }

        var nonNullClaims = claims.Where(static claim => claim.Kind == DocumentedClaimKind.NonNull).ToImmutableArray();
        if (!nonNullClaims.IsEmpty && type.IsReferenceType && !declared.NonNull)
        {
            report(Diagnostic.Create(
                DocumentedValidationDiagnosticDescriptors.MismatchRule,
                location,
                subject,
                JoinSentences(nonNullClaims),
                nonNullGap));
        }

        var intervalClaims = claims.Where(static claim => claim.Kind == DocumentedClaimKind.Interval).ToImmutableArray();
        if (intervalClaims.IsEmpty || !IntegralDomain.TryGet(type, out var minimum, out var maximum))
        {
            return;
        }

        var documentedLower = minimum;
        var documentedUpper = maximum;
        foreach (var claim in intervalClaims)
        {
            documentedLower = Math.Max(documentedLower, claim.Lower ?? minimum);
            documentedUpper = Math.Min(documentedUpper, claim.Upper ?? maximum);
        }

        // Contradictory prose such as "between 5 and 1" has no admitted values
        // to compare, so it is not reported as a mismatch.
        if (documentedLower > documentedUpper)
        {
            return;
        }

        var declaredLower = declared.Lower is { } lower ? Math.Max(lower, minimum) : minimum;
        var declaredUpper = declared.Upper is { } upper ? Math.Min(upper, maximum) : maximum;
        if (declaredLower == documentedLower && declaredUpper == documentedUpper)
        {
            return;
        }

        var reason = declared.Lower is null && declared.Upper is null
            ? boundsGap
            : "its declared contract admits " +
                Describe(declaredLower, minimum, "the type minimum") + " through " +
                Describe(declaredUpper, maximum, "the type maximum");
        report(Diagnostic.Create(
            DocumentedValidationDiagnosticDescriptors.MismatchRule,
            location,
            subject,
            JoinSentences(intervalClaims),
            reason));
    }

    private static string JoinSentences(ImmutableArray<DocumentedClaim> claims)
    {
        return string.Join("; ", claims.Select(static claim => claim.Sentence));
    }

    private static string Describe(long bound, long domainBound, string domainName)
    {
        return bound == domainBound
            ? domainName
            : bound.ToString(CultureInfo.InvariantCulture);
    }

    private static Location FirstLocation(ImmutableArray<Location> locations)
    {
        return locations.IsDefaultOrEmpty ? Location.None : locations[0];
    }
}
