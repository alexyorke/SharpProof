namespace SharpProof.Analyzer.Configuration;
internal static class AnalyzerConfigurationOptionRegistry {
    public static ImmutableArray<AnalyzerConfigurationOption> All { get; } = [
        new("sharpproof_profile", ["advisory", "strict", "off"], "SharpProofProfile"),
        new("sharpproof_features", ["effects", "contracts", "all"], "SharpProofFeatures"),
        new("sharpproof_mode", ["off", "effects", "contracts", "all-experimental"], "SharpProofMode")
    ];

    internal static bool IsAcceptedValue(AnalyzerConfigurationOption option, string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        option.AllowedValues.Contains(value!.Trim(), StringComparer.OrdinalIgnoreCase);
}

internal sealed class AnalyzerConfigurationOption(
    string key,
    ImmutableArray<string> allowedValues,
    string buildPropertyName) {
    internal string Key { get; } = key;
    internal ImmutableArray<string> AllowedValues { get; } = allowedValues;
    internal string BuildPropertyName { get; } = buildPropertyName;
}
