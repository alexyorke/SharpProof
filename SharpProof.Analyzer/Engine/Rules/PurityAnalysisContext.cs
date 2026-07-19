namespace SharpProof.Analyzer.Engine.Rules;

internal sealed record PurityAnalysisContext(
    SemanticModel SemanticModel,
    INamedTypeSymbol EnforcePureAttributeSymbol,
    INamedTypeSymbol? PureAttributeSymbol,
    INamedTypeSymbol? AllowSynchronizationAttributeSymbol,
    HashSet<IMethodSymbol> VisitedMethods,
    Dictionary<IMethodSymbol, PurityAnalysisEngine.PurityAnalysisResult> PurityCache,
    IMethodSymbol ContainingMethodSymbol,
    CancellationToken CancellationToken,
    SmtAnalysisService smtAnalysis,
    SharpProofAttributeIdentityPolicy? attributePolicy = null,
    Func<IMethodSymbol, SemanticModel?>? semanticModelResolver = null)
{
    public SmtAnalysisService SmtAnalysis { get; } = smtAnalysis ?? throw new ArgumentNullException(nameof(smtAnalysis));
    public SharpProofAttributeIdentityPolicy AttributePolicy { get; } = attributePolicy ??
        RequiresContractHelpers.OfficialAttributePolicy;
    public Func<IMethodSymbol, SemanticModel?>? SemanticModelResolver { get; } = semanticModelResolver;
}
