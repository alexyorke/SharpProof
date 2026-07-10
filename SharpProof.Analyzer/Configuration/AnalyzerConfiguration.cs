using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Configuration;

internal class AnalyzerConfiguration
{
    private AnalyzerConfiguration(
        ImmutableHashSet<string> extraImpureMethods,
        ImmutableHashSet<string> extraPureMethods,
        ImmutableHashSet<string> extraImpureNamespaces,
        ImmutableHashSet<string> extraImpureTypes,
        ImmutableHashSet<string> attributeStubNamespaces,
        bool suggestMissingEnforcePure,
        MissingPuritySuggestionOptions missingPuritySuggestions,
        bool emitExplanations,
        bool reportBclFallbackGuesses,
        RuntimeHazardMode runtimeHazardMode,
        bool reportExceptions,
        bool checkedExceptions,
        bool enableEffectSummaryJson,
        string purityProfile,
        SmtAnalysisOptions smtOptions,
        ImmutableArray<InvalidAnalyzerConfigurationValue> invalidConfigurationValues)
    {
        ExtraKnownImpureMethods = extraImpureMethods;
        ExtraKnownPureMethods = extraPureMethods;
        ExtraKnownImpureNamespaces = extraImpureNamespaces;
        ExtraKnownImpureTypes = extraImpureTypes;
        AttributeStubNamespaces = attributeStubNamespaces;
        SuggestMissingEnforcePure = suggestMissingEnforcePure;
        MissingPuritySuggestions = missingPuritySuggestions;
        EmitExplanations = emitExplanations;
        ReportBclFallbackGuesses = reportBclFallbackGuesses;
        RuntimeHazardMode = runtimeHazardMode;
        ReportExceptions = reportExceptions;
        CheckedExceptions = checkedExceptions;
        EnableEffectSummaryJson = enableEffectSummaryJson;
        PurityProfile = purityProfile;
        SmtOptions = smtOptions;
        InvalidConfigurationValues = invalidConfigurationValues;
    }

    public ImmutableHashSet<string> ExtraKnownImpureMethods { get; }
    public ImmutableHashSet<string> ExtraKnownPureMethods { get; }
    public ImmutableHashSet<string> ExtraKnownImpureNamespaces { get; }
    public ImmutableHashSet<string> ExtraKnownImpureTypes { get; }
    public ImmutableHashSet<string> AttributeStubNamespaces { get; }
    public bool SuggestMissingEnforcePure { get; }
    public MissingPuritySuggestionOptions MissingPuritySuggestions { get; }
    public bool EmitExplanations { get; }
    public bool ReportBclFallbackGuesses { get; }
    public RuntimeHazardMode RuntimeHazardMode { get; }
    public bool ReportExceptions { get; }
    public bool CheckedExceptions { get; }
    public bool EnableEffectSummaryJson { get; }
    public string PurityProfile { get; }
    public SmtAnalysisOptions SmtOptions { get; }
    public ImmutableArray<InvalidAnalyzerConfigurationValue> InvalidConfigurationValues { get; }

    public static AnalyzerConfiguration FromOptions(AnalyzerOptions options)
    {
        var impureMethods = GetValues(options, ConfigKeys.KnownImpureMethods);
        var pureMethods = GetValues(options, ConfigKeys.KnownPureMethods);
        var impureNamespaces = GetValues(options, ConfigKeys.KnownImpureNamespaces);
        var impureTypes = GetValues(options, ConfigKeys.KnownImpureTypes);
        var attributeStubNamespaces = GetValues(options, ConfigKeys.AttributeStubNamespaces);
        var invalidConfigurationValues = GetInvalidGlobalConfigurationValues(options);
        var suggestMissing = GetBoolOrDefaultTrue(options, ConfigKeys.SuggestMissingEnforcePure);
        var missingPuritySuggestions = new MissingPuritySuggestionOptions(
            suggestMissing,
            GetMissingPuritySuggestionScope(options),
            GetBool(options, ConfigKeys.SuggestMissingEnforcePureExcludeGenerated),
            GetBool(options, ConfigKeys.SuggestMissingEnforcePureExcludeTests),
            GetNonNegativeInt(options, ConfigKeys.SuggestMissingEnforcePureMinComplexity),
            GetValues(options, ConfigKeys.SuggestMissingEnforcePureNamespaceFilters));
        var emitExplanations = GetBool(options, ConfigKeys.EmitExplanations);
        var reportBclFallbackGuesses = GetBool(options, ConfigKeys.ReportBclFallbackGuesses);
        var runtimeHazardMode = GetRuntimeHazardMode(options, RuntimeHazardMode.Off);
        var reportExceptions = GetBool(options, ConfigKeys.ReportExceptions);
        var checkedExceptions = GetBool(options, ConfigKeys.CheckedExceptions);
        var enableEffectSummaryJson = GetBool(options, ConfigKeys.EnableEffectSummaryJson);
        return new AnalyzerConfiguration(
            impureMethods,
            pureMethods,
            impureNamespaces,
            impureTypes,
            attributeStubNamespaces,
            suggestMissing,
            missingPuritySuggestions,
            emitExplanations,
            reportBclFallbackGuesses,
            runtimeHazardMode,
            reportExceptions,
            checkedExceptions,
            enableEffectSummaryJson,
            GetPurityProfile(options),
            GetSmtOptions(options),
            invalidConfigurationValues);
    }

    public static MissingPuritySuggestionOptions GetMissingPuritySuggestionOptions(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        MissingPuritySuggestionOptions fallback)
    {
        try
        {
            var treeOptions = options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
            var suggestMissing = GetBoolOrDefault(treeOptions, ConfigKeys.SuggestMissingEnforcePure, fallback.Enabled);
            return new MissingPuritySuggestionOptions(
                suggestMissing,
                GetMissingPuritySuggestionScope(treeOptions, fallback.Scope),
                GetBoolOrDefault(treeOptions, ConfigKeys.SuggestMissingEnforcePureExcludeGenerated,
                    fallback.ExcludeGeneratedFiles),
                GetBoolOrDefault(treeOptions, ConfigKeys.SuggestMissingEnforcePureExcludeTests,
                    fallback.ExcludeTestFiles),
                GetNonNegativeInt(treeOptions, ConfigKeys.SuggestMissingEnforcePureMinComplexity,
                    fallback.MinimumComplexity),
                GetValues(treeOptions, ConfigKeys.SuggestMissingEnforcePureNamespaceFilters,
                    fallback.NamespaceFilters));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return fallback;
        }
    }

    public static bool GetEmitExplanations(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        bool fallback)
    {
        try
        {
            var treeOptions = options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
            return GetBoolOrDefault(treeOptions, ConfigKeys.EmitExplanations, fallback);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return fallback;
        }
    }

    public static bool GetReportBclFallbackGuesses(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        bool fallback)
    {
        try
        {
            var treeOptions = options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
            return GetBoolOrDefault(treeOptions, ConfigKeys.ReportBclFallbackGuesses, fallback);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return fallback;
        }
    }

    public static bool GetReportExceptions(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        bool fallback)
    {
        try
        {
            var treeOptions = options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
            return GetBoolOrDefault(treeOptions, ConfigKeys.ReportExceptions, fallback);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return fallback;
        }
    }

    public static bool GetCheckedExceptions(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        bool fallback)
    {
        try
        {
            var treeOptions = options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
            return GetBoolOrDefault(treeOptions, ConfigKeys.CheckedExceptions, fallback);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return fallback;
        }
    }

    public static RuntimeHazardMode GetRuntimeHazardMode(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        RuntimeHazardMode fallback)
    {
        try
        {
            var treeOptions = options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
            return GetRuntimeHazardMode(treeOptions, fallback);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return fallback;
        }
    }

    public static bool RuntimeHazardReportsMethodSummaries(RuntimeHazardMode mode)
    {
        return (mode & RuntimeHazardMode.Summaries) != 0;
    }

    public static bool RuntimeHazardReportsSites(RuntimeHazardMode mode)
    {
        return (mode & RuntimeHazardMode.Sites) != 0;
    }

    public static bool RuntimeHazardReportsUnknownCandidates(RuntimeHazardMode mode)
    {
        return (mode & RuntimeHazardMode.Unknowns) != 0;
    }

    private static ImmutableHashSet<string> GetValues(AnalyzerOptions options, string key)
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        if (TryGetGlobalOption(options, key, out var value))
            foreach (var token in value.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var item = token.Trim();
                if (item.Length > 0) builder.Add(item);
            }

        return builder.ToImmutable();
    }

    private static ImmutableHashSet<string> GetValues(
        AnalyzerConfigOptions options,
        string key,
        ImmutableHashSet<string> fallback)
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        if (!options.TryGetValue(key, out var value)) return fallback;

        if (string.IsNullOrWhiteSpace(value)) return builder.ToImmutable();

        foreach (var token in value.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var item = token.Trim();
            if (item.Length > 0) builder.Add(item);
        }

        return builder.ToImmutable();
    }

    private static bool GetBool(AnalyzerOptions options, string key)
    {
        return TryGetGlobalOption(options, key, out var value) &&
               TryParseBool(value, out var parsed) &&
               parsed;
    }

    private static bool GetBoolOrDefaultTrue(AnalyzerOptions options, string key)
    {
        if (!TryGetGlobalOption(options, key, out var value)) return true;

        if (TryParseBool(value, out var parsed)) return parsed;

        return true;
    }

    private static bool GetBoolOrDefault(AnalyzerConfigOptions options, string key, bool fallback)
    {
        if (!options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) return fallback;

        return TryParseBool(value, out var parsed) ? parsed : fallback;
    }

    private static bool TryParseBool(string value, out bool parsed)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "1":
            case "true":
            case "yes":
            case "on":
                parsed = true;
                return true;
            case "0":
            case "false":
            case "no":
            case "off":
                parsed = false;
                return true;
            default:
                parsed = false;
                return false;
        }
    }

    private static MissingPuritySuggestionScope GetMissingPuritySuggestionScope(AnalyzerOptions options)
    {
        if (TryGetGlobalOption(options, ConfigKeys.SuggestMissingEnforcePureScope, out var value))
            switch (value.Trim().ToLowerInvariant())
            {
                case "all":
                    return MissingPuritySuggestionScope.All;
                case "public":
                case "public-only":
                    return MissingPuritySuggestionScope.Public;
                case "internal":
                case "internal-only":
                    return MissingPuritySuggestionScope.Internal;
                case "off":
                case "none":
                case "false":
                    return MissingPuritySuggestionScope.Off;
            }

        return MissingPuritySuggestionScope.All;
    }

    private static string GetPurityProfile(AnalyzerOptions options)
    {
        if (TryGetGlobalOption(options, ConfigKeys.PurityProfile, out var value))
        {
            var normalized = value.Trim().ToLowerInvariant();
            if (normalized == "strict" || normalized == "balanced" || normalized == "pragmatic") return normalized;
        }

        return "balanced";
    }

    private static RuntimeHazardMode GetRuntimeHazardMode(AnalyzerOptions options, RuntimeHazardMode fallback)
    {
        return TryGetGlobalOption(options, ConfigKeys.RuntimeHazardMode, out var value)
            ? ParseRuntimeHazardMode(value, fallback)
            : fallback;
    }

    private static RuntimeHazardMode GetRuntimeHazardMode(AnalyzerConfigOptions options, RuntimeHazardMode fallback)
    {
        if (options.TryGetValue(ConfigKeys.RuntimeHazardMode, out var value) && !string.IsNullOrWhiteSpace(value))
            return ParseRuntimeHazardMode(value, fallback);

        return fallback;
    }

    private static RuntimeHazardMode ParseRuntimeHazardMode(string value, RuntimeHazardMode fallback)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "none":
            case "disabled":
                return RuntimeHazardMode.Off;
            case "sites":
            case "site":
            case "checked":
            case "checked-exceptions":
            case "warnings":
            case "warning":
                return RuntimeHazardMode.Sites;
            case "summaries":
            case "summary":
            case "method-summaries":
            case "method-summary":
            case "report":
                return RuntimeHazardMode.Summaries;
            case "all":
            case "both":
                return RuntimeHazardMode.All;
            case "unknowns":
            case "unknown":
            case "candidates":
                return RuntimeHazardMode.Unknowns;
            case "sites-and-unknowns":
            case "sites+unknowns":
                return RuntimeHazardMode.SitesAndUnknowns;
            case "all-and-unknowns":
            case "all+unknowns":
                return RuntimeHazardMode.AllAndUnknowns;
        }

        if (TryParseBool(value, out var parsed)) return parsed ? RuntimeHazardMode.Sites : RuntimeHazardMode.Off;

        return fallback;
    }

    private static SmtAnalysisOptions GetSmtOptions(AnalyzerOptions options)
    {
        var mode = GetSmtMode(options, SmtAnalysisOptions.Default.Mode);
        var defaults = SmtAnalysisOptions.ForMode(mode);
        var timeoutMs = GetPositiveInt(options, ConfigKeys.SmtTimeoutMs, (int)defaults.QueryTimeout.TotalMilliseconds);
        var methodBudgetMs = GetPositiveInt(options, ConfigKeys.SmtMethodBudgetMs,
            (int)defaults.MethodBudget.TotalMilliseconds);
        var maxPathConditions = GetPositiveInt(options, ConfigKeys.SmtMaxPathConditions, defaults.MaxPathConditions);
        var maxExpressionNodes = GetPositiveInt(options, ConfigKeys.SmtMaxExpressionNodes, defaults.MaxExpressionNodes);
        return new SmtAnalysisOptions(
            mode,
            TimeSpan.FromMilliseconds(timeoutMs),
            TimeSpan.FromMilliseconds(methodBudgetMs),
            maxPathConditions,
            maxExpressionNodes,
            true);
    }

    private static SmtAnalysisMode GetSmtMode(AnalyzerOptions options, SmtAnalysisMode fallback)
    {
        if (TryGetGlobalOption(options, ConfigKeys.SmtMode, out var value))
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "disabled":
                    return SmtAnalysisMode.Off;
                case "bounded":
                case "default":
                    return SmtAnalysisMode.Bounded;
                case "deep":
                case "aggressive":
                    return SmtAnalysisMode.Deep;
            }

            if (TryParseBool(value, out var parsed)) return parsed ? SmtAnalysisMode.Bounded : SmtAnalysisMode.Off;
        }

        return fallback;
    }

    private static int GetPositiveInt(AnalyzerOptions options, string key, int fallback)
    {
        if (TryGetGlobalOption(options, key, out var value) &&
            int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed > 0)
            return parsed;

        return fallback;
    }

    private static MissingPuritySuggestionScope GetMissingPuritySuggestionScope(
        AnalyzerConfigOptions options,
        MissingPuritySuggestionScope fallback)
    {
        if (options.TryGetValue(ConfigKeys.SuggestMissingEnforcePureScope, out var value) &&
            !string.IsNullOrWhiteSpace(value))
            switch (value.Trim().ToLowerInvariant())
            {
                case "all":
                    return MissingPuritySuggestionScope.All;
                case "public":
                case "public-only":
                    return MissingPuritySuggestionScope.Public;
                case "internal":
                case "internal-only":
                    return MissingPuritySuggestionScope.Internal;
                case "off":
                case "none":
                case "false":
                    return MissingPuritySuggestionScope.Off;
            }

        return fallback;
    }

    private static int GetNonNegativeInt(AnalyzerOptions options, string key)
    {
        if (TryGetGlobalOption(options, key, out var value) &&
            int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed >= 0)
            return parsed;

        return 0;
    }

    private static int GetNonNegativeInt(AnalyzerConfigOptions options, string key, int fallback)
    {
        return options.TryGetValue(key, out var value) &&
               int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
               parsed >= 0
            ? parsed
            : fallback;
    }

    private static bool TryGetGlobalOption(AnalyzerOptions options, string key, out string value)
    {
        try
        {
            var global = options.AnalyzerConfigOptionsProvider.GlobalOptions;
            if (global.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
            {
                value = found;
                return true;
            }

            if (global.TryGetValue("build_property." + key, out found) && !string.IsNullOrWhiteSpace(found))
            {
                value = found;
                return true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }

        value = string.Empty;
        return false;
    }

    private static ImmutableArray<InvalidAnalyzerConfigurationValue> GetInvalidGlobalConfigurationValues(
        AnalyzerOptions options)
    {
        var builder = ImmutableArray.CreateBuilder<InvalidAnalyzerConfigurationValue>();

        foreach (var option in AnalyzerConfigurationOptionRegistry.GlobalOptions)
            ValidateOption(builder, TryGetOption, option);

        return builder.ToImmutable();

        bool TryGetOption(string key, out string value)
        {
            return TryGetGlobalOption(options, key, out value);
        }
    }

    internal static ImmutableArray<InvalidAnalyzerConfigurationValue> GetInvalidTreeConfigurationValues(
        AnalyzerConfigOptions options,
        AnalyzerConfigOptions? globalOptions = null)
    {
        var builder = ImmutableArray.CreateBuilder<InvalidAnalyzerConfigurationValue>();

        foreach (var option in AnalyzerConfigurationOptionRegistry.TreeOptions)
            ValidateOption(builder, TryGetOption, option);

        foreach (var option in AnalyzerConfigurationOptionRegistry.GlobalOnlyOptions)
            ValidateGlobalOnlyTreeOption(builder, TryGetOption, globalOptions, option);

        return builder.ToImmutable();

        bool TryGetOption(string key, out string value)
        {
            return TryGetAnalyzerConfigOption(options, key, out value);
        }
    }

    private static void ValidateOption(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        TryGetConfigurationOption tryGetOption,
        AnalyzerConfigurationOption option)
    {
        switch (option.ValueKind)
        {
            case AnalyzerConfigurationValueKind.Bool:
                ValidateBool(builder, tryGetOption, option.Key);
                return;
            case AnalyzerConfigurationValueKind.StringList:
                return;
            case AnalyzerConfigurationValueKind.NonNegativeInteger:
                ValidateNonNegativeInt(builder, tryGetOption, option.Key);
                return;
            case AnalyzerConfigurationValueKind.PositiveInteger:
                ValidatePositiveInt(builder, tryGetOption, option.Key);
                return;
            case AnalyzerConfigurationValueKind.PurityProfile:
                ValidatePurityProfile(builder, tryGetOption);
                return;
            case AnalyzerConfigurationValueKind.MissingPuritySuggestionScope:
                ValidateMissingPuritySuggestionScope(builder, tryGetOption);
                return;
            case AnalyzerConfigurationValueKind.RuntimeHazardMode:
                ValidateRuntimeHazardMode(builder, tryGetOption);
                return;
            case AnalyzerConfigurationValueKind.SmtMode:
                ValidateSmtMode(builder, tryGetOption);
                return;
            default:
                return;
        }
    }

    private static void ValidateGlobalOnlyTreeOption(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        TryGetConfigurationOption tryGetOption,
        AnalyzerConfigOptions? globalOptions,
        AnalyzerConfigurationOption option)
    {
        if (tryGetOption(option.Key, out var value))
        {
            if (TryGetMatchingGlobalOption(globalOptions, option.Key, value)) return;

            AddInvalidConfigurationValue(
                builder,
                option.Key,
                value,
                "option is compilation-global; set it in a global AnalyzerConfig or MSBuild property");
        }
    }

    private static bool TryGetMatchingGlobalOption(
        AnalyzerConfigOptions? globalOptions,
        string key,
        string treeValue)
    {
        if (globalOptions == null) return false;

        if (globalOptions.TryGetValue(key, out var globalValue) &&
            string.Equals(globalValue, treeValue, StringComparison.Ordinal))
            return true;

        return globalOptions.TryGetValue("build_property." + key, out globalValue) &&
               string.Equals(globalValue, treeValue, StringComparison.Ordinal);
    }

    private static void ValidateBool(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        TryGetConfigurationOption tryGetOption,
        string key)
    {
        if (tryGetOption(key, out var value) &&
            !TryParseBool(value, out _))
            AddInvalidConfigurationValue(builder, key, value, "expected a boolean value");
    }

    private static void ValidatePurityProfile(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        TryGetConfigurationOption tryGetOption)
    {
        if (!tryGetOption(ConfigKeys.PurityProfile, out var value)) return;

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized != "strict" &&
            normalized != "balanced" &&
            normalized != "pragmatic")
            AddInvalidConfigurationValue(builder, ConfigKeys.PurityProfile, value,
                "expected one of: strict, balanced, pragmatic");
    }

    private static void ValidateMissingPuritySuggestionScope(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        TryGetConfigurationOption tryGetOption)
    {
        if (!tryGetOption(ConfigKeys.SuggestMissingEnforcePureScope, out var value)) return;

        var normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "all":
            case "public":
            case "public-only":
            case "internal":
            case "internal-only":
            case "off":
            case "none":
            case "false":
                return;
            default:
                AddInvalidConfigurationValue(builder, ConfigKeys.SuggestMissingEnforcePureScope, value,
                    "expected one of: all, public, internal, off");
                return;
        }
    }

    private static void ValidateRuntimeHazardMode(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        TryGetConfigurationOption tryGetOption)
    {
        if (!tryGetOption(ConfigKeys.RuntimeHazardMode, out var value)) return;

        var normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "none":
            case "disabled":
            case "sites":
            case "site":
            case "checked":
            case "checked-exceptions":
            case "warnings":
            case "warning":
            case "summaries":
            case "summary":
            case "method-summaries":
            case "method-summary":
            case "report":
            case "all":
            case "both":
            case "unknowns":
            case "unknown":
            case "candidates":
            case "sites-and-unknowns":
            case "sites+unknowns":
            case "all-and-unknowns":
            case "all+unknowns":
                return;
        }

        if (TryParseBool(value, out _)) return;

        AddInvalidConfigurationValue(builder, ConfigKeys.RuntimeHazardMode, value,
            "expected one of: none, sites, summaries, all, unknowns, sites-and-unknowns, " +
            "all-and-unknowns, or a boolean value");
    }

    private static void ValidateSmtMode(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        TryGetConfigurationOption tryGetOption)
    {
        if (!tryGetOption(ConfigKeys.SmtMode, out var value)) return;

        var normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "disabled":
            case "bounded":
            case "default":
            case "deep":
            case "aggressive":
                return;
        }

        if (TryParseBool(value, out _)) return;

        AddInvalidConfigurationValue(builder, ConfigKeys.SmtMode, value,
            "expected one of: disabled, bounded, default, deep, aggressive, or a boolean value");
    }

    private static void ValidatePositiveInt(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        TryGetConfigurationOption tryGetOption,
        string key)
    {
        if (tryGetOption(key, out var value) &&
            (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
             parsed <= 0))
            AddInvalidConfigurationValue(builder, key, value, "expected a positive integer");
    }

    private static void ValidateNonNegativeInt(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        TryGetConfigurationOption tryGetOption,
        string key)
    {
        if (tryGetOption(key, out var value) &&
            (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
             parsed < 0))
            AddInvalidConfigurationValue(builder, key, value, "expected a non-negative integer");
    }

    private static void AddInvalidConfigurationValue(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        string key,
        string value,
        string reason)
    {
        builder.Add(new InvalidAnalyzerConfigurationValue(key, value.Trim(), reason));
    }

    private static bool TryGetAnalyzerConfigOption(AnalyzerConfigOptions options, string key, out string value)
    {
        if (options.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private delegate bool TryGetConfigurationOption(string key, out string value);
}

internal readonly record struct InvalidAnalyzerConfigurationValue(
    string Key,
    string Value,
    string Reason);

internal enum MissingPuritySuggestionScope
{
    All,
    Public,
    Internal,
    Off
}

[Flags]
internal enum RuntimeHazardMode
{
    Off = 0,
    Sites = 1,
    Summaries = 2,
    All = Sites | Summaries,
    Unknowns = 4,
    SitesAndUnknowns = Sites | Unknowns,
    AllAndUnknowns = All | Unknowns
}

internal sealed class MissingPuritySuggestionOptions
{
    public MissingPuritySuggestionOptions(
        bool enabled,
        MissingPuritySuggestionScope scope,
        bool excludeGeneratedFiles,
        bool excludeTestFiles,
        int minimumComplexity,
        ImmutableHashSet<string> namespaceFilters)
    {
        Enabled = enabled;
        Scope = scope;
        ExcludeGeneratedFiles = excludeGeneratedFiles;
        ExcludeTestFiles = excludeTestFiles;
        MinimumComplexity = minimumComplexity;
        NamespaceFilters = namespaceFilters;
    }

    public bool Enabled { get; }
    public MissingPuritySuggestionScope Scope { get; }
    public bool ExcludeGeneratedFiles { get; }
    public bool ExcludeTestFiles { get; }
    public int MinimumComplexity { get; }
    public ImmutableHashSet<string> NamespaceFilters { get; }

    public bool IsEnabled => Enabled && Scope != MissingPuritySuggestionScope.Off;
}
