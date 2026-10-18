namespace SharpProof.Compiler.Semantics;

internal static class CompilerConfigurationOptionRegistry
{
    internal static CompilerConfigurationOption Profile { get; } =
        new(
            SharpProofConfigurationCatalog.ProfileKey,
            [SharpProofConfigurationCatalog.ProfileAdvisory, SharpProofConfigurationCatalog.ProfileStrict, SharpProofConfigurationCatalog.ProfileOff],
            SharpProofConfigurationCatalog.ProfileBuildPropertyName);

    internal static CompilerConfigurationOption Features { get; } =
        new(SharpProofConfigurationCatalog.FeaturesKey, [SharpProofConfigurationCatalog.FeaturesEffects, SharpProofConfigurationCatalog.FeaturesContracts, SharpProofConfigurationCatalog.FeaturesAll], SharpProofConfigurationCatalog.FeaturesBuildPropertyName);

    public static ImmutableArray<CompilerConfigurationOption> All
    {
        get;
    } = [
        Profile,
        Features
    ];

    internal static bool IsAcceptedValue(CompilerConfigurationOption option, string? value)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        option = RepositoryParameterGuard.NotNull(option, nameof(option));
        return !string.IsNullOrWhiteSpace(value) &&
        option.AllowedValues.Contains(value!.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed class CompilerConfigurationOption(
    string key,
    ImmutableArray<string> allowedValues,
    string buildPropertyName)
{
    internal string Key { get; } = (key ?? throw new System.ArgumentNullException(nameof(key)));
    internal ImmutableArray<string> AllowedValues { get; } = (allowedValues.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(allowedValues)) : allowedValues);
    internal string BuildPropertyName { get; } = (buildPropertyName ?? throw new System.ArgumentNullException(nameof(buildPropertyName)));
}
