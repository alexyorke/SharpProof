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

            return SymbolicOperationTransitionResult.Unsupported(
                state,
                SymbolicUnknownReason.UnsupportedIrEncoding,
                provenance);
        }

        return SymbolicOperationTransitionResult.Exact(state, provenance);
    }

    private static bool TryApplyAssignment(
        ref SymbolicState state,
        SymbolicAssignmentOperation assignment)
    {
        foreach (var binding in assignment.Bindings)
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
                assignment.Origin.Provenance + ".value",
                assignment.Origin.SourceSpan,
                null,
                assignment.Origin.Provenance + ".value")));
        }

        return true;
    }
}
