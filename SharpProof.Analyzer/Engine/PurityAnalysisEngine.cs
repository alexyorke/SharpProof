using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    private static readonly SymbolDisplayFormat _signatureFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Omitted,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        SymbolDisplayGenericsOptions.IncludeTypeParameters,
        SymbolDisplayMemberOptions.IncludeContainingType |
        SymbolDisplayMemberOptions.IncludeParameters |
        SymbolDisplayMemberOptions.IncludeModifiers,
        parameterOptions:
        SymbolDisplayParameterOptions.IncludeType |
        SymbolDisplayParameterOptions.IncludeParamsRefOut |
        SymbolDisplayParameterOptions.IncludeDefaultValue,
        miscellaneousOptions:
        SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
        SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
        SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
    );


    private static readonly ImmutableList<IPurityRule> _purityRules = RuleRegistry.GetDefaultRules();

    /// <summary>
    ///     First registry rule per <see cref="OperationKind" />; matches former <c>FirstOrDefault</c> over
    ///     <see cref="_purityRules" />.
    /// </summary>
    private static readonly ImmutableDictionary<OperationKind, IPurityRule> _firstRuleByOperationKind =
        BuildFirstRuleByOperationKind(_purityRules);

    private readonly SharpProofAttributeIdentityPolicy _attributePolicy;
    private readonly CompilationPurityService? _purityService;
    private readonly SmtAnalysisService _smtAnalysis;

    public PurityAnalysisEngine(CompilationPurityService purityService)
    {
        _purityService = purityService ?? throw new ArgumentNullException(nameof(purityService));
        _smtAnalysis = purityService.SmtAnalysis;
        _attributePolicy = purityService.AttributePolicy;
    }

    internal PurityAnalysisEngine(SmtAnalysisService smtAnalysis)
        : this(smtAnalysis, RequiresContractHelpers.OfficialAttributePolicy)
    {
    }

    internal PurityAnalysisEngine(SmtAnalysisService smtAnalysis, SharpProofAttributeIdentityPolicy attributePolicy)
    {
        _smtAnalysis = smtAnalysis ?? throw new ArgumentNullException(nameof(smtAnalysis));
        _attributePolicy = attributePolicy ?? throw new ArgumentNullException(nameof(attributePolicy));
    }

    private static ImmutableDictionary<OperationKind, IPurityRule> BuildFirstRuleByOperationKind(
        ImmutableList<IPurityRule> rules)
    {
        var builder = ImmutableDictionary.CreateBuilder<OperationKind, IPurityRule>();
        foreach (var rule in rules)
            foreach (var kind in rule.ApplicableOperationKinds)
                if (!builder.ContainsKey(kind))
                    builder.Add(kind, rule);

        return builder.ToImmutable();
    }
}