namespace SharpProof.Analyzer.Configuration;
internal static class AnalyzerConfigurationOptionRegistry
{
    internal static AnalyzerConfigurationOption Profile { get; } =
        new("sharpproof_profile", ["advisory", "strict", "off"], "SharpProofProfile");

    internal static AnalyzerConfigurationOption Features { get; } =
        new("sharpproof_features", ["effects", "contracts", "all"], "SharpProofFeatures");

    public static ImmutableArray<AnalyzerConfigurationOption> All
    {
        get;
    } = [
        Profile,
        Features
    ];

    internal static bool IsAcceptedValue(AnalyzerConfigurationOption option, string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
        option.AllowedValues.Contains(value!.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed class AnalyzerConfigurationOption(
    string key,
    ImmutableArray<string> allowedValues,
    string buildPropertyName)
{
    internal string Key { get; } = key;
    internal ImmutableArray<string> AllowedValues { get; } = allowedValues;
    internal string BuildPropertyName { get; } = buildPropertyName;
}
