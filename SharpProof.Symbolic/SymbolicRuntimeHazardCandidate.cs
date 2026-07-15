using Microsoft.CodeAnalysis;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Symbolic;

internal readonly struct RuntimeHazardCandidate
{
    public RuntimeHazardCandidate(
        SyntaxNode site,
        SymbolicRuntimeHazardKind kind,
        RuntimeHazardTrigger trigger,
        string exceptionType,
        string category)
    {
        Site = site;
        Kind = kind;
        TriggerPrecondition = trigger.Precondition;
        ExceptionType = exceptionType;
        Category = category;
    }

    public RuntimeHazardCandidate(SyntaxNode site, SymbolicHazardOperation operation)
    {
        Site = site;
        Kind = operation.HazardKind;
        TriggerPrecondition = new SymbolicFact(
            new SymbolicExceptionPreconditionAtom(
                operation.PreconditionKind,
                operation.Subject,
                operation.Trigger),
            true,
            operation.Confidence,
            operation.Origin.Provenance,
            operation.Origin.SourceSpan,
            null,
            operation.Confidence == SymbolicFactConfidence.Unsupported
                ? operation.Origin.Provenance
                : null);
        ExceptionType = operation.ExceptionType;
        Category = operation.Category;
    }

    public SyntaxNode Site { get; }

    public SymbolicRuntimeHazardKind Kind { get; }

    public SymbolicFact TriggerPrecondition { get; }

    public string ExceptionType { get; }

    public string Category { get; }

}

internal readonly struct RuntimeHazardTrigger
{
    private RuntimeHazardTrigger(SymbolicFact precondition)
    {
        Precondition = precondition ?? throw new ArgumentNullException(nameof(precondition));
    }

    internal static bool TryCreate(SymbolicFact precondition, out RuntimeHazardTrigger trigger)
    {
        if (precondition == null)
        {
            trigger = default;
            return false;
        }

        trigger = new RuntimeHazardTrigger(precondition);
        return true;
    }

    internal SymbolicFact Precondition { get; }
}
