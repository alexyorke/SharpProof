using System.Collections.Immutable;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicOperationTransferKernel
{
    internal static SymbolicOperationTransitionResult Apply(
        SymbolicState initialState,
        SymbolicOperationSequence sequence)
    {
        if (initialState == null) throw new ArgumentNullException(nameof(initialState));
        if (sequence == null) throw new ArgumentNullException(nameof(sequence));

        var state = initialState;
        var provenance = ImmutableArray.CreateBuilder<SymbolicLoweringProvenance>(sequence.Operations.Length);
        var previousSequence = -1;
        foreach (var operation in sequence.Operations)
        {
            provenance.Add(new SymbolicLoweringProvenance(
                "operation-transfer",
                operation.Origin.SourceSpan,
                operation.Origin.Provenance));
            if (operation.Origin.Sequence <= previousSequence)
                return SymbolicOperationTransitionResult.Unsupported(
                    state,
                    SymbolicUnknownReason.UnsupportedIrEncoding,
                    provenance);

            previousSequence = operation.Origin.Sequence;
            if (operation is SymbolicAssignmentOperation assignment &&
                TryApplyAssignment(ref state, assignment))
                continue;
            if (operation is SymbolicMutationOperation mutation &&
                TryApplyMutation(ref state, mutation))
                continue;
            if (operation is SymbolicLifetimeOperation lifetime &&
                TryApplyLifetime(ref state, lifetime))
                continue;

            return SymbolicOperationTransitionResult.Unsupported(
                state,
                SymbolicUnknownReason.UnsupportedIrEncoding,
                provenance);
        }

        return SymbolicOperationTransitionResult.Exact(state, provenance);
    }

    internal static SymbolicOperationTransitionResult Invalidate(
        SymbolicState state,
        ImmutableArray<SymbolicInvalidationTarget> targets,
        Microsoft.CodeAnalysis.Text.TextSpan sourceSpan,
        string provenance)
    {
        var operation = new SymbolicMutationOperation(
            ImmutableArray<SymbolicAssignmentBinding>.Empty,
            targets,
            SymbolicMutationOperationKind.Invalidate,
            IsChecked: false,
            CallerVisible: false,
            new SymbolicOperationOrigin(sourceSpan, 0, provenance));
        return Apply(state, SymbolicOperationSequence.Single(operation));
    }

    internal static int GetDefinitionVersion(Microsoft.CodeAnalysis.Text.TextSpan sourceSpan)
    {
        var spanStart = Math.Max(0, sourceSpan.Start);
        // Definition versions are even; CFG join (phi) versions are odd. Using the
        // syntax position keeps reprocessing the same block idempotent.
        return spanStart <= (int.MaxValue - 2) / 2
            ? (spanStart + 1) * 2
            : 2 + spanStart % ((int.MaxValue - 2) / 2) * 2;
    }

    private static bool TryApplyAssignment(
        ref SymbolicState state,
        SymbolicAssignmentOperation assignment)
    {
        if (!TryApplyBindings(ref state, assignment.Bindings, assignment.Origin)) return false;
        foreach (var postcondition in assignment.Postconditions)
            state = state.AddPathCondition(postcondition);
        return true;
    }

    private static bool TryApplyMutation(
        ref SymbolicState state,
        SymbolicMutationOperation mutation)
    {
        foreach (var target in mutation.Invalidations)
            state = target.MatchKind == SymbolicInvalidationMatchKind.VariablePrefix
                ? SymbolicIrReferenceScanner.RemoveVariableReferences(state, target.Key)
                : SymbolicIrReferenceScanner.RemoveVariableOrMemberReferences(state, target.Key);
        return TryApplyBindings(ref state, mutation.Bindings, mutation.Origin);
    }

    private static bool TryApplyLifetime(
        ref SymbolicState state,
        SymbolicLifetimeOperation lifetime)
    {
        SymbolicAtom? atom = lifetime.LifetimeKind switch
        {
            SymbolicLifetimeOperationKind.Alias when lifetime.RelatedSubject != null =>
                new SymbolicAliasAtom(lifetime.Subject, lifetime.RelatedSubject, true),
            SymbolicLifetimeOperationKind.BorrowShared when lifetime.RelatedSubject != null =>
                new SymbolicBorrowAtom(lifetime.Subject, lifetime.RelatedSubject, SymbolicBorrowKind.Shared),
            SymbolicLifetimeOperationKind.BorrowMutable when lifetime.RelatedSubject != null =>
                new SymbolicBorrowAtom(lifetime.Subject, lifetime.RelatedSubject, SymbolicBorrowKind.Mutable),
            _ => null
        };
        if (atom == null) return false;

        state = state.AddFact(new SymbolicFact(
            atom,
            true,
            SymbolicFactConfidence.Exact,
            lifetime.Origin.Provenance,
            lifetime.Origin.SourceSpan,
            lifetime.Symbol,
            lifetime.EvidenceKey));
        return true;
    }

    private static bool TryApplyBindings(
        ref SymbolicState state,
        ImmutableArray<SymbolicAssignmentBinding> bindings,
        SymbolicOperationOrigin origin)
    {
        foreach (var binding in bindings)
        {
            if (binding.Source == null ||
                !SymbolicStateFactBuilder.CanCompareIrTerms(binding.Target, binding.Source))
                return false;

            state = SymbolicStateValueFacts.RemoveReferences(state, binding.TargetKey);
            state = state.AddPathCondition(new SymbolicFactCondition(new SymbolicFact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    binding.Target,
                    binding.Source),
                true,
                SymbolicFactConfidence.Exact,
                binding.Provenance ?? origin.Provenance + ".value",
                origin.SourceSpan,
                null,
                binding.EvidenceKey)));
        }

        return true;
    }
}
