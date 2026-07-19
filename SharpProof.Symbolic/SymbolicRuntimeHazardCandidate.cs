namespace SharpProof.Symbolic;

internal readonly struct RuntimeHazardCandidate(SyntaxNode site, SymbolicHazardOperation operation)
{
    public SyntaxNode Site { get; } = site;

    public SymbolicHazardOperation Operation { get; } = operation ?? throw new ArgumentNullException(nameof(operation));

    public SymbolicRuntimeHazardKind Kind => Operation.HazardKind;
}
