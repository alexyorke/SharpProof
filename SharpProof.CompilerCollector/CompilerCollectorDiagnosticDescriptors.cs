namespace SharpProof.CompilerCollector;

internal static class CompilerCollectorDiagnosticDescriptors
{
    internal static readonly DiagnosticDescriptor CompilerManifestFailureRule = new(
        id: "SP0049",
        title: "Compiler Manifest Emission Failed",
        messageFormat: "SharpProof could not emit the final compiler manifest: {0}",
        category: "Infrastructure",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Reports a fatal failure to attest the compiler's final compilation.",
        helpLinkUri: "https://github.com/alexyorke/SharpProof/blob/master/docs/diagnostic-examples.md#sp0049",
        customTags: []);
}
