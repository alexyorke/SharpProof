#pragma warning disable RS2001 // Disabled-by-default rules are preserved exactly; release tracking reports them as severity changes.
#pragma warning disable RS1037 // Compilation-end reporting policy is separate from descriptor boundary metadata.

namespace SharpProof.Analyzer;

internal static class AnalyzerDiagnosticCatalog {
    private const string ResourceName = "SharpProof.Analyzer.DiagnosticCatalog.json";

    private static readonly ImmutableDictionary<string, DiagnosticDescriptor> DescriptorsByField = Load();

    internal static readonly ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics = DescriptorsByField
        .Values
        .OrderBy(static descriptor => int.Parse(descriptor.Id.Substring(2), CultureInfo.InvariantCulture))
        .ToImmutableArray();

    internal static DiagnosticDescriptor Get(string fieldName) => DescriptorsByField[fieldName];

    private static ImmutableDictionary<string, DiagnosticDescriptor> Load() {
        using var stream = typeof(AnalyzerDiagnosticCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded diagnostic catalog '{ResourceName}'.");
        var definitions = JsonSerializer.Deserialize<DiagnosticDefinition[]>(stream)
            ?? throw new InvalidOperationException("The embedded diagnostic catalog is empty.");
        var descriptors = ImmutableDictionary.CreateBuilder<string, DiagnosticDescriptor>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var definition in definitions) {
            if (string.IsNullOrWhiteSpace(definition.FieldName) || string.IsNullOrWhiteSpace(definition.Id)) {
                throw new InvalidOperationException("Every diagnostic catalog entry requires a field name and ID.");
            }

            if (!Enum.TryParse<DiagnosticSeverity>(definition.DefaultSeverity, out var severity)) {
                throw new InvalidOperationException($"Diagnostic '{definition.Id}' has invalid severity '{definition.DefaultSeverity}'.");
            }

            if (!ids.Add(definition.Id) || descriptors.ContainsKey(definition.FieldName)) {
                throw new InvalidOperationException($"Duplicate diagnostic catalog entry '{definition.FieldName}'/'{definition.Id}'.");
            }

            descriptors.Add(definition.FieldName, new DiagnosticDescriptor(
                definition.Id,
                definition.Title,
                definition.MessageFormat,
                definition.Category,
                severity,
                definition.IsEnabledByDefault,
                definition.Description,
                definition.HelpLinkUri,
                definition.CustomTags ?? Array.Empty<string>()));
        }

        return descriptors.ToImmutable();
    }

    private sealed class DiagnosticDefinition {
        public string FieldName { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string MessageFormat { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string DefaultSeverity { get; set; } = string.Empty;
        public bool IsEnabledByDefault { get; set; }
        public string Description { get; set; } = string.Empty;
        public string HelpLinkUri { get; set; } = string.Empty;
        public string[]? CustomTags { get; set; }
    }
}

#pragma warning restore RS1037
#pragma warning restore RS2001
