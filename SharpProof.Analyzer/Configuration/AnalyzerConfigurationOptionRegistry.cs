namespace SharpProof.Analyzer.Configuration;
internal static class AnalyzerConfigurationOptionRegistry {
    public static ImmutableArray<AnalyzerConfigurationOption> All { get; } = [
        Choice(
            "sharpproof_mode",
            ["off", "effects", "contracts", "all-experimental"],
            "SharpProofMode")
    ];

    internal static bool IsAcceptedValue(AnalyzerConfigurationOption option, string? value) =>
        !string.IsNullOrWhiteSpace(value) && option.AllowedValues.Contains(value!.Trim().ToLowerInvariant(), StringComparer.Ordinal);

    private static AnalyzerConfigurationOption Choice(
        string key,
        ImmutableArray<string> allowedValues,
        string? buildPropertyName = null) =>
        new(key, AnalyzerConfigurationValueKind.Choice, allowedValues, buildPropertyName);
}

internal sealed class AnalyzerConfigurationOption {
    internal AnalyzerConfigurationOption(
        string key,
        AnalyzerConfigurationValueKind valueKind,
        ImmutableArray<string> allowedValues = default,
        string? buildPropertyName = null) {
        Key = key;
        ValueKind = valueKind;
        AllowedValues = allowedValues;
        BuildPropertyName = buildPropertyName;
    }

    internal string Key { get; }
    internal AnalyzerConfigurationValueKind ValueKind { get; }
    internal ImmutableArray<string> AllowedValues { get; }
    internal string? BuildPropertyName { get; }
}

internal enum AnalyzerConfigurationValueKind { Bool, Choice, NonNegativeInteger, PositiveInteger }
