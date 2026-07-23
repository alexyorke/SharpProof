namespace SharpProof.Analyzer;
internal static class MethodExpectedComplexityAnalyzer {
    internal static void AnalyzeSymbolForExpectedComplexity(MethodBodyAnalysisContext context) {
        var methodSymbol = context.MethodSymbol;
        if (methodSymbol.DeclaringSyntaxReferences.IsDefaultOrEmpty) return;
        if (!TryGetExpectedComplexity(
                methodSymbol,
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
            context.ReportDiagnostic(diagnostic);
            return;
        }
        if (AnalyzerSyntaxHelpers.IsBodylessAutoPropertyGetter(context)) return;
        var outcome = context.State.GetComplexityOutcome(context.CancellationToken);
        if (!outcome.IsSuccess) {
            context.CancellationToken.ThrowIfCancellationRequested();
            var error = outcome.Error!;
            var diagnostic = CreateDiagnostic(
                "ComplexityCouldNotBeVerifiedRule",
                methodSymbol,
                declaredComplexity.Text,
                attributeLocation,
                "complexity query failed: " + error.Message,
                context.CancellationToken);
            context.ReportDiagnostic(diagnostic);
            return;
        }
        var result = outcome.Value!;
        var classification = Classify(result, declaredComplexity);
        switch (classification.Comparison) {
            case SymbolicComplexityComparison.Within:
                return;
            case SymbolicComplexityComparison.Exceeds:
                var exceededDiagnostic = CreateDiagnostic(
                    "ComplexityExceededRule",
                    methodSymbol,
                    declaredComplexity.Text,
                    attributeLocation,
                    result.Complexity.Text,
                    context.CancellationToken);
                context.ReportDiagnostic(exceededDiagnostic);
                return;
            default:
                var unknownDiagnostic = CreateDiagnostic(
                    "ComplexityCouldNotBeVerifiedRule",
                    methodSymbol,
                    declaredComplexity.Text,
                    attributeLocation,
                    classification.Reason,
                    context.CancellationToken);
                context.ReportDiagnostic(unknownDiagnostic);
                return;
        }
    }
    private static bool TryGetExpectedComplexity(
        IMethodSymbol methodSymbol,
        CancellationToken cancellationToken,
        out (int Kind, string Text) declaredComplexity,
        out Location? attributeLocation,
        out InvalidContractArgument? invalidContract) {
        declaredComplexity = default;
        attributeLocation = null;
        invalidContract = null;
        foreach (var source in MethodContractHierarchy.EnumerateSources(methodSymbol, cancellationToken))
            foreach (var attribute in SharpProofAttributeIdentityPolicy.GetAcceptedAttributes(
                         source, "ExpectedComplexityAttribute")) {
                cancellationToken.ThrowIfCancellationRequested();
                attributeLocation = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
                if (attribute.ConstructorArguments.Length != 1 ||
                    attribute.ConstructorArguments[0].Value is not int intValue) {
                    declaredComplexity = (default, "invalid");
                    invalidContract = new InvalidContractArgument(
                        AnalyzerSyntaxHelpers.GetFirstAttributeArgumentText(attribute, cancellationToken),
                        "expected a ComplexityKind enum value");
                    return true;
                }
                if (!SymbolicComplexityFacts.IsDefinedBound(intValue)) {
                    declaredComplexity = (intValue, intValue.ToString(CultureInfo.InvariantCulture));
                    invalidContract = new InvalidContractArgument(
                        intValue.ToString(CultureInfo.InvariantCulture),
                        "undefined ComplexityKind value");
                    return true;
                }
                declaredComplexity = (intValue, SymbolicComplexityFacts.GetBoundText(intValue));
                return true;
            }
        return false;
    }
    private static (SymbolicComplexityComparison Comparison, string Reason) Classify(
        SymbolicComplexityResult result,
        (int Kind, string Text) declaredComplexity) {
        if (result.Complexity.IsUnknown || result.Complexity.IsRecursiveUnknown) {
            var reason = result.UnknownReasons.Count > 0
                ? result.UnknownReasons[0].ToString()
                : "complexity unknown";
            return (SymbolicComplexityComparison.Incomparable, reason);
        }
        if (result.Complexity.IsConservative)
            return (SymbolicComplexityComparison.Incomparable,
                "inferred complexity '" + result.Complexity.Text + "' contains conservative alternatives");
        var comparison = SymbolicComplexityFacts.Compare(result.Complexity.Kind, declaredComplexity.Kind);
        return (comparison, comparison == SymbolicComplexityComparison.Incomparable
            ?
                "inferred complexity '" + result.Complexity.Text + "' is not directly comparable to declared bound '" +
                declaredComplexity.Text + "'"
            : string.Empty);
    }
    private static Diagnostic CreateDiagnostic(
        string rule,
        IMethodSymbol methodSymbol,
        string declaredComplexity,
        Location? attributeLocation,
        string detail,
        CancellationToken cancellationToken) {
        var location = AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(methodSymbol, cancellationToken);
        return Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get(rule),
            location,
            attributeLocation == null ? null : [attributeLocation],
            methodSymbol.Name,
            declaredComplexity,
            detail);
    }
    sealed record InvalidContractArgument(string Argument, string Reason);
}
