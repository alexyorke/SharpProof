using Microsoft.CodeAnalysis;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Symbolic;

internal readonly struct RuntimeHazardCandidate
{
    public RuntimeHazardCandidate(SyntaxNode site, SymbolicHazardOperation operation)
    {
        Site = site;
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
    }

    public SyntaxNode Site { get; }

    public SymbolicHazardOperation Operation { get; }

    public SymbolicRuntimeHazardKind Kind => Operation.HazardKind;
}
