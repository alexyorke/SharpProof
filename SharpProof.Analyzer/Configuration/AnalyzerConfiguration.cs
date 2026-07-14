using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Configuration;

internal class AnalyzerConfiguration
{
    private static readonly ImmutableHashSet<string> EmptyValues =
        ImmutableHashSet.Create<string>(StringComparer.Ordinal);

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
        var optionSource = new ConfigurationOptionSource(options);
        var impureMethods = GetConfiguredMemberKeys(optionSource, ConfigKeys.KnownImpureMethods);
        var pureMethods = GetConfiguredMemberKeys(optionSource, ConfigKeys.KnownPureMethods);
        var impureNamespaces = GetValues(optionSource, ConfigKeys.KnownImpureNamespaces, EmptyValues);
        var impureTypes = GetValues(optionSource, ConfigKeys.KnownImpureTypes, EmptyValues);
        var attributeStubNamespaces = GetValues(optionSource, ConfigKeys.AttributeStubNamespaces, EmptyValues);
        var invalidConfigurationValues = GetInvalidGlobalConfigurationValues(options);
        var suggestMissing = GetBoolOrDefault(optionSource, ConfigKeys.SuggestMissingEnforcePure, true);
        var missingPuritySuggestions = new MissingPuritySuggestionOptions(
            suggestMissing,
            GetSuggestionScope(optionSource, ConfigKeys.SuggestMissingEnforcePureScope,
                MissingPuritySuggestionScope.All),
            GetBoolOrDefault(optionSource, ConfigKeys.SuggestMissingEnforcePureExcludeGenerated, false),
            GetBoolOrDefault(optionSource, ConfigKeys.SuggestMissingEnforcePureExcludeTests, false),
            GetNonNegativeInt(optionSource, ConfigKeys.SuggestMissingEnforcePureMinComplexity, 0),
            GetValues(optionSource, ConfigKeys.SuggestMissingEnforcePureNamespaceFilters, EmptyValues));
        var inferredContractSuggestions = new InferredContractSuggestionOptions(
            GetBoolOrDefault(optionSource, ConfigKeys.SuggestInferredContracts, false),
            GetSuggestionScope(optionSource, ConfigKeys.SuggestInferredContractsScope,
                MissingPuritySuggestionScope.All),
            GetInferredContractKinds(optionSource, InferredContractSuggestionOptions.AllKinds),
            GetInferredContractConfidence(optionSource, ConfigKeys.SuggestInferredContractsMinimumConfidence,
                InferredContractConfidence.High));
        var emitExplanations = GetBoolOrDefault(optionSource, ConfigKeys.EmitExplanations, false);
        var reportBclFallbackGuesses = GetBoolOrDefault(optionSource, ConfigKeys.ReportBclFallbackGuesses, false);
        var runtimeHazardMode = GetRuntimeHazardMode(optionSource, RuntimeHazardMode.Off);
        var provenDiagnosticSuppressions = new ProvenDiagnosticSuppressionOptions(
            GetBoolOrDefault(optionSource, ConfigKeys.SuppressProvenDiagnostics, false),
            GetSuppressionDiagnosticIds(
                optionSource,
                ProvenDiagnosticSuppressionOptions.AllSupportedDiagnosticIds));
        var reportExceptions = GetBoolOrDefault(optionSource, ConfigKeys.ReportExceptions, false);
        var checkedExceptions = GetBoolOrDefault(optionSource, ConfigKeys.CheckedExceptions, false);
        var enableEffectSummaryJson = GetBoolOrDefault(optionSource, ConfigKeys.EnableEffectSummaryJson, false);
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
            GetPurityProfile(optionSource),
            GetTrustedBoundaryReviewMode(optionSource),
            symbolicConfiguration.SmtOptions,
            symbolicConfiguration.AnalysisLimits,
            invalidConfigurationValues);
    }

    public static MissingPuritySuggestionOptions GetMissingPuritySuggestionOptions(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        MissingPuritySuggestionOptions fallback)
    {
        return GetTreeOptions(options, syntaxTree, fallback, treeOptions =>
        {
            var optionSource = new ConfigurationOptionSource(treeOptions);
            var suggestMissing = GetBoolOrDefault(
                optionSource,
                ConfigKeys.SuggestMissingEnforcePure,
                fallback.Enabled);
            return new MissingPuritySuggestionOptions(
                suggestMissing,
                GetSuggestionScope(optionSource, ConfigKeys.SuggestMissingEnforcePureScope, fallback.Scope),
                GetBoolOrDefault(optionSource, ConfigKeys.SuggestMissingEnforcePureExcludeGenerated,
                    fallback.ExcludeGeneratedFiles),
                GetBoolOrDefault(optionSource, ConfigKeys.SuggestMissingEnforcePureExcludeTests,
                    fallback.ExcludeTestFiles),
                GetNonNegativeInt(optionSource, ConfigKeys.SuggestMissingEnforcePureMinComplexity,
                    fallback.MinimumComplexity),
                GetValues(optionSource, ConfigKeys.SuggestMissingEnforcePureNamespaceFilters,
                    fallback.NamespaceFilters));
        });
    }

    public static InferredContractSuggestionOptions GetInferredContractSuggestionOptions(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        InferredContractSuggestionOptions fallback)
    {
        return GetTreeOptions(
            options,
            syntaxTree,
            fallback,
            treeOptions =>
            {
                var optionSource = new ConfigurationOptionSource(treeOptions);
                return new InferredContractSuggestionOptions(
                    GetBoolOrDefault(optionSource, ConfigKeys.SuggestInferredContracts, fallback.Enabled),
                    GetSuggestionScope(optionSource, ConfigKeys.SuggestInferredContractsScope, fallback.Scope),
                    GetInferredContractKinds(optionSource, fallback.Kinds),
                    GetInferredContractConfidence(
                        optionSource,
                        ConfigKeys.SuggestInferredContractsMinimumConfidence,
                        fallback.MinimumConfidence));
            });
    }

    public static bool GetEmitExplanations(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        bool fallback)
    {
        return GetTreeOptions(
            options,
            syntaxTree,
            fallback,
            treeOptions => GetBoolOrDefault(
                new ConfigurationOptionSource(treeOptions),
                ConfigKeys.EmitExplanations,
                fallback));
    }

    public static bool GetReportBclFallbackGuesses(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        bool fallback)
    {
        return GetTreeOptions(
            options,
            syntaxTree,
            fallback,
            treeOptions => GetBoolOrDefault(
                new ConfigurationOptionSource(treeOptions),
                ConfigKeys.ReportBclFallbackGuesses,
                fallback));
    }

    public static bool GetReportExceptions(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        bool fallback)
    {
        return GetTreeOptions(
            options,
            syntaxTree,
            fallback,
            treeOptions => GetBoolOrDefault(
                new ConfigurationOptionSource(treeOptions),
                ConfigKeys.ReportExceptions,
                fallback));
    }

    public static bool GetCheckedExceptions(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        bool fallback)
    {
        return GetTreeOptions(
            options,
            syntaxTree,
            fallback,
            treeOptions => GetBoolOrDefault(
                new ConfigurationOptionSource(treeOptions),
                ConfigKeys.CheckedExceptions,
                fallback));
    }

    public static RuntimeHazardMode GetRuntimeHazardMode(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        RuntimeHazardMode fallback)
    {
        return GetTreeOptions(
            options,
            syntaxTree,
            fallback,
            treeOptions => GetRuntimeHazardMode(new ConfigurationOptionSource(treeOptions), fallback));
    }

    public static ProvenDiagnosticSuppressionOptions GetProvenDiagnosticSuppressionOptions(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        ProvenDiagnosticSuppressionOptions fallback)
    {
        return GetTreeOptions(
            options,
            syntaxTree,
            fallback,
            treeOptions =>
            {
                var optionSource = new ConfigurationOptionSource(treeOptions);
                return new ProvenDiagnosticSuppressionOptions(
                    GetBoolOrDefault(
                        optionSource,
                        ConfigKeys.SuppressProvenDiagnostics,
                        fallback.Enabled),
                    GetSuppressionDiagnosticIds(optionSource, fallback.DiagnosticIds));
            });
    }

    private static T GetTreeOptions<T>(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        T fallback,
        Func<AnalyzerConfigOptions, T> readOptions)
    {
        try
        {
            return readOptions(options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree));
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

    private static ImmutableHashSet<string> GetConfiguredMemberKeys(ConfigurationOptionSource options, string key)
    {
        return GetValues(options, key, EmptyValues)
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

    private static ImmutableHashSet<string> GetSuppressionDiagnosticIds(
        ConfigurationOptionSource options,
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
        ConfigurationOptionSource options,
        string key,
        ImmutableHashSet<string> fallback)
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        if (!options.TryGetValue(key, out var value)) return fallback;

        if (string.IsNullOrWhiteSpace(value)) return builder.ToImmutable();

        foreach (var item in SplitValues(value)) builder.Add(item);

        return builder.ToImmutable();
    }

    private static bool GetBoolOrDefault(ConfigurationOptionSource options, string key, bool fallback)
    {
        if (!options.TryGetValue(key, out var value)) return fallback;

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

    private static string GetPurityProfile(ConfigurationOptionSource options)
    {
        if (options.TryGetValue(ConfigKeys.PurityProfile, out var value))
        {
            var normalized = value.Trim().ToLowerInvariant();
            if (normalized == "strict" || normalized == "balanced" || normalized == "pragmatic") return normalized;
        }

        return "balanced";
    }

    private static TrustedBoundaryReviewMode GetTrustedBoundaryReviewMode(ConfigurationOptionSource options)
    {
        if (!options.TryGetValue(ConfigKeys.TrustedBoundaryReviewMode, out var value))
            return TrustedBoundaryReviewMode.Off;

        return value.Trim().ToLowerInvariant() switch
        {
            "used" => TrustedBoundaryReviewMode.Used,
            "all" => TrustedBoundaryReviewMode.All,
            _ => TrustedBoundaryReviewMode.Off
        };
    }

    private static RuntimeHazardMode GetRuntimeHazardMode(
        ConfigurationOptionSource options,
        RuntimeHazardMode fallback)
    {
        return options.TryGetValue(ConfigKeys.RuntimeHazardMode, out var value)
            ? ParseRuntimeHazardMode(value, fallback)
            : fallback;
    }

    private static RuntimeHazardMode ParseRuntimeHazardMode(string value, RuntimeHazardMode fallback)
    {
        return AnalyzerConfigurationOptionRegistry.TryParseRuntimeHazardMode(value, out var parsed)
            ? parsed
            : fallback;
    }

    private static MissingPuritySuggestionScope GetSuggestionScope(
        ConfigurationOptionSource options,
        string key,
        MissingPuritySuggestionScope fallback)
    {
        return options.TryGetValue(key, out var value)
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

    private static ImmutableHashSet<string> GetInferredContractKinds(
        ConfigurationOptionSource options,
        ImmutableHashSet<string> fallback)
    {
        return NormalizeInferredContractKinds(
            GetValues(options, ConfigKeys.SuggestInferredContractsKinds, fallback));
    }

    private static ImmutableHashSet<string> NormalizeInferredContractKinds(IEnumerable<string> values)
    {
        return values
            .Select(static value => value.Trim().ToLowerInvariant())
            .ToImmutableHashSet(StringComparer.Ordinal);
    }

    private static InferredContractConfidence GetInferredContractConfidence(
        ConfigurationOptionSource options,
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

    private static int GetNonNegativeInt(ConfigurationOptionSource options, string key, int fallback)
    {
        return options.TryGetValue(key, out var value) &&
               AnalyzerConfigurationValueReader.TryParseInteger(value, 0, out var parsed)
            ? parsed
            : fallback;
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

        var invalid = SplitValues(value)
            .Select(static item => item.ToLowerInvariant())
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

    private readonly struct ConfigurationOptionSource
    {
        private readonly AnalyzerOptions? _globalOptions;
        private readonly AnalyzerConfigOptions? _treeOptions;

        internal ConfigurationOptionSource(AnalyzerOptions options)
        {
            _globalOptions = options;
            _treeOptions = null;
        }

        internal ConfigurationOptionSource(AnalyzerConfigOptions options)
        {
            _globalOptions = null;
            _treeOptions = options;
        }

        internal bool TryGetValue(string key, out string value)
        {
            if (_globalOptions != null)
                return TryGetGlobalOption(_globalOptions, key, out value);
            if (_treeOptions != null)
            {
                if (_treeOptions.TryGetValue(key, out var treeValue))
                {
                    value = treeValue ?? string.Empty;
                    return true;
                }
            }

            value = string.Empty;
            return false;
        }
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
