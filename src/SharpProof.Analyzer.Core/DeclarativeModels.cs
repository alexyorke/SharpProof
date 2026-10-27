#nullable enable
namespace SharpProof.Analyzer;

internal sealed partial record EffectClaimEvaluation(
    EffectEvaluationContractKind Kind,
    ImmutableArray<AttributeData> Attributes,
    EffectEvaluationOutcome Outcome,
    EffectEvaluationReason Reason,
    EffectEvaluationCertainty Certainty,
    string Evidence,
    EffectDirectWitness? Witness,
    EffectClaimConstraint Constraint,
    DiagnosticDescriptor? Diagnostic,
    Location DiagnosticLocation,
    ImmutableArray<object?> DiagnosticArguments
)
{
    public ImmutableArray<AttributeData> Attributes { get; init; } = Attributes.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Attributes)) : Attributes;
    public string Evidence { get; init; } = Evidence ?? throw new ArgumentNullException(nameof(Evidence));
    public EffectClaimConstraint Constraint { get; init; } = Constraint ?? throw new ArgumentNullException(nameof(Constraint));
    public Location DiagnosticLocation { get; init; } = DiagnosticLocation ?? throw new ArgumentNullException(nameof(DiagnosticLocation));
    public ImmutableArray<object?> DiagnosticArguments { get; init; } = DiagnosticArguments.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(DiagnosticArguments)) : DiagnosticArguments;
}

internal sealed partial class SharpProofAnalyzerEngine
{
    private readonly partial record struct AdvisoryActivation(
        bool RequiresSymbolAnalysis,
        bool RequiresOperationAnalysis,
        bool RequiresFullOperationAnalysis
    );
}

internal static partial class AnalyzerFeaturePipeline
{
    private readonly partial record struct MethodSelection(ContractSelectionFeatures Features, bool IsSuppressed);
}
