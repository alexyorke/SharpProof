namespace SharpProof.Analyzer.Configuration;

internal static class AnalyzerConfigurationOptionRegistry
{
    private static ImmutableDictionary<string, AnalyzerConfigurationOption>? _optionsByKey;

    // Computed lazily so it never reads All during static initialization: static initializers run
    // in textual order, and member-ordering rules (fields before properties) can place this ahead
    // of All, which would otherwise read a default ImmutableArray and throw in the type initializer.
    private static ImmutableDictionary<string, AnalyzerConfigurationOption> OptionsByKey =>
        _optionsByKey ??= All.ToImmutableDictionary(static option => option.Key, StringComparer.Ordinal);

    private const string ResourceName = "SharpProof.Analyzer.Configuration.Options.json";

    public static ImmutableArray<AnalyzerConfigurationOption> All { get; } = Load();

    public static ImmutableArray<AnalyzerConfigurationOption> GlobalOptions =>
        All.Where(static option => option.IsGlobal).ToImmutableArray();

    public static AnalyzerConfigurationOption Get(string key) => OptionsByKey[key];

    private static ImmutableArray<AnalyzerConfigurationOption> Load()
    {
        using var stream = typeof(AnalyzerConfigurationOptionRegistry).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded configuration catalog '{ResourceName}'.");
        var definitions = JsonSerializer.Deserialize<ConfigurationOptionDefinition[]>(stream)
            ?? throw new InvalidOperationException("The embedded configuration catalog is empty.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        return definitions.Select(definition =>
        {
            if (!keys.Add(definition.Key))
                throw new InvalidOperationException($"Duplicate configuration key '{definition.Key}'.");
            if (!Enum.TryParse<AnalyzerConfigurationScope>(definition.Scope, out var scope) ||
                !Enum.TryParse<AnalyzerConfigurationValueKind>(definition.ValueKind, out var valueKind) ||
                !Enum.TryParse<PurityPolicyImpact>(definition.PurityPolicyImpact, out var policyImpact))
                throw new InvalidOperationException($"Configuration option '{definition.Key}' has invalid enum metadata.");
            return new AnalyzerConfigurationOption(
                definition.Key,
                scope,
                valueKind,
                new AnalyzerConfigurationDefault(
                    definition.ConstantValue,
                    definition.BoundedValue,
                    definition.DeepValue,
                    definition.Unit),
                definition.Description,
                definition.AllowedValues.ToImmutableArray(),
                policyImpact,
                definition.AcceptedAliases.ToImmutableArray(),
                definition.ValueDescription,
                definition.RelatedDiagnostics,
                definition.SampleValue);
        }).ToImmutableArray();
    }

    private sealed class ConfigurationOptionDefinition
    {
        public string Key { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string ValueKind { get; set; } = string.Empty;
        public string? ConstantValue { get; set; }
        public int BoundedValue { get; set; }
        public int DeepValue { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string[] AllowedValues { get; set; } = Array.Empty<string>();
        public string PurityPolicyImpact { get; set; } = string.Empty;
        public string[] AcceptedAliases { get; set; } = Array.Empty<string>();
        public string ValueDescription { get; set; } = string.Empty;
        public string RelatedDiagnostics { get; set; } = string.Empty;
        public string SampleValue { get; set; } = string.Empty;
    }
    internal static bool TryParseRuntimeHazardMode(string? value, out RuntimeHazardMode mode)
    {
        mode = value?.Trim().ToLowerInvariant() switch
        {
            "none" => RuntimeHazardMode.Off,
            "sites" => RuntimeHazardMode.Sites,
            "summaries" => RuntimeHazardMode.Summaries,
            "all" => RuntimeHazardMode.All,
            "unknowns" => RuntimeHazardMode.Unknowns,
            "sites-and-unknowns" => RuntimeHazardMode.SitesAndUnknowns,
            "all-and-unknowns" => RuntimeHazardMode.AllAndUnknowns,
            _ => (RuntimeHazardMode)(-1)
        };
        return mode != (RuntimeHazardMode)(-1);
    }

    internal static bool IsCanonicalAllowedValue(AnalyzerConfigurationOption option, string? value)
    {
        if (option == null) throw new ArgumentNullException(nameof(option));
        if (string.IsNullOrWhiteSpace(value)) return false;
        return option.AllowedValues.Contains(value!.Trim().ToLowerInvariant(), StringComparer.Ordinal);
    }

    internal static bool IsAcceptedValue(AnalyzerConfigurationOption option, string? value)
    {
        if (IsCanonicalAllowedValue(option, value)) return true;
        return !string.IsNullOrWhiteSpace(value) &&
               option.AcceptedAliases.Contains(value!.Trim().ToLowerInvariant(), StringComparer.Ordinal);
    }

    public static ImmutableArray<AnalyzerConfigurationOption> PurityPolicyOptions =>
        All.Where(static option => option.PurityPolicyImpact != PurityPolicyImpact.None).ToImmutableArray();
}

internal sealed record AnalyzerConfigurationOption(
    string Key,
    AnalyzerConfigurationScope Scope,
    AnalyzerConfigurationValueKind ValueKind,
    AnalyzerConfigurationDefault Default,
    string Description,
    ImmutableArray<string> AllowedValues = default,
    PurityPolicyImpact PurityPolicyImpact = PurityPolicyImpact.None,
    ImmutableArray<string> AcceptedAliases = default,
    string ValueDescription = "",
    string RelatedDiagnostics = "",
    string SampleValue = "")
{
    public string DefaultValue => Default.DocumentationValue;

    public bool IsGlobal =>
        Scope == AnalyzerConfigurationScope.GlobalOnly ||
        Scope == AnalyzerConfigurationScope.GlobalAndTree;

    public bool IsTree =>
        Scope == AnalyzerConfigurationScope.TreeOnly ||
        Scope == AnalyzerConfigurationScope.GlobalAndTree;
}

internal readonly record struct AnalyzerConfigurationDefault(
    string? ConstantValue,
    int BoundedValue,
    int DeepValue,
    string Unit)
{
    internal bool IsModeDependent => ConstantValue == null;

    internal string DocumentationValue => IsModeDependent
        ? Format(BoundedValue) + " (disabled/bounded), " + Format(DeepValue) + " (deep)"
        : ConstantValue ?? string.Empty;

    internal string Resolve(SmtAnalysisMode mode)
    {
        if (!IsModeDependent) return ConstantValue ?? string.Empty;
        return (mode == SmtAnalysisMode.Deep ? DeepValue : BoundedValue)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static implicit operator AnalyzerConfigurationDefault(string value)
    {
        return new AnalyzerConfigurationDefault(
            value ?? throw new ArgumentNullException(nameof(value)),
            0,
            0,
            string.Empty);
    }

    private string Format(int value)
    {
        var formatted = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Unit.Length == 0 ? formatted : formatted + " " + Unit;
    }

}

[Flags]
internal enum PurityPolicyImpact
{
    None = 0,
    TrustsPure = 1,
    ForcesImpure = 2,
    ChangesStrictness = 4,
    ChangesAttributeIdentity = 8,
    EnablesGeneratedOverrides = 16
}

internal enum AnalyzerConfigurationScope
{
    GlobalOnly,
    TreeOnly,
    GlobalAndTree
}

internal enum AnalyzerConfigurationValueKind
{
    Bool,
    StringList,
    StructuralMemberKeyList,
    NonNegativeInteger,
    PositiveInteger,
    PurityProfile,
    MissingPuritySuggestionScope,
    RuntimeHazardMode,
    SmtMode,
    AllowedValue,
    AllowedValueList
}
