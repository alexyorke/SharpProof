using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Engine.Rules;

internal class PurityAnalysisContext
{
    public PurityAnalysisContext(
        SemanticModel semanticModel,
        INamedTypeSymbol enforcePureAttributeSymbol,
        INamedTypeSymbol? pureAttributeSymbol,
        INamedTypeSymbol? allowSynchronizationAttributeSymbol,
        HashSet<IMethodSymbol> visitedMethods,
        Dictionary<IMethodSymbol, PurityAnalysisEngine.PurityAnalysisResult> purityCache,
        IMethodSymbol containingMethodSymbol,
        ImmutableList<IPurityRule> purityRules,
        CancellationToken cancellationToken,
        CompilationPurityService? purityService,
        SmtAnalysisService? smtAnalysis = null,
        SharpProofAttributeIdentityPolicy? attributePolicy = null)
    {
        SemanticModel = semanticModel;
        EnforcePureAttributeSymbol = enforcePureAttributeSymbol;
        PureAttributeSymbol = pureAttributeSymbol;
        AllowSynchronizationAttributeSymbol = allowSynchronizationAttributeSymbol;
        VisitedMethods = visitedMethods;
        PurityCache = purityCache;
        ContainingMethodSymbol = containingMethodSymbol;
        PurityRules = purityRules;
        CancellationToken = cancellationToken;
        PurityService = purityService;
        SmtAnalysis = smtAnalysis ?? purityService?.SmtAnalysis ??
            throw new ArgumentNullException(nameof(smtAnalysis),
                "Purity analysis requires a compilation-scoped SMT service.");
        AttributePolicy = attributePolicy ??
                          purityService?.AttributePolicy ?? RequiresContractHelpers.OfficialAttributePolicy;
    }

    public CancellationToken CancellationToken { get; }
    public SemanticModel SemanticModel { get; }
    public INamedTypeSymbol EnforcePureAttributeSymbol { get; }
    public INamedTypeSymbol? PureAttributeSymbol { get; }
    public INamedTypeSymbol? AllowSynchronizationAttributeSymbol { get; }
    public HashSet<IMethodSymbol> VisitedMethods { get; }
    public Dictionary<IMethodSymbol, PurityAnalysisEngine.PurityAnalysisResult> PurityCache { get; }
    public IMethodSymbol ContainingMethodSymbol { get; }
    public ImmutableList<IPurityRule> PurityRules { get; }
    public CompilationPurityService? PurityService { get; }
    public SmtAnalysisService SmtAnalysis { get; }
    public SharpProofAttributeIdentityPolicy AttributePolicy { get; }
}