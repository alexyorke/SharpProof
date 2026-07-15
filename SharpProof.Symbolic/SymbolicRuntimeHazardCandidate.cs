using Microsoft.CodeAnalysis;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Symbolic;

internal readonly struct RuntimeHazardCandidate
{
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
