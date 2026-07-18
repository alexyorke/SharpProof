using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer;

public static partial class SharpProofDiagnostics
{
    public static readonly DiagnosticDescriptor ExceptionSummaryRule = CreateDescriptor(
        ExceptionSummaryId,
        "Method May Throw Exceptions",
        "Method '{0}' can throw: {1}",
        "ExceptionFlow",
        DiagnosticSeverity.Info,
        "Reports exception types that can escape a method. Enable with sharpproof_report_exceptions = true or sharpproof_runtime_hazard_mode = summaries/all/all-and-unknowns. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the exception proof evidence.");

    public static readonly DiagnosticDescriptor UncaughtExceptionSiteRule = CreateDescriptor(
        UncaughtExceptionSiteId,
        "Operation May Throw Uncaught Exceptions",
        "Operation '{0}' may throw uncaught exceptions: {1}",
        "ExceptionFlow",
        DiagnosticSeverity.Warning,
        "Reports uncaught exceptions and proven runtime hazards at specific operations. Enable with sharpproof_checked_exceptions = true or sharpproof_runtime_hazard_mode = sites/all/sites-and-unknowns/all-and-unknowns. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the runtime hazard evidence.");

    public static readonly DiagnosticDescriptor UnknownRuntimeHazardRule = CreateDescriptor(
        UnknownRuntimeHazardId,
        "Runtime Hazard Candidate Could Not Be Proven",
        "Runtime hazard candidate '{0}' at operation '{1}' could not be proven: {2}",
        "ExceptionFlow",
        DiagnosticSeverity.Info,
        "Reports bounded runtime-hazard candidates whose trigger could not be proven or rejected. " +
        "Enable with sharpproof_runtime_hazard_mode = unknowns, sites-and-unknowns, or all-and-unknowns. " +
        "The diagnostic is informational by default and carries stable proof, reason, trigger, and baseline metadata.");

    public static readonly DiagnosticDescriptor ExceptionContractViolationRule = CreateDescriptor(
        ExceptionContractViolationId,
        "Exception Contract Violated",
        "Method '{0}' is marked {1}, but operation '{2}' can throw disallowed exceptions: {3}",
        "ExceptionFlow",
        DiagnosticSeverity.Warning,
        "Reports operations whose escaping exceptions violate [DoesNotThrow] or [AllowedExceptions] contracts. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the exception proof evidence.");
}
