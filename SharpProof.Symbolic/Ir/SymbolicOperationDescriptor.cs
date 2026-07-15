using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SharpProof.Symbolic.Ir;

internal enum SymbolicAssignmentOperationKind
{
    Simple,
    Compound,
    Coalesce,
    Deconstruction,
    Ref,
    Out
}

internal enum SymbolicMutationOperationKind
{
    Increment,
    Decrement,
    Invalidate,
    CallerVisible
}

internal enum SymbolicComputedUpdateKind
{
    CompoundAssignment,
    Increment,
    Decrement
}

internal enum SymbolicInvalidationMatchKind
{
    VariablePrefix,
    VariableOrMember
}

internal readonly record struct SymbolicInvalidationTarget(
    string Key,
    SymbolicInvalidationMatchKind MatchKind = SymbolicInvalidationMatchKind.VariablePrefix,
    int? DefinitionVersion = null);

internal enum SymbolicLoopEdgeKind
{
    Entry,
    Body,
    BackEdge,
    Exit
}

internal enum SymbolicCompletionKind
{
    Normal,
    NoFallthrough,
    Return,
    Throw,
    Break,
    Continue
}

internal enum SymbolicLifetimeOperationKind
{
    Alias,
    CreateOwnedValue,
    CreateOwned,
    AcquireDisposable,
    BorrowShared,
    BorrowMutable,
    Escape,
    Return,
    Dispose,
    Release
}

internal readonly record struct SymbolicOperationOrigin(
    TextSpan SourceSpan,
    int Sequence,
    string Provenance);

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

internal sealed record SymbolicOperationSequence(
    ImmutableArray<SymbolicOperationDescriptor> Operations)
{
    internal static SymbolicOperationSequence Single(SymbolicOperationDescriptor operation)
    {
        return new SymbolicOperationSequence(ImmutableArray.Create(operation));
    }
}

internal sealed record SymbolicAssignmentOperation(
    ImmutableArray<SymbolicAssignmentBinding> Bindings,
    ImmutableArray<SymbolicCondition> Postconditions,
    SymbolicAssignmentOperationKind AssignmentKind,
    bool IsChecked,
    SymbolicOperationOrigin Origin,
    ImmutableArray<SymbolicTermPropagation> Propagations = default) : SymbolicOperationDescriptor(Origin);

internal sealed record SymbolicMutationOperation(
    ImmutableArray<SymbolicAssignmentBinding> Bindings,
    ImmutableArray<SymbolicInvalidationTarget> Invalidations,
    SymbolicTerm? Subject,
    SymbolicMutationOperationKind MutationKind,
    bool IsChecked,
    bool CallerVisible,
    ISymbol? Symbol,
    string? EvidenceKey,
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin);

internal sealed record SymbolicBranchAssumptionOperation(
    SymbolicCondition Condition,
    bool AssumeTrue,
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin);

internal sealed record SymbolicMergeOperation(
    ImmutableArray<SymbolicState> IncomingStates,
    SyntaxNode Source,
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin);

internal sealed record SymbolicLoopEdgeOperation(
    SymbolicLoopEdgeKind EdgeKind,
    SymbolicCondition? Condition,
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin);

internal sealed record SymbolicCompletionOperation(
    SymbolicCompletionKind CompletionKind,
    SymbolicTerm? Value,
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin);

internal sealed record SymbolicLifetimeOperation(
    SymbolicTerm Subject,
    SymbolicLifetimeOperationKind LifetimeKind,
    SymbolicTerm? RelatedSubject,
    SymbolicEscapeKind EscapeKind,
    ISymbol? Symbol,
    string? EvidenceKey,
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin);

internal sealed record SymbolicHazardOperation(
    SymbolicRuntimeHazardKind HazardKind,
    SymbolicExceptionPreconditionKind PreconditionKind,
    SymbolicTerm? Subject,
    SymbolicCondition Trigger,
    SymbolicFactConfidence Confidence,
    string ExceptionType,
    string Category,
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin)
{
    internal SymbolicFact ToPreconditionFact()
    {
        return new SymbolicFact(
            new SymbolicExceptionPreconditionAtom(PreconditionKind, Subject, Trigger),
            true,
            Confidence,
            Origin.Provenance,
            Origin.SourceSpan,
            null,
            Confidence == SymbolicFactConfidence.Unsupported ? Origin.Provenance : null);
    }
}
