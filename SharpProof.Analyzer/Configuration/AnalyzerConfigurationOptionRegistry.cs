namespace SharpProof.Analyzer.Configuration;

internal static class AnalyzerConfigurationOptionRegistry {
    private const string ResourceName = "SharpProof.Analyzer.Configuration.Options.json";

    public static ImmutableArray<AnalyzerConfigurationOption> All { get; } = Load();

    public static ImmutableArray<AnalyzerConfigurationOption> GlobalOptions =>
        All.Where(static option => option.IsGlobal).ToImmutableArray();

    private static ImmutableArray<AnalyzerConfigurationOption> Load() {
        using var stream = typeof(AnalyzerConfigurationOptionRegistry).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded configuration catalog '{ResourceName}'.");
        var definitions = JsonSerializer.Deserialize<ConfigurationOptionDefinition[]>(stream)
            ?? throw new InvalidOperationException("The embedded configuration catalog is empty.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        return definitions.Select(definition => {
            if (!keys.Add(definition.Key))
                throw new InvalidOperationException($"Duplicate configuration key '{definition.Key}'.");
            if (!Enum.TryParse<AnalyzerConfigurationScope>(definition.Scope, out var scope) ||
                !Enum.TryParse<AnalyzerConfigurationValueKind>(definition.ValueKind, out var valueKind))
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
                definition.AcceptedAliases.ToImmutableArray());
        }).ToImmutableArray();
    }

    private sealed class ConfigurationOptionDefinition {
        public string Key { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string ValueKind { get; set; } = string.Empty;
        public string? ConstantValue { get; set; }
        public int BoundedValue { get; set; }
        public int DeepValue { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string[] AllowedValues { get; set; } = Array.Empty<string>();
        public string[] AcceptedAliases { get; set; } = Array.Empty<string>();
    }
    internal static bool TryParseRuntimeHazardMode(string? value, out RuntimeHazardMode mode) {
        mode = value?.Trim().ToLowerInvariant() switch {
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

    internal static bool IsCanonicalAllowedValue(AnalyzerConfigurationOption option, string? value) {
        if (option == null) throw new ArgumentNullException(nameof(option));
        if (string.IsNullOrWhiteSpace(value)) return false;
        return option.AllowedValues.Contains(value!.Trim().ToLowerInvariant(), StringComparer.Ordinal);
    }

    internal static bool IsAcceptedValue(AnalyzerConfigurationOption option, string? value) {
        if (IsCanonicalAllowedValue(option, value)) return true;
        return !string.IsNullOrWhiteSpace(value) &&
               option.AcceptedAliases.Contains(value!.Trim().ToLowerInvariant(), StringComparer.Ordinal);
    }

}

internal sealed record AnalyzerConfigurationOption(
    string Key,
    AnalyzerConfigurationScope Scope,
    AnalyzerConfigurationValueKind ValueKind,
    AnalyzerConfigurationDefault Default,
    string Description,
    ImmutableArray<string> AllowedValues = default,
    ImmutableArray<string> AcceptedAliases = default) {

    public bool IsGlobal =>
        Scope == AnalyzerConfigurationScope.GlobalOnly ||
        Scope == AnalyzerConfigurationScope.GlobalAndTree;

}

internal readonly record struct AnalyzerConfigurationDefault(
    string? ConstantValue,
    int BoundedValue,
    int DeepValue,
    string Unit) {
    internal bool IsModeDependent => ConstantValue == null;

}

internal enum AnalyzerConfigurationScope {
    GlobalOnly,
    TreeOnly,
    GlobalAndTree
}

internal enum AnalyzerConfigurationValueKind {
    Bool,
    StringList,
    NonNegativeInteger,
    PositiveInteger,
    InferredContractSuggestionScope,
    RuntimeHazardMode,
    SmtMode,
    AllowedValue,
    AllowedValueList
}
