using System.Collections.Immutable;

namespace SharpProof.Analyzer.Configuration;

internal static class PurityPolicyAuditRegistry
{
    public static ImmutableArray<PurityBoundaryPolicy> BoundarySources { get; } = ImmutableArray.Create(
        new PurityBoundaryPolicy(
            "member_impure_attribute",
            "SharpProof.Attributes.ImpureAttribute",
            PurityPolicyImpact.ForcesImpure,
            10,
            "A direct [Impure] boundary wins over direct or assembly pure trust."),
        new PurityBoundaryPolicy(
            "member_pure_external_attribute",
            "SharpProof.Attributes.PureExternalAttribute",
            PurityPolicyImpact.TrustsPure,
            10,
            "A direct [PureExternal] boundary overrides an assembly [Impure] default."),
        new PurityBoundaryPolicy(
            "recognized_external_pure_attribute",
            "JetBrains.Annotations.PureAttribute;System.Diagnostics.Contracts.PureAttribute",
            PurityPolicyImpact.TrustsPure,
            10,
            "Recognized external annotations provide member-level pure boundary evidence."),
        new PurityBoundaryPolicy(
            "assembly_impure_attribute",
            "SharpProof.Attributes.ImpureAttribute",
            PurityPolicyImpact.ForcesImpure,
            20,
            "Assembly [Impure] is the default unless a direct SharpProof boundary overrides it."),
        new PurityBoundaryPolicy(
            "assembly_pure_external_attribute",
            "SharpProof.Attributes.PureExternalAttribute",
            PurityPolicyImpact.TrustsPure,
            20,
            "Assembly [PureExternal] is the default unless a direct [Impure] boundary overrides it."),
        new PurityBoundaryPolicy(
            "additional_generated_summary",
            "*.SharpProof.EffectSummary.json",
            PurityPolicyImpact.TrustsPure |
            PurityPolicyImpact.ForcesImpure |
            PurityPolicyImpact.EnablesGeneratedOverrides,
            40,
            "An identity-compatible additional summary outranks an embedded summary for the same symbol."),
        new PurityBoundaryPolicy(
            "built_in_generated_summary",
            "SharpProof.Analyzer.GeneratedPurity.*.SharpProof.EffectSummary.json",
            PurityPolicyImpact.TrustsPure | PurityPolicyImpact.ForcesImpure,
            40,
            "Embedded generated evidence participates even when additional summary loading is disabled."),
        new PurityBoundaryPolicy(
            "built_in_purity_catalog",
            "SharpProof built-in semantic and member catalogs",
            PurityPolicyImpact.TrustsPure | PurityPolicyImpact.ForcesImpure,
            50,
            "Built-in catalogs apply after trusted generated evidence."));
}

internal sealed record PurityBoundaryPolicy(
    string Id,
    string Source,
    PurityPolicyImpact Impact,
    int DecisionStage,
    string DecisionRule);
