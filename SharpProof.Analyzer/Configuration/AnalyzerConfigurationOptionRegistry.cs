using System;
using System.Collections.Immutable;
using System.Linq;

namespace SharpProof.Analyzer.Configuration
{
    internal static class AnalyzerConfigurationOptionRegistry
    {
        private static readonly ImmutableArray<AnalyzerConfigurationOption> Options = ImmutableArray.Create(
            new AnalyzerConfigurationOption(
                ConfigKeys.KnownImpureMethods,
                AnalyzerConfigurationScope.GlobalOnly,
                AnalyzerConfigurationValueKind.StringList,
                string.Empty,
                "Additional method symbols treated as impure."),
            new AnalyzerConfigurationOption(
                ConfigKeys.KnownPureMethods,
                AnalyzerConfigurationScope.GlobalOnly,
                AnalyzerConfigurationValueKind.StringList,
                string.Empty,
                "Additional method symbols treated as pure."),
            new AnalyzerConfigurationOption(
                ConfigKeys.KnownImpureNamespaces,
                AnalyzerConfigurationScope.GlobalOnly,
                AnalyzerConfigurationValueKind.StringList,
                string.Empty,
                "Namespaces treated as impure trust boundaries."),
            new AnalyzerConfigurationOption(
                ConfigKeys.KnownImpureTypes,
                AnalyzerConfigurationScope.GlobalOnly,
                AnalyzerConfigurationValueKind.StringList,
                string.Empty,
                "Types treated as impure trust boundaries."),
            new AnalyzerConfigurationOption(
                ConfigKeys.AttributeStubNamespaces,
                AnalyzerConfigurationScope.GlobalOnly,
                AnalyzerConfigurationValueKind.StringList,
                "SharpProof.Attributes",
                "Namespaces accepted for source-only SharpProof attribute stubs."),
            new AnalyzerConfigurationOption(
                ConfigKeys.PurityProfile,
                AnalyzerConfigurationScope.GlobalOnly,
                AnalyzerConfigurationValueKind.PurityProfile,
                "balanced",
                "Purity strictness profile.",
                ImmutableArray.Create("strict", "balanced", "pragmatic")),
            new AnalyzerConfigurationOption(
                ConfigKeys.EnableDebugLogging,
                AnalyzerConfigurationScope.GlobalOnly,
                AnalyzerConfigurationValueKind.Bool,
                "false",
                "Reserved debug logging switch."),
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
                "off",
                "Controls SP0010 and SP0011 runtime-hazard reporting.",
                ImmutableArray.Create("none", "sites", "summaries", "all")),
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
                "Loads analyzer AdditionalFiles effect-summary JSON."),
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
                "Maximum SMT expression nodes considered per query."));

        private static readonly ImmutableDictionary<string, AnalyzerConfigurationOption> OptionsByKey =
            Options.ToImmutableDictionary(static option => option.Key, StringComparer.Ordinal);

        public static ImmutableArray<AnalyzerConfigurationOption> All => Options;

        public static ImmutableArray<AnalyzerConfigurationOption> GlobalOptions =>
            Options.Where(static option => option.IsGlobal).ToImmutableArray();

        public static ImmutableArray<AnalyzerConfigurationOption> TreeOptions =>
            Options.Where(static option => option.IsTree).ToImmutableArray();

        public static ImmutableArray<AnalyzerConfigurationOption> GlobalOnlyOptions =>
            Options.Where(static option => option.Scope == AnalyzerConfigurationScope.GlobalOnly).ToImmutableArray();

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
    }

    internal sealed class AnalyzerConfigurationOption
    {
        public AnalyzerConfigurationOption(
            string key,
            AnalyzerConfigurationScope scope,
            AnalyzerConfigurationValueKind valueKind,
            string defaultValue,
            string description,
            ImmutableArray<string> allowedValues = default)
        {
            Key = key;
            Scope = scope;
            ValueKind = valueKind;
            DefaultValue = defaultValue;
            Description = description;
            AllowedValues = allowedValues.IsDefault ? ImmutableArray<string>.Empty : allowedValues;
        }

        public string Key { get; }
        public AnalyzerConfigurationScope Scope { get; }
        public AnalyzerConfigurationValueKind ValueKind { get; }
        public string DefaultValue { get; }
        public string Description { get; }
        public ImmutableArray<string> AllowedValues { get; }

        public bool IsGlobal =>
            Scope == AnalyzerConfigurationScope.GlobalOnly ||
            Scope == AnalyzerConfigurationScope.GlobalAndTree;

        public bool IsTree =>
            Scope == AnalyzerConfigurationScope.TreeOnly ||
            Scope == AnalyzerConfigurationScope.GlobalAndTree;
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
        SmtMode
    }
}
