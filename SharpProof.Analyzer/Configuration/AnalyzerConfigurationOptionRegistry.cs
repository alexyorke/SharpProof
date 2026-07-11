using System.Collections.Immutable;

namespace SharpProof.Analyzer.Configuration;

internal static class AnalyzerConfigurationOptionRegistry
{
    private static ImmutableDictionary<string, AnalyzerConfigurationOption>? _optionsByKey;

    // Computed lazily so it never reads All during static initialization: static initializers run
    // in textual order, and member-ordering rules (fields before properties) can place this ahead
    // of All, which would otherwise read a default ImmutableArray and throw in the type initializer.
    private static ImmutableDictionary<string, AnalyzerConfigurationOption> OptionsByKey =>
        _optionsByKey ??= All.ToImmutableDictionary(static option => option.Key, StringComparer.Ordinal);

    public static ImmutableArray<AnalyzerConfigurationOption> All { get; } = ImmutableArray.Create(
        new AnalyzerConfigurationOption(
            ConfigKeys.KnownImpureMethods,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.StringList,
            string.Empty,
            "Additional exact method symbols forced impure before generated or built-in purity evidence.",
            purityPolicyImpact: PurityPolicyImpact.ForcesImpure),
        new AnalyzerConfigurationOption(
            ConfigKeys.KnownPureMethods,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.StringList,
            string.Empty,
            "Additional exact method symbols trusted pure unless a higher-priority impure or generated policy wins.",
            purityPolicyImpact: PurityPolicyImpact.TrustsPure),
        new AnalyzerConfigurationOption(
            ConfigKeys.KnownImpureNamespaces,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.StringList,
            string.Empty,
            "Namespaces forced impure except for exact configured-pure member exemptions.",
            purityPolicyImpact: PurityPolicyImpact.ForcesImpure),
        new AnalyzerConfigurationOption(
            ConfigKeys.KnownImpureTypes,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.StringList,
            string.Empty,
            "Types forced impure except for exact configured-pure member exemptions.",
            purityPolicyImpact: PurityPolicyImpact.ForcesImpure),
        new AnalyzerConfigurationOption(
            ConfigKeys.AttributeStubNamespaces,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.StringList,
            "SharpProof.Attributes",
            "Namespaces accepted for source-only SharpProof attributes, including purity boundary attributes.",
            purityPolicyImpact: PurityPolicyImpact.TrustsPure |
                                PurityPolicyImpact.ForcesImpure |
                                PurityPolicyImpact.ChangesAttributeIdentity),
        new AnalyzerConfigurationOption(
            ConfigKeys.PurityProfile,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.PurityProfile,
            "balanced",
            "Selects strict, balanced, or pragmatic purity fallback policy.",
            ImmutableArray.Create("strict", "balanced", "pragmatic"),
            PurityPolicyImpact.ChangesStrictness),
        new AnalyzerConfigurationOption(
            ConfigKeys.SuggestMissingEnforcePure,
            AnalyzerConfigurationScope.GlobalAndTree,
            AnalyzerConfigurationValueKind.Bool,
            "true",
            "Controls SP0004 inferred purity suggestions."),
        new AnalyzerConfigurationOption(
            ConfigKeys.SuggestMissingEnforcePureScope,
            AnalyzerConfigurationScope.GlobalAndTree,
            AnalyzerConfigurationValueKind.MissingPuritySuggestionScope,
            "all",
            "Controls which method visibility SP0004 can suggest.",
            ImmutableArray.Create("all", "public", "internal", "off")),
        new AnalyzerConfigurationOption(
            ConfigKeys.SuggestMissingEnforcePureExcludeGenerated,
            AnalyzerConfigurationScope.GlobalAndTree,
            AnalyzerConfigurationValueKind.Bool,
            "false",
            "Suppresses SP0004 in generated-looking source paths."),
        new AnalyzerConfigurationOption(
            ConfigKeys.SuggestMissingEnforcePureExcludeTests,
            AnalyzerConfigurationScope.GlobalAndTree,
            AnalyzerConfigurationValueKind.Bool,
            "false",
            "Suppresses SP0004 in test-looking namespaces and source paths."),
        new AnalyzerConfigurationOption(
            ConfigKeys.SuggestMissingEnforcePureMinComplexity,
            AnalyzerConfigurationScope.GlobalAndTree,
            AnalyzerConfigurationValueKind.NonNegativeInteger,
            "0",
            "Minimum inferred complexity required before SP0004 is suggested."),
        new AnalyzerConfigurationOption(
            ConfigKeys.SuggestMissingEnforcePureNamespaceFilters,
            AnalyzerConfigurationScope.GlobalAndTree,
            AnalyzerConfigurationValueKind.StringList,
            string.Empty,
            "Namespace prefixes eligible for SP0004 suggestions."),
        new AnalyzerConfigurationOption(
            ConfigKeys.SuggestInferredContracts,
            AnalyzerConfigurationScope.GlobalAndTree,
            AnalyzerConfigurationValueKind.Bool,
            "false",
            "Controls opt-in SP0034-SP0039 inferred contract suggestions."),
        new AnalyzerConfigurationOption(
            ConfigKeys.SuggestInferredContractsScope,
            AnalyzerConfigurationScope.GlobalAndTree,
            AnalyzerConfigurationValueKind.MissingPuritySuggestionScope,
            "all",
            "Controls which method visibility can receive inferred contract suggestions.",
            ImmutableArray.Create("all", "public", "internal", "off")),
        new AnalyzerConfigurationOption(
            ConfigKeys.SuggestInferredContractsKinds,
            AnalyzerConfigurationScope.GlobalAndTree,
            AnalyzerConfigurationValueKind.AllowedValueList,
            "zero-allocations, capabilities, complexity, exceptions, ensures, requires",
            "Selects inferred contract families.",
            ImmutableArray.Create(
                "zero-allocations",
                "capabilities",
                "complexity",
                "exceptions",
                "ensures",
                "requires")),
        new AnalyzerConfigurationOption(
            ConfigKeys.SuggestInferredContractsMinimumConfidence,
            AnalyzerConfigurationScope.GlobalAndTree,
            AnalyzerConfigurationValueKind.AllowedValue,
            "high",
            "Minimum confidence for inferred contract suggestions.",
            ImmutableArray.Create("medium", "high")),
        new AnalyzerConfigurationOption(
            ConfigKeys.EmitExplanations,
            AnalyzerConfigurationScope.GlobalAndTree,
            AnalyzerConfigurationValueKind.Bool,
            "false",
            "Emits optional SP0009 proof explanation diagnostics."),
        new AnalyzerConfigurationOption(
            ConfigKeys.ReportBclFallbackGuesses,
            AnalyzerConfigurationScope.GlobalAndTree,
            AnalyzerConfigurationValueKind.Bool,
            "false",
            "Emits optional SP0012 BCL fallback guess diagnostics."),
        new AnalyzerConfigurationOption(
            ConfigKeys.RuntimeHazardMode,
            AnalyzerConfigurationScope.GlobalAndTree,
            AnalyzerConfigurationValueKind.RuntimeHazardMode,
            "none",
            "Controls SP0010, SP0011, and opt-in SP0033 runtime-hazard reporting.",
            ImmutableArray.Create(
                "none",
                "sites",
                "summaries",
                "all",
                "unknowns",
                "sites-and-unknowns",
                "all-and-unknowns")),
        new AnalyzerConfigurationOption(
            ConfigKeys.SuppressProvenDiagnostics,
            AnalyzerConfigurationScope.GlobalAndTree,
            AnalyzerConfigurationValueKind.Bool,
            "false",
            "Controls opt-in suppression of allowlisted external diagnostics backed by exact SharpProof proofs."),
        new AnalyzerConfigurationOption(
            ConfigKeys.SuppressionDiagnosticIds,
            AnalyzerConfigurationScope.GlobalAndTree,
            AnalyzerConfigurationValueKind.AllowedValueList,
            "CS8509, CS8524, CS8602, CS8605, CS8629, CS8655, CS8670, CS8846, CS8847, S2259, S3655, V3064, V3080, V3095, V3106, V3151, V3152, V3218",
            "Restricts exact-proof suppression to supported external diagnostic IDs.",
            ImmutableArray.Create(
                "none",
                "cs8509",
                "cs8524",
                "cs8602",
                "cs8605",
                "cs8629",
                "cs8655",
                "cs8670",
                "cs8846",
                "cs8847",
                "s2259",
                "s3655",
                "v3064",
                "v3080",
                "v3095",
                "v3106",
                "v3151",
                "v3152",
                "v3218")),
        new AnalyzerConfigurationOption(
            ConfigKeys.ReportExceptions,
            AnalyzerConfigurationScope.GlobalAndTree,
            AnalyzerConfigurationValueKind.Bool,
            "false",
            "Emits optional SP0010 exception summary diagnostics."),
        new AnalyzerConfigurationOption(
            ConfigKeys.CheckedExceptions,
            AnalyzerConfigurationScope.GlobalAndTree,
            AnalyzerConfigurationValueKind.Bool,
            "false",
            "Emits optional SP0011 exception site diagnostics."),
        new AnalyzerConfigurationOption(
            ConfigKeys.EnableEffectSummaryJson,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.Bool,
            "false",
            "Enables identity-validated AdditionalFiles summaries that can override built-in purity evidence.",
            purityPolicyImpact: PurityPolicyImpact.TrustsPure |
                                PurityPolicyImpact.ForcesImpure |
                                PurityPolicyImpact.EnablesGeneratedOverrides),
        new AnalyzerConfigurationOption(
            ConfigKeys.SmtMode,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.SmtMode,
            "bounded",
            "Controls bounded SMT proof mode.",
            ImmutableArray.Create("disabled", "bounded", "deep")),
        new AnalyzerConfigurationOption(
            ConfigKeys.SmtTimeoutMs,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.PositiveInteger,
            "mode default",
            "Per-query SMT timeout in milliseconds."),
        new AnalyzerConfigurationOption(
            ConfigKeys.SmtMethodBudgetMs,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.PositiveInteger,
            "mode default",
            "Per-method SMT budget in milliseconds."),
        new AnalyzerConfigurationOption(
            ConfigKeys.SmtMaxPathConditions,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.PositiveInteger,
            "mode default",
            "Maximum SMT path conditions considered per method."),
        new AnalyzerConfigurationOption(
            ConfigKeys.SmtMaxExpressionNodes,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.PositiveInteger,
            "mode default",
            "Maximum SMT expression nodes considered per query."),
        new AnalyzerConfigurationOption(
            ConfigKeys.SmtTransientRetryCount,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.NonNegativeInteger,
            "1",
            "Retries after a transient Z3 context failure."),
        new AnalyzerConfigurationOption(
            ConfigKeys.SmtRecycleContextOnTransientFailure,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.Bool,
            "true",
            "Recycles the current thread's solver context after a transient failure."),
        new AnalyzerConfigurationOption(
            ConfigKeys.SmtDisposeThreadContextOnServiceDispose,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.Bool,
            "false",
            "Disposes the current thread's shared solver context with the analysis service."),
        new AnalyzerConfigurationOption(
            ConfigKeys.AnalysisMaxMergedIfElseFacts,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.PositiveInteger,
            "16",
            "Maximum facts retained while merging if/else branches."),
        new AnalyzerConfigurationOption(
            ConfigKeys.AnalysisMaxMergedSwitchFacts,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.PositiveInteger,
            "32",
            "Maximum facts retained while merging switch branches."),
        new AnalyzerConfigurationOption(
            ConfigKeys.AnalysisMaxMergedTryFacts,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.PositiveInteger,
            "16",
            "Maximum facts retained while merging try/catch/finally branches."),
        new AnalyzerConfigurationOption(
            ConfigKeys.AnalysisMaxTryCompletionBranches,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.PositiveInteger,
            "8",
            "Maximum try/catch completion branches analyzed at a program point."),
        new AnalyzerConfigurationOption(
            ConfigKeys.AnalysisMaxFiniteForeachElementFacts,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.PositiveInteger,
            "8",
            "Maximum finite collection elements modeled for foreach facts."),
        new AnalyzerConfigurationOption(
            ConfigKeys.AnalysisMaxScopedBlockCompletionStatements,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.PositiveInteger,
            "32",
            "Maximum completed statements scanned while deriving scoped block facts."),
        new AnalyzerConfigurationOption(
            ConfigKeys.AnalysisMaxStructuralNullStateDepth,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.PositiveInteger,
            "4",
            "Maximum structural expression depth inspected for null-state facts."),
        new AnalyzerConfigurationOption(
            ConfigKeys.AnalysisMaxMergedPathConditions,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.PositiveInteger,
            "32",
            "Maximum synthesized path conditions retained across merged states."),
        new AnalyzerConfigurationOption(
            ConfigKeys.AnalysisMaxMergeableFactsPerTargetPerState,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.PositiveInteger,
            "4",
            "Maximum mergeable facts retained per target and state."),
        new AnalyzerConfigurationOption(
            ConfigKeys.AnalysisMaxFactChoiceCombinationsPerTarget,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.PositiveInteger,
            "64",
            "Maximum fact-choice combinations explored per merge target."),
        new AnalyzerConfigurationOption(
            ConfigKeys.AnalysisMaxGuardFactsPerTargetPerState,
            AnalyzerConfigurationScope.GlobalOnly,
            AnalyzerConfigurationValueKind.PositiveInteger,
            "6",
            "Maximum guard facts retained per target and state."));

    public static ImmutableArray<AnalyzerConfigurationOption> GlobalOptions =>
        All.Where(static option => option.IsGlobal).ToImmutableArray();

    public static ImmutableArray<AnalyzerConfigurationOption> TreeOptions =>
        All.Where(static option => option.IsTree).ToImmutableArray();

    public static ImmutableArray<AnalyzerConfigurationOption> GlobalOnlyOptions =>
        All.Where(static option => option.Scope == AnalyzerConfigurationScope.GlobalOnly).ToImmutableArray();

    public static AnalyzerConfigurationOption Get(string key)
    {
        return OptionsByKey[key];
    }

    public static bool TryGet(string key, out AnalyzerConfigurationOption option)
    {
        if (OptionsByKey.TryGetValue(key, out var found))
        {
            option = found;
            return true;
        }

        option = null!;
        return false;
    }

    public static ImmutableArray<AnalyzerConfigurationOption> PurityPolicyOptions =>
        All.Where(static option => option.PurityPolicyImpact != PurityPolicyImpact.None).ToImmutableArray();
}

internal sealed class AnalyzerConfigurationOption
{
    public AnalyzerConfigurationOption(
        string key,
        AnalyzerConfigurationScope scope,
        AnalyzerConfigurationValueKind valueKind,
        string defaultValue,
        string description,
        ImmutableArray<string> allowedValues = default,
        PurityPolicyImpact purityPolicyImpact = PurityPolicyImpact.None)
    {
        Key = key;
        Scope = scope;
        ValueKind = valueKind;
        DefaultValue = defaultValue;
        Description = description;
        AllowedValues = allowedValues.IsDefault ? ImmutableArray<string>.Empty : allowedValues;
        PurityPolicyImpact = purityPolicyImpact;
    }

    public string Key { get; }
    public AnalyzerConfigurationScope Scope { get; }
    public AnalyzerConfigurationValueKind ValueKind { get; }
    public string DefaultValue { get; }
    public string Description { get; }
    public ImmutableArray<string> AllowedValues { get; }
    public PurityPolicyImpact PurityPolicyImpact { get; }

    public bool IsGlobal =>
        Scope == AnalyzerConfigurationScope.GlobalOnly ||
        Scope == AnalyzerConfigurationScope.GlobalAndTree;

    public bool IsTree =>
        Scope == AnalyzerConfigurationScope.TreeOnly ||
        Scope == AnalyzerConfigurationScope.GlobalAndTree;
}

[Flags]
internal enum PurityPolicyImpact
{
    None = 0,
    TrustsPure = 1,
    ForcesImpure = 2,
    ChangesStrictness = 4,
    ChangesAttributeIdentity = 8,
    EnablesGeneratedOverrides = 16
}

internal enum AnalyzerConfigurationScope
{
    GlobalOnly,
    TreeOnly,
    GlobalAndTree
}

internal enum AnalyzerConfigurationValueKind
{
    Bool,
    StringList,
    NonNegativeInteger,
    PositiveInteger,
    PurityProfile,
    MissingPuritySuggestionScope,
    RuntimeHazardMode,
    SmtMode,
    AllowedValue,
    AllowedValueList
}
