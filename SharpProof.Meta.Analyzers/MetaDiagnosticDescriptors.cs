using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SharpProof.Meta.Analyzers;

internal static class MetaDiagnosticDescriptors {
    private const string Category = "SharpProof.Soundness";
    private const string HelpBase =
        "https://github.com/alexyorke/SharpProof/blob/master/docs/architecture.md#mechanized-boundaries";

    internal static readonly DiagnosticDescriptor ForbiddenRoslynApi = new(
        "SPMETA001",
        "Forbidden compiler API",
        "API '{0}' is forbidden in soundness-critical SharpProof layers",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        "Compiler APIs that synthesize or speculatively bind source, " +
        "use display text as identity, or enumerate whole compilations are forbidden.",
        HelpBase);

    internal static readonly DiagnosticDescriptor MutableStaticState = new(
        "SPMETA002",
        "Mutable static state",
        "Mutable static field '{0}' is forbidden in analyzer, frontend, and verifier layers",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        "Analysis state must be compilation- or worker-scoped.",
        HelpBase);

    internal static readonly DiagnosticDescriptor SwallowedCancellation = new(
        "SPMETA003",
        "Cancellation is swallowed",
        "Catch handler for OperationCanceledException must rethrow cancellation",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        "Cancellation is control flow and may not be converted into a semantic answer.",
        HelpBase);

    internal static readonly DiagnosticDescriptor SemanticStringControlFlow = new(
        "SPMETA004",
        "Semantic string controls behavior",
        "Reason or provenance literal '{0}' may not control semantic behavior",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        "Closed enums and typed records must represent semantic reasons and provenance.",
        HelpBase);

    internal static readonly DiagnosticDescriptor DescriptorConstruction = new(
        "SPMETA005",
        "Diagnostic descriptor bypasses the catalog",
        "DiagnosticDescriptor instances must be declared in the generated catalog",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        "Diagnostic descriptors are generated static fields with stable identifiers and help links.",
        HelpBase);

    internal static readonly DiagnosticDescriptor StringFieldInIr = new(
        "SPMETA006",
        "String field in program IR",
        "String field '{0}' is forbidden in SharpProof.Ir",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        "Semantic identity in the program IR must use scoped typed identifiers.",
        HelpBase);

    internal static readonly DiagnosticDescriptor AssumptionConstruction = new(
        "SPMETA007",
        "Assumption bypasses the trusted boundary",
        "Assumption instances may only be created by the proof kernel or callable verifier",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        "Only allowlisted proof-producing code may turn justified predicates into assumptions.",
        HelpBase);

    internal static readonly DiagnosticDescriptor EffectSummaryConstruction = new(
        "SPMETA008",
        "Effect summary bypasses the trusted boundary",
        "EffectSummary instances may only be created by allowlisted effect-domain code",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        "Trusted effect summaries must be constructed by the effect domain, operations, or external-spec resolver.",
        HelpBase);

    internal static readonly DiagnosticDescriptor CSharpExpressionText = new(
        "SPMETA009",
        "C# expression text is synthesized",
        "String concatenation may not synthesize C# expression text containing '{0}'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        "Soundness-critical layers must bind compiler operations rather than construct source expressions as text.",
        HelpBase);

    internal static readonly DiagnosticDescriptor NonCacheableSemanticAnswer = new(
        "SPMETA010",
        "Non-cacheable semantic answer",
        "Timeout, error, failure, and Unknown answers may not be written to a semantic cache",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        "Transient and abstaining results are not stable semantic facts and must not be cached.",
        HelpBase);

    internal static readonly DiagnosticDescriptor ProofOutcomeConstruction = new(
        "SPMETA011",
        "Proof outcome bypasses the trusted kernel",
        "'{0}' instances may only be created by the proof kernel",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        "Only the proof kernel may construct proven or refuted outcomes and replay-validated models.",
        HelpBase);

    internal static readonly ImmutableArray<DiagnosticDescriptor> All = [
        ForbiddenRoslynApi,
        MutableStaticState,
        SwallowedCancellation,
        SemanticStringControlFlow,
        DescriptorConstruction,
        StringFieldInIr,
        AssumptionConstruction,
        EffectSummaryConstruction,
        CSharpExpressionText,
        NonCacheableSemanticAnswer,
        ProofOutcomeConstruction
    ];
}
