namespace SharpProof.Analyzer;

internal static class MethodExpectedComplexityAnalyzer {
    internal static void AnalyzeSymbolForExpectedComplexity(
        MethodBodyAnalysisContext context,
        SharpProofAttributeIdentityPolicy attributePolicy) {
        var methodSymbol = context.MethodSymbol;
        Action<Diagnostic> report = context.ReportDiagnostic;

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
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(methodSymbol, context.CancellationToken));
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
                context.CancellationToken);
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
                    context.CancellationToken);
                report(exceededDiagnostic);

                return;

            default:
                var unknownDiagnostic = CreateUnknownDiagnostic(
                    methodSymbol,
                    declaredComplexity,
                    attributeLocation,
                    classification.Reason,
                    context.CancellationToken);
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

        return SymbolicComplexityFacts.Compare(result.Complexity.Kind, declaredComplexity.Kind) switch {
            SymbolicComplexityComparison.Within => ComplexityVerificationClassification.Verified,
            SymbolicComplexityComparison.Exceeds => ComplexityVerificationClassification.Exceeded,
            _ => ComplexityVerificationClassification.Unknown(
                "inferred complexity '" + result.Complexity.Text + "' is not directly comparable to declared bound '" +
                declaredComplexity.Text + "'")
        };
    }

    private static Diagnostic CreateExceededDiagnostic(
        IMethodSymbol methodSymbol,
        DeclaredComplexity declaredComplexity,
        SymbolicComplexityResult result,
        Location? attributeLocation,
        CancellationToken cancellationToken) {
        var location = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(methodSymbol, cancellationToken);
        return Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("ComplexityExceededRule"),
            location,
            attributeLocation == null ? null : [attributeLocation],
            methodSymbol.Name,
            declaredComplexity.Text,
            result.Complexity.Text);
    }

    private static Diagnostic CreateUnknownDiagnostic(
        IMethodSymbol methodSymbol,
        DeclaredComplexity declaredComplexity,
        Location? attributeLocation,
        string reason,
        CancellationToken cancellationToken) {
        var location = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(methodSymbol, cancellationToken);
        return Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("ComplexityCouldNotBeVerifiedRule"),
            location,
            attributeLocation == null ? null : [attributeLocation],
            methodSymbol.Name,
            declaredComplexity.Text,
            reason);
    }

    readonly record struct DeclaredComplexity(
        int Kind,
        string? TextOverride = null) {
        public string Text => TextOverride ?? SymbolicComplexityFacts.GetBoundText(Kind);
    }

    readonly record struct ComplexityVerificationClassification(
        ComplexityVerificationKind Kind,
        string Reason) {
        public static readonly ComplexityVerificationClassification Verified =
            new(ComplexityVerificationKind.Verified, string.Empty);

        public static readonly ComplexityVerificationClassification Exceeded =
            new(ComplexityVerificationKind.Exceeded, string.Empty);

        public static ComplexityVerificationClassification Unknown(string reason) =>
            new ComplexityVerificationClassification(ComplexityVerificationKind.Unknown, reason);
    }

    enum ComplexityVerificationKind {
        Verified,
        Exceeded,
        Unknown
    }

    sealed record InvalidContractArgument(string Argument, string Reason);
}
