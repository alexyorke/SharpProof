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
    Return,
    Throw,
    Break,
    Continue
}

internal enum SymbolicLifetimeOperationKind
{
    CreateOwned,
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
    SymbolicTerm Target,
    SymbolicTerm? Source);

internal sealed record SymbolicAssignmentOperation(
    ImmutableArray<SymbolicAssignmentBinding> Bindings,
    SymbolicAssignmentOperationKind AssignmentKind,
    bool IsChecked,
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin);

internal sealed record SymbolicMutationOperation(
    ImmutableArray<SymbolicTerm> Targets,
    SymbolicMutationOperationKind MutationKind,
    bool CallerVisible,
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin);

internal sealed record SymbolicInvocationOperation(
    IMethodSymbol TargetMethod,
    SymbolicTerm? Receiver,
    ImmutableArray<SymbolicTerm> Arguments,
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin);

internal sealed record SymbolicBranchAssumptionOperation(
    SymbolicCondition Condition,
    bool AssumeTrue,
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin);

internal sealed record SymbolicMergeOperation(
    ImmutableArray<SymbolicState> IncomingStates,
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
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin);

internal sealed record SymbolicHazardOperation(
    SymbolicExceptionPreconditionKind PreconditionKind,
    SymbolicTerm? Subject,
    SymbolicCondition Trigger,
    string ExceptionType,
    string Category,
    SymbolicOperationOrigin Origin) : SymbolicOperationDescriptor(Origin);
