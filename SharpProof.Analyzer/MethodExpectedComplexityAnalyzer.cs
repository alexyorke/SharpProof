namespace SharpProof.Analyzer;

internal static class MethodExpectedComplexityAnalyzer {
    internal static void AnalyzeSymbolForExpectedComplexity(
        MethodBodyAnalysisContext context,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy) {
        var methodSymbol = context.MethodSymbol;

        var report = AnalyzerDiagnosticReporter.CreateBaselineReporter(context, baseline);

        if (methodSymbol.DeclaringSyntaxReferences.IsDefaultOrEmpty) return;

        if (!TryGetExpectedComplexity(
                methodSymbol,
                attributePolicy,
                context.CancellationToken,
                out var declaredComplexity,
                out var attributeLocation,
                out var invalidContract))
            return;

        if (invalidContract != null) {
            var diagnostic = InvalidContractArgumentDiagnostics.Create(
                "[ExpectedComplexity]",
                invalidContract.Argument,
                invalidContract.Reason,
                attributeLocation ??
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(methodSymbol, context.CancellationToken),
                methodSymbol,
                context.Node.SyntaxTree);
            report(diagnostic);

            return;
        }

        if (AnalyzerSyntaxHelpers.IsBodylessAutoPropertyGetter(context)) return;

        var outcome = context.State.GetComplexityOutcome(context.CancellationToken);
        if (!outcome.IsSuccess) {
            context.CancellationToken.ThrowIfCancellationRequested();
            var error = outcome.Error!;
            var diagnostic = CreateUnknownDiagnostic(
                methodSymbol,
                declaredComplexity,
                attributeLocation,
                "complexity query failed: " + error.Message,
                context.CancellationToken,
                context.Node.SyntaxTree,
                SymbolicUnknownReasonTaxonomy.ForComplexityFailure(error.Code + ": " + error.Message));
            report(diagnostic);

            return;
        }

        var result = outcome.Value!;

        var classification = Classify(result, declaredComplexity);
        switch (classification.Kind) {
            case ComplexityVerificationKind.Verified:
                return;

            case ComplexityVerificationKind.Exceeded:
                var exceededDiagnostic = CreateExceededDiagnostic(
                    methodSymbol,
                    declaredComplexity,
                    result,
                    attributeLocation,
                    context.CancellationToken,
                    context.Node.SyntaxTree);
                report(exceededDiagnostic);

                return;

            default:
                var unknownDiagnostic = CreateUnknownDiagnostic(
                    methodSymbol,
                    declaredComplexity,
                    attributeLocation,
                    classification.Reason,
                    context.CancellationToken,
                    context.Node.SyntaxTree,
                    result.UnknownReasonDetails.FirstOrDefault());
                report(unknownDiagnostic);

                return;
        }
    }

    private static bool TryGetExpectedComplexity(
        IMethodSymbol methodSymbol,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken,
        out DeclaredComplexity declaredComplexity,
        out Location? attributeLocation,
        out InvalidContractArgument? invalidContract) {
        declaredComplexity = default;
        attributeLocation = null;
        invalidContract = null;

        foreach (var source in MethodContractHierarchy.EnumerateSources(methodSymbol, cancellationToken))
        foreach (var attribute in attributePolicy.GetAcceptedAttributes(
                     source,
                     "ExpectedComplexityAttribute")) {
            cancellationToken.ThrowIfCancellationRequested();
            attributeLocation = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
            if (attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not int intValue) {
                declaredComplexity = new DeclaredComplexity(default, "invalid");
                invalidContract = new InvalidContractArgument(
                    AnalyzerSyntaxHelpers.GetFirstAttributeArgumentText(attribute, cancellationToken),
                    "expected a ComplexityKind enum value");
                return true;
            }

            if (!SymbolicComplexityFacts.IsDefinedBound(intValue)) {
                declaredComplexity = new DeclaredComplexity(
                    intValue,
                    intValue.ToString());
                invalidContract = new InvalidContractArgument(
                    intValue.ToString(CultureInfo.InvariantCulture),
                    "undefined ComplexityKind value");
                return true;
            }

            declaredComplexity = new DeclaredComplexity(intValue);
            return true;
        }

        return false;
    }

    private static ComplexityVerificationClassification Classify(
        SymbolicComplexityResult result,
        DeclaredComplexity declaredComplexity) {
        if (result.Complexity.IsUnknown || result.Complexity.IsRecursiveUnknown) {
            var reason = result.UnknownReasons.Count > 0
                ? result.UnknownReasons[0].ToString()
                : "complexity unknown";
            return ComplexityVerificationClassification.Unknown(reason);
        }

        if (result.Complexity.IsConservative)
            return ComplexityVerificationClassification.Unknown(
                "inferred complexity '" + result.Complexity.Text + "' contains conservative alternatives");

        switch (SymbolicComplexityFacts.Compare(result.Complexity.Kind, declaredComplexity.Kind)) {
                case SymbolicComplexityComparison.Within:
                    return ComplexityVerificationClassification.Verified;
                case SymbolicComplexityComparison.Exceeds:
                    return ComplexityVerificationClassification.Exceeded;
        }

        return ComplexityVerificationClassification.Unknown(
            "inferred complexity '" + result.Complexity.Text + "' is not directly comparable to declared bound '" +
            declaredComplexity.Text + "'");
    }

    private static Diagnostic CreateExceededDiagnostic(
        IMethodSymbol methodSymbol,
        DeclaredComplexity declaredComplexity,
        SymbolicComplexityResult result,
        Location? attributeLocation,
        CancellationToken cancellationToken,
        SyntaxTree syntaxTree) {
        var location = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(methodSymbol, cancellationToken);
        var properties = AnalyzerDiagnosticProperties.AddBaselineAndExplain(
            ImmutableDictionary<string, string?>.Empty
                .Add(DiagnosticPropertyNames.ExpectedComplexityProperty, declaredComplexity.Text)
                .Add("sharpproof.complexity.actual", result.Complexity.Text),
            methodSymbol,
            syntaxTree,
            "ExpectedComplexity",
            declaredComplexity.Text,
            "exceeded:" + declaredComplexity.Text + ":" + result.Complexity.Text,
            location,
            declaredComplexity.Text,
            "exceeded");

        return Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("ComplexityExceededRule"),
            location,
            attributeLocation == null ? null : new[] { attributeLocation },
            properties,
            methodSymbol.Name,
            declaredComplexity.Text,
            result.Complexity.Text);
    }

    private static Diagnostic CreateUnknownDiagnostic(
        IMethodSymbol methodSymbol,
        DeclaredComplexity declaredComplexity,
        Location? attributeLocation,
        string reason,
        CancellationToken cancellationToken,
        SyntaxTree syntaxTree,
        SymbolicUnknownReasonInfo? unknownReasonInfo) {
        var location = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(methodSymbol, cancellationToken);
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(DiagnosticPropertyNames.ExpectedComplexityProperty, declaredComplexity.Text)
            .Add("sharpproof.complexity.unknown_reason", reason);
        var effectiveUnknownReason = unknownReasonInfo ?? SymbolicUnknownReasonTaxonomy.ForComplexityFailure(reason);
        properties = UnknownReasonDiagnosticProperties.Add(properties, effectiveUnknownReason);
        properties = AnalyzerDiagnosticProperties.AddBaselineAndExplain(
            properties,
            methodSymbol,
            syntaxTree,
            "ExpectedComplexity",
            declaredComplexity.Text,
            "unknown:" + declaredComplexity.Text + ":" + reason,
            location,
            declaredComplexity.Text,
            "unknown",
            effectiveUnknownReason.Code);

        return Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("ComplexityCouldNotBeVerifiedRule"),
            location,
            attributeLocation == null ? null : new[] { attributeLocation },
            properties,
            methodSymbol.Name,
            declaredComplexity.Text,
            reason);
    }

    private readonly record struct DeclaredComplexity(
        int Kind,
        string? TextOverride = null) {
        public string Text => TextOverride ?? SymbolicComplexityFacts.GetBoundText(Kind);
    }

    private readonly record struct ComplexityVerificationClassification(
        ComplexityVerificationKind Kind,
        string Reason) {
        public static readonly ComplexityVerificationClassification Verified =
            new(ComplexityVerificationKind.Verified, string.Empty);

        public static readonly ComplexityVerificationClassification Exceeded =
            new(ComplexityVerificationKind.Exceeded, string.Empty);

        public static ComplexityVerificationClassification Unknown(string reason) =>
            new ComplexityVerificationClassification(ComplexityVerificationKind.Unknown, reason);
    }

    private enum ComplexityVerificationKind {
        Verified,
        Exceeded,
        Unknown
    }

    private sealed record InvalidContractArgument(string Argument, string Reason);
}
