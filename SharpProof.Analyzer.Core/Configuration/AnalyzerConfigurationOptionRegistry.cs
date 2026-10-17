namespace SharpProof.Analyzer.Configuration;

internal static class AnalyzerConfigurationOptionRegistry
{
    internal static AnalyzerConfigurationOption Profile { get; } =
        new(
            SharpProofConfigurationCatalog.ProfileKey,
            ["advisory", "strict", SharpProofConfigurationCatalog.ProfileOff],
            SharpProofConfigurationCatalog.ProfileBuildPropertyName);

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
        if (value is null)
        {
            throw new System.ArgumentNullException(nameof(value));
        }
        if (option is null)
        {
            throw new System.ArgumentNullException(nameof(option));
        }
        return !string.IsNullOrWhiteSpace(value) &&
        option.AllowedValues.Contains(value!.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed class AnalyzerConfigurationOption(
    string key,
    ImmutableArray<string> allowedValues,
    string buildPropertyName)
{
    internal string Key { get; } = (key ?? throw new System.ArgumentNullException(nameof(key)));
    internal ImmutableArray<string> AllowedValues { get; } = (allowedValues.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(allowedValues)) : allowedValues);
    internal string BuildPropertyName { get; } = (buildPropertyName ?? throw new System.ArgumentNullException(nameof(buildPropertyName)));
}
