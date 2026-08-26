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

            var optionsByKind = AnalyzerConfigurationOptionRegistry.All;
            var hasProfile = TryGet(options, optionsByKind[0], out var profile);
            var hasFeatures = TryGet(options, optionsByKind[1], out var features);
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
        var builder = ImmutableArray.CreateBuilder<InvalidAnalyzerConfigurationValue>();
        foreach (var option in AnalyzerConfigurationOptionRegistry.All)
        {
            if (TryGetConflictingAliases(options, option, out var conflict))
            {
                builder.Add(new(
                    option.Key,
                    conflict,
                    "configuration aliases disagree; use one effective value"));
                continue;
            }
            if (!TryGet(options, option, out var value) ||
                AnalyzerConfigurationOptionRegistry.IsAcceptedValue(option, value))
            {
                continue;
            }

            builder.Add(new(option.Key, value.Trim(),
                "expected one of: " + string.Join(", ", option.AllowedValues)));
        }
        AddRetiredMode(options, builder);

        return builder.ToImmutable();
    }

    private static bool TryGetConflictingAliases(
        AnalyzerConfigOptions options,
        AnalyzerConfigurationOption option,
        out string conflict)
    {
        var values = new List<string>();
        foreach (var key in new[] {
                     option.Key,
                     "build_property." + option.Key,
                     "build_property." + option.BuildPropertyName
                 })
        {
            if (options.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }

        var distinct = values.Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        conflict = string.Join(" / ", distinct);
        return distinct.Length > 1;
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
        var builder = ImmutableArray.CreateBuilder<InvalidAnalyzerConfigurationValue>();
        foreach (var option in AnalyzerConfigurationOptionRegistry.All)
        {
            if (TryGetConflictingAliases(options, option, out var conflict))
            {
                builder.Add(new InvalidAnalyzerConfigurationValue(
                    option.Key,
                    conflict,
                    "configuration aliases disagree; use one effective value"));
                continue;
            }
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
        if (TryGetRetiredMode(options, out var retiredMode) &&
            (globalOptions == null ||
             !TryGetRetiredMode(globalOptions, out var globalRetiredMode) ||
             !Is(globalRetiredMode, retiredMode)))
        {
            builder.Add(new InvalidAnalyzerConfigurationValue(
                "sharpproof_mode",
                retiredMode.Trim(),
                "option was removed; use sharpproof_profile and sharpproof_features"));
        }
        return builder.ToImmutable();
    }

    private static void AddRetiredMode(
        AnalyzerConfigOptions options,
        ImmutableArray<InvalidAnalyzerConfigurationValue>.Builder builder)
    {
        if (TryGetRetiredMode(options, out var retiredMode))
        {
            builder.Add(new(
                "sharpproof_mode",
                retiredMode.Trim(),
                "option was removed; use sharpproof_profile and sharpproof_features"));
        }
    }

    private static bool TryGetRetiredMode(
        AnalyzerConfigOptions options,
        out string value)
    {
        foreach (var key in new[] {
                     "sharpproof_mode",
                     "build_property.SharpProofMode"
                 })
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
