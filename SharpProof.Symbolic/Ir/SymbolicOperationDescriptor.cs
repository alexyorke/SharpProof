namespace SharpProof.Symbolic.Ir;
internal enum SymbolicInvalidationMatchKind {
    VariablePrefix,
    VariableOrMember
}
internal readonly record struct SymbolicInvalidationTarget(
    string Key,
    SymbolicInvalidationMatchKind MatchKind = SymbolicInvalidationMatchKind.VariablePrefix,
    int? DefinitionVersion = null);
internal readonly record struct SymbolicOperationOrigin(TextSpan SourceSpan, string Provenance);
internal abstract record SymbolicOperationDescriptor(SymbolicOperationOrigin Origin);
internal sealed record SymbolicAssignmentBinding(
    string TargetKey,
    SymbolicTerm Target,
    SymbolicTerm? Source,
    string? Provenance = null,
    string? EvidenceKey = null,
    bool PropagateSourceFacts = false,
    bool DeriveIntegerBounds = false,
    bool InvalidateTarget = true);
internal sealed record SymbolicAssignmentOperation(
    ImmutableArray<SymbolicAssignmentBinding> Bindings,
    ImmutableArray<SymbolicCondition> Postconditions,
    SymbolicOperationOrigin Origin,
    ImmutableArray<SymbolicInvalidationTarget> Invalidations = default) : SymbolicOperationDescriptor(Origin);
internal sealed record SymbolicMutationOperation(
    ImmutableArray<SymbolicAssignmentBinding> Bindings,
    ImmutableArray<SymbolicInvalidationTarget> Invalidations,
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin);
internal sealed record SymbolicHazardOperation(
    SymbolicRuntimeHazardKind HazardKind,
    SymbolicExceptionPreconditionKind PreconditionKind,
    SymbolicTerm? Subject,
    SymbolicCondition Trigger,
    SymbolicFactConfidence Confidence,
    string ExceptionType,
    string Category,
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin) {
    internal SymbolicFact ToPreconditionFact() => new(
            new SymbolicExceptionPreconditionAtom(PreconditionKind, Subject, Trigger),
            true,
            Confidence,
            Origin.Provenance,
            Origin.SourceSpan,
            null,
            Confidence == SymbolicFactConfidence.Unsupported ? Origin.Provenance : null);
}
