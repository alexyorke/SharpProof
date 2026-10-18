namespace SharpProof.Compiler.Semantics;

internal sealed class CompilerConfiguration
{
    internal static CompilerConfiguration AdvisoryAll
    {
        get;
    } = new(
        SharpProofProfile.Advisory, SharpProofFeatures.All, []);

    private CompilerConfiguration(
        SharpProofProfile profile, SharpProofFeatures features,
        ImmutableArray<InvalidCompilerConfigurationValue> invalidConfigurationValues)
    {

        RepositoryParameterGuard.ThrowIfInvalid(invalidConfigurationValues.IsDefault, nameof(invalidConfigurationValues));
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
    internal ImmutableArray<InvalidCompilerConfigurationValue> InvalidConfigurationValues
    {
        get;
    }

    public static CompilerConfiguration FromOptions(AnalyzerOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return FromOptions(options.AnalyzerConfigOptionsProvider);
    }

    internal static CompilerConfiguration FromOptions(
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        optionsProvider = RepositoryParameterGuard.NotNull(optionsProvider, nameof(optionsProvider));
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
                ParseProfile(profileAliases.Found ? profileAliases.Value : SharpProofConfigurationCatalog.DefaultProfile),
                ParseFeatures(featuresAliases.Found ? featuresAliases.Value : SharpProofConfigurationCatalog.DefaultFeatures),
                invalidConfigurationValues);
        }
        catch (AggregateException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(
                SharpProofProfile.Off,
                SharpProofFeatures.All,
                [ProviderFailure(exception)]);
        }
    }

    private static ImmutableArray<InvalidCompilerConfigurationValue>
        GetInvalidGlobalConfigurationValues(
            AnalyzerConfigOptions options,
            out (bool Found, string Value, bool HasConflict, string Conflict)
                profileAliases,
            out (bool Found, string Value, bool HasConflict, string Conflict)
                featuresAliases)
    {
        options = RepositoryParameterGuard.NotNull(options, nameof(options));
        profileAliases = ReadOptionAliases(
            options,
            CompilerConfigurationOptionRegistry.Profile);
        featuresAliases = ReadOptionAliases(
            options,
            CompilerConfigurationOptionRegistry.Features);
        var invalid = GetInvalidConfigurationValues(
            options,
            null,
            parseValues: true,
            profileAliases: profileAliases,
            featuresAliases: featuresAliases)
            .ToImmutableArray().ToBuilder();
        if (!invalid.Any(static value =>
                value.Key == CompilerConfigurationOptionRegistry.Profile.Key) &&
            options.TryGetValue(
                CompilerConfigurationOptionRegistry.Profile.Key,
                out var analyzerProfile) &&
            options.TryGetValue(
                "build_property." +
                    CompilerConfigurationOptionRegistry.Profile.BuildPropertyName,
                out var msBuildProfile) &&
            CompilerConfigurationOptionRegistry.IsAcceptedValue(
                CompilerConfigurationOptionRegistry.Profile,
                analyzerProfile) &&
            CompilerConfigurationOptionRegistry.IsAcceptedValue(
                CompilerConfigurationOptionRegistry.Profile,
                msBuildProfile) &&
            !Is(analyzerProfile, msBuildProfile))
        {
            invalid.Add(new InvalidCompilerConfigurationValue(
                CompilerConfigurationOptionRegistry.Profile.Key,
                analyzerProfile.Trim() + " / " + msBuildProfile.Trim(),
                "profile must match the MSBuild SharpProofProfile property, which controls package and verifier behavior"));
        }

        return [.. invalid];
    }

    private static (bool Found, string Value, bool HasConflict, string Conflict)
        ReadOptionAliases(
        AnalyzerConfigOptions options,
        CompilerConfigurationOption option)
    {
        option = RepositoryParameterGuard.NotNull(option, nameof(option));
        options = RepositoryParameterGuard.NotNull(options, nameof(options));
        var candidates = ImmutableList<(string Key, string Value)>.Empty;
        foreach (var key in ImmutableArray.Create(
                     option.Key,
                     "build_property." + option.Key,
                     "build_property." + option.BuildPropertyName
                 ))
        {
            if (!options.TryGetValue(key, out var candidate))
            {
                continue;
            }
            candidates = candidates.Add((key, candidate));
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
            .ToImmutableArray();
        var effective = hasExplicitAnalyzerValue
            ? explicitAnalyzerValue
            : candidates.Count == 0
                ? string.Empty
                : candidates[0].Value;
        var distinct = values.Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
        return (
            candidates.Count != 0,
            effective,
            distinct.Length > 1,
            string.Join(" / ", distinct));
    }

    internal static InvalidCompilerConfigurationValue ProviderFailure(
        Exception exception)
    {
        exception = RepositoryParameterGuard.NotNull(exception, nameof(exception));
        return new(
            "AnalyzerConfigOptionsProvider",
            exception.GetType().Name,
            "configuration provider failed; analysis was disabled");
    }

    internal static ImmutableArray<InvalidCompilerConfigurationValue> GetInvalidTreeConfigurationValues(
        AnalyzerConfigOptions options,
        AnalyzerConfigOptions? globalOptions = null)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return [.. GetInvalidConfigurationValues(options, globalOptions, parseValues: false)];
    }

    private static IEnumerable<InvalidCompilerConfigurationValue>
        GetInvalidConfigurationValues(
            AnalyzerConfigOptions options,
            AnalyzerConfigOptions? globalOptions,
            bool parseValues,
            (bool Found, string Value, bool HasConflict, string Conflict)?
                profileAliases = null,
            (bool Found, string Value, bool HasConflict, string Conflict)?
                featuresAliases = null)
    {
        options = RepositoryParameterGuard.NotNull(options, nameof(options));
        foreach (var option in CompilerConfigurationOptionRegistry.All)
        {
            var aliases = option == CompilerConfigurationOptionRegistry.Profile &&
                    profileAliases is { } cachedProfile
                ? cachedProfile
                : option == CompilerConfigurationOptionRegistry.Features &&
                    featuresAliases is { } cachedFeatures
                    ? cachedFeatures
                    : ReadOptionAliases(options, option);
            if (aliases.HasConflict)
            {
                yield return new InvalidCompilerConfigurationValue(
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
                if (CompilerConfigurationOptionRegistry.IsAcceptedValue(option, value))
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
            yield return new InvalidCompilerConfigurationValue(
                "sharpproof_mode",
                retiredMode.Trim(),
                "option was removed; use sharpproof_profile and sharpproof_features");
        }
    }

    private static bool TryGetRetiredMode(
        AnalyzerConfigOptions options,
        out string value)
    {
        foreach (var key in ImmutableArray.Create(
                     "sharpproof_mode",
                     "build_property.sharpproof_mode",
                     "build_property.SharpProofMode"
                 ))
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
        CompilerConfigurationOption option,
        string key,
        string value)
    {
        key = RepositoryParameterGuard.NotNull(key, nameof(key));
        option = RepositoryParameterGuard.NotNull(option, nameof(option));
        options = RepositoryParameterGuard.NotNull(options, nameof(options));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
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
        CompilerConfigurationOption option,
        string value)
    {
        option = RepositoryParameterGuard.NotNull(option, nameof(option));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return option == CompilerConfigurationOptionRegistry.Profile
            ? Is(value, SharpProofConfigurationCatalog.DefaultProfile)
            : option == CompilerConfigurationOptionRegistry.Features &&
                Is(value, SharpProofConfigurationCatalog.DefaultFeatures);
    }

    private static bool TryGet(
        AnalyzerConfigOptions options,
        CompilerConfigurationOption option,
        out string value)
    {
        option = RepositoryParameterGuard.NotNull(option, nameof(option));
        var keys = ImmutableArray.Create(
            option.Key,
            "build_property." + option.Key,
            "build_property." + option.BuildPropertyName);
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
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return Is(value, SharpProofConfigurationCatalog.ProfileOff)
            ? SharpProofProfile.Off :
        Is(value, SharpProofConfigurationCatalog.ProfileStrict) ? SharpProofProfile.Strict :
        SharpProofProfile.Advisory;
    }

    private static SharpProofFeatures ParseFeatures(string value)
    {
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return Enum.TryParse(
            value.Trim(),
            true,
            out SharpProofFeatures features)
            ? features
            : SharpProofFeatures.All;
    }

    private static bool Is(string value, string expected)
    {
        ArgumentNullGuard.NotNull(value, nameof(value));
        ArgumentNullGuard.NotNull(expected, nameof(expected));

        return string.Equals(
            value.Trim(),
            expected.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }
}

internal readonly struct InvalidCompilerConfigurationValue(
    string key,
    string value,
    string reason)
{
    internal string Key { get; } = (key ?? throw new System.ArgumentNullException(nameof(key)));
    internal string Value { get; } = (value ?? throw new System.ArgumentNullException(nameof(value)));
    internal string Reason { get; } = (reason ?? throw new System.ArgumentNullException(nameof(reason)));
}

internal enum SharpProofProfile
{
    Advisory, Strict, Off
}
internal enum SharpProofFeatures
{
    Effects, Contracts, All
}
