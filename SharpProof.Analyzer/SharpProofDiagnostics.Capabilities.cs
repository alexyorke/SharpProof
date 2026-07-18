using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer;

public static partial class SharpProofDiagnostics
{
    public static readonly DiagnosticDescriptor CapabilityViolationRule = CreateDescriptor(
        CapabilityViolationId,
        "Disallowed Capability Use",
        "Method '{1}' is marked [AllowedCapabilities], but operation '{0}' requires capabilities: {2}",
        "Capabilities",
        DiagnosticSeverity.Warning,
        "Reports source-visible operations or proven transitive callees that exceed the method's declared allowed capability set.");

    public static readonly DiagnosticDescriptor CapabilityUnknownRule = CreateDescriptor(
        CapabilityUnknownId,
        "Capability Contract Not Proven",
        "Method '{1}' is marked [AllowedCapabilities], but operation '{0}' could not be capability-verified: {2}",
        "Capabilities",
        DiagnosticSeverity.Warning,
        "Reports operations whose capability set could not be conservatively proven under the current capability analysis. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the capability proof evidence.");
}
