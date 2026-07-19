using System.Collections.Immutable;
using SharpProof.Symbolic.Smt;

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
        GlobalOption(ConfigKeys.KnownImpureMethods, AnalyzerConfigurationValueKind.StructuralMemberKeyList,
            string.Empty,
            "Canonical structural method keys forced impure before generated or built-in purity evidence; property accessors require .get or .set.",
            purityPolicyImpact: PurityPolicyImpact.ForcesImpure),
        GlobalOption(ConfigKeys.KnownPureMethods, AnalyzerConfigurationValueKind.StructuralMemberKeyList,
            string.Empty,
            "Canonical structural method keys trusted pure unless a higher-priority impure or generated policy wins; property accessors require .get or .set.",
            purityPolicyImpact: PurityPolicyImpact.TrustsPure),
        GlobalOption(ConfigKeys.KnownImpureNamespaces, AnalyzerConfigurationValueKind.StringList,
            string.Empty,
            "Namespaces forced impure except for exact configured-pure member exemptions.",
            purityPolicyImpact: PurityPolicyImpact.ForcesImpure),
        GlobalOption(ConfigKeys.KnownImpureTypes, AnalyzerConfigurationValueKind.StringList,
            string.Empty,
            "Types forced impure except for exact configured-pure member exemptions.",
            purityPolicyImpact: PurityPolicyImpact.ForcesImpure),
        GlobalOption(ConfigKeys.AttributeStubNamespaces, AnalyzerConfigurationValueKind.StringList,
            "SharpProof.Attributes",
            "Namespaces accepted for source-only SharpProof attributes, including purity boundary attributes.",
            purityPolicyImpact: PurityPolicyImpact.TrustsPure |
                                PurityPolicyImpact.ForcesImpure |
                                PurityPolicyImpact.ChangesAttributeIdentity),
        GlobalOption(ConfigKeys.PurityProfile, AnalyzerConfigurationValueKind.PurityProfile,
            "balanced",
            "Selects strict, balanced, or pragmatic purity fallback policy.",
            ImmutableArray.Create("strict", "balanced", "pragmatic"),
            PurityPolicyImpact.ChangesStrictness),
        GlobalOption(ConfigKeys.TrustedBoundaryReviewMode, AnalyzerConfigurationValueKind.AllowedValue,
            "off",
            "Reports applied purity trust shortcuts, or all candidates including overridden shortcuts.",
            ImmutableArray.Create("off", "used", "all")),
        TreeBool(ConfigKeys.SuggestMissingEnforcePure, "true",
            "Controls SP0004 inferred purity suggestions."),
        TreeSuggestionScope(ConfigKeys.SuggestMissingEnforcePureScope,
            "Controls which method visibility SP0004 can suggest."),
        TreeBool(ConfigKeys.SuggestMissingEnforcePureExcludeGenerated, "false",
            "Suppresses SP0004 in generated-looking source paths."),
        TreeBool(ConfigKeys.SuggestMissingEnforcePureExcludeTests, "false",
            "Suppresses SP0004 in test-looking namespaces and source paths."),
        TreeOption(ConfigKeys.SuggestMissingEnforcePureMinComplexity,
            AnalyzerConfigurationValueKind.NonNegativeInteger,
            "0",
            "Minimum inferred complexity required before SP0004 is suggested."),
        TreeOption(ConfigKeys.SuggestMissingEnforcePureNamespaceFilters, AnalyzerConfigurationValueKind.StringList,
            string.Empty,
            "Namespace prefixes eligible for SP0004 suggestions."),
        TreeBool(ConfigKeys.SuggestInferredContracts, "false",
            "Controls opt-in SP0034-SP0039 and SP0046 inferred contract suggestions."),
        TreeSuggestionScope(ConfigKeys.SuggestInferredContractsScope,
            "Controls which method visibility can receive inferred contract suggestions."),
        TreeOption(ConfigKeys.SuggestInferredContractsKinds, AnalyzerConfigurationValueKind.AllowedValueList,
            "zero-allocations, capabilities, complexity, exceptions, ensures, requires, nullability",
            "Selects inferred contract families.",
            ImmutableArray.Create(
                "zero-allocations",
                "capabilities",
                "complexity",
                "exceptions",
                "ensures",
                "requires",
                "nullability")),
        TreeOption(ConfigKeys.SuggestInferredContractsMinimumConfidence, AnalyzerConfigurationValueKind.AllowedValue,
            "high",
            "Minimum confidence for inferred contract suggestions.",
            ImmutableArray.Create("medium", "high")),
        TreeBool(ConfigKeys.EmitExplanations, "false",
            "Emits optional SP0009 proof explanation diagnostics."),
        TreeBool(ConfigKeys.ReportBclFallbackGuesses, "false",
            "Emits optional SP0012 BCL fallback guess diagnostics."),
        TreeOption(ConfigKeys.RuntimeHazardMode, AnalyzerConfigurationValueKind.RuntimeHazardMode,
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
        TreeBool(ConfigKeys.SuppressProvenDiagnostics, "false",
            "Controls opt-in suppression of allowlisted external diagnostics backed by exact SharpProof proofs."),
        TreeOption(ConfigKeys.SuppressionDiagnosticIds, AnalyzerConfigurationValueKind.AllowedValueList,
            string.Join(", ", SharpProofDiagnosticSuppressor.SupportedDiagnosticIds.OrderBy(
                static id => id,
                StringComparer.Ordinal)),
            "Restricts exact-proof suppression to supported external diagnostic IDs.",
            SharpProofDiagnosticSuppressor.SupportedDiagnosticIds
                .OrderBy(static id => id, StringComparer.Ordinal)
                .Select(static id => id.ToLowerInvariant())
                .Prepend("none")
                .ToImmutableArray()),
        TreeBool(ConfigKeys.ReportExceptions, "false",
            "Emits optional SP0010 exception summary diagnostics."),
        TreeBool(ConfigKeys.ReportNullableInconclusive, "false",
            "Emits SP0047 when nullable contract or suppression verification is inconclusive."),
        TreeBool(ConfigKeys.CheckedExceptions, "false",
            "Emits optional SP0011 exception site diagnostics."),
        GlobalBool(ConfigKeys.EnableEffectSummaryJson,
            "false",
            "Enables identity-validated AdditionalFiles summaries that can override built-in purity evidence.",
            purityPolicyImpact: PurityPolicyImpact.TrustsPure |
                                PurityPolicyImpact.ForcesImpure |
                                PurityPolicyImpact.EnablesGeneratedOverrides),
        GlobalOption(ConfigKeys.SmtMode, AnalyzerConfigurationValueKind.SmtMode,
            "bounded",
            "Controls bounded SMT proof mode.",
            ImmutableArray.Create("disabled", "bounded", "deep")),
        GlobalPositiveInteger(ConfigKeys.SmtTimeoutMs,
            AnalyzerConfigurationDefault.ForSmtModes(750, 2000, "ms"),
            "Per-query SMT timeout in milliseconds."),
        GlobalPositiveInteger(ConfigKeys.SmtMethodBudgetMs,
            AnalyzerConfigurationDefault.ForSmtModes(5000, 15000, "ms"),
            "Per-method SMT budget in milliseconds."),
        GlobalPositiveInteger(ConfigKeys.SmtMaxPathConditions,
            AnalyzerConfigurationDefault.ForSmtModes(192, 512),
            "Maximum SMT path conditions considered per method."),
        GlobalPositiveInteger(ConfigKeys.SmtMaxExpressionNodes,
            AnalyzerConfigurationDefault.ForSmtModes(2048, 8192),
            "Maximum SMT expression nodes considered per query."),
        GlobalOption(ConfigKeys.SmtTransientRetryCount, AnalyzerConfigurationValueKind.NonNegativeInteger,
            "1",
            "Retries after a transient Z3 context failure."),
        GlobalBool(ConfigKeys.SmtRecycleContextOnTransientFailure, "true",
            "Recycles the current thread's solver context after a transient failure."),
        GlobalBool(ConfigKeys.SmtDisposeThreadContextOnServiceDispose, "false",
            "Disposes the current thread's shared solver context with the analysis service."),
        GlobalPositiveInteger(ConfigKeys.AnalysisMaxMergedIfElseFacts, "16",
            "Maximum facts retained while merging if/else branches."),
        GlobalPositiveInteger(ConfigKeys.AnalysisMaxMergedSwitchFacts, "32",
            "Maximum facts retained while merging switch branches."),
        GlobalPositiveInteger(ConfigKeys.AnalysisMaxMergedTryFacts, "16",
            "Maximum facts retained while merging try/catch/finally branches."),
        GlobalPositiveInteger(ConfigKeys.AnalysisMaxTryCompletionBranches, "8",
            "Maximum try/catch completion branches analyzed at a program point."),
        GlobalPositiveInteger(ConfigKeys.AnalysisMaxFiniteForeachElementFacts, "8",
            "Maximum finite collection elements modeled for foreach facts."),
        GlobalPositiveInteger(ConfigKeys.AnalysisMaxScopedBlockCompletionStatements, "32",
            "Maximum completed statements scanned while deriving scoped block facts."),
        GlobalPositiveInteger(ConfigKeys.AnalysisMaxStructuralNullStateDepth, "4",
            "Maximum structural expression depth inspected for null-state facts."),
        GlobalPositiveInteger(ConfigKeys.AnalysisMaxMergedPathConditions, "32",
            "Maximum synthesized path conditions retained across merged states."),
        GlobalPositiveInteger(ConfigKeys.AnalysisMaxMergeableFactsPerTargetPerState, "4",
            "Maximum mergeable facts retained per target and state."),
        GlobalPositiveInteger(ConfigKeys.AnalysisMaxFactChoiceCombinationsPerTarget, "64",
            "Maximum fact-choice combinations explored per merge target."),
        GlobalPositiveInteger(ConfigKeys.AnalysisMaxGuardFactsPerTargetPerState, "6",
            "Maximum guard facts retained per target and state."));

    private static AnalyzerConfigurationOption GlobalOption(
        string key,
        AnalyzerConfigurationValueKind valueKind,
        AnalyzerConfigurationDefault defaultValue,
        string description,
        ImmutableArray<string> allowedValues = default,
        PurityPolicyImpact purityPolicyImpact = PurityPolicyImpact.None,
        ImmutableArray<string> acceptedAliases = default) =>
        Option(key, AnalyzerConfigurationScope.GlobalOnly, valueKind, defaultValue, description,
            allowedValues, purityPolicyImpact, acceptedAliases);

    private static AnalyzerConfigurationOption TreeOption(
        string key,
        AnalyzerConfigurationValueKind valueKind,
        AnalyzerConfigurationDefault defaultValue,
        string description,
        ImmutableArray<string> allowedValues = default,
        PurityPolicyImpact purityPolicyImpact = PurityPolicyImpact.None,
        ImmutableArray<string> acceptedAliases = default) =>
        Option(key, AnalyzerConfigurationScope.GlobalAndTree, valueKind, defaultValue, description,
            allowedValues, purityPolicyImpact, acceptedAliases);

    private static AnalyzerConfigurationOption TreeSuggestionScope(string key, string description) =>
        TreeOption(
            key,
            AnalyzerConfigurationValueKind.MissingPuritySuggestionScope,
            "all",
            description,
            ImmutableArray.Create("all", "public", "internal", "off"),
            acceptedAliases: ImmutableArray.Create("public-only", "internal-only", "none", "false"));

    private static AnalyzerConfigurationOption TreeBool(
        string key,
        string defaultValue,
        string description) =>
        TreeOption(key, AnalyzerConfigurationValueKind.Bool, defaultValue, description);

    private static AnalyzerConfigurationOption GlobalBool(
        string key,
        string defaultValue,
        string description,
        PurityPolicyImpact purityPolicyImpact = PurityPolicyImpact.None) =>
        GlobalOption(key, AnalyzerConfigurationValueKind.Bool, defaultValue, description,
            purityPolicyImpact: purityPolicyImpact);

    private static AnalyzerConfigurationOption GlobalPositiveInteger(
        string key,
        AnalyzerConfigurationDefault defaultValue,
        string description) =>
        GlobalOption(key, AnalyzerConfigurationValueKind.PositiveInteger, defaultValue, description);

    private static AnalyzerConfigurationOption Option(
        string key,
        AnalyzerConfigurationScope scope,
        AnalyzerConfigurationValueKind valueKind,
        AnalyzerConfigurationDefault defaultValue,
        string description,
        ImmutableArray<string> allowedValues = default,
        PurityPolicyImpact purityPolicyImpact = PurityPolicyImpact.None,
        ImmutableArray<string> acceptedAliases = default) =>
        new(key, scope, valueKind, defaultValue, description, allowedValues, purityPolicyImpact, acceptedAliases);

    public static ImmutableArray<AnalyzerConfigurationOption> GlobalOptions =>
        All.Where(static option => option.IsGlobal).ToImmutableArray();

    public static AnalyzerConfigurationOption Get(string key)
    {
        return OptionsByKey[key];
    }


    internal static bool TryParseRuntimeHazardMode(string? value, out RuntimeHazardMode mode)
    {
        mode = value?.Trim().ToLowerInvariant() switch
        {
            "none" => RuntimeHazardMode.Off,
            "sites" => RuntimeHazardMode.Sites,
            "summaries" => RuntimeHazardMode.Summaries,
            "all" => RuntimeHazardMode.All,
            "unknowns" => RuntimeHazardMode.Unknowns,
            "sites-and-unknowns" => RuntimeHazardMode.SitesAndUnknowns,
            "all-and-unknowns" => RuntimeHazardMode.AllAndUnknowns,
            _ => (RuntimeHazardMode)(-1)
        };
        return mode != (RuntimeHazardMode)(-1);
    }

    internal static bool IsCanonicalAllowedValue(AnalyzerConfigurationOption option, string? value)
    {
        if (option == null) throw new ArgumentNullException(nameof(option));
        if (string.IsNullOrWhiteSpace(value)) return false;
        return option.AllowedValues.Contains(value!.Trim().ToLowerInvariant(), StringComparer.Ordinal);
    }

    internal static bool IsAcceptedValue(AnalyzerConfigurationOption option, string? value)
    {
        if (IsCanonicalAllowedValue(option, value)) return true;
        return !string.IsNullOrWhiteSpace(value) &&
               option.AcceptedAliases.Contains(value!.Trim().ToLowerInvariant(), StringComparer.Ordinal);
    }

    public static ImmutableArray<AnalyzerConfigurationOption> PurityPolicyOptions =>
        All.Where(static option => option.PurityPolicyImpact != PurityPolicyImpact.None).ToImmutableArray();
}

internal sealed class AnalyzerConfigurationOption(
    string key,
    AnalyzerConfigurationScope scope,
    AnalyzerConfigurationValueKind valueKind,
    AnalyzerConfigurationDefault defaultValue,
    string description,
    ImmutableArray<string> allowedValues = default,
    PurityPolicyImpact purityPolicyImpact = PurityPolicyImpact.None,
    ImmutableArray<string> acceptedAliases = default)
{
    public string Key { get; } = key;
    public AnalyzerConfigurationScope Scope { get; } = scope;
    public AnalyzerConfigurationValueKind ValueKind { get; } = valueKind;
    public AnalyzerConfigurationDefault Default { get; } = defaultValue;
    public string DefaultValue => Default.DocumentationValue;
    public string Description { get; } = description;
    public ImmutableArray<string> AllowedValues { get; } =
        allowedValues.IsDefault ? ImmutableArray<string>.Empty : allowedValues;
    public PurityPolicyImpact PurityPolicyImpact { get; } = purityPolicyImpact;
    public ImmutableArray<string> AcceptedAliases { get; } =
        acceptedAliases.IsDefault ? ImmutableArray<string>.Empty : acceptedAliases;

    public bool IsGlobal =>
        Scope == AnalyzerConfigurationScope.GlobalOnly ||
        Scope == AnalyzerConfigurationScope.GlobalAndTree;

    public bool IsTree =>
        Scope == AnalyzerConfigurationScope.TreeOnly ||
        Scope == AnalyzerConfigurationScope.GlobalAndTree;
}

internal readonly record struct AnalyzerConfigurationDefault(
    string? ConstantValue,
    int BoundedValue,
    int DeepValue,
    string Unit)
{
    internal bool IsModeDependent => ConstantValue == null;

    internal string DocumentationValue => IsModeDependent
        ? Format(BoundedValue) + " (disabled/bounded), " + Format(DeepValue) + " (deep)"
        : ConstantValue ?? string.Empty;

    internal static AnalyzerConfigurationDefault ForSmtModes(
        int boundedValue,
        int deepValue,
        string unit = "")
    {
        if (boundedValue <= 0) throw new ArgumentOutOfRangeException(nameof(boundedValue));
        if (deepValue <= 0) throw new ArgumentOutOfRangeException(nameof(deepValue));
        return new AnalyzerConfigurationDefault(null, boundedValue, deepValue, unit ?? string.Empty);
    }

    internal string Resolve(SmtAnalysisMode mode)
    {
        if (!IsModeDependent) return ConstantValue ?? string.Empty;
        return (mode == SmtAnalysisMode.Deep ? DeepValue : BoundedValue)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static implicit operator AnalyzerConfigurationDefault(string value)
    {
        return new AnalyzerConfigurationDefault(
            value ?? throw new ArgumentNullException(nameof(value)),
            0,
            0,
            string.Empty);
    }

    private string Format(int value)
    {
        var formatted = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Unit.Length == 0 ? formatted : formatted + " " + Unit;
    }

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
    StructuralMemberKeyList,
    NonNegativeInteger,
    PositiveInteger,
    PurityProfile,
    MissingPuritySuggestionScope,
    RuntimeHazardMode,
    SmtMode,
    AllowedValue,
    AllowedValueList
}
