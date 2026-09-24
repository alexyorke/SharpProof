namespace SharpProof.Analyzer.Configuration;

internal sealed class AnalyzerConfiguration
{
    internal static AnalyzerConfiguration AdvisoryAll
    {
        get;
    } = new(
        SharpProofProfile.Advisory, SharpProofFeatures.All, []);

    private AnalyzerConfiguration(
        SharpProofProfile profile, SharpProofFeatures features,
        ImmutableArray<InvalidAnalyzerConfigurationValue> invalidConfigurationValues)
    {
        Profile = profile;
        Features = features;
        InvalidConfigurationValues = invalidConfigurationValues;
    }

    internal SharpProofProfile Profile
    {
        get;
    }
    internal SharpProofFeatures Features
    {
        get;
    }
    internal bool EffectsEnabled => Features is SharpProofFeatures.Effects or SharpProofFeatures.All;
    internal bool ContractsEnabled => Features is SharpProofFeatures.Contracts or SharpProofFeatures.All;
    internal ImmutableArray<InvalidAnalyzerConfigurationValue> InvalidConfigurationValues
    {
        get;
    }

    public static AnalyzerConfiguration FromOptions(AnalyzerOptions options)
    {
        return FromOptions(options.AnalyzerConfigOptionsProvider);
    }

    internal static AnalyzerConfiguration FromOptions(
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        try
        {
            var options = optionsProvider.GlobalOptions;
            var invalidConfigurationValues =
                GetInvalidGlobalConfigurationValues(
                    options,
                    out var profileAliases,
                    out var featuresAliases);
            if (!invalidConfigurationValues.IsEmpty)
            {
                return new(SharpProofProfile.Off, SharpProofFeatures.All, invalidConfigurationValues);
            }

            return new(
                ParseProfile(profileAliases.Found ? profileAliases.Value : "advisory"),
                ParseFeatures(featuresAliases.Found ? featuresAliases.Value : "all"),
                invalidConfigurationValues);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(
                SharpProofProfile.Off,
                SharpProofFeatures.All,
                [ProviderFailure(exception)]);
        }
    }

    private static ImmutableArray<InvalidAnalyzerConfigurationValue>
        GetInvalidGlobalConfigurationValues(
            AnalyzerConfigOptions options,
            out (bool Found, string Value, bool HasConflict, string Conflict)
                profileAliases,
            out (bool Found, string Value, bool HasConflict, string Conflict)
                featuresAliases)
    {
        profileAliases = ReadOptionAliases(
            options,
            AnalyzerConfigurationOptionRegistry.Profile);
        featuresAliases = ReadOptionAliases(
            options,
            AnalyzerConfigurationOptionRegistry.Features);
        var invalid = GetInvalidConfigurationValues(
            options,
            null,
            parseValues: true,
            profileAliases: profileAliases,
            featuresAliases: featuresAliases)
            .ToList();
        if (!invalid.Any(static value =>
                value.Key == AnalyzerConfigurationOptionRegistry.Profile.Key) &&
            options.TryGetValue(
                AnalyzerConfigurationOptionRegistry.Profile.Key,
                out var analyzerProfile) &&
            options.TryGetValue(
                "build_property." +
                    AnalyzerConfigurationOptionRegistry.Profile.BuildPropertyName,
                out var msBuildProfile) &&
            AnalyzerConfigurationOptionRegistry.IsAcceptedValue(
                AnalyzerConfigurationOptionRegistry.Profile,
                analyzerProfile) &&
            AnalyzerConfigurationOptionRegistry.IsAcceptedValue(
                AnalyzerConfigurationOptionRegistry.Profile,
                msBuildProfile) &&
            !Is(analyzerProfile, msBuildProfile))
        {
            invalid.Add(new InvalidAnalyzerConfigurationValue(
                AnalyzerConfigurationOptionRegistry.Profile.Key,
                analyzerProfile.Trim() + " / " + msBuildProfile.Trim(),
                "profile must match the MSBuild SharpProofProfile property, which controls package and verifier behavior"));
        }

        return [.. invalid];
    }

    private static (bool Found, string Value, bool HasConflict, string Conflict)
        ReadOptionAliases(
        AnalyzerConfigOptions options,
        AnalyzerConfigurationOption option)
    {
        var candidates = new List<(string Key, string Value)>();
        foreach (var key in new[] {
                     option.Key,
                     "build_property." + option.Key,
                     "build_property." + option.BuildPropertyName
                 })
        {
            if (!options.TryGetValue(key, out var candidate))
            {
                continue;
            }
            candidates.Add((key, candidate));
        }

        var hasExplicitAnalyzerValue = candidates.Any(
            candidate => string.Equals(
                candidate.Key,
                option.Key,
                StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(candidate.Value));
        var explicitAnalyzerValue = hasExplicitAnalyzerValue
            ? candidates.First(
                candidate => string.Equals(
                    candidate.Key,
                    option.Key,
                    StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(candidate.Value)).Value
            : string.Empty;
        var values = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Value))
            .Where(candidate => !hasExplicitAnalyzerValue ||
                !IsPackageDefaultProperty(
                    options,
                    option,
                    candidate.Key,
                    candidate.Value))
            .Select(static candidate => candidate.Value.Trim())
            .ToArray();
        var effective = hasExplicitAnalyzerValue
            ? explicitAnalyzerValue
            : candidates.Count == 0
                ? string.Empty
                : candidates[0].Value;
        var distinct = values.Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return (
            candidates.Count != 0,
            effective,
            distinct.Length > 1,
            string.Join(" / ", distinct));
    }

    internal static InvalidAnalyzerConfigurationValue ProviderFailure(
        Exception exception)
    {
        return new(
            "AnalyzerConfigOptionsProvider",
            exception.GetType().Name,
            "configuration provider failed; analysis was disabled");
    }

    internal static ImmutableArray<InvalidAnalyzerConfigurationValue> GetInvalidTreeConfigurationValues(
        AnalyzerConfigOptions options,
        AnalyzerConfigOptions? globalOptions = null)
    {
        return [.. GetInvalidConfigurationValues(options, globalOptions, parseValues: false)];
    }

    private static IEnumerable<InvalidAnalyzerConfigurationValue>
        GetInvalidConfigurationValues(
            AnalyzerConfigOptions options,
            AnalyzerConfigOptions? globalOptions,
            bool parseValues,
            (bool Found, string Value, bool HasConflict, string Conflict)?
                profileAliases = null,
            (bool Found, string Value, bool HasConflict, string Conflict)?
                featuresAliases = null)
    {
        foreach (var option in AnalyzerConfigurationOptionRegistry.All)
        {
            var aliases = option == AnalyzerConfigurationOptionRegistry.Profile &&
                    profileAliases is { } cachedProfile
                ? cachedProfile
                : option == AnalyzerConfigurationOptionRegistry.Features &&
                    featuresAliases is { } cachedFeatures
                    ? cachedFeatures
                    : ReadOptionAliases(options, option);
            if (aliases.HasConflict)
            {
                yield return new InvalidAnalyzerConfigurationValue(
                    option.Key,
                    aliases.Conflict,
                    "configuration aliases disagree; use one effective value");
                continue;
            }
            if (!aliases.Found)
            {
                continue;
            }
            var value = aliases.Value;

            if (parseValues)
            {
                if (AnalyzerConfigurationOptionRegistry.IsAcceptedValue(option, value))
                {
                    continue;
                }

                yield return new(
                    option.Key,
                    value.Trim(),
                    "expected one of: " + string.Join(", ", option.AllowedValues));
                continue;
            }

            if (globalOptions != null &&
                TryGet(globalOptions, option, out var global) &&
                Is(global, value))
            {
                continue;
            }

            yield return new(
                option.Key,
                value.Trim(),
                "option is compilation-global; set it in a global AnalyzerConfig or MSBuild property");
        }
        if (TryGetRetiredMode(options, out var retiredMode))
        {
            yield return new InvalidAnalyzerConfigurationValue(
                "sharpproof_mode",
                retiredMode.Trim(),
                "option was removed; use sharpproof_profile and sharpproof_features");
        }
    }

    private static bool TryGetRetiredMode(
        AnalyzerConfigOptions options,
        out string value)
    {
        foreach (var key in new[] {
                     "sharpproof_mode",
                     "build_property.sharpproof_mode",
                     "build_property.SharpProofMode"
                 })
        {
            if (options.TryGetValue(key, out var candidate) &&
                !string.IsNullOrWhiteSpace(candidate))
            {
                value = candidate;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool IsPackageDefaultProperty(
        AnalyzerConfigOptions options,
        AnalyzerConfigurationOption option,
        string key,
        string value)
    {
        return string.Equals(
                key,
                "build_property." + option.BuildPropertyName,
                StringComparison.OrdinalIgnoreCase) &&
            options.TryGetValue(
                "build_property._" + option.BuildPropertyName + "WasDefaulted",
                out var wasDefaulted) &&
            Is(wasDefaulted, "true") &&
            IsPackageDefaultValue(option, value);
    }

    private static bool IsPackageDefaultValue(
        AnalyzerConfigurationOption option,
        string value)
    {
        return option == AnalyzerConfigurationOptionRegistry.Profile
            ? Is(value, "advisory")
            : option == AnalyzerConfigurationOptionRegistry.Features &&
                Is(value, "all");
    }

    private static bool TryGet(
        AnalyzerConfigOptions options,
        AnalyzerConfigurationOption option,
        out string value)
    {
        var keys = new[] {
            option.Key,
            "build_property." + option.Key,
            "build_property." + option.BuildPropertyName
        };
        foreach (var key in keys)
        {
            if (options.TryGetValue(key, out var found))
            {
                value = found;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static SharpProofProfile ParseProfile(string value)
    {
        return Is(value, SharpProofConfigurationCatalog.ProfileOff)
            ? SharpProofProfile.Off :
        Is(value, "strict") ? SharpProofProfile.Strict :
        SharpProofProfile.Advisory;
    }

    private static SharpProofFeatures ParseFeatures(string value)
    {
        return Enum.TryParse(
            value.Trim(),
            true,
            out SharpProofFeatures features)
            ? features
            : SharpProofFeatures.All;
    }

    private static bool Is(string value, string expected)
    {
        return string.Equals(
            value.Trim(),
            expected.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }
}

internal readonly struct InvalidAnalyzerConfigurationValue(
    string key,
    string value,
    string reason)
{
    internal string Key { get; } = key;
    internal string Value { get; } = value;
    internal string Reason { get; } = reason;
}

internal enum SharpProofProfile
{
    Advisory, Strict, Off
}
internal enum SharpProofFeatures
{
    Effects, Contracts, All
}
