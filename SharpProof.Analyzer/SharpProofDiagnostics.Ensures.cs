using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer;

public static partial class SharpProofDiagnostics
{
    public static readonly DiagnosticDescriptor EnsuresNotProvenRule = CreateDescriptor(
        EnsuresNotProvenId,
        "Postcondition Not Proven",
        "Method '{1}' is marked [Ensures], but return site '{0}' does not prove postcondition '{2}'",
        "Contracts",
        DiagnosticSeverity.Warning,
        "Reports return sites that contradict a declared [Ensures] postcondition. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the proof evidence for each return site.");

    public static readonly DiagnosticDescriptor EnsuresUnsupportedRule = CreateDescriptor(
        EnsuresUnsupportedId,
        "Postcondition Could Not Be Verified",
        "Method '{1}' is marked [Ensures], but postcondition '{0}' could not be verified: {2}",
        "Contracts",
        DiagnosticSeverity.Warning,
        "Reports [Ensures] contracts that could not be parsed, lowered, or proven within the supported bounded proof surface. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the unprovable contract details.");
}
