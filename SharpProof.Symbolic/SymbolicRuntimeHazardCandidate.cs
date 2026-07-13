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

    public SyntaxNode Site { get; }

    public SymbolicRuntimeHazardKind Kind { get; }

    public SymbolicFact TriggerPrecondition { get; }

    public string ExceptionType { get; }

    public string Category { get; }

    internal bool TryGetExactTriggerCondition(out SymbolicCondition condition)
    {
        if (TriggerPrecondition.Confidence == SymbolicFactConfidence.Exact &&
            TriggerPrecondition.Atom is SymbolicExceptionPreconditionAtom precondition)
        {
            condition = precondition.Trigger;
            return true;
        }

        condition = null!;
        return false;
    }
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
