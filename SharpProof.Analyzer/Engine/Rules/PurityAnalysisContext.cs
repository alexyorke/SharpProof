using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Engine.Rules;

internal sealed record PurityAnalysisContext(
    SemanticModel SemanticModel,
    INamedTypeSymbol EnforcePureAttributeSymbol,
    INamedTypeSymbol? PureAttributeSymbol,
    INamedTypeSymbol? AllowSynchronizationAttributeSymbol,
    HashSet<IMethodSymbol> VisitedMethods,
    Dictionary<IMethodSymbol, PurityAnalysisEngine.PurityAnalysisResult> PurityCache,
    IMethodSymbol ContainingMethodSymbol,
    ImmutableList<IPurityRule> PurityRules,
    CancellationToken CancellationToken,
    CompilationPurityService? PurityService,
    SmtAnalysisService? smtAnalysis = null,
    SharpProofAttributeIdentityPolicy? attributePolicy = null)
{
    public SmtAnalysisService SmtAnalysis { get; } = smtAnalysis ?? PurityService?.SmtAnalysis ??
        throw new ArgumentNullException(nameof(smtAnalysis),
            "Purity analysis requires a compilation-scoped SMT service.");
    public SharpProofAttributeIdentityPolicy AttributePolicy { get; } = attributePolicy ??
        PurityService?.AttributePolicy ?? RequiresContractHelpers.OfficialAttributePolicy;
}
