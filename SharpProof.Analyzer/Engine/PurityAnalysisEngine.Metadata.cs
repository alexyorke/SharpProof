namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    internal static PurityAnalysisResult ImpureResult(
        IOperation operation,
        string category,
        string? ruleName = null,
        ISymbol? symbol = null,
        string? catalogSource = null)
    {
        return PurityAnalysisResult.Impure(
            operation.Syntax,
            PurityEvidence.Create(
                category,
                ruleName,
                operation,
                symbol: symbol,
                catalogSource: catalogSource));
    }

    internal static PurityAnalysisResult ImpureResult(
        SyntaxNode? syntaxNode,
        string category,
        string? ruleName = null,
        ISymbol? symbol = null,
        string? catalogSource = null)
    {
        return ImpureResult(
            syntaxNode,
            PurityEvidence.Create(
                category,
                ruleName,
                syntaxNode: syntaxNode,
                symbol: symbol,
                catalogSource: catalogSource));
    }

    internal static bool TryCreateBclFallbackImpurity(
        ISymbol? symbol,
        SyntaxNode? syntaxNode,
        IOperation? operation,
        string ruleName,
        out PurityAnalysisResult result)
    {
        result = PurityAnalysisResult.Pure;
        if (!BclPurityFallbackClassifier.TryClassify(symbol, out var classification)) return false;

        var evidence = PurityEvidence.Create(
            classification.Category,
            ruleName,
            operation,
            syntaxNode,
            symbol,
            BclPurityFallbackClassifier.CatalogSource,
            bclFallbackGuess: classification.Guess,
            bclFallbackConfidence: classification.Confidence,
            bclFallbackReason: classification.Reason);
        result = ImpureResult(syntaxNode ?? operation?.Syntax, evidence);
        return true;
    }

    internal static bool TryGetTrustedGeneratedPurity(
        IMethodSymbol methodSymbol,
        Compilation compilation,
        out GeneratedPurityCatalog.PurityEntry purity)
    {
        return GeneratedPurityCatalog.Current.TryGetPurity(methodSymbol, compilation, out purity);
    }

    internal static bool IsMetadataSymbol(ISymbol? symbol)
    {
        return symbol?.Locations.FirstOrDefault()?.IsInMetadata == true;
    }

    internal static bool TryGetTrustedDefinitiveGeneratedPurity(
        IMethodSymbol? methodSymbol,
        Compilation compilation,
        out GeneratedPurityCatalog.PurityEntry purity)
    {
        return TryGetTrustedGeneratedPurityCore(
            methodSymbol,
            compilation,
            requireDefinitive: true,
            TryGetTrustedGeneratedPurity,
            out purity);
    }

    internal static bool TryGetTrustedGeneratedPurityCoverage(
        IMethodSymbol? methodSymbol,
        Compilation compilation,
        out GeneratedPurityCatalog.PurityEntry purity)
    {
        return TryGetTrustedGeneratedPurityCore(
            methodSymbol,
            compilation,
            requireDefinitive: false,
            TryGetTrustedGeneratedPurity,
            out purity);
    }

    internal static TrustedMethodPurityMetadata GetTrustedMethodPurityMetadata(
        IMethodSymbol methodSymbol,
        Compilation compilation)
    {
        if (methodSymbol == null) return default;

        var originalDefinition = methodSymbol.OriginalDefinition;
        var knownImpureMemberSource = PurityCalleeResolver.GetKnownImpureMemberSource(originalDefinition);
        var hasConfiguredKnownImpureMember = string.Equals(
            knownImpureMemberSource,
            "config_known_impure",
            StringComparison.Ordinal);

        GeneratedPurityCatalog.PurityEntry generatedPurity = default;
        var hasTrustedGeneratedPurity = !hasConfiguredKnownImpureMember &&
                                        TryGetTrustedGeneratedPurityCoverage(originalDefinition, compilation,
                                            out generatedPurity);

        return new TrustedMethodPurityMetadata(
            knownImpureMemberSource,
            hasTrustedGeneratedPurity,
            generatedPurity);
    }

    internal static bool TryGetTrustedGeneratedFieldPurity(
        IFieldSymbol fieldSymbol,
        Compilation compilation,
        out GeneratedPurityCatalog.PurityEntry purity)
    {
        return GeneratedPurityCatalog.Current.TryGetFieldPurity(fieldSymbol, compilation, out purity);
    }

    internal static bool TryGetTrustedDefinitiveGeneratedFieldPurity(
        IFieldSymbol? fieldSymbol,
        Compilation compilation,
        out GeneratedPurityCatalog.PurityEntry purity)
    {
        return TryGetTrustedGeneratedPurityCore(
            fieldSymbol,
            compilation,
            requireDefinitive: true,
            TryGetTrustedGeneratedFieldPurity,
            out purity);
    }

    private static bool TryGetTrustedGeneratedPurityCore<TSymbol>(
        TSymbol? symbol,
        Compilation compilation,
        bool requireDefinitive,
        TryGetGeneratedPurity<TSymbol> lookup,
        out GeneratedPurityCatalog.PurityEntry purity)
        where TSymbol : class, ISymbol
    {
        purity = default;
        return symbol != null &&
               IsMetadataSymbol(symbol) &&
               symbol.OriginalDefinition is TSymbol originalDefinition &&
               lookup(originalDefinition, compilation, out purity) &&
               (!requireDefinitive || purity.IsDefinitive);
    }

    private delegate bool TryGetGeneratedPurity<TSymbol>(
        TSymbol symbol,
        Compilation compilation,
        out GeneratedPurityCatalog.PurityEntry purity)
        where TSymbol : class, ISymbol;

    internal static bool IsTrustedGeneratedFreshOwnedArrayReturningMember(
        IMethodSymbol methodSymbol,
        Compilation compilation)
    {
        return TryGetTrustedGeneratedPurity(methodSymbol, compilation, out var purity) &&
               purity.IsPure &&
               purity.IsFreshArrayCandidate;
    }

    internal static bool IsTrustedGeneratedNonEscapingArrayReturningMember(
        IMethodSymbol methodSymbol,
        Compilation compilation)
    {
        return methodSymbol?.ReturnType is IArrayTypeSymbol &&
               TryGetTrustedGeneratedPurity(methodSymbol, compilation, out var purity) &&
               purity.AllowsNonEscapingArrayReturn;
    }

    internal readonly struct TrustedMethodPurityMetadata(
        string? knownImpureMemberSource,
        bool hasTrustedGeneratedPurity,
        GeneratedPurityCatalog.PurityEntry generatedPurity)
    {
        public string? KnownImpureMemberSource { get; } = knownImpureMemberSource;

        public bool HasConfiguredKnownImpureMember =>
            string.Equals(KnownImpureMemberSource, "config_known_impure", StringComparison.Ordinal);

        public bool HasTrustedGeneratedPurity { get; } = hasTrustedGeneratedPurity;
        public GeneratedPurityCatalog.PurityEntry GeneratedPurity { get; } = generatedPurity;
        public bool AllowsKnownPureFallback => !HasTrustedGeneratedPurity;
    }
}
