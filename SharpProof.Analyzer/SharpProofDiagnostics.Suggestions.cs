using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer;

public static partial class SharpProofDiagnostics
{
    private static readonly LocalizableString InferredContractSuggestionMessageFormat =
        "Method '{0}' has {1}; consider adding {2} ({3} confidence)";

    private static readonly LocalizableString InferredContractSuggestionDescription =
        "Reports opt-in inferred contract candidates backed by bounded analyzer evidence. " +
        "Enable with sharpproof_suggest_inferred_contracts and filter by family, visibility, and confidence.";

    public static readonly DiagnosticDescriptor SuggestZeroAllocationsRule = CreateInferredContractDescriptor(
        SuggestZeroAllocationsId,
        "Inferred [ZeroAllocations] Contract");

    public static readonly DiagnosticDescriptor SuggestAllowedCapabilitiesRule = CreateInferredContractDescriptor(
        SuggestAllowedCapabilitiesId,
        "Inferred [AllowedCapabilities] Contract");

    public static readonly DiagnosticDescriptor SuggestExpectedComplexityRule = CreateInferredContractDescriptor(
        SuggestExpectedComplexityId,
        "Inferred [ExpectedComplexity] Contract");

    public static readonly DiagnosticDescriptor SuggestExceptionContractRule = CreateInferredContractDescriptor(
        SuggestExceptionContractId,
        "Inferred Exception Contract");

    public static readonly DiagnosticDescriptor SuggestEnsuresRule = CreateInferredContractDescriptor(
        SuggestEnsuresId,
        "Inferred [Ensures] Contract");

    public static readonly DiagnosticDescriptor SuggestRequiresRule = CreateInferredContractDescriptor(
        SuggestRequiresId,
        "Inferred [Requires] Contract");
}
