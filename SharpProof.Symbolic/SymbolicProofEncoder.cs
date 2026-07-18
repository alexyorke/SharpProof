using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Symbolic;

internal static class SymbolicProofEncoder
{
    private static readonly ExpressionSyntax s_syntheticProofNode = SyntaxFactory.IdentifierName("__symbolic_proof__");
    private static readonly SafeDivisorProofStrategy<SymbolicState> StateSafeDivisorStrategy = new(
        IsTermProvablyNonZero,
        AssumeStatePathCondition,
        true);

    internal static bool TryEncodeConditionWithPathState(
        SymbolicCondition condition,
        SymbolicState state,
        out SmtFormula formula) =>
        TryEncodeConditionWithPathState(condition, state, s_syntheticProofNode, true, out formula);

    internal static bool TryEncodeConditionWithPathState(
        SymbolicCondition condition,
        SymbolicState state,
        SyntaxNode sourceNode,
        out SmtFormula formula)
    {
        return TryEncodeConditionWithPathState(
            condition,
            state,
            sourceNode,
            true,
            out formula);
    }

    private static bool TryEncodeConditionWithPathState(
        SymbolicCondition condition,
        SymbolicState state,
        SyntaxNode sourceNode,
        bool rewriteQueryVersions,
        out SmtFormula formula)
    {
        if (condition == null) throw new ArgumentNullException(nameof(condition));

        if (state == null) throw new ArgumentNullException(nameof(state));

        if (sourceNode == null) throw new ArgumentNullException(nameof(sourceNode));

        state = SymbolicProofStateFacts.NormalizeState(state);
        if (rewriteQueryVersions) condition = SymbolicProofStateFacts.RewriteQueryConditionToCurrentVersions(condition, state);

        if (state.IsContradictory) return SymbolicIrFormulaEncoder.TryEncode(condition, out formula);

        if (!HasSafeIntegerDivisors(condition, state, sourceNode))
        {
            formula = null!;
            return false;
        }

        return SymbolicIrFormulaEncoder.TryEncode(condition, out formula);
    }


    internal static bool TryEncodeFactWithPathState(
        SymbolicFact fact,
        SymbolicState state,
        out SmtFormula formula) =>
        TryEncodeFactWithPathState(fact, state, s_syntheticProofNode, out formula);

    internal static bool TryEncodeFactWithPathState(
        SymbolicFact fact,
        SymbolicState state,
        SyntaxNode sourceNode,
        out SmtFormula formula)
    {
        if (fact == null) throw new ArgumentNullException(nameof(fact));

        return TryEncodeConditionWithPathState(
            new SymbolicFactCondition(fact),
            state,
            sourceNode,
            false,
            out formula);
    }


    private static bool HasSafeIntegerDivisors(
        SymbolicCondition condition,
        SymbolicState state,
        SyntaxNode sourceNode)
    {
        return HasSafeIntegerDivisorsCore(condition, state, sourceNode, StateSafeDivisorStrategy);
    }

    private static bool HasSafeIntegerDivisorsCore<TContext>(
        SymbolicTerm term,
        TContext context,
        SyntaxNode sourceNode,
        SafeDivisorProofStrategy<TContext> strategy)
    {
        switch (term)
        {
            case SymbolicConditionalTerm conditional:
                if (!HasSafeIntegerDivisorsCore(conditional.Condition, context, sourceNode, strategy)) return false;

                var whenTrue = strategy.AssumeCondition(context, conditional.Condition, true);
                if (!whenTrue.IsSupported ||
                    !whenTrue.IsContradictory &&
                    !HasSafeIntegerDivisorsCore(conditional.WhenTrue, whenTrue.Context, sourceNode, strategy))
                    return false;

                var whenFalse = strategy.AssumeCondition(context, conditional.Condition, false);
                return whenFalse.IsSupported &&
                       (whenFalse.IsContradictory ||
                        HasSafeIntegerDivisorsCore(conditional.WhenFalse, whenFalse.Context, sourceNode, strategy));
            case SymbolicBinaryTerm binary:
                return HasSafeIntegerDivisorsCore(binary.Left, context, sourceNode, strategy) &&
                       HasSafeIntegerDivisorsCore(binary.Right, context, sourceNode, strategy) &&
                       (binary.Operator is not (SymbolicBinaryTermOperator.Divide
                            or SymbolicBinaryTermOperator.Remainder) ||
                        strategy.IsTermProvablyNonZero(binary.Right, context, sourceNode));
            case SymbolicMemberTerm member:
                return HasSafeIntegerDivisorsCore(member.Receiver, context, sourceNode, strategy);
            case SymbolicElementTerm element:
                return HasSafeIntegerDivisorsCore(element.Receiver, context, sourceNode, strategy) &&
                       HasSafeIntegerDivisorsCore(element.Index, context, sourceNode, strategy);
            case SymbolicMultiElementTerm element:
                return HasSafeIntegerDivisorsCore(element.Receiver, context, sourceNode, strategy) &&
                       element.Indices.All(index =>
                           HasSafeIntegerDivisorsCore(index, context, sourceNode, strategy));
            case SymbolicFromEndIndexTerm fromEnd:
                return HasSafeIntegerDivisorsCore(fromEnd.Value, context, sourceNode, strategy);
            case SymbolicStringContentTerm stringContent:
                return HasSafeIntegerDivisorsCore(stringContent.Reference, context, sourceNode, strategy);
            case SymbolicStringConcatTerm stringConcat:
                return HasSafeIntegerDivisorsCore(stringConcat.Left, context, sourceNode, strategy) &&
                       HasSafeIntegerDivisorsCore(stringConcat.Right, context, sourceNode, strategy);
            case SymbolicLengthTerm length:
                return HasSafeIntegerDivisorsCore(length.Value, context, sourceNode, strategy);
            case SymbolicArrayDimensionLengthTerm arrayLength:
                return HasSafeIntegerDivisorsCore(arrayLength.Value, context, sourceNode, strategy);
            case SymbolicCountTerm count:
                return HasSafeIntegerDivisorsCore(count.Value, context, sourceNode, strategy);
            default:
                return true;
        }
    }

    private static bool HasSafeIntegerDivisorsCore<TContext>(
        SymbolicCondition condition,
        TContext context,
        SyntaxNode sourceNode,
        SafeDivisorProofStrategy<TContext> strategy)
    {
        switch (condition)
        {
            case SymbolicFactCondition factCondition:
                return HasSafeIntegerDivisorsCore(factCondition.Fact.Atom, context, sourceNode, strategy);
            case SymbolicNotCondition notCondition:
                return HasSafeIntegerDivisorsCore(notCondition.Operand, context, sourceNode, strategy);
            case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.And } andCondition
                when strategy.RefineShortCircuitConditions:
                return HasSafeIntegerDivisorsInShortCircuitRight(
                    andCondition.Left,
                    andCondition.Right,
                    context,
                    sourceNode,
                    strategy,
                    leftMustBeTrue: true);
            case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or } orCondition
                when strategy.RefineShortCircuitConditions:
                return HasSafeIntegerDivisorsInShortCircuitRight(
                    orCondition.Left,
                    orCondition.Right,
                    context,
                    sourceNode,
                    strategy,
                    leftMustBeTrue: false);
            case SymbolicBinaryCondition binaryCondition:
                return HasSafeIntegerDivisorsCore(binaryCondition.Left, context, sourceNode, strategy) &&
                       HasSafeIntegerDivisorsCore(binaryCondition.Right, context, sourceNode, strategy);
            default:
                return true;
        }
    }

    private static bool HasSafeIntegerDivisorsInShortCircuitRight<TContext>(
        SymbolicCondition left,
        SymbolicCondition right,
        TContext context,
        SyntaxNode sourceNode,
        SafeDivisorProofStrategy<TContext> strategy,
        bool leftMustBeTrue)
    {
        if (!HasSafeIntegerDivisorsCore(left, context, sourceNode, strategy)) return false;

        var rightContext = strategy.AssumeCondition(context, left, leftMustBeTrue);
        return rightContext.IsSupported &&
               (rightContext.IsContradictory ||
                HasSafeIntegerDivisorsCore(right, rightContext.Context, sourceNode, strategy));
    }

    private static bool HasSafeIntegerDivisorsCore<TContext>(
        SymbolicAtom atom,
        TContext context,
        SyntaxNode sourceNode,
        SafeDivisorProofStrategy<TContext> strategy)
    {
        switch (atom)
        {
            case SymbolicTruthAtom truth:
                return HasSafeIntegerDivisorsCore(truth.Condition, context, sourceNode, strategy);
            case SymbolicRelationAtom relation:
                return HasSafeIntegerDivisorsCore(relation.Left, context, sourceNode, strategy) &&
                       HasSafeIntegerDivisorsCore(relation.Right, context, sourceNode, strategy);
            case SymbolicStringPredicateAtom predicate:
                return HasSafeIntegerDivisorsCore(predicate.Value, context, sourceNode, strategy) &&
                       HasSafeIntegerDivisorsCore(predicate.Argument, context, sourceNode, strategy);
            case SymbolicBoundsAtom bounds:
                return HasSafeIntegerDivisorsCore(bounds.Index, context, sourceNode, strategy) &&
                       HasSafeIntegerDivisorsCore(bounds.Length, context, sourceNode, strategy);
            case SymbolicFreshnessAtom freshness:
                return HasSafeIntegerDivisorsCore(freshness.Value, context, sourceNode, strategy);
            case SymbolicOwnershipAtom ownership:
                return HasSafeIntegerDivisorsCore(ownership.Value, context, sourceNode, strategy);
            case SymbolicAliasAtom alias:
                return HasSafeIntegerDivisorsCore(alias.Source, context, sourceNode, strategy) &&
                       HasSafeIntegerDivisorsCore(alias.Target, context, sourceNode, strategy);
            case SymbolicBorrowAtom borrow:
                return HasSafeIntegerDivisorsCore(borrow.Owner, context, sourceNode, strategy) &&
                       HasSafeIntegerDivisorsCore(borrow.Borrow, context, sourceNode, strategy);
            case SymbolicEscapeAtom escape:
                return HasSafeIntegerDivisorsCore(escape.Value, context, sourceNode, strategy);
            case SymbolicReturnedOwnershipAtom returnedOwnership:
                return HasSafeIntegerDivisorsCore(returnedOwnership.Value, context, sourceNode, strategy);
            case SymbolicMutationAtom mutation:
                return HasSafeIntegerDivisorsCore(mutation.Target, context, sourceNode, strategy);
            case SymbolicDisposalAtom disposal:
                return HasSafeIntegerDivisorsCore(disposal.Resource, context, sourceNode, strategy);
            case SymbolicResourceLifetimeAtom lifetime:
                return HasSafeIntegerDivisorsCore(lifetime.Resource, context, sourceNode, strategy);
            case SymbolicTypeTestAtom typeTest:
                return HasSafeIntegerDivisorsCore(typeTest.Value, context, sourceNode, strategy);
            case SymbolicExceptionPreconditionAtom precondition:
                return (precondition.Subject == null ||
                        HasSafeIntegerDivisorsCore(precondition.Subject, context, sourceNode, strategy)) &&
                       HasSafeIntegerDivisorsCore(precondition.Trigger, context, sourceNode, strategy);
            default:
                return true;
        }
    }

    private static bool IsTermProvablyNonZero(
        SymbolicTerm term,
        SymbolicState state,
        SyntaxNode sourceNode)
    {
        if (term is SymbolicIntegerConstantTerm integerConstant) return integerConstant.Value != 0;

        var zero = new SymbolicIntegerConstantTerm(0);
        foreach (var relationOperator in new[]
                 {
                     SymbolicRelationOperator.NotEqual,
                     SymbolicRelationOperator.GreaterThan,
                     SymbolicRelationOperator.LessThan
                 })
        {
            var nonZeroFact = SymbolicFact.Exact(
                new SymbolicRelationAtom(relationOperator, term, zero),
                sourceNode,
                "ir.safe-divisor.non-zero");
            if (SymbolicProofStateFacts.StateContainsFact(state, nonZeroFact)) return true;
        }

        var zeroCondition = SymbolicIrLowerer.CreateIntegerZeroCondition(
            term,
            sourceNode,
            "ir.safe-divisor.zero");
        if (zeroCondition is SymbolicFactCondition factCondition)
        {
            if (SymbolicProofStateFacts.StateContradictsFact(state, factCondition.Fact)) return true;

            if (SymbolicProofStateFacts.StateContainsFact(state, factCondition.Fact)) return false;
        }

        if (SymbolicProofStateFacts.TryEvaluateConditionFromState(state, zeroCondition, out var value)) return !value;

        return SymbolicProofStateFacts.StateContradictsCondition(state, zeroCondition);
    }

    private static SymbolicState AssumePathCondition(SymbolicState state, SymbolicCondition condition)
    {
        return SymbolicProofStateFacts.NormalizeState(state.AddPathCondition(condition));
    }

    private static SafeDivisorAssumption<SymbolicState> AssumeStatePathCondition(
        SymbolicState state,
        SymbolicCondition condition,
        bool whenTrue)
    {
        var assumedState = AssumePathCondition(
            state,
            whenTrue ? condition : new SymbolicNotCondition(condition));
        return new SafeDivisorAssumption<SymbolicState>(true, assumedState.IsContradictory, assumedState);
    }

    private readonly record struct SafeDivisorAssumption<TContext>(
        bool IsSupported,
        bool IsContradictory,
        TContext Context);

    private sealed class SafeDivisorProofStrategy<TContext>(
        Func<SymbolicTerm, TContext, SyntaxNode, bool> isTermProvablyNonZero,
        Func<TContext, SymbolicCondition, bool, SafeDivisorAssumption<TContext>> assumeCondition,
        bool refineShortCircuitConditions)
    {
        public Func<SymbolicTerm, TContext, SyntaxNode, bool> IsTermProvablyNonZero { get; } = isTermProvablyNonZero;

        public Func<TContext, SymbolicCondition, bool, SafeDivisorAssumption<TContext>> AssumeCondition { get; } = assumeCondition;

        public bool RefineShortCircuitConditions { get; } = refineShortCircuitConditions;
    }

    internal static SymbolicEncodedState EncodeState(SymbolicState state)
    {
        var builder = ImmutableArray.CreateBuilder<SmtFormula>(
            state.Facts.Length + state.PathConditions.Length);
        var skippedUnsupported = false;

        foreach (var fact in state.Facts)
        {
            if (!TryEncodeFactWithPathState(fact, state, s_syntheticProofNode, out var formula))
            {
                skippedUnsupported = true;
                continue;
            }

            builder.Add(formula);
        }

        foreach (var condition in state.PathConditions)
        {
            if (!TryEncodeConditionWithPathState(
                    condition,
                    state,
                    s_syntheticProofNode,
                    false,
                    out var formula))
            {
                skippedUnsupported = true;
                continue;
            }

            builder.Add(formula);
        }

        if (skippedUnsupported && builder.Count == 0)
            return new SymbolicEncodedState(
                false,
                ImmutableArray<SmtFormula>.Empty,
                SymbolicUnknownReason.UnsupportedIrEncoding);

        return new SymbolicEncodedState(
            true,
            builder.ToImmutable(),
            SymbolicUnknownReason.None);
    }
}
