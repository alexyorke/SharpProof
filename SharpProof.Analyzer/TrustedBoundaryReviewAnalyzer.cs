namespace SharpProof.Analyzer;

internal static class TrustedBoundaryReviewAnalyzer {
    private const string EffectContractName = "SharpProof.Attributes.EffectContractAttribute";

    internal static void Analyze(MethodBodyAnalysisContext context, AnalyzerSession session) {
        if (session.Configuration.TrustedBoundaryReviewMode == TrustedBoundaryReviewMode.Off) return;
        foreach (var operation in context.Snapshot.VisibleOperations) {
            ISymbol? symbol = operation switch {
                IInvocationOperation invocation => invocation.TargetMethod,
                IObjectCreationOperation creation => creation.Constructor,
                IPropertyReferenceOperation property => property.Property,
                _ => null
            };
            if (symbol == null) continue;
            foreach (var attribute in symbol.GetAttributes()) {
                if (attribute.AttributeClass?.ToDisplayString() != EffectContractName) continue;
                session.RecordTrustedBoundaryFinding(new TrustedBoundaryReviewFinding(
                    symbol,
                    symbol.ToDisplayString(),
                    "effect_contract",
                    attribute.ToString(),
                    "applied",
                    string.Empty,
                    string.Empty,
                    "effects",
                    operation.Syntax.GetLocation()));
            }
        }
    }

    internal static void ReportDiagnostics(CompilationAnalysisContext context, AnalyzerSession session) {
        foreach (var finding in session.GetTrustedBoundaryFindings()) {
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add("sharpproof.trusted_boundary.symbol", finding.SymbolDisplay)
                .Add("sharpproof.trusted_boundary.source", finding.Source)
                .Add("sharpproof.trusted_boundary.value", finding.Value)
                .Add("sharpproof.trusted_boundary.disposition", finding.Disposition)
                .Add("sharpproof.trusted_boundary.classification", finding.Classification);
            var diagnostic = Diagnostic.Create(
                AnalyzerDiagnosticCatalog.Get("TrustedBoundaryReviewRule"),
                finding.Location,
                null,
                properties,
                finding.Source,
                finding.SymbolDisplay,
                finding.Disposition,
                string.Empty);
            if (!session.Baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
        }
    }
}

internal sealed record TrustedBoundaryReviewFinding(
    ISymbol Symbol,
    string SymbolDisplay,
    string Source,
    string Value,
    string Disposition,
    string OverriddenBy,
    string OverrideValue,
    string Classification,
    Location Location) {
    internal string Key => SymbolDisplay + "\u001f" + Source + "\u001f" + Value;
}
