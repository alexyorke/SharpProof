using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer;

public static partial class SharpProofDiagnostics
{
    public static readonly DiagnosticDescriptor ComplexityExceededRule = CreateDescriptor(
        ComplexityExceededId,
        "Declared Complexity Exceeded",
        "Method '{0}' is marked [ExpectedComplexity({1})], but inferred complexity '{2}' exceeds the declared bound",
        "Complexity",
        DiagnosticSeverity.Warning,
        "Reports methods whose inferred bounded complexity is stronger than the declared [ExpectedComplexity] contract allows. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the complexity proof evidence.");

    public static readonly DiagnosticDescriptor ComplexityCouldNotBeVerifiedRule = CreateDescriptor(
        ComplexityCouldNotBeVerifiedId,
        "Declared Complexity Could Not Be Verified",
        "Method '{0}' is marked [ExpectedComplexity({1})], but the declared bound could not be verified conservatively: {2}",
        "Complexity",
        DiagnosticSeverity.Warning,
        "Reports [ExpectedComplexity] contracts that could not be verified because the inferred complexity is unknown, unsupported, or incomparable with the declared bound. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the complexity analysis details.");
}
