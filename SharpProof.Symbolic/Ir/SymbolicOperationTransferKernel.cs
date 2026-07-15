using System.Collections.Immutable;

using SharpProof.ProofCore.Smt;

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
            if (operation is SymbolicBranchAssumptionOperation branch)
            {
                state = state.AddPathCondition(branch.AssumeTrue
                    ? branch.Condition
                    : new SymbolicNotCondition(branch.Condition));
                continue;
            }
            if (operation is SymbolicLoopEdgeOperation loop)
            {
                if (loop.Condition != null) state = state.AddPathCondition(loop.Condition);
                continue;
            }
            if (operation is SymbolicCompletionOperation completion)
            {
                if (completion.CompletionKind != SymbolicCompletionKind.Normal)
                    state = state.MarkContradictory();
                continue;
            }
            if (operation is SymbolicMergeOperation merge)
            {
                state = SymbolicStateMerger.MergeCompletionStates(
                    merge.IncomingStates,
                    state,
                    merge.Source);
                continue;
            }

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
            null,
            SymbolicMutationOperationKind.Invalidate,
            IsChecked: false,
            CallerVisible: false,
            null,
            null,
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

    internal static SymbolicOperationTransitionResult TransitionLifetime(
        SymbolicState state,
        SymbolicTerm subject,
        SymbolicLifetimeOperationKind kind,
        Microsoft.CodeAnalysis.Text.TextSpan sourceSpan,
        string provenance,
        Microsoft.CodeAnalysis.ISymbol? symbol = null,
        string? evidenceKey = null,
        SymbolicTerm? relatedSubject = null,
        SymbolicEscapeKind escapeKind = SymbolicEscapeKind.Unknown)
    {
        return Apply(
            state,
            SymbolicOperationSequence.Single(new SymbolicLifetimeOperation(
                subject,
                kind,
                relatedSubject,
                escapeKind,
                symbol,
                evidenceKey,
                new SymbolicOperationOrigin(sourceSpan, 0, provenance))));
    }

    internal static SymbolicOperationTransitionResult TransitionMutation(
        SymbolicState state,
        SymbolicTerm subject,
        Microsoft.CodeAnalysis.Text.TextSpan sourceSpan,
        string provenance,
        Microsoft.CodeAnalysis.ISymbol? symbol = null,
        string? evidenceKey = null,
        bool callerVisible = true)
    {
        return Apply(
            state,
            SymbolicOperationSequence.Single(new SymbolicMutationOperation(
                ImmutableArray<SymbolicAssignmentBinding>.Empty,
                ImmutableArray<SymbolicInvalidationTarget>.Empty,
                subject,
                SymbolicMutationOperationKind.CallerVisible,
                IsChecked: false,
                callerVisible,
                symbol,
                evidenceKey,
                new SymbolicOperationOrigin(sourceSpan, 0, provenance))));
    }

    internal static SymbolicOperationTransitionResult Assume(
        SymbolicState state,
        SymbolicCondition condition,
        bool assumeTrue,
        Microsoft.CodeAnalysis.Text.TextSpan sourceSpan,
        string provenance)
    {
        return Apply(
            state,
            SymbolicOperationSequence.Single(new SymbolicBranchAssumptionOperation(
                condition,
                assumeTrue,
                new SymbolicOperationOrigin(sourceSpan, 0, provenance))));
    }

    internal static SymbolicOperationTransitionResult TransitionLoopEdge(
        SymbolicState state,
        SymbolicLoopEdgeKind kind,
        SymbolicCondition condition,
        Microsoft.CodeAnalysis.Text.TextSpan sourceSpan,
        string provenance) =>
        Apply(
            state,
            SymbolicOperationSequence.Single(new SymbolicLoopEdgeOperation(
                kind,
                condition,
                new SymbolicOperationOrigin(sourceSpan, 0, provenance))));

    internal static SymbolicOperationTransitionResult Complete(
        SymbolicState state,
        Microsoft.CodeAnalysis.Text.TextSpan sourceSpan) =>
        Apply(
            state,
            SymbolicOperationSequence.Single(new SymbolicCompletionOperation(
                SymbolicCompletionKind.NoFallthrough,
                null,
                new SymbolicOperationOrigin(sourceSpan, 0, "operation-transfer.no-fallthrough"))));

    internal static SymbolicOperationTransitionResult Merge(
        SymbolicState entryState,
        ImmutableArray<SymbolicState> incomingStates,
        Microsoft.CodeAnalysis.SyntaxNode source) =>
        Apply(
            entryState,
            SymbolicOperationSequence.Single(new SymbolicMergeOperation(
                incomingStates,
                source,
                new SymbolicOperationOrigin(source.Span, 0, "operation-transfer.completion-merge"))));

    private static bool TryApplyAssignment(
        ref SymbolicState state,
        SymbolicAssignmentOperation assignment)
    {
        if (!TryApplyBindings(ref state, assignment.Bindings, assignment.Origin)) return false;
        foreach (var postcondition in assignment.Postconditions)
            state = state.AddPathCondition(postcondition);
        if (!assignment.Propagations.IsDefaultOrEmpty)
            foreach (var propagation in assignment.Propagations)
                state = PropagateSourceFacts(state, propagation.Source, propagation.Target);
        foreach (var binding in assignment.Bindings)
            if (binding.DeriveIntegerBounds)
                AddDerivedIntegerBounds(ref state, binding, assignment.Origin);
        return true;
    }

    private static bool TryApplyMutation(
        ref SymbolicState state,
        SymbolicMutationOperation mutation)
    {
        foreach (var target in mutation.Invalidations)
        {
            state = target.MatchKind == SymbolicInvalidationMatchKind.VariablePrefix
                ? SymbolicIrReferenceScanner.RemoveVariableReferences(state, target.Key)
                : SymbolicIrReferenceScanner.RemoveVariableOrMemberReferences(state, target.Key);
            if (target.DefinitionVersion is { } definitionVersion)
                state = state.WithSymbolVersion(target.Key, definitionVersion);
        }
        if (!TryApplyBindings(ref state, mutation.Bindings, mutation.Origin)) return false;
        if (mutation.MutationKind != SymbolicMutationOperationKind.CallerVisible) return true;
        if (mutation.Subject == null) return false;

        state = state.AddFact(new SymbolicFact(
            new SymbolicMutationAtom(mutation.Subject, mutation.CallerVisible),
            true,
            SymbolicFactConfidence.Exact,
            mutation.Origin.Provenance,
            mutation.Origin.SourceSpan,
            mutation.Symbol,
            mutation.EvidenceKey));
        return true;
    }

    private static bool TryApplyLifetime(
        ref SymbolicState state,
        SymbolicLifetimeOperation lifetime)
    {
        if (lifetime.LifetimeKind is SymbolicLifetimeOperationKind.Return or
            SymbolicLifetimeOperationKind.Dispose)
            state = RemoveExclusiveLifetimeFacts(
                state,
                lifetime.Subject,
                lifetime.Symbol,
                removeDisposal: lifetime.LifetimeKind == SymbolicLifetimeOperationKind.Dispose);

        var atoms = lifetime.LifetimeKind switch
        {
            SymbolicLifetimeOperationKind.Alias when lifetime.RelatedSubject != null =>
                ImmutableArray.Create<(SymbolicAtom Atom, string Provenance)>(
                    (new SymbolicAliasAtom(lifetime.Subject, lifetime.RelatedSubject, true), lifetime.Origin.Provenance)),
            SymbolicLifetimeOperationKind.BorrowShared when lifetime.RelatedSubject != null =>
                ImmutableArray.Create<(SymbolicAtom, string)>(
                    (new SymbolicBorrowAtom(lifetime.Subject, lifetime.RelatedSubject, SymbolicBorrowKind.Shared), lifetime.Origin.Provenance)),
            SymbolicLifetimeOperationKind.BorrowMutable when lifetime.RelatedSubject != null =>
                ImmutableArray.Create<(SymbolicAtom, string)>(
                    (new SymbolicBorrowAtom(lifetime.Subject, lifetime.RelatedSubject, SymbolicBorrowKind.Mutable), lifetime.Origin.Provenance)),
            SymbolicLifetimeOperationKind.CreateOwnedValue => CreateOwnedAtoms(lifetime, includeLifetime: false),
            SymbolicLifetimeOperationKind.CreateOwned => CreateOwnedAtoms(lifetime, includeLifetime: true),
            SymbolicLifetimeOperationKind.AcquireDisposable => CreateOwnedAtoms(lifetime, includeLifetime: true).Add(
                (new SymbolicDisposalAtom(lifetime.Subject, SymbolicDisposalState.NotDisposed),
                    lifetime.Origin.Provenance + ".disposal")),
            SymbolicLifetimeOperationKind.Return => ImmutableArray.Create<(SymbolicAtom, string)>(
                (new SymbolicReturnedOwnershipAtom(lifetime.Subject), lifetime.Origin.Provenance),
                (new SymbolicResourceLifetimeAtom(lifetime.Subject, SymbolicResourceLifetimeState.Returned),
                    lifetime.Origin.Provenance + ".lifetime")),
            SymbolicLifetimeOperationKind.Dispose => ImmutableArray.Create<(SymbolicAtom, string)>(
                (new SymbolicDisposalAtom(lifetime.Subject, SymbolicDisposalState.Disposed), lifetime.Origin.Provenance),
                (new SymbolicResourceLifetimeAtom(lifetime.Subject, SymbolicResourceLifetimeState.Released),
                    lifetime.Origin.Provenance + ".lifetime")),
            SymbolicLifetimeOperationKind.Release => ImmutableArray.Create<(SymbolicAtom, string)>(
                (new SymbolicResourceLifetimeAtom(lifetime.Subject, SymbolicResourceLifetimeState.Released),
                    lifetime.Origin.Provenance)),
            SymbolicLifetimeOperationKind.Escape => ImmutableArray.Create<(SymbolicAtom, string)>(
                (new SymbolicEscapeAtom(lifetime.Subject, lifetime.EscapeKind), lifetime.Origin.Provenance)),
            _ => ImmutableArray<(SymbolicAtom, string)>.Empty
        };
        if (atoms.IsDefaultOrEmpty) return false;

        foreach (var (atom, provenance) in atoms)
            state = state.AddFact(new SymbolicFact(
                atom,
                true,
                SymbolicFactConfidence.Exact,
                provenance,
                lifetime.Origin.SourceSpan,
                lifetime.Symbol,
                lifetime.EvidenceKey));
        return true;
    }

    private static SymbolicState RemoveExclusiveLifetimeFacts(
        SymbolicState state,
        SymbolicTerm resource,
        Microsoft.CodeAnalysis.ISymbol? symbol,
        bool removeDisposal)
    {
        var facts = state.Facts.Where(fact => fact.Atom switch
        {
            SymbolicDisposalAtom disposal when removeDisposal =>
                !Equals(disposal.Resource, resource) && !MatchesSymbol(fact.Symbol, symbol),
            SymbolicResourceLifetimeAtom lifetime =>
                !Equals(lifetime.Resource, resource) && !MatchesSymbol(fact.Symbol, symbol),
            _ => true
        });
        return new SymbolicState(facts, state.PathConditions, state.SymbolVersions);
    }

    private static bool MatchesSymbol(
        Microsoft.CodeAnalysis.ISymbol? left,
        Microsoft.CodeAnalysis.ISymbol? right)
    {
        return right != null && Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(left, right);
    }

    private static ImmutableArray<(SymbolicAtom Atom, string Provenance)> CreateOwnedAtoms(
        SymbolicLifetimeOperation lifetime,
        bool includeLifetime)
    {
        var builder = ImmutableArray.CreateBuilder<(SymbolicAtom, string)>(includeLifetime ? 3 : 2);
        builder.Add((new SymbolicFreshnessAtom(lifetime.Subject), lifetime.Origin.Provenance + ".fresh"));
        builder.Add((new SymbolicOwnershipAtom(lifetime.Subject, false), lifetime.Origin.Provenance + ".owned"));
        if (includeLifetime)
            builder.Add((new SymbolicResourceLifetimeAtom(
                lifetime.Subject,
                SymbolicResourceLifetimeState.Owned), lifetime.Origin.Provenance + ".lifetime"));
        return builder.MoveToImmutable();
    }

    internal static SymbolicState PropagateSourceFacts(
        SymbolicState state,
        SymbolicTerm source,
        SymbolicTerm target)
    {
        if (!SymbolicStateFactBuilder.CanCompareIrTerms(source, target) ||
            string.Equals(
                SymbolicState.CreateProofTermKey(source),
                SymbolicState.CreateProofTermKey(target),
                StringComparison.Ordinal))
            return state;

        var facts = state.Facts;
        var conditions = state.PathConditions;
        foreach (var fact in facts)
        {
            var substituted = SymbolicIrSubstitution.ReplaceTerm(fact, source, target);
            if (!string.Equals(
                    SymbolicState.CreateProofFactKey(substituted),
                    SymbolicState.CreateProofFactKey(fact),
                    StringComparison.Ordinal))
                state = state.AddFact(substituted);
        }

        foreach (var condition in conditions)
        {
            var substituted = SymbolicIrSubstitution.ReplaceTerm(condition, source, target);
            if (!string.Equals(
                    SymbolicState.CreateProofConditionKey(substituted),
                    SymbolicState.CreateProofConditionKey(condition),
                    StringComparison.Ordinal))
                state = state.AddPathCondition(substituted);
        }

        return state;
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

            if (binding.InvalidateTarget)
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
            if (binding.PropagateSourceFacts)
                state = PropagateSourceFacts(state, binding.Source, binding.Target);
        }

        return true;
    }

    private static void AddDerivedIntegerBounds(
        ref SymbolicState state,
        SymbolicAssignmentBinding binding,
        SymbolicOperationOrigin origin)
    {
        if (binding.Target.Kind != SmtValueKind.Int || binding.Source is not { Kind: SmtValueKind.Int } source)
            return;

        if (StateProvesIntegerBound(state, source, strictlyPositive: true))
            AddIntegerBound(
                ref state,
                binding.Target,
                SymbolicRelationOperator.GreaterThan,
                new SymbolicIntegerConstantTerm(0),
                origin,
                ".assigned-integer.positive");
        else if (StateProvesIntegerBound(state, source, strictlyPositive: false))
            AddIntegerBound(
                ref state,
                binding.Target,
                SymbolicRelationOperator.GreaterThanOrEqual,
                new SymbolicIntegerConstantTerm(0),
                origin,
                ".assigned-integer.non-negative");

        if (source is not SymbolicBinaryTerm
            {
                Operator: SymbolicBinaryTermOperator.Remainder,
                Left: { Kind: SmtValueKind.Int } dividend,
                Right: { Kind: SmtValueKind.Int } divisor
            } ||
            !StateProvesIntegerBound(state, dividend, strictlyPositive: false) ||
            !StateProvesIntegerBound(state, divisor, strictlyPositive: true))
            return;

        AddIntegerBound(
            ref state,
            binding.Target,
            SymbolicRelationOperator.GreaterThanOrEqual,
            new SymbolicIntegerConstantTerm(0),
            origin,
            ".assigned-remainder.non-negative");
        AddIntegerBound(
            ref state,
            binding.Target,
            SymbolicRelationOperator.LessThan,
            divisor,
            origin,
            ".assigned-remainder.upper-bound");
    }

    private static bool StateProvesIntegerBound(
        SymbolicState state,
        SymbolicTerm term,
        bool strictlyPositive)
    {
        return state.PathConditions.Any(condition => ConditionProvesIntegerBound(condition, term, strictlyPositive)) ||
               state.Facts.Any(fact => FactProvesIntegerBound(fact, term, strictlyPositive));
    }

    private static bool ConditionProvesIntegerBound(
        SymbolicCondition condition,
        SymbolicTerm term,
        bool strictlyPositive)
    {
        return condition switch
        {
            SymbolicFactCondition fact => FactProvesIntegerBound(fact.Fact, term, strictlyPositive),
            SymbolicBinaryCondition { Operator: SymbolicConditionOperator.And } binary =>
                ConditionProvesIntegerBound(binary.Left, term, strictlyPositive) ||
                ConditionProvesIntegerBound(binary.Right, term, strictlyPositive),
            SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or } binary =>
                ConditionProvesIntegerBound(binary.Left, term, strictlyPositive) &&
                ConditionProvesIntegerBound(binary.Right, term, strictlyPositive),
            _ => false
        };
    }

    private static bool FactProvesIntegerBound(
        SymbolicFact fact,
        SymbolicTerm term,
        bool strictlyPositive)
    {
        if (!fact.Polarity || fact.Atom is not SymbolicRelationAtom relation) return false;
        return Equals(relation.Left, term) && relation.Right is SymbolicIntegerConstantTerm right
            ? RelationProvesIntegerBound(relation.Operator, right.Value, strictlyPositive, termOnLeft: true)
            : Equals(relation.Right, term) && relation.Left is SymbolicIntegerConstantTerm left &&
              RelationProvesIntegerBound(relation.Operator, left.Value, strictlyPositive, termOnLeft: false);
    }

    private static bool RelationProvesIntegerBound(
        SymbolicRelationOperator relation,
        long constant,
        bool strictlyPositive,
        bool termOnLeft)
    {
        return (termOnLeft, relation) switch
        {
            (true, SymbolicRelationOperator.GreaterThan) => strictlyPositive ? constant >= 0 : constant >= -1,
            (true, SymbolicRelationOperator.GreaterThanOrEqual) => strictlyPositive ? constant > 0 : constant >= 0,
            (true, SymbolicRelationOperator.Equal) => strictlyPositive ? constant > 0 : constant >= 0,
            (false, SymbolicRelationOperator.LessThan) => strictlyPositive ? constant <= 0 : constant <= -1,
            (false, SymbolicRelationOperator.LessThanOrEqual) => strictlyPositive ? constant < 0 : constant <= 0,
            (false, SymbolicRelationOperator.Equal) => strictlyPositive ? constant > 0 : constant >= 0,
            _ => false
        };
    }

    private static void AddIntegerBound(
        ref SymbolicState state,
        SymbolicTerm left,
        SymbolicRelationOperator relation,
        SymbolicTerm right,
        SymbolicOperationOrigin origin,
        string provenanceSuffix)
    {
        state = state.AddPathCondition(new SymbolicFactCondition(new SymbolicFact(
            new SymbolicRelationAtom(relation, left, right),
            true,
            SymbolicFactConfidence.Exact,
            origin.Provenance + provenanceSuffix,
            origin.SourceSpan,
            null,
            null)));
    }
}
