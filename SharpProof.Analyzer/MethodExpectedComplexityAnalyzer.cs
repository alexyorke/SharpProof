namespace SharpProof.Analyzer;
internal static class MethodExpectedComplexityAnalyzer {
    internal static void AnalyzeSymbolForExpectedComplexity(MethodBodyAnalysisContext context) {
        var methodSymbol = context.MethodSymbol;
        if (methodSymbol.DeclaringSyntaxReferences.IsDefaultOrEmpty) return;
        var contracts = CollectExpectedComplexities(methodSymbol, context.CancellationToken);
        if (contracts.IsDefaultOrEmpty) return;
        foreach (var contract in contracts.Where(static contract => contract.InvalidContract != null)) {
            var invalidContract = contract.InvalidContract!;
            context.ReportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                    "[ExpectedComplexity]",
                    invalidContract.Argument,
                    invalidContract.Reason,
                    contract.AttributeLocation ??
                    AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(methodSymbol, context.CancellationToken)));
        }
        var validContracts = contracts.Where(static contract => contract.InvalidContract == null).ToArray();
        if (validContracts.Length == 0 || methodSymbol.IsAbstract) return;
        if (AnalyzerSyntaxHelpers.IsBodylessAutoPropertyGetter(context)) return;
        var outcome = context.State.GetComplexityOutcome(context.CancellationToken);
        if (outcome.Error != null)
            context.CancellationToken.ThrowIfCancellationRequested();
        var failure = outcome.Error is { } error
            ? "complexity query failed: " + error.Message
            : outcome.Value == null ? "complexity query completed without a result" : null;
        if (failure != null) {
            foreach (var contract in validContracts)
                Report(contract, "ComplexityCouldNotBeVerifiedRule", failure);
            return;
        }
        var result = outcome.Value!;
        foreach (var contract in validContracts) {
            var classification = Classify(result, contract.DeclaredComplexity);
            if (classification.Comparison == SymbolicComplexityComparison.Within) continue;
            var exceeds = classification.Comparison == SymbolicComplexityComparison.Exceeds;
            Report(contract, exceeds ? "ComplexityExceededRule" : "ComplexityCouldNotBeVerifiedRule",
                exceeds ? result.Complexity.Text : classification.Reason);
        }
        void Report(ExpectedComplexityContract contract, string rule, string detail) =>
            context.ReportDiagnostic(Diagnostic.Create(
                AnalyzerDiagnosticCatalog.Get(rule),
                AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(methodSymbol, context.CancellationToken),
                contract.AttributeLocation == null ? null : [contract.AttributeLocation],
                methodSymbol.Name,
                contract.DeclaredComplexity.Text,
                detail));
    }
    private static ImmutableArray<ExpectedComplexityContract> CollectExpectedComplexities(
        IMethodSymbol methodSymbol,
        CancellationToken cancellationToken) {
        var contracts = ImmutableArray.CreateBuilder<ExpectedComplexityContract>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in MethodContractHierarchy.EnumerateSources(methodSymbol, cancellationToken))
            foreach (var attribute in SharpProofAttributeIdentityPolicy.GetAcceptedAttributes(
                         source, "ExpectedComplexityAttribute")) {
                cancellationToken.ThrowIfCancellationRequested();
                var attributeLocation = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
                var declaredComplexity = (Kind: default(int), Text: "invalid");
                InvalidContractArgument? invalidContract;
                if (attribute.ConstructorArguments.Length != 1 ||
                    attribute.ConstructorArguments[0].Value is not int intValue) {
                    invalidContract = new(
                        AnalyzerSyntaxHelpers.GetFirstAttributeArgumentText(attribute, cancellationToken),
                        "expected a ComplexityKind enum value");
                }
                else {
                    var text = intValue.ToString(CultureInfo.InvariantCulture);
                    var defined = SymbolicComplexityFacts.IsDefinedBound(intValue);
                    declaredComplexity = (intValue, defined ? SymbolicComplexityFacts.GetBoundText(intValue) : text);
                    invalidContract = defined ? null : new(text, "undefined ComplexityKind value");
                }
                var contract = new ExpectedComplexityContract(
                    declaredComplexity, attributeLocation, invalidContract);
                var key = invalidContract == null
                    ? "valid:" + declaredComplexity.Kind.ToString(CultureInfo.InvariantCulture)
                    : "invalid:" + invalidContract.Argument + ":" + invalidContract.Reason;
                if (seen.Add(key)) contracts.Add(contract);
            }
        return contracts.ToImmutable();
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
            ? "inferred complexity '" + result.Complexity.Text + "' is not directly comparable to declared bound '" +
              declaredComplexity.Text + "'"
            : string.Empty);
    }
    sealed record ExpectedComplexityContract(
        (int Kind, string Text) DeclaredComplexity,
        Location? AttributeLocation,
        InvalidContractArgument? InvalidContract);
    sealed record InvalidContractArgument(string Argument, string Reason);
}
