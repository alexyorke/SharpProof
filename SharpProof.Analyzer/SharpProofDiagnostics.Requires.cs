using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer;

public static partial class SharpProofDiagnostics
{
    public static readonly DiagnosticDescriptor RequiresNotProvenRule = CreateDescriptor(
        RequiresNotProvenId,
        "Precondition Not Proven",
        "Call to '{0}' does not prove precondition '{1}'",
        "Contracts",
        DiagnosticSeverity.Warning,
        "Reports calls whose current path facts contradict a declared [Requires] precondition. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the proof evidence.");

    public static readonly DiagnosticDescriptor RequiresUnsupportedRule = CreateDescriptor(
        RequiresUnsupportedId,
        "Precondition Could Not Be Verified",
        "Precondition '{1}' for '{0}' could not be verified: {2}",
        "Contracts",
        DiagnosticSeverity.Warning,
        "Reports [Requires] contracts that could not be parsed, lowered, or proven within the supported bounded proof surface. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the unprovable contract details.");
}
