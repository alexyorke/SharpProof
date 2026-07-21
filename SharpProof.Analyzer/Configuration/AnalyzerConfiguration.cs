namespace SharpProof.Analyzer.Configuration;

internal sealed record AnalyzerConfiguration(
    ImmutableHashSet<string> AttributeStubNamespaces,
    InferredContractSuggestionOptions InferredContractSuggestions,
    bool EmitExplanations,
    RuntimeHazardMode RuntimeHazardMode,
    ProvenDiagnosticSuppressionOptions ProvenDiagnosticSuppressions,
    bool ReportExceptions,
    bool CheckedExceptions,
    bool ReportNullableInconclusive,
    TrustedBoundaryReviewMode TrustedBoundaryReviewMode,
    SmtAnalysisOptions SmtOptions,
    SharpProofAnalysisBudget AnalysisLimits,
    ImmutableArray<InvalidAnalyzerConfigurationValue> InvalidConfigurationValues) {
    private static readonly ImmutableHashSet<string> EmptyValues =
        ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    public static AnalyzerConfiguration FromOptions(AnalyzerOptions options) {
        var optionSource = new ConfigurationOptionSource(options);
        var attributeStubNamespaces = GetValues(optionSource, ConfigKeys.AttributeStubNamespaces, EmptyValues);
        var invalidConfigurationValues = GetInvalidGlobalConfigurationValues(options);
        var inferredContractSuggestions = new InferredContractSuggestionOptions(
            GetBoolOrDefault(optionSource, ConfigKeys.SuggestInferredContracts, false),
            GetSuggestionScope(optionSource, ConfigKeys.SuggestInferredContractsScope,
                MissingPuritySuggestionScope.All),
            GetInferredContractKinds(optionSource, InferredContractSuggestionOptions.AllKinds),
            GetInferredContractConfidence(optionSource, ConfigKeys.SuggestInferredContractsMinimumConfidence,
                InferredContractConfidence.High));
        var emitExplanations = GetBoolOrDefault(optionSource, ConfigKeys.EmitExplanations, false);
        var runtimeHazardMode = GetRuntimeHazardMode(optionSource, RuntimeHazardMode.Off);
        var provenDiagnosticSuppressions = new ProvenDiagnosticSuppressionOptions(
            GetBoolOrDefault(optionSource, ConfigKeys.SuppressProvenDiagnostics, false),
            GetSuppressionDiagnosticIds(
                optionSource,
                SharpProofDiagnosticSuppressor.SupportedDiagnosticIds));
        var reportExceptions = GetBoolOrDefault(optionSource, ConfigKeys.ReportExceptions, false);
        var checkedExceptions = GetBoolOrDefault(optionSource, ConfigKeys.CheckedExceptions, false);
        var reportNullableInconclusive =
            GetBoolOrDefault(optionSource, ConfigKeys.ReportNullableInconclusive, false);
        var symbolicConfiguration = SymbolicProjectConfiguration.FromAnalyzerOptions(options);
        return new AnalyzerConfiguration(
            attributeStubNamespaces,
            inferredContractSuggestions,
            emitExplanations,
            runtimeHazardMode,
            provenDiagnosticSuppressions,
            reportExceptions,
            checkedExceptions,
            reportNullableInconclusive,
            GetTrustedBoundaryReviewMode(optionSource),
            symbolicConfiguration.SmtOptions,
            symbolicConfiguration.AnalysisLimits,
            invalidConfigurationValues);
    }

    internal static AnalyzerTreeConfiguration GetTreeConfiguration(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        AnalyzerConfiguration fallback) {
        var global = new AnalyzerTreeConfiguration(
            fallback.InferredContractSuggestions,
            fallback.EmitExplanations,
            fallback.RuntimeHazardMode,
            fallback.ProvenDiagnosticSuppressions,
            fallback.ReportExceptions,
            fallback.CheckedExceptions,
            fallback.ReportNullableInconclusive);
        return GetTreeOptions(options, syntaxTree, global, treeOptions => {
            var optionSource = new ConfigurationOptionSource(treeOptions);
            var inferredContracts = new InferredContractSuggestionOptions(
                    GetBoolOrDefault(
                        optionSource,
                        ConfigKeys.SuggestInferredContracts,
                        fallback.InferredContractSuggestions.Enabled),
                    GetSuggestionScope(
                        optionSource,
                        ConfigKeys.SuggestInferredContractsScope,
                        fallback.InferredContractSuggestions.Scope),
                    GetInferredContractKinds(optionSource, fallback.InferredContractSuggestions.Kinds),
                    GetInferredContractConfidence(
                        optionSource,
                        ConfigKeys.SuggestInferredContractsMinimumConfidence,
                        fallback.InferredContractSuggestions.MinimumConfidence));
            var suppressions = new ProvenDiagnosticSuppressionOptions(
                    GetBoolOrDefault(
                        optionSource,
                        ConfigKeys.SuppressProvenDiagnostics,
                        fallback.ProvenDiagnosticSuppressions.Enabled),
                    GetSuppressionDiagnosticIds(
                        optionSource,
                        fallback.ProvenDiagnosticSuppressions.DiagnosticIds));
            return new AnalyzerTreeConfiguration(
                inferredContracts,
                GetBoolOrDefault(optionSource, ConfigKeys.EmitExplanations, fallback.EmitExplanations),
                GetRuntimeHazardMode(optionSource, fallback.RuntimeHazardMode),
                suppressions,
                GetBoolOrDefault(optionSource, ConfigKeys.ReportExceptions, fallback.ReportExceptions),
                GetBoolOrDefault(optionSource, ConfigKeys.CheckedExceptions, fallback.CheckedExceptions),
                GetBoolOrDefault(
                    optionSource,
                    ConfigKeys.ReportNullableInconclusive,
                    fallback.ReportNullableInconclusive));
        });
    }

    private static T GetTreeOptions<T>(
        AnalyzerOptions options,
        SyntaxTree syntaxTree,
        T fallback,
        Func<AnalyzerConfigOptions, T> readOptions) {
        try {
            return readOptions(options.AnalyzerConfigOptionsProvider.GetOptions(syntaxTree));
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            return fallback;
        }
    }

    private static IEnumerable<string> SplitValues(string value) {
        foreach (var token in value.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
            var item = token.Trim();
            if (item.Length > 0) yield return item;
        }
    }

    private static ImmutableHashSet<string> GetSuppressionDiagnosticIds(
        ConfigurationOptionSource options,
        ImmutableHashSet<string> fallback) => options.TryGetValue(ConfigKeys.SuppressionDiagnosticIds, out var value)
            ? ParseSuppressionDiagnosticIds(value)
            : fallback;

    private static ImmutableHashSet<string> ParseSuppressionDiagnosticIds(string? value) {
        if (string.IsNullOrWhiteSpace(value)) return ImmutableHashSet<string>.Empty;

        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var token in SplitValues(value!)) {
            var normalized = token.ToUpperInvariant();
            if (normalized == "NONE") return ImmutableHashSet<string>.Empty;

            if (SharpProofDiagnosticSuppressor.SupportedDiagnosticIds.Contains(normalized))
                builder.Add(normalized);
        }

        return builder.ToImmutable();
    }

    private static ImmutableHashSet<string> GetValues(
        ConfigurationOptionSource options,
        string key,
        ImmutableHashSet<string> fallback) {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        if (!options.TryGetValue(key, out var value)) return fallback;

        if (string.IsNullOrWhiteSpace(value)) return builder.ToImmutable();

        foreach (var item in SplitValues(value)) builder.Add(item);

        return builder.ToImmutable();
    }

    private static bool GetBoolOrDefault(ConfigurationOptionSource options, string key, bool fallback) {
        if (!options.TryGetValue(key, out var value)) return fallback;

        return TryParseBool(value, out var parsed) ? parsed : fallback;
    }

    internal static bool TryParseBool(string value, out bool parsed) {
        switch (value.Trim().ToLowerInvariant()) {
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

    private static TrustedBoundaryReviewMode GetTrustedBoundaryReviewMode(ConfigurationOptionSource options) {
        if (!options.TryGetValue(ConfigKeys.TrustedBoundaryReviewMode, out var value))
            return TrustedBoundaryReviewMode.Off;

        return ParseNormalizedValue(value, TrustedBoundaryReviewMode.Off,
            ("used", TrustedBoundaryReviewMode.Used), ("all", TrustedBoundaryReviewMode.All));
    }

    private static RuntimeHazardMode GetRuntimeHazardMode(
        ConfigurationOptionSource options,
        RuntimeHazardMode fallback) => options.TryGetValue(ConfigKeys.RuntimeHazardMode, out var value)
            ? AnalyzerConfigurationOptionRegistry.TryParseRuntimeHazardMode(value, out var parsed)
                ? parsed
                : fallback
            : fallback;

    private static MissingPuritySuggestionScope GetSuggestionScope(
        ConfigurationOptionSource options,
        string key,
        MissingPuritySuggestionScope fallback) => options.TryGetValue(key, out var value)
            ? ParseNormalizedValue(value, fallback, ("all", MissingPuritySuggestionScope.All),
                ("public", MissingPuritySuggestionScope.Public), ("public-only", MissingPuritySuggestionScope.Public),
                ("internal", MissingPuritySuggestionScope.Internal),
                ("internal-only", MissingPuritySuggestionScope.Internal), ("off", MissingPuritySuggestionScope.Off),
                ("none", MissingPuritySuggestionScope.Off), ("false", MissingPuritySuggestionScope.Off))
            : fallback;

    private static ImmutableHashSet<string> GetInferredContractKinds(
        ConfigurationOptionSource options,
        ImmutableHashSet<string> fallback) => NormalizeInferredContractKinds(
            GetValues(options, ConfigKeys.SuggestInferredContractsKinds, fallback));

    private static ImmutableHashSet<string> NormalizeInferredContractKinds(IEnumerable<string> values) => values
            .Select(static value => value.Trim().ToLowerInvariant())
            .ToImmutableHashSet(StringComparer.Ordinal);

    private static InferredContractConfidence GetInferredContractConfidence(
        ConfigurationOptionSource options,
        string key,
        InferredContractConfidence fallback) => options.TryGetValue(key, out var value)
            ? ParseNormalizedValue(value, fallback, ("medium", InferredContractConfidence.Medium),
                ("high", InferredContractConfidence.High))
            : fallback;

    private static T ParseNormalizedValue<T>(string value, T fallback, params (string Name, T Value)[] values) {
        var normalized = value.Trim().ToLowerInvariant();
        foreach (var candidate in values)
            if (candidate.Name == normalized)
                return candidate.Value;

        return fallback;
    }

    private static ImmutableArray<InvalidAnalyzerConfigurationValue> GetInvalidGlobalConfigurationValues(
        AnalyzerOptions options) {
        var builder = ImmutableArray.CreateBuilder<InvalidAnalyzerConfigurationValue>();

        foreach (var option in AnalyzerConfigurationOptionRegistry.GlobalOptions)
            ValidateOption(builder, TryGetOption, option);

        return builder.ToImmutable();

        bool TryGetOption(string key, out string value) => AnalyzerConfigurationValueReader.TryGetGlobalOption(options, key, out value);
    }

    internal static ImmutableArray<InvalidAnalyzerConfigurationValue> GetInvalidTreeConfigurationValues(
        AnalyzerConfigOptions options,
        AnalyzerConfigOptions? globalOptions = null) {
        var builder = ImmutableArray.CreateBuilder<InvalidAnalyzerConfigurationValue>();

        foreach (var option in AnalyzerConfigurationOptionRegistry.All) {
            switch (option.Scope) {
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

        bool TryGetOption(string key, out string value) => TryGetAnalyzerConfigOption(options, key, out value);
    }

    private static void ValidateOption(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        TryGetConfigurationOption tryGetOption,
        AnalyzerConfigurationOption option) {
        if (!tryGetOption(option.Key, out var value)) return;

        var reason = option.ValueKind switch {
            AnalyzerConfigurationValueKind.Bool when !TryParseBool(value, out _) =>
                "expected a boolean value",
            AnalyzerConfigurationValueKind.NonNegativeInteger =>
                GetIntegerError(value, 0, "expected a non-negative integer"),
            AnalyzerConfigurationValueKind.PositiveInteger =>
                GetIntegerError(value, 1, "expected a positive integer"),
            AnalyzerConfigurationValueKind.MissingPuritySuggestionScope or
                AnalyzerConfigurationValueKind.RuntimeHazardMode or
                AnalyzerConfigurationValueKind.SmtMode or
                AnalyzerConfigurationValueKind.AllowedValue
                when !AnalyzerConfigurationOptionRegistry.IsAcceptedValue(option, value) =>
                "expected one of: " + string.Join(", ", option.AllowedValues),
            AnalyzerConfigurationValueKind.AllowedValueList => GetAllowedValueListError(option, value),
            _ => null
        };
        if (reason != null) AddInvalidConfigurationValue(builder, option.Key, value, reason);
    }

    private static void ValidateGlobalOnlyTreeOption(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        TryGetConfigurationOption tryGetOption,
        AnalyzerConfigOptions? globalOptions,
        AnalyzerConfigurationOption option) {
        if (tryGetOption(option.Key, out var value)) {
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
        string treeValue) {
        if (globalOptions == null) return false;

        if (globalOptions.TryGetValue(key, out var globalValue) &&
            string.Equals(globalValue, treeValue, StringComparison.Ordinal))
            return true;

        return globalOptions.TryGetValue("build_property." + key, out globalValue) &&
               string.Equals(globalValue, treeValue, StringComparison.Ordinal);
    }

    private static string? GetAllowedValueListError(AnalyzerConfigurationOption option, string value) {
        var invalid = SplitValues(value)
            .Select(static item => item.ToLowerInvariant())
            .Where(item => !option.AllowedValues.Contains(item, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        return invalid.Length == 0
            ? null
            : "unknown values: " + string.Join(", ", invalid) + "; expected: " +
              string.Join(", ", option.AllowedValues);
    }

    private static string? GetIntegerError(string value, int minimum, string reason) => int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
               parsed >= minimum
            ? null
            : reason;

    private static void AddInvalidConfigurationValue(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        string key,
        string value,
        string reason) => builder.Add(new InvalidAnalyzerConfigurationValue(key, value.Trim(), reason));

    private static bool TryGetAnalyzerConfigOption(AnalyzerConfigOptions options, string key, out string value) =>
        AnalyzerConfigurationValueReader.TryGetNonEmpty(options, key, out value);

    private readonly struct ConfigurationOptionSource {
        private readonly AnalyzerOptions? _globalOptions;
        private readonly AnalyzerConfigOptions? _treeOptions;

        internal ConfigurationOptionSource(AnalyzerOptions options) {
            _globalOptions = options;
            _treeOptions = null;
        }

        internal ConfigurationOptionSource(AnalyzerConfigOptions options) {
            _globalOptions = null;
            _treeOptions = options;
        }

        internal bool TryGetValue(string key, out string value) {
            if (_globalOptions != null)
                return AnalyzerConfigurationValueReader.TryGetGlobalOption(_globalOptions, key, out value);
            if (_treeOptions != null) {
                if (_treeOptions.TryGetValue(key, out var treeValue)) {
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

internal sealed record AnalyzerTreeConfiguration(
    InferredContractSuggestionOptions InferredContractSuggestions,
    bool EmitExplanations,
    RuntimeHazardMode RuntimeHazardMode,
    ProvenDiagnosticSuppressionOptions ProvenDiagnosticSuppressions,
    bool ReportExceptions,
    bool CheckedExceptions,
    bool ReportNullableInconclusive);

internal enum MissingPuritySuggestionScope {
    All,
    Public,
    Internal,
    Off
}

internal enum InferredContractConfidence {
    Medium = 1,
    High = 2
}

internal sealed record InferredContractSuggestionOptions(
    bool Enabled,
    MissingPuritySuggestionScope Scope,
    ImmutableHashSet<string> Kinds,
    InferredContractConfidence MinimumConfidence) {
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

    public bool IsEnabled => Enabled && Scope != MissingPuritySuggestionScope.Off && Kinds.Count > 0;

    public bool Includes(string kind, InferredContractConfidence confidence) =>
        IsEnabled && Kinds.Contains(kind) && confidence >= MinimumConfidence;
}

internal sealed record ProvenDiagnosticSuppressionOptions(
    bool Enabled,
    ImmutableHashSet<string> DiagnosticIds) {
    public bool Includes(string diagnosticId) =>
        Enabled && DiagnosticIds.Contains(diagnosticId);
}

[Flags]
internal enum RuntimeHazardMode {
    Off = 0,
    Sites = 1,
    Summaries = 2,
    All = Sites | Summaries,
    Unknowns = 4,
    SitesAndUnknowns = Sites | Unknowns,
    AllAndUnknowns = All | Unknowns
}

internal enum TrustedBoundaryReviewMode {
    Off,
    Used,
    All
}
