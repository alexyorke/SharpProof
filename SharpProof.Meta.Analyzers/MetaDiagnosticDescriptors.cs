using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SharpProof.Meta.Analyzers;

internal static class MetaDiagnosticDescriptors {
    private const string Category = "SharpProof.Soundness";
    private const string HelpBase =
        "https://github.com/alexyorke/SharpProof/blob/master/docs/architecture.md#mechanized-boundaries";

    internal static readonly DiagnosticDescriptor ForbiddenRoslynApi = Create(
        "SPMETA001",
        "Forbidden compiler API",
        "API '{0}' is forbidden in soundness-critical SharpProof layers",
        "Compiler APIs that synthesize or speculatively bind source, " +
        "use display text as identity, or enumerate whole compilations are forbidden.");

    internal static readonly DiagnosticDescriptor MutableStaticState = Create(
        "SPMETA002",
        "Mutable static state",
        "Mutable static field '{0}' is forbidden in analyzer, frontend, and verifier layers",
        "Analysis state must be compilation- or worker-scoped.");

    internal static readonly DiagnosticDescriptor SwallowedCancellation = Create(
        "SPMETA003",
        "Cancellation is swallowed",
        "Catch handler for OperationCanceledException must rethrow cancellation",
        "Cancellation is control flow and may not be converted into a semantic answer.");

    internal static readonly DiagnosticDescriptor SemanticStringControlFlow = Create(
        "SPMETA004",
        "Semantic string controls behavior",
        "Reason or provenance literal '{0}' may not control semantic behavior",
        "Closed enums and typed records must represent semantic reasons and provenance.");

    internal static readonly DiagnosticDescriptor DescriptorConstruction = Create(
        "SPMETA005",
        "Diagnostic descriptor bypasses the catalog",
        "DiagnosticDescriptor instances must be declared in the generated catalog",
        "Diagnostic descriptors are generated static fields with stable identifiers and help links.");

    internal static readonly DiagnosticDescriptor StringFieldInIr = Create(
        "SPMETA006",
        "String field in program IR",
        "String field '{0}' is forbidden in SharpProof.Ir",
        "Semantic identity in the program IR must use scoped typed identifiers.");

    internal static readonly DiagnosticDescriptor AssumptionConstruction = Create(
        "SPMETA007",
        "Assumption bypasses the trusted boundary",
        "Assumption instances may only be created by the proof kernel or callable verifier",
        "Only allowlisted proof-producing code may turn justified predicates into assumptions.");

    internal static readonly DiagnosticDescriptor EffectSummaryConstruction = Create(
        "SPMETA008",
        "Effect summary bypasses the trusted boundary",
        "EffectSummary instances may only be created by allowlisted effect-domain code",
        "Trusted effect summaries must be constructed by the effect domain, operations, or external-spec resolver.");

    internal static readonly DiagnosticDescriptor CSharpExpressionText = Create(
        "SPMETA009",
        "C# expression text is synthesized",
        "String concatenation may not synthesize C# expression text containing '{0}'",
        "Soundness-critical layers must bind compiler operations rather than construct source expressions as text.");

    internal static readonly DiagnosticDescriptor NonCacheableSemanticAnswer = Create(
        "SPMETA010",
        "Non-cacheable semantic answer",
        "Timeout, error, failure, and Unknown answers may not be written to a semantic cache",
        "Transient and abstaining results are not stable semantic facts and must not be cached.");

    internal static readonly DiagnosticDescriptor ProofOutcomeConstruction = Create(
        "SPMETA011",
        "Proof outcome bypasses the trusted kernel",
        "'{0}' instances may only be created by the proof kernel",
        "Only the proof kernel may construct proven or refuted outcomes and replay-validated models.");

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

    private static DiagnosticDescriptor Create(
        string id,
        string title,
        string message,
        string description) =>
        new(
            id,
            title,
            message,
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description,
            HelpBase);
}
