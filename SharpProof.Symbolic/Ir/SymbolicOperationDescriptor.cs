namespace SharpProof.Symbolic.Ir;

internal enum SymbolicInvalidationMatchKind {
    VariablePrefix,
    VariableOrMember
}
internal readonly record struct SymbolicInvalidationTarget(
    string Key,
    SymbolicInvalidationMatchKind MatchKind = SymbolicInvalidationMatchKind.VariablePrefix,
    int? DefinitionVersion = null);

internal readonly record struct SymbolicOperationOrigin(TextSpan SourceSpan, int Sequence, string Provenance);

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

internal readonly record struct SymbolicTermPropagation(SymbolicTerm Source, SymbolicTerm Target);

internal sealed record SymbolicOperationSequence(ImmutableArray<SymbolicOperationDescriptor> Operations) {
    internal static SymbolicOperationSequence Single(SymbolicOperationDescriptor operation) =>
        new([operation]);
}
internal sealed record SymbolicAssignmentOperation(
    ImmutableArray<SymbolicAssignmentBinding> Bindings,
    ImmutableArray<SymbolicCondition> Postconditions,
    SymbolicOperationOrigin Origin,
    ImmutableArray<SymbolicTermPropagation> Propagations = default,
    ImmutableArray<SymbolicInvalidationTarget> Invalidations = default) : SymbolicOperationDescriptor(Origin);

internal sealed record SymbolicMutationOperation(
    ImmutableArray<SymbolicAssignmentBinding> Bindings,
    ImmutableArray<SymbolicInvalidationTarget> Invalidations,
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin);

internal sealed record SymbolicBranchAssumptionOperation(SymbolicCondition Condition, bool AssumeTrue,
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin);

internal sealed record SymbolicMergeOperation(
    ImmutableArray<SymbolicState> IncomingStates,
    SyntaxNode Source,
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin);

internal sealed record SymbolicLoopEdgeOperation(SymbolicCondition? Condition,
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin);

internal sealed record SymbolicCompletionOperation(SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin);

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
