namespace SharpProof.Analyzer;
internal static class InvalidContractArgumentDiagnostics
{
    internal static Diagnostic Create(string attributeName, string argument, string reason, Location location)
    {
        return Diagnostic.Create(
                GeneratedDiagnosticDescriptors.InvalidContractArgumentRule,
                location,
                attributeName,
                argument,
                reason);
    }
}
