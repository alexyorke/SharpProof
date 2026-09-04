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
                GetInvalidGlobalConfigurationValues(options);
            if (!invalidConfigurationValues.IsEmpty)
            {
                return new(SharpProofProfile.Off, SharpProofFeatures.All, invalidConfigurationValues);
            }

            var hasProfile = TryGet(
                options,
                AnalyzerConfigurationOptionRegistry.Profile,
                out var profile);
            var hasFeatures = TryGet(
                options,
                AnalyzerConfigurationOptionRegistry.Features,
                out var features);
            return new(
                ParseProfile(hasProfile ? profile : "advisory"),
                ParseFeatures(hasFeatures ? features : "all"),
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
        GetInvalidGlobalConfigurationValues(AnalyzerConfigOptions options)
    {
        return [.. GetInvalidConfigurationValues(options, null, parseValues: true)];
    }

    private static (bool Found, string Value, bool HasConflict, string Conflict)
        ReadOptionAliases(
        AnalyzerConfigOptions options,
        AnalyzerConfigurationOption option)
    {
        var values = new List<string>();
        var found = false;
        var value = string.Empty;
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
            if (!found)
            {
                value = candidate;
                found = true;
            }
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                values.Add(candidate.Trim());
            }
        }

        var distinct = values.Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return (
            found,
            value,
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
        bool parseValues)
    {
        foreach (var option in AnalyzerConfigurationOptionRegistry.All)
        {
            var aliases = ReadOptionAliases(
                options,
                option);
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
        return Is(value, "effects") ? SharpProofFeatures.Effects :
            Is(value, "contracts") ? SharpProofFeatures.Contracts :
            SharpProofFeatures.All;
    }

    private static bool Is(string value, string expected)
    {
        return string.Equals(value.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
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
