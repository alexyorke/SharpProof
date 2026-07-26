namespace SharpProof.Analyzer.Configuration;

internal sealed class AnalyzerConfiguration {
    private AnalyzerConfiguration(
        SharpProofMode mode,
        ImmutableArray<InvalidAnalyzerConfigurationValue> invalidConfigurationValues) {
        Mode = mode;
        InvalidConfigurationValues = invalidConfigurationValues;
    }

    internal SharpProofMode Mode { get; }
    internal ImmutableArray<InvalidAnalyzerConfigurationValue> InvalidConfigurationValues { get; }

    public static AnalyzerConfiguration FromOptions(AnalyzerOptions options) {
        var invalidConfigurationValues = GetInvalidGlobalConfigurationValues(options);
        var mode = invalidConfigurationValues.Any(static value =>
            string.Equals(value.Key, "sharpproof_mode", StringComparison.Ordinal))
            ? SharpProofMode.Off
            : GetMode(options);
        return new AnalyzerConfiguration(mode, invalidConfigurationValues);
    }

    private static ImmutableArray<InvalidAnalyzerConfigurationValue> GetInvalidGlobalConfigurationValues(AnalyzerOptions options) {
        var builder = ImmutableArray.CreateBuilder<InvalidAnalyzerConfigurationValue>();
        foreach (var option in AnalyzerConfigurationOptionRegistry.All)
            ValidateOption(
                builder,
                (AnalyzerConfigurationOption candidate, out string value) =>
                    TryGetGlobalOption(options, candidate, out value),
                option);
        return builder.ToImmutable();
    }
    internal static ImmutableArray<InvalidAnalyzerConfigurationValue> GetInvalidTreeConfigurationValues(
        AnalyzerConfigOptions options,
        AnalyzerConfigOptions? globalOptions = null) {
        var builder = ImmutableArray.CreateBuilder<InvalidAnalyzerConfigurationValue>();
        foreach (var option in AnalyzerConfigurationOptionRegistry.All) {
            if (!TryGetAnalyzerConfigOption(options, option.Key, out var value)) continue;
            if (TryGetMatchingGlobalOption(globalOptions, option, value)) continue;
            builder.Add(new InvalidAnalyzerConfigurationValue(
                option.Key,
                value.Trim(),
                "option is compilation-global; set it in a global AnalyzerConfig or MSBuild property"));
        }
        return builder.ToImmutable();
    }
    private static void ValidateOption(
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder,
        TryGetConfigurationOption tryGetOption,
        AnalyzerConfigurationOption option) {
        if (!tryGetOption(option, out var value)) return;
        var reason = option.ValueKind switch {
            AnalyzerConfigurationValueKind.Bool when !TryParseBool(value) =>
                "expected a boolean value",
            AnalyzerConfigurationValueKind.NonNegativeInteger =>
                GetIntegerError(value, 0, "expected a non-negative integer"),
            AnalyzerConfigurationValueKind.PositiveInteger =>
                GetIntegerError(value, 1, "expected a positive integer"),
            AnalyzerConfigurationValueKind.Choice when !AnalyzerConfigurationOptionRegistry.IsAcceptedValue(option, value) =>
                "expected one of: " + string.Join(", ", option.AllowedValues),
            _ => null
        };
        if (reason != null)
            builder.Add(new InvalidAnalyzerConfigurationValue(option.Key, value.Trim(), reason));
    }
    private static bool TryParseBool(string value) =>
        value.Trim().ToUpperInvariant() is
            "1" or "TRUE" or "YES" or "ON" or
            "0" or "FALSE" or "NO" or "OFF";
    private static string? GetIntegerError(string value, int minimum, string reason) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
        parsed >= minimum
            ? null
            : reason;
    private static bool TryGetMatchingGlobalOption(
        AnalyzerConfigOptions? globalOptions,
        AnalyzerConfigurationOption option,
        string treeValue) {
        if (globalOptions == null) return false;
        if (TryGetGlobalOption(globalOptions, option, out var value))
            return AreEquivalent(option, value, treeValue);
        return false;
    }
    private static bool AreEquivalent(
        AnalyzerConfigurationOption option,
        string left,
        string right) => option.ValueKind switch {
            AnalyzerConfigurationValueKind.Bool =>
                TryParseBool(left, out var leftBool) &&
                TryParseBool(right, out var rightBool) &&
                leftBool == rightBool,
            AnalyzerConfigurationValueKind.NonNegativeInteger or
                AnalyzerConfigurationValueKind.PositiveInteger =>
                int.TryParse(
                    left.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var leftInteger) &&
                int.TryParse(
                    right.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var rightInteger) &&
                leftInteger == rightInteger,
            AnalyzerConfigurationValueKind.Choice =>
                string.Equals(
                    left.Trim(),
                    right.Trim(),
                    StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(
                left.Trim(),
                right.Trim(),
                StringComparison.Ordinal)
        };
    private static bool TryParseBool(string value, out bool parsed) {
        switch (value.Trim().ToUpperInvariant()) {
            case "1":
            case "TRUE":
            case "YES":
            case "ON":
                parsed = true;
                return true;
            case "0":
            case "FALSE":
            case "NO":
            case "OFF":
                parsed = false;
                return true;
            default:
                parsed = false;
                return false;
        }
    }
    private static bool TryGetAnalyzerConfigOption(AnalyzerConfigOptions options, string key, out string value)
        => AnalyzerConfigurationValueReader.TryGetNonEmpty(options, key, out value);
    private static SharpProofMode GetMode(AnalyzerOptions options) {
        var option = AnalyzerConfigurationOptionRegistry.All[0];
        if (!TryGetGlobalOption(options, option, out var value)) return SharpProofMode.Off;
        return value.Trim().ToUpperInvariant() switch {
            "EFFECTS" => SharpProofMode.Effects,
            "CONTRACTS" => SharpProofMode.Contracts,
            "ALL-EXPERIMENTAL" => SharpProofMode.AllExperimental,
            _ => SharpProofMode.Off
        };
    }
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
        out string value) {
        if (option.BuildPropertyName != null &&
            AnalyzerConfigurationValueReader.TryGetNonEmpty(
                options,
                "build_property." + option.BuildPropertyName,
                out value))
            return true;
        value = string.Empty;
        return false;
    }
    private delegate bool TryGetConfigurationOption(
        AnalyzerConfigurationOption option,
        out string value);
}

internal readonly struct InvalidAnalyzerConfigurationValue {
    internal InvalidAnalyzerConfigurationValue(string key, string value, string reason) {
        Key = key;
        Value = value;
        Reason = reason;
    }

    internal string Key { get; }
    internal string Value { get; }
    internal string Reason { get; }
}

internal enum SharpProofMode {
    Off,
    Effects,
    Contracts,
    AllExperimental
}
