using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Symbolic;
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
        InferredContractSuggestionOptions inferredContractSuggestions,
        bool emitExplanations,
        bool reportBclFallbackGuesses,
        RuntimeHazardMode runtimeHazardMode,
        ProvenDiagnosticSuppressionOptions provenDiagnosticSuppressions,
        bool reportExceptions,
        bool checkedExceptions,
        bool enableEffectSummaryJson,
        string purityProfile,
        TrustedBoundaryReviewMode trustedBoundaryReviewMode,
        SmtAnalysisOptions smtOptions,
        SymbolicAnalysisLimits analysisLimits,
        ImmutableArray<InvalidAnalyzerConfigurationValue> invalidConfigurationValues)
    {
        ExtraKnownImpureMethods = extraImpureMethods;
        ExtraKnownPureMethods = extraPureMethods;
        ExtraKnownImpureNamespaces = extraImpureNamespaces;
        ExtraKnownImpureTypes = extraImpureTypes;
        AttributeStubNamespaces = attributeStubNamespaces;
        SuggestMissingEnforcePure = suggestMissingEnforcePure;
        MissingPuritySuggestions = missingPuritySuggestions;
        InferredContractSuggestions = inferredContractSuggestions;
        EmitExplanations = emitExplanations;
        ReportBclFallbackGuesses = reportBclFallbackGuesses;
        RuntimeHazardMode = runtimeHazardMode;
        ProvenDiagnosticSuppressions = provenDiagnosticSuppressions;
        ReportExceptions = reportExceptions;
        CheckedExceptions = checkedExceptions;
        EnableEffectSummaryJson = enableEffectSummaryJson;
        PurityProfile = purityProfile;
        TrustedBoundaryReviewMode = trustedBoundaryReviewMode;
        SmtOptions = smtOptions;
        AnalysisLimits = analysisLimits;
        InvalidConfigurationValues = invalidConfigurationValues;
    }

    public ImmutableHashSet<string> ExtraKnownImpureMethods { get; }
    public ImmutableHashSet<string> ExtraKnownPureMethods { get; }
    public ImmutableHashSet<string> ExtraKnownImpureNamespaces { get; }
    public ImmutableHashSet<string> ExtraKnownImpureTypes { get; }
    public ImmutableHashSet<string> AttributeStubNamespaces { get; }
    public bool SuggestMissingEnforcePure { get; }
    public MissingPuritySuggestionOptions MissingPuritySuggestions { get; }
    public InferredContractSuggestionOptions InferredContractSuggestions { get; }
    public bool EmitExplanations { get; }
    public bool ReportBclFallbackGuesses { get; }
    public RuntimeHazardMode RuntimeHazardMode { get; }
    public ProvenDiagnosticSuppressionOptions ProvenDiagnosticSuppressions { get; }
    public bool ReportExceptions { get; }
    public bool CheckedExceptions { get; }
    public bool EnableEffectSummaryJson { get; }
    public string PurityProfile { get; }
    public TrustedBoundaryReviewMode TrustedBoundaryReviewMode { get; }
    public SmtAnalysisOptions SmtOptions { get; }
    public SymbolicAnalysisLimits AnalysisLimits { get; }
    public ImmutableArray<InvalidAnalyzerConfigurationValue> InvalidConfigurationValues { get; }

    public static AnalyzerConfiguration FromOptions(AnalyzerOptions options)
    {
        var impureMethods = GetConfiguredMemberKeys(options, ConfigKeys.KnownImpureMethods);
        var pureMethods = GetConfiguredMemberKeys(options, ConfigKeys.KnownPureMethods);
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
        var inferredContractSuggestions = new InferredContractSuggestionOptions(
            GetBool(options, ConfigKeys.SuggestInferredContracts),
            GetSuggestionScope(options, ConfigKeys.SuggestInferredContractsScope),
            GetInferredContractKinds(options),
            GetInferredContractConfidence(options, ConfigKeys.SuggestInferredContractsMinimumConfidence,
                InferredContractConfidence.High));
        var emitExplanations = GetBool(options, ConfigKeys.EmitExplanations);
        var reportBclFallbackGuesses = GetBool(options, ConfigKeys.ReportBclFallbackGuesses);
        var runtimeHazardMode = GetRuntimeHazardMode(options, RuntimeHazardMode.Off);
        var provenDiagnosticSuppressions = new ProvenDiagnosticSuppressionOptions(
            GetBool(options, ConfigKeys.SuppressProvenDiagnostics),
            GetSuppressionDiagnosticIds(options));
        var reportExceptions = GetBool(options, ConfigKeys.ReportExceptions);
        var checkedExceptions = GetBool(options, ConfigKeys.CheckedExceptions);
        var enableEffectSummaryJson = GetBool(options, ConfigKeys.EnableEffectSummaryJson);
        var symbolicConfiguration = SymbolicProjectConfiguration.FromAnalyzerOptions(options);
        return new AnalyzerConfiguration(
            impureMethods,
            pureMethods,
            impureNamespaces,
            impureTypes,
            attributeStubNamespaces,
            suggestMissing,
            missingPuritySuggestions,
            inferredContractSuggestions,
            emitExplanations,
            reportBclFallbackGuesses,
            runtimeHazardMode,
            provenDiagnosticSuppressions,
            reportExceptions,
            checkedExceptions,
            enableEffectSummaryJson,
            GetPurityProfile(options),
            GetTrustedBoundaryReviewMode(options),
            symbolicConfiguration.SmtOptions,
            symbolicConfiguration.AnalysisLimits,
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

    public static InferredContractSuggestionOptions GetInferredContractSuggestionOptions(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        InferredContractSuggestionOptions fallback)
    {
        try
        {
            var treeOptions = options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
            return new InferredContractSuggestionOptions(
                GetBoolOrDefault(treeOptions, ConfigKeys.SuggestInferredContracts, fallback.Enabled),
                GetSuggestionScope(treeOptions, ConfigKeys.SuggestInferredContractsScope, fallback.Scope),
                GetInferredContractKinds(treeOptions, fallback.Kinds),
                GetInferredContractConfidence(
                    treeOptions,
                    ConfigKeys.SuggestInferredContractsMinimumConfidence,
                    fallback.MinimumConfidence));
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

    public static ProvenDiagnosticSuppressionOptions GetProvenDiagnosticSuppressionOptions(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        ProvenDiagnosticSuppressionOptions fallback)
    {
        try
        {
            var treeOptions = options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree);
            return new ProvenDiagnosticSuppressionOptions(
                GetBoolOrDefault(
                    treeOptions,
                    ConfigKeys.SuppressProvenDiagnostics,
                    fallback.Enabled),
                GetSuppressionDiagnosticIds(treeOptions, fallback.DiagnosticIds));
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
            foreach (var item in SplitValues(value))
                builder.Add(item);

        return builder.ToImmutable();
    }

    private static ImmutableHashSet<string> GetConfiguredMemberKeys(AnalyzerOptions options, string key)
    {
        return GetValues(options, key)
            .Where(static value => ConfiguredMemberKey.TryParse(value, out _))
            .ToImmutableHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> SplitValues(string value)
    {
        foreach (var token in value.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var item = token.Trim();
            if (item.Length > 0) yield return item;
        }
    }

    private static ImmutableHashSet<string> GetSuppressionDiagnosticIds(AnalyzerOptions options)
    {
        return TryGetGlobalOption(options, ConfigKeys.SuppressionDiagnosticIds, out var value)
            ? ParseSuppressionDiagnosticIds(value)
            : ProvenDiagnosticSuppressionOptions.AllSupportedDiagnosticIds;
    }

    private static ImmutableHashSet<string> GetSuppressionDiagnosticIds(
        AnalyzerConfigOptions options,
        ImmutableHashSet<string> fallback)
    {
        return options.TryGetValue(ConfigKeys.SuppressionDiagnosticIds, out var value)
            ? ParseSuppressionDiagnosticIds(value)
            : fallback;
    }

    private static ImmutableHashSet<string> ParseSuppressionDiagnosticIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return ImmutableHashSet<string>.Empty;

        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var token in SplitValues(value!))
        {
            var normalized = token.ToUpperInvariant();
            if (normalized == "NONE") return ImmutableHashSet<string>.Empty;

            if (ProvenDiagnosticSuppressionOptions.AllSupportedDiagnosticIds.Contains(normalized))
                builder.Add(normalized);
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

        foreach (var item in SplitValues(value)) builder.Add(item);

        return builder.ToImmutable();
    }

    private static bool GetBool(AnalyzerOptions options, string key)
    {
        return GetBoolOrDefault(options, key, fallback: false);
    }

    private static bool GetBoolOrDefaultTrue(AnalyzerOptions options, string key)
    {
        return GetBoolOrDefault(options, key, fallback: true);
    }

    private static bool GetBoolOrDefault(AnalyzerOptions options, string key, bool fallback)
    {
        return TryGetGlobalOption(options, key, out var value) && TryParseBool(value, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool GetBoolOrDefault(AnalyzerConfigOptions options, string key, bool fallback)
    {
        if (!options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) return fallback;

        return TryParseBool(value, out var parsed) ? parsed : fallback;
    }

    internal static bool TryParseBool(string value, out bool parsed)
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
        return GetSuggestionScope(options, ConfigKeys.SuggestMissingEnforcePureScope);
    }

    private static MissingPuritySuggestionScope GetSuggestionScope(AnalyzerOptions options, string key)
    {
        return TryGetGlobalOption(options, key, out var value)
            ? ParseSuggestionScope(value, MissingPuritySuggestionScope.All)
            : MissingPuritySuggestionScope.All;
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

    private static TrustedBoundaryReviewMode GetTrustedBoundaryReviewMode(AnalyzerOptions options)
    {
        if (!TryGetGlobalOption(options, ConfigKeys.TrustedBoundaryReviewMode, out var value))
            return TrustedBoundaryReviewMode.Off;

        return value.Trim().ToLowerInvariant() switch
        {
            "used" => TrustedBoundaryReviewMode.Used,
            "all" => TrustedBoundaryReviewMode.All,
            _ => TrustedBoundaryReviewMode.Off
        };
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
        return AnalyzerConfigurationOptionRegistry.TryParseRuntimeHazardMode(value, out var parsed)
            ? parsed
            : fallback;
    }

    private static MissingPuritySuggestionScope GetMissingPuritySuggestionScope(
        AnalyzerConfigOptions options,
        MissingPuritySuggestionScope fallback)
    {
        return GetSuggestionScope(options, ConfigKeys.SuggestMissingEnforcePureScope, fallback);
    }

    private static MissingPuritySuggestionScope GetSuggestionScope(
        AnalyzerConfigOptions options,
        string key,
        MissingPuritySuggestionScope fallback)
    {
        return options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? ParseSuggestionScope(value, fallback)
            : fallback;
    }

    private static MissingPuritySuggestionScope ParseSuggestionScope(
        string value,
        MissingPuritySuggestionScope fallback)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "all" => MissingPuritySuggestionScope.All,
            "public" or "public-only" => MissingPuritySuggestionScope.Public,
            "internal" or "internal-only" => MissingPuritySuggestionScope.Internal,
            "off" or "none" or "false" => MissingPuritySuggestionScope.Off,
            _ => fallback
        };
    }

    private static ImmutableHashSet<string> GetInferredContractKinds(AnalyzerOptions options)
    {
        var values = GetValues(options, ConfigKeys.SuggestInferredContractsKinds);
        return values.Count == 0
            ? InferredContractSuggestionOptions.AllKinds
            : values.Select(static value => value.Trim().ToLowerInvariant()).ToImmutableHashSet(StringComparer.Ordinal);
    }

    private static ImmutableHashSet<string> GetInferredContractKinds(
        AnalyzerConfigOptions options,
        ImmutableHashSet<string> fallback)
    {
        var values = GetValues(options, ConfigKeys.SuggestInferredContractsKinds, fallback);
        return values.Select(static value => value.Trim().ToLowerInvariant()).ToImmutableHashSet(StringComparer.Ordinal);
    }

    private static InferredContractConfidence GetInferredContractConfidence(
        AnalyzerOptions options,
        string key,
        InferredContractConfidence fallback)
    {
        return TryGetGlobalOption(options, key, out var value)
            ? ParseInferredContractConfidence(value, fallback)
            : fallback;
    }

    private static InferredContractConfidence GetInferredContractConfidence(
        AnalyzerConfigOptions options,
        string key,
        InferredContractConfidence fallback)
    {
        return options.TryGetValue(key, out var value)
            ? ParseInferredContractConfidence(value, fallback)
            : fallback;
    }

    private static InferredContractConfidence ParseInferredContractConfidence(
        string value,
        InferredContractConfidence fallback)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "medium" => InferredContractConfidence.Medium,
            "high" => InferredContractConfidence.High,
            _ => fallback
        };
    }

    private static int GetNonNegativeInt(AnalyzerOptions options, string key)
    {
        return GetNonNegativeInt(options, key, 0);
    }

    private static int GetNonNegativeInt(AnalyzerOptions options, string key, int fallback)
    {
        return AnalyzerConfigurationValueReader.GetInteger(options, key, fallback, 0);
    }

    private static int GetNonNegativeInt(AnalyzerConfigOptions options, string key, int fallback)
    {
        return AnalyzerConfigurationValueReader.GetInteger(options, key, fallback, 0);
    }

    private static bool TryGetGlobalOption(AnalyzerOptions options, string key, out string value)
    {
        return AnalyzerConfigurationValueReader.TryGetGlobalOption(options, key, out value);
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

        foreach (var option in AnalyzerConfigurationOptionRegistry.All)
        {
            switch (option.Scope)
            {
                case AnalyzerConfigurationScope.TreeOnly:
                    ValidateOption(builder, TryGetOption, option);
                    break;
                case AnalyzerConfigurationScope.GlobalAndTree:
                    if (!TryGetOption(option.Key, out var treeValue) ||
                        !TryGetMatchingGlobalOption(globalOptions, option.Key, treeValue))
                        ValidateOption(builder, TryGetOption, option);
                    break;
                case AnalyzerConfigurationScope.GlobalOnly:
                    ValidateGlobalOnlyTreeOption(builder, TryGetOption, globalOptions, option);
                    break;
            }
        }

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
            case AnalyzerConfigurationValueKind.StructuralMemberKeyList:
                ValidateStructuralMemberKeyList(builder, tryGetOption, option.Key);
                return;
            case AnalyzerConfigurationValueKind.NonNegativeInteger:
                ValidateNonNegativeInt(builder, tryGetOption, option.Key);
                return;
            case AnalyzerConfigurationValueKind.PositiveInteger:
                ValidatePositiveInt(builder, tryGetOption, option.Key);
                return;
            case AnalyzerConfigurationValueKind.PurityProfile:
            case AnalyzerConfigurationValueKind.MissingPuritySuggestionScope:
            case AnalyzerConfigurationValueKind.RuntimeHazardMode:
            case AnalyzerConfigurationValueKind.SmtMode:
            case AnalyzerConfigurationValueKind.AllowedValue:
                ValidateAllowedValue(builder, tryGetOption, option);
                return;
            case AnalyzerConfigurationValueKind.AllowedValueList:
                ValidateAllowedValueList(builder, tryGetOption, option);
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

    private static void ValidateAllowedValue(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        TryGetConfigurationOption tryGetOption,
        AnalyzerConfigurationOption option)
    {
        if (!tryGetOption(option.Key, out var value)) return;

        if (AnalyzerConfigurationOptionRegistry.IsAcceptedValue(option, value)) return;

        AddInvalidConfigurationValue(
            builder,
            option.Key,
            value,
            "expected one of: " + string.Join(", ", option.AllowedValues));
    }

    private static void ValidateAllowedValueList(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        TryGetConfigurationOption tryGetOption,
        AnalyzerConfigurationOption option)
    {
        if (!tryGetOption(option.Key, out var value)) return;

        var invalid = value
            .Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static item => item.Trim().ToLowerInvariant())
            .Where(item => !option.AllowedValues.Contains(item, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        if (invalid.Length == 0) return;

        AddInvalidConfigurationValue(
            builder,
            option.Key,
            value,
            "unknown values: " + string.Join(", ", invalid) + "; expected: " +
            string.Join(", ", option.AllowedValues));
    }

    private static void ValidateStructuralMemberKeyList(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        TryGetConfigurationOption tryGetOption,
        string key)
    {
        if (!tryGetOption(key, out var value)) return;

        var invalid = SplitValues(value)
            .Where(static item => !ConfiguredMemberKey.TryParse(item, out _))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        if (invalid.Length == 0) return;

        AddInvalidConfigurationValue(
            builder,
            key,
            value,
            "expected canonical structural method keys (spm1|...); property accessors require matching .get or .set suffixes");
    }

    private static void ValidatePositiveInt(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        TryGetConfigurationOption tryGetOption,
        string key)
    {
        ValidateIntAtLeast(
            builder,
            tryGetOption,
            key,
            minimum: 1,
            reason: "expected a positive integer");
    }

    private static void ValidateNonNegativeInt(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        TryGetConfigurationOption tryGetOption,
        string key)
    {
        ValidateIntAtLeast(
            builder,
            tryGetOption,
            key,
            minimum: 0,
            reason: "expected a non-negative integer");
    }

    private static void ValidateIntAtLeast(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        TryGetConfigurationOption tryGetOption,
        string key,
        int minimum,
        string reason)
    {
        if (tryGetOption(key, out var value) &&
            (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
             parsed < minimum))
            AddInvalidConfigurationValue(builder, key, value, reason);
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
        return AnalyzerConfigurationValueReader.TryGetNonEmpty(options, key, out value);
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

internal enum InferredContractConfidence
{
    Medium = 1,
    High = 2
}

internal sealed class InferredContractSuggestionOptions
{
    internal static readonly ImmutableHashSet<string> AllKinds =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "zero-allocations",
            "capabilities",
            "complexity",
            "exceptions",
            "ensures",
            "requires",
            "nullability");

    public InferredContractSuggestionOptions(
        bool enabled,
        MissingPuritySuggestionScope scope,
        ImmutableHashSet<string> kinds,
        InferredContractConfidence minimumConfidence)
    {
        Enabled = enabled;
        Scope = scope;
        Kinds = kinds;
        MinimumConfidence = minimumConfidence;
    }

    public bool Enabled { get; }
    public MissingPuritySuggestionScope Scope { get; }
    public ImmutableHashSet<string> Kinds { get; }
    public InferredContractConfidence MinimumConfidence { get; }
    public bool IsEnabled => Enabled && Scope != MissingPuritySuggestionScope.Off && Kinds.Count > 0;

    public bool Includes(string kind, InferredContractConfidence confidence)
    {
        return IsEnabled && Kinds.Contains(kind) && confidence >= MinimumConfidence;
    }
}

internal sealed class ProvenDiagnosticSuppressionOptions
{
    internal static readonly ImmutableHashSet<string> AllSupportedDiagnosticIds =
        ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            "CS8509",
            "CS8524",
            "CS8602",
            "CS8605",
            "CS8629",
            "CS8655",
            "CS8670",
            "CS8846",
            "CS8847",
            "S2259",
            "S3655",
            "V3064",
            "V3080",
            "V3095",
            "V3106",
            "V3151",
            "V3152",
            "V3218");

    public ProvenDiagnosticSuppressionOptions(
        bool enabled,
        ImmutableHashSet<string> diagnosticIds)
    {
        Enabled = enabled;
        DiagnosticIds = diagnosticIds;
    }

    public bool Enabled { get; }

    public ImmutableHashSet<string> DiagnosticIds { get; }

    public bool Includes(string diagnosticId)
    {
        return Enabled && DiagnosticIds.Contains(diagnosticId);
    }
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

internal enum TrustedBoundaryReviewMode
{
    Off,
    Used,
    All
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
