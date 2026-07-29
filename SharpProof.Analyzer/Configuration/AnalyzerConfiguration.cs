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
        var invalidConfigurationValues = GetInvalidGlobalConfigurationValues(options);
        if (!invalidConfigurationValues.IsEmpty)
        {
            return new(SharpProofProfile.Off, SharpProofFeatures.All, invalidConfigurationValues);
        }

        var optionsByKind = AnalyzerConfigurationOptionRegistry.All;
        var hasProfile = TryGet(options, optionsByKind[0], out var profile);
        var hasFeatures = TryGet(options, optionsByKind[1], out var features);
        var hasLegacy = TryGet(options, optionsByKind[2], out var legacy);
        return new(
            ParseProfile(hasProfile ? profile : hasLegacy && Is(legacy, "off") ? "off" : "advisory"),
            ParseFeatures(hasFeatures ? features : hasLegacy ? legacy : "all"),
            invalidConfigurationValues);
    }

    private static ImmutableArray<InvalidAnalyzerConfigurationValue>
        GetInvalidGlobalConfigurationValues(AnalyzerOptions options)
    {
        var builder = ImmutableArray.CreateBuilder<InvalidAnalyzerConfigurationValue>();
        foreach (var option in AnalyzerConfigurationOptionRegistry.All)
        {
            if (!TryGet(options, option, out var value) ||
                AnalyzerConfigurationOptionRegistry.IsAcceptedValue(option, value))
            {
                continue;
            }

            builder.Add(new(option.Key, value.Trim(),
                "expected one of: " + string.Join(", ", option.AllowedValues)));
        }
        if (builder.Count != 0)
        {
            return builder.ToImmutable();
        }

        var legacyOption = AnalyzerConfigurationOptionRegistry.All[2];
        var hasProfile = TryGet(options, AnalyzerConfigurationOptionRegistry.All[0], out var profile);
        var hasFeatures = TryGet(options, AnalyzerConfigurationOptionRegistry.All[1], out var features);
        if (TryGet(options, legacyOption, out var legacy) &&
            (hasProfile || hasFeatures) &&
            !IsLegacyEquivalent(
                legacy,
                hasProfile ? profile : Is(legacy, "off") ? "off" : "advisory",
                hasFeatures ? features : LegacyFeatures(legacy)))
        {
            builder.Add(new(legacyOption.Key, legacy.Trim(),
                "deprecated option conflicts with sharpproof_profile or sharpproof_features"));
        }

        return builder.ToImmutable();
    }

    private static bool TryGet(
        AnalyzerOptions options,
        AnalyzerConfigurationOption option,
        out string value)
    {
        try
        {
            return TryGet(
                options.AnalyzerConfigOptionsProvider.GlobalOptions, option, out value);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            value = string.Empty;
            return false;
        }
    }

    private static bool IsLegacyEquivalent(string legacy, string profile, string features)
    {
        return Is(legacy, "off")
            ? Is(profile, "off")
            : !Is(profile, "off") &&
              (Is(legacy, "effects") && Is(features, "effects") ||
               Is(legacy, "contracts") && Is(features, "contracts") ||
               Is(legacy, "all-experimental") && Is(features, "all"));
    }

    private static string LegacyFeatures(string legacy)
    {
        return Is(legacy, "effects") ? "effects" :
        Is(legacy, "contracts") ? "contracts" :
        "all";
    }

    internal static ImmutableArray<InvalidAnalyzerConfigurationValue> GetInvalidTreeConfigurationValues(
        AnalyzerConfigOptions options,
        AnalyzerConfigOptions? globalOptions = null)
    {
        var builder = ImmutableArray.CreateBuilder<InvalidAnalyzerConfigurationValue>();
        foreach (var option in AnalyzerConfigurationOptionRegistry.All)
        {
            if (!TryGet(options, option, out var value))
            {
                continue;
            }

            if (globalOptions != null &&
                TryGet(globalOptions, option, out var global) &&
                Is(global, value))
            {
                continue;
            }

            builder.Add(new InvalidAnalyzerConfigurationValue(option.Key, value.Trim(),
                "option is compilation-global; set it in a global AnalyzerConfig or MSBuild property"));
        }
        return builder.ToImmutable();
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
            if (options.TryGetValue(key, out var found) &&
                !string.IsNullOrWhiteSpace(found))
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
        return Is(value, "off") ? SharpProofProfile.Off :
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
