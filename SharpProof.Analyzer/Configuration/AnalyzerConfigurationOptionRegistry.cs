namespace SharpProof.Analyzer.Configuration;

internal static class AnalyzerConfigurationOptionRegistry {
    private const string ResourceName = "SharpProof.Analyzer.Configuration.Options.json";

    public static ImmutableArray<AnalyzerConfigurationOption> All { get; } = Load();

    private static ImmutableArray<AnalyzerConfigurationOption> Load() {
        using var stream = typeof(AnalyzerConfigurationOptionRegistry).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded configuration catalog '{ResourceName}'.");
        var definitions = JsonSerializer.Deserialize<ConfigurationOptionDefinition[]>(stream)
            ?? throw new InvalidOperationException("The embedded configuration catalog is empty.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        return definitions.Select(definition => {
            if (!keys.Add(definition.Key))
                throw new InvalidOperationException($"Duplicate configuration key '{definition.Key}'.");
            if (!Enum.TryParse<AnalyzerConfigurationValueKind>(definition.ValueKind, out var valueKind))
                throw new InvalidOperationException($"Configuration option '{definition.Key}' has invalid enum metadata.");
            return new AnalyzerConfigurationOption(
                definition.Key,
                valueKind,
                definition.AllowedValues.ToImmutableArray());
        }).ToImmutableArray();
    }

    sealed class ConfigurationOptionDefinition {
        public string Key { get; set; } = string.Empty;
        public string ValueKind { get; set; } = string.Empty;
        public string[] AllowedValues { get; set; } = [];
    }

    internal static bool IsAcceptedValue(AnalyzerConfigurationOption option, string? value) {
        if (option == null) throw new ArgumentNullException(nameof(option));
        return !string.IsNullOrWhiteSpace(value) && option.AllowedValues.Contains(
            value!.Trim().ToLowerInvariant(), StringComparer.Ordinal);
    }

}

internal sealed record AnalyzerConfigurationOption(
    string Key,
    AnalyzerConfigurationValueKind ValueKind,
    ImmutableArray<string> AllowedValues = default);

internal enum AnalyzerConfigurationValueKind {
    Bool,
    NonNegativeInteger,
    PositiveInteger,
    SmtMode
}
