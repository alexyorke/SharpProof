using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer;

public static partial class SharpProofDiagnostics
{
    public static readonly DiagnosticDescriptor PurityNotVerifiedRule = CreateDescriptor(
        PurityNotVerifiedId,
        "Purity Not Proven",
        "Method '{0}' is marked [EnforcePure]/[Pure], but its body contains operations the analyzer cannot prove pure",
        "Purity",
        DiagnosticSeverity.Error,
        "Methods marked with [EnforcePure] require analysis. This diagnostic indicates the analysis rules did not determine the method's purity status. Use `SharpProof.SymbolicCli explain --file <path> --line <number>` to inspect the symbolic proof evidence.");

    public static readonly DiagnosticDescriptor BclFallbackGuessRule = CreateDescriptor(
        BclFallbackGuessId,
        "BCL Purity Fallback Guess",
        "BCL purity fallback for '{0}': {1} ({2})",
        "Purity",
        DiagnosticSeverity.Info,
        "Reports a non-authoritative purity guess for a metadata BCL member when no stronger analyzer, attribute, generated summary, or user configuration evidence was available. Enable with sharpproof_emit_explanations or sharpproof_report_bcl_fallback_guesses.");

    public static readonly DiagnosticDescriptor PurityExplanationRule = CreateDescriptor(
        PurityExplanationId,
        "Purity Diagnostic Explanation",
        "Purity analysis for '{0}': {1}",
        "Purity",
        DiagnosticSeverity.Info,
        "Provides structured explanation data for a purity diagnostic.");

    public static readonly DiagnosticDescriptor MissingEnforcePureAttributeRule = CreateDescriptor(
        MissingEnforcePureAttributeId,
        "Missing [EnforcePure] Attribute",
        "Method '{0}' appears to be pure but is not marked with [EnforcePure]. Consider adding the attribute to enforce and document its purity.",
        "Purity",
        DiagnosticSeverity.Warning,
        "This method seems to only contain operations considered pure, but it lacks the [EnforcePure] attribute. Adding the attribute helps ensure its purity is maintained and communicates intent.");

    public static readonly DiagnosticDescriptor ConflictingPurityAttributesRule = CreateDescriptor(
        ConflictingPurityAttributesId,
        "Conflicting purity attributes",
        "Method '{0}' has conflicting purity attributes applied",
        "Usage",
        DiagnosticSeverity.Warning,
        "Apply one purity contract to a method. Combining enforcing, trusted-external, and explicit-impure attributes is contradictory or confusing.");

    public static readonly DiagnosticDescriptor AllowSynchronizationWithoutPurityAttributeRule = CreateDescriptor(
        AllowSynchronizationWithoutPurityAttributeId,
        "[AllowSynchronization] requires a purity attribute",
        "Method '{0}' is marked with [AllowSynchronization] but is not marked with [EnforcePure] or [Pure]",
        "Usage",
        DiagnosticSeverity.Warning,
        "[AllowSynchronization] only affects methods participating in purity analysis. Apply [EnforcePure] or [Pure] for it to have effect.");

    public static readonly DiagnosticDescriptor RedundantAllowSynchronizationRule = CreateDescriptor(
        RedundantAllowSynchronizationId,
        "Redundant [AllowSynchronization]",
        "Method '{0}' is marked with [AllowSynchronization] but contains no synchronization constructs",
        "Usage",
        DiagnosticSeverity.Info,
        "Remove [AllowSynchronization] when the method does not use synchronization (e.g., lock).");
}
