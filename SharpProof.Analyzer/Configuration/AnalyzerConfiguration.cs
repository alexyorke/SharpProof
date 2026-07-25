namespace SharpProof.Analyzer.Configuration;
internal sealed record AnalyzerConfiguration(
    SmtAnalysisOptions SmtOptions,
    SharpProofAnalysisBudget AnalysisLimits,
    ImmutableArray<InvalidAnalyzerConfigurationValue> InvalidConfigurationValues) {
    public static AnalyzerConfiguration FromOptions(AnalyzerOptions options) {
        var symbolic = SymbolicProjectConfiguration.FromAnalyzerOptions(options);
        return new AnalyzerConfiguration(symbolic.SmtOptions, symbolic.AnalysisLimits, GetInvalidGlobalConfigurationValues(options));
    }
    private static ImmutableArray<InvalidAnalyzerConfigurationValue> GetInvalidGlobalConfigurationValues(AnalyzerOptions options) {
        var builder = ImmutableArray.CreateBuilder<InvalidAnalyzerConfigurationValue>();
        foreach (var option in AnalyzerConfigurationOptionRegistry.All)
            ValidateOption(
                builder,
                (string key, out string value) =>
                    AnalyzerConfigurationValueReader.TryGetGlobalOption(options, key, out value),
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
        if (!tryGetOption(option.Key, out var value)) return;
        var reason = option.ValueKind switch {
            AnalyzerConfigurationValueKind.Bool when !TryParseBool(value) =>
                "expected a boolean value",
            AnalyzerConfigurationValueKind.NonNegativeInteger =>
                GetIntegerError(value, 0, "expected a non-negative integer"),
            AnalyzerConfigurationValueKind.PositiveInteger =>
                GetIntegerError(value, 1, "expected a positive integer"),
            AnalyzerConfigurationValueKind.SmtMode when !AnalyzerConfigurationOptionRegistry.IsAcceptedValue(option, value) =>
                "expected one of: " + string.Join(", ", option.AllowedValues),
            _ => null
        };
        if (reason != null)
            builder.Add(new InvalidAnalyzerConfigurationValue(option.Key, value.Trim(), reason));
    }
    private static bool TryParseBool(string value) =>
        value.Trim().ToLowerInvariant() is
            "1" or "true" or "yes" or "on" or
            "0" or "false" or "no" or "off";
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
        if (AnalyzerConfigurationValueReader.TryGetNonEmpty(
                globalOptions,
                option.Key,
                out var value))
            return AreEquivalent(option, value, treeValue);
        return AnalyzerConfigurationValueReader.TryGetNonEmpty(
                   globalOptions,
                   "build_property." + option.Key,
                   out value) &&
               AreEquivalent(option, value, treeValue);
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
            AnalyzerConfigurationValueKind.SmtMode =>
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
    private static bool TryGetAnalyzerConfigOption(AnalyzerConfigOptions options, string key, out string value)
        => AnalyzerConfigurationValueReader.TryGetNonEmpty(options, key, out value);
    private delegate bool TryGetConfigurationOption(string key, out string value);
}
internal readonly record struct InvalidAnalyzerConfigurationValue(string Key, string Value, string Reason);
