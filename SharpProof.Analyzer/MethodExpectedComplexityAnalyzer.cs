namespace SharpProof.Analyzer;
internal static class MethodExpectedComplexityAnalyzer {
    internal static void AnalyzeSymbolForExpectedComplexity(MethodBodyAnalysisContext context) {
        var methodSymbol = context.MethodSymbol;
        if (methodSymbol.DeclaringSyntaxReferences.IsDefaultOrEmpty) return;
        var contracts = CollectExpectedComplexities(methodSymbol, context.CancellationToken);
        if (contracts.IsDefaultOrEmpty) return;
        foreach (var contract in contracts) {
            if (contract.InvalidContract is { } invalidContract) {
                var diagnostic = InvalidContractArgumentDiagnostics.Create(
                    "[ExpectedComplexity]",
                    invalidContract.Argument,
                    invalidContract.Reason,
                    contract.AttributeLocation ??
                    AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(methodSymbol, context.CancellationToken));
                context.ReportDiagnostic(diagnostic);
            }
        }
        var validContracts = contracts.Where(static contract => contract.InvalidContract == null).ToArray();
        if (validContracts.Length == 0 || methodSymbol.IsAbstract) return;
        if (AnalyzerSyntaxHelpers.IsBodylessAutoPropertyGetter(context)) return;
        var outcome = context.State.GetComplexityOutcome(context.CancellationToken);
        if (!outcome.IsSuccess) {
            context.CancellationToken.ThrowIfCancellationRequested();
            var error = outcome.Error!;
            foreach (var contract in validContracts)
                context.ReportDiagnostic(CreateDiagnostic(
                    "ComplexityCouldNotBeVerifiedRule",
                    methodSymbol,
                    contract.DeclaredComplexity.Text,
                    contract.AttributeLocation,
                    "complexity query failed: " + error.Message,
                    context.CancellationToken));
            return;
        }
        var result = outcome.Value!;
        foreach (var contract in validContracts) {
            var classification = Classify(result, contract.DeclaredComplexity);
            switch (classification.Comparison) {
                case SymbolicComplexityComparison.Within:
                    continue;
                case SymbolicComplexityComparison.Exceeds:
                    context.ReportDiagnostic(CreateDiagnostic(
                        "ComplexityExceededRule",
                        methodSymbol,
                        contract.DeclaredComplexity.Text,
                        contract.AttributeLocation,
                        result.Complexity.Text,
                        context.CancellationToken));
                    continue;
                default:
                    context.ReportDiagnostic(CreateDiagnostic(
                        "ComplexityCouldNotBeVerifiedRule",
                        methodSymbol,
                        contract.DeclaredComplexity.Text,
                        contract.AttributeLocation,
                        classification.Reason,
                        context.CancellationToken));
                    continue;
            }
        }
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
                if (attribute.ConstructorArguments.Length != 1 ||
                    attribute.ConstructorArguments[0].Value is not int intValue) {
                    var invalidContract = new InvalidContractArgument(
                        AnalyzerSyntaxHelpers.GetFirstAttributeArgumentText(attribute, cancellationToken),
                        "expected a ComplexityKind enum value");
                    Add((default, "invalid"), attributeLocation, invalidContract);
                    continue;
                }
                if (!SymbolicComplexityFacts.IsDefinedBound(intValue)) {
                    var text = intValue.ToString(CultureInfo.InvariantCulture);
                    var invalidContract = new InvalidContractArgument(
                        intValue.ToString(CultureInfo.InvariantCulture),
                        "undefined ComplexityKind value");
                    Add((intValue, text), attributeLocation, invalidContract);
                    continue;
                }
                Add((intValue, SymbolicComplexityFacts.GetBoundText(intValue)), attributeLocation, null);
            }
        return contracts.ToImmutable();
        void Add(
            (int Kind, string Text) declaredComplexity,
            Location? attributeLocation,
            InvalidContractArgument? invalidContract) {
            var key = invalidContract == null
                ? "valid:" + declaredComplexity.Kind.ToString(CultureInfo.InvariantCulture)
                : "invalid:" + invalidContract.Argument + ":" + invalidContract.Reason;
            if (seen.Add(key))
                contracts.Add(new ExpectedComplexityContract(
                    declaredComplexity, attributeLocation, invalidContract));
        }
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
    sealed record ExpectedComplexityContract(
        (int Kind, string Text) DeclaredComplexity,
        Location? AttributeLocation,
        InvalidContractArgument? InvalidContract);
    sealed record InvalidContractArgument(string Argument, string Reason);
}
