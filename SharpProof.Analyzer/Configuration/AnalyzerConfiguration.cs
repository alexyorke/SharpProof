namespace SharpProof.Analyzer.Configuration;

internal sealed class AnalyzerConfiguration {
    private AnalyzerConfiguration(
        SharpProofProfile profile,
        SharpProofFeatures features,
        ImmutableArray<InvalidAnalyzerConfigurationValue> invalidConfigurationValues) {
        Profile = profile;
        Features = features;
        InvalidConfigurationValues = invalidConfigurationValues;
    }

    internal SharpProofProfile Profile { get; }
    internal SharpProofFeatures Features { get; }
    internal bool EffectsEnabled => Features is SharpProofFeatures.Effects or SharpProofFeatures.All;
    internal bool ContractsEnabled => Features is SharpProofFeatures.Contracts or SharpProofFeatures.All;
    internal ImmutableArray<InvalidAnalyzerConfigurationValue> InvalidConfigurationValues { get; }

    public static AnalyzerConfiguration FromOptions(AnalyzerOptions options) {
        var invalidConfigurationValues = GetInvalidGlobalConfigurationValues(options);
        if (!invalidConfigurationValues.IsEmpty)
            return new(
                SharpProofProfile.Off,
                SharpProofFeatures.All,
                invalidConfigurationValues);
        var hasProfile = TryGetGlobalOption(options, AnalyzerConfigurationOptionRegistry.All[0], out var profile);
        var hasFeatures = TryGetGlobalOption(options, AnalyzerConfigurationOptionRegistry.All[1], out var features);
        var hasLegacy = TryGetGlobalOption(options, AnalyzerConfigurationOptionRegistry.All[2], out var legacy);
        var useLegacy = hasLegacy && !hasProfile && !hasFeatures;
        return new(
            ParseProfile(hasProfile ? profile : useLegacy && Is(legacy, "off") ? "off" : "advisory"),
            ParseFeatures(hasFeatures ? features : useLegacy ? legacy : "all"),
            invalidConfigurationValues);
    }

    private static ImmutableArray<InvalidAnalyzerConfigurationValue> GetInvalidGlobalConfigurationValues(AnalyzerOptions options) {
        var builder = ImmutableArray.CreateBuilder<InvalidAnalyzerConfigurationValue>();
        foreach (var option in AnalyzerConfigurationOptionRegistry.All) {
            if (!TryGetGlobalOption(options, option, out var value) ||
                AnalyzerConfigurationOptionRegistry.IsAcceptedValue(option, value))
                continue;
            builder.Add(new(
                option.Key,
                value.Trim(),
                "expected one of: " + string.Join(", ", option.AllowedValues)));
        }
        return builder.ToImmutable();
    }
    internal static ImmutableArray<InvalidAnalyzerConfigurationValue> GetInvalidTreeConfigurationValues(
        AnalyzerConfigOptions options,
        AnalyzerConfigOptions? globalOptions = null) {
        var builder = ImmutableArray.CreateBuilder<InvalidAnalyzerConfigurationValue>();
        foreach (var option in AnalyzerConfigurationOptionRegistry.All) {
            if (!AnalyzerConfigurationValueReader.TryGetNonEmpty(
                    options,
                    option.Key,
                    out var value))
                continue;
            if (TryGetMatchingGlobalOption(globalOptions, option, value)) continue;
            builder.Add(new InvalidAnalyzerConfigurationValue(
                option.Key,
                value.Trim(),
                "option is compilation-global; set it in a global AnalyzerConfig or MSBuild property"));
        }
        return builder.ToImmutable();
    }
    private static bool TryGetMatchingGlobalOption(
        AnalyzerConfigOptions? globalOptions,
        AnalyzerConfigurationOption option,
        string treeValue) =>
        globalOptions != null &&
        TryGetGlobalOption(globalOptions, option, out var value) &&
        string.Equals(
                value.Trim(),
                treeValue.Trim(),
                StringComparison.OrdinalIgnoreCase);
    private static SharpProofProfile ParseProfile(string value) =>
        Is(value, "off") ? SharpProofProfile.Off :
        Is(value, "strict") ? SharpProofProfile.Strict :
        SharpProofProfile.Advisory;
    private static SharpProofFeatures ParseFeatures(string value) =>
        Is(value, "effects") ? SharpProofFeatures.Effects :
        Is(value, "contracts") ? SharpProofFeatures.Contracts :
        SharpProofFeatures.All;
    private static bool Is(string value, string expected) =>
        string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    private static bool TryGetGlobalOption(
        AnalyzerOptions options,
        AnalyzerConfigurationOption option,
        out string value) {
        if (AnalyzerConfigurationValueReader.TryGetGlobalOption(options, option.Key, out value))
            return true;
        try {
            return TryGetBuildProperty(
                options.AnalyzerConfigOptionsProvider.GlobalOptions,
                option,
                out value);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) {
            value = string.Empty;
            return false;
        }
    }
    private static bool TryGetGlobalOption(
        AnalyzerConfigOptions options,
        AnalyzerConfigurationOption option,
        out string value) =>
        AnalyzerConfigurationValueReader.TryGetNonEmpty(options, option.Key, out value) ||
        TryGetBuildProperty(options, option, out value) ||
        AnalyzerConfigurationValueReader.TryGetNonEmpty(
            options,
            "build_property." + option.Key,
            out value);
    private static bool TryGetBuildProperty(
        AnalyzerConfigOptions options,
        AnalyzerConfigurationOption option,
        out string value) =>
        AnalyzerConfigurationValueReader.TryGetNonEmpty(
            options,
            "build_property." + option.BuildPropertyName,
            out value);
}

internal readonly struct InvalidAnalyzerConfigurationValue(
    string key,
    string value,
    string reason) {
    internal string Key { get; } = key;
    internal string Value { get; } = value;
    internal string Reason { get; } = reason;
}

internal enum SharpProofProfile { Advisory, Strict, Off }
internal enum SharpProofFeatures { Effects, Contracts, All }
