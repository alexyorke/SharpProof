namespace SharpProof.Analyzer;

internal static class DocumentedValidationDiagnosticDescriptors
{
    internal static readonly DiagnosticDescriptor MismatchRule = new(
        id: "SP0061",
        title: "Documented validation disagrees with the declared contract",
        messageFormat: "{0} is documented as '{1}', but {2}",
        category: "Documentation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports an XML documentation <param> or <returns> sentence that claims a validation the declared SharpProof contract does not establish, or that no declared contract backs.",
        helpLinkUri: "https://github.com/alexyorke/SharpProof/blob/master/docs/diagnostic-examples.md#sp0061");

    internal static ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [MismatchRule];
}
