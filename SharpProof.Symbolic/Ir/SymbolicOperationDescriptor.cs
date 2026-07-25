namespace SharpProof.Symbolic.Ir;
internal enum SymbolicInvalidationMatchKind {
    VariablePrefix,
    VariableOrMember,
    VariableDescendants,
    VariableElements
}
internal readonly record struct SymbolicInvalidationTarget(
    string Key,
    SymbolicInvalidationMatchKind MatchKind = SymbolicInvalidationMatchKind.VariablePrefix,
    int? DefinitionVersion = null);
internal readonly record struct SymbolicOperationOrigin(TextSpan SourceSpan, string Provenance);
internal sealed record SymbolicAssignmentBinding(
    string TargetKey,
    SymbolicTerm Target,
    SymbolicTerm? Source,
    string? Provenance = null,
    string? EvidenceKey = null,
    bool PropagateSourceFacts = false,
    bool DeriveIntegerBounds = false,
    bool InvalidateTarget = true);
internal sealed record SymbolicStateDelta(
    ImmutableArray<SymbolicAssignmentBinding> Bindings,
    SymbolicOperationOrigin Origin,
    ImmutableArray<SymbolicCondition> Assumptions = default,
    ImmutableArray<SymbolicInvalidationTarget> Invalidations = default,
    SymbolicUnknownReason UnknownReason = SymbolicUnknownReason.None);
internal sealed record SymbolicHazardOperation(
    SymbolicRuntimeHazardKind HazardKind,
    SymbolicExceptionPreconditionKind PreconditionKind,
    SymbolicTerm? Subject,
    SymbolicCondition Trigger,
    SymbolicFactConfidence Confidence,
    string ExceptionType,
    string Category,
    SymbolicOperationOrigin Origin) {
    internal SymbolicFact ToPreconditionFact() => new(
            new SymbolicExceptionPreconditionAtom(PreconditionKind, Subject, Trigger),
            true,
            Confidence,
            Origin.Provenance,
            Origin.SourceSpan,
            null,
            Confidence == SymbolicFactConfidence.Unsupported ? Origin.Provenance : null);
}
