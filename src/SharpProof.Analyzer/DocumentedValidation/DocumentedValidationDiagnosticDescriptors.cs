namespace SharpProof.Analyzer;

internal static class DocumentedValidationDiagnosticDescriptors
{
    internal static readonly DiagnosticDescriptor MismatchRule = new(
        id: "SP0061",
        title: "Documented validation disagrees with the code",
        messageFormat: "{0} is documented as '{1}', but {2}",
        category: "Documentation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports an XML documentation <param> or <returns> validation sentence that the declarations and runtime checks on the same value do not establish, or that no declaration or runtime check backs. An HTTP entry handler is checked against its runtime guards and the framework validation it declares, not against caller preconditions.",
        helpLinkUri: "https://github.com/alexyorke/SharpProof/blob/master/docs/diagnostic-examples.md#sp0061");

    internal static readonly DiagnosticDescriptor OpenApiRule = new(
        id: "SP0062",
        title: "OpenAPI parameter disagrees with the code or documentation",
        messageFormat: "{0}",
        category: "Documentation",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reports an OpenAPI 3 parameter constraint (bounds or nullability) for an HTTP entry route that disagrees with the validation the handler performs or with the XML documentation of the handler parameter.",
        helpLinkUri: "https://github.com/alexyorke/SharpProof/blob/master/docs/diagnostic-examples.md#sp0062");

    internal static readonly DiagnosticDescriptor UndocumentedRule = new(
        id: "SP0063",
        title: "Runtime validation is not documented",
        messageFormat: "{0} is checked at runtime and admits {1}, but its XML documentation does not state it",
        category: "Documentation",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Reports a bound or non-null requirement that a runtime guard or DataAnnotation enforces on a parameter whose XML documentation does not state it.",
        helpLinkUri: "https://github.com/alexyorke/SharpProof/blob/master/docs/diagnostic-examples.md#sp0063");

    internal static readonly DiagnosticDescriptor ReviewRule = new(
        id: "SP0064",
        title: "Documentation claim awaits review",
        messageFormat: "{0}",
        category: "Documentation",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Reports a validation claim that a local model proposed from a parameter's documentation and that a person has not accepted or rejected, or that the model could not read as a claim. Only accepted claims are checked.",
        helpLinkUri: "https://github.com/alexyorke/SharpProof/blob/master/docs/diagnostic-examples.md#sp0064");

    internal static ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [MismatchRule, OpenApiRule, UndocumentedRule, ReviewRule];
}
