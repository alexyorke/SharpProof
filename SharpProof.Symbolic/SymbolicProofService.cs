using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Purity;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Symbolic;

internal sealed class SymbolicProofService
{
    private const string ContradictoryStateReason = "path_unsatisfiable";
    private const int PerServiceProofCacheEntryLimit = 2048;
    private const int ProcessFallbackProofCacheEntryLimit = 4096;
    private static readonly ConditionalWeakTable<SmtAnalysisService, ProofResultCache> s_serviceCaches = new();
    private static readonly ProofResultCache s_fallbackCache = new(ProcessFallbackProofCacheEntryLimit);
    private static readonly ExpressionSyntax s_syntheticProofNode = SyntaxFactory.IdentifierName("__symbolic_proof__");
    private static readonly SafeDivisorProofStrategy<FormulaSafeDivisorContext> FormulaSafeDivisorStrategy = new(
        IsTermProvablyNonZero,
        AssumeFormulaPathCondition,
        false);
    private static readonly SafeDivisorProofStrategy<SymbolicState> StateSafeDivisorStrategy = new(
        IsTermProvablyNonZero,
        AssumeStatePathCondition,
        true);
    private readonly SymbolicProofPipeline proofPipeline;
    private readonly SmtAnalysisService? smtAnalysis;

    public SymbolicProofService(SmtAnalysisService? smtAnalysis)
    {
        this.smtAnalysis = smtAnalysis;
        proofPipeline = new SymbolicProofPipeline(smtAnalysis);
    }

    public SymbolicIrProofResult ClassifyReachability(SymbolicState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        state = NormalizeState(state);
        if (state.IsContradictory)
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.Unreachable,
                ContradictoryStateReason);

        if (state.Facts.Length == 0 && state.PathConditions.Length == 0)
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.Reachable,
                "ir_state_empty");

        return ClassifyWithIrCache(
            "reachability:" + state.NormalizedProofKey,
            () =>
            {
                if (!TryEncodeState(state, out var pathConditions, out var unknownReason))
                    return SymbolicIrProofResult.Unknown(unknownReason);

                return proofPipeline.ClassifyReachability(
                    pathConditions,
                    CreateBudgetInfo,
                    SymbolicProofSupport.Exact);
            });
    }

    internal bool TryEncode(SymbolicState state, out ImmutableArray<SmtFormula> pathConditions)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        state = NormalizeState(state);
        return TryEncodeState(state, out pathConditions, out _);
    }

    internal static bool TryEncodeStatePathConditions(SymbolicState state, out ImmutableArray<SmtFormula> pathConditions)
    {
        return new SymbolicProofService(smtAnalysis: null).TryEncode(state, out pathConditions);
    }

    internal static bool TryEncodeTermWithPathState(
        SymbolicTerm term,
        SymbolicState state,
        SyntaxNode sourceNode,
        out SmtFormula formula)
    {
        if (term == null) throw new ArgumentNullException(nameof(term));

        if (state == null) throw new ArgumentNullException(nameof(state));

        if (sourceNode == null) throw new ArgumentNullException(nameof(sourceNode));

        state = NormalizeState(state);
        term = RewriteQueryTermToCurrentVersions(term, state);
        if (state.IsContradictory) return SymbolicIrFormulaEncoder.TryEncodeTerm(term, out formula);

        if (!HasSafeIntegerDivisors(term, state, sourceNode))
        {
            formula = null!;
            return false;
        }

        return SymbolicIrFormulaEncoder.TryEncodeTerm(term, out formula);
    }

    internal static bool TryEncodeTermWithFormulaPathConditions(
        SymbolicTerm term,
        IEnumerable<SmtFormula> pathConditions,
        SyntaxNode sourceNode,
        out SmtFormula formula)
    {
        if (term == null) throw new ArgumentNullException(nameof(term));
        if (pathConditions == null) throw new ArgumentNullException(nameof(pathConditions));
        if (sourceNode == null) throw new ArgumentNullException(nameof(sourceNode));

        var normalizedPath = pathConditions as IReadOnlyCollection<SmtFormula> ?? pathConditions.ToArray();
        var proofPipeline = new SymbolicProofPipeline(smtAnalysis: null);
        if (!HasSafeIntegerDivisors(term, normalizedPath, sourceNode, proofPipeline))
        {
            formula = null!;
            return false;
        }

        return SymbolicIrFormulaEncoder.TryEncodeTerm(term, out formula);
    }

    private static bool HasSafeIntegerDivisors(
        SymbolicTerm term,
        IReadOnlyCollection<SmtFormula> pathConditions,
        SyntaxNode sourceNode,
        SymbolicProofPipeline proofPipeline)
    {
        var context = new FormulaSafeDivisorContext(pathConditions, proofPipeline);
        return HasSafeIntegerDivisorsCore(term, context, sourceNode, FormulaSafeDivisorStrategy);
    }

    private static bool IsTermProvablyNonZero(
        SymbolicTerm term,
        IReadOnlyCollection<SmtFormula> pathConditions,
        SyntaxNode sourceNode,
        SymbolicProofPipeline proofPipeline)
    {
        if (term is SymbolicIntegerConstantTerm integerConstant) return integerConstant.Value != 0;

        var nonZeroCondition = new SymbolicNotCondition(SymbolicIrLowerer.CreateIntegerZeroCondition(
            term,
            sourceNode,
            "ir.safe-divisor.formula-path.non-zero"));
        return SymbolicIrFormulaEncoder.TryEncode(nonZeroCondition, out var nonZeroFormula) &&
               proofPipeline.ClassifyRawImplication(pathConditions, nonZeroFormula).Outcome ==
               PurityProofOutcome.ProvablyPure;
    }

    private static bool IsTermProvablyNonZero(
        SymbolicTerm term,
        FormulaSafeDivisorContext context,
        SyntaxNode sourceNode)
    {
        return IsTermProvablyNonZero(term, context.PathConditions, sourceNode, context.ProofPipeline);
    }

    private static SafeDivisorAssumption<FormulaSafeDivisorContext> AssumeFormulaPathCondition(
        FormulaSafeDivisorContext context,
        SymbolicCondition condition,
        bool whenTrue)
    {
        var assumedCondition = whenTrue ? condition : new SymbolicNotCondition(condition);
        if (!SymbolicIrFormulaEncoder.TryEncode(assumedCondition, out var formula))
            return new SafeDivisorAssumption<FormulaSafeDivisorContext>(false, false, context);

        return new SafeDivisorAssumption<FormulaSafeDivisorContext>(
            true,
            false,
            new FormulaSafeDivisorContext(
                context.PathConditions.Append(formula).ToArray(),
                context.ProofPipeline));
    }

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

        state = NormalizeState(state);
        if (rewriteQueryVersions) condition = RewriteQueryConditionToCurrentVersions(condition, state);

        if (state.IsContradictory) return SymbolicIrFormulaEncoder.TryEncode(condition, out formula);

        if (!HasSafeIntegerDivisors(condition, state, sourceNode))
        {
            formula = null!;
            return false;
        }

        return SymbolicIrFormulaEncoder.TryEncode(condition, out formula);
    }

    internal static bool TryEncodeConditionWithPathState(
        SymbolicCondition condition,
        SymbolicState state,
        out SmtFormula formula)
    {
        return TryEncodeConditionWithPathState(
            condition,
            state,
            s_syntheticProofNode,
            out formula);
    }

    private static bool TryEncodeFactWithPathState(
        SymbolicFact fact,
        SymbolicState state,
        SyntaxNode sourceNode,
        out SmtFormula formula)
    {
        if (fact == null) throw new ArgumentNullException(nameof(fact));

        if (state == null) throw new ArgumentNullException(nameof(state));

        if (sourceNode == null) throw new ArgumentNullException(nameof(sourceNode));

        state = NormalizeState(state);
        if (state.IsContradictory) return SymbolicIrFormulaEncoder.TryEncode(fact, out formula);

        if (!HasSafeIntegerDivisors(fact, state, sourceNode))
        {
            formula = null!;
            return false;
        }

        return SymbolicIrFormulaEncoder.TryEncode(fact, out formula);
    }

    private static bool HasSafeIntegerDivisors(
        SymbolicTerm term,
        SymbolicState state,
        SyntaxNode sourceNode)
    {
        return HasSafeIntegerDivisorsCore(term, state, sourceNode, StateSafeDivisorStrategy);
    }

    private static bool HasSafeIntegerDivisors(
        SymbolicCondition condition,
        SymbolicState state,
        SyntaxNode sourceNode)
    {
        return HasSafeIntegerDivisorsCore(condition, state, sourceNode, StateSafeDivisorStrategy);
    }

    private static bool HasSafeIntegerDivisors(
        SymbolicFact fact,
        SymbolicState state,
        SyntaxNode sourceNode)
    {
        return HasSafeIntegerDivisorsCore(fact.Atom, state, sourceNode, StateSafeDivisorStrategy);
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
            if (StateContainsFact(state, nonZeroFact)) return true;
        }

        var zeroCondition = SymbolicIrLowerer.CreateIntegerZeroCondition(
            term,
            sourceNode,
            "ir.safe-divisor.zero");
        if (zeroCondition is SymbolicFactCondition factCondition)
        {
            if (StateContradictsFact(state, factCondition.Fact)) return true;

            if (StateContainsFact(state, factCondition.Fact)) return false;
        }

        if (TryEvaluateConditionFromState(state, zeroCondition, out var value)) return !value;

        return StateContradictsCondition(state, zeroCondition);
    }

    private static SymbolicState AssumePathCondition(SymbolicState state, SymbolicCondition condition)
    {
        return NormalizeState(state.AddPathCondition(condition));
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

    private readonly record struct FormulaSafeDivisorContext(
        IReadOnlyCollection<SmtFormula> PathConditions,
        SymbolicProofPipeline ProofPipeline);

    private readonly record struct SafeDivisorAssumption<TContext>(
        bool IsSupported,
        bool IsContradictory,
        TContext Context);

    private sealed class SafeDivisorProofStrategy<TContext>
    {
        public SafeDivisorProofStrategy(
            Func<SymbolicTerm, TContext, SyntaxNode, bool> isTermProvablyNonZero,
            Func<TContext, SymbolicCondition, bool, SafeDivisorAssumption<TContext>> assumeCondition,
            bool refineShortCircuitConditions)
        {
            IsTermProvablyNonZero = isTermProvablyNonZero;
            AssumeCondition = assumeCondition;
            RefineShortCircuitConditions = refineShortCircuitConditions;
        }

        public Func<SymbolicTerm, TContext, SyntaxNode, bool> IsTermProvablyNonZero { get; }

        public Func<TContext, SymbolicCondition, bool, SafeDivisorAssumption<TContext>> AssumeCondition { get; }

        public bool RefineShortCircuitConditions { get; }
    }

    public SymbolicIrProofResult ClassifyImplication(SymbolicState state, SymbolicFact fact)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        if (fact == null) throw new ArgumentNullException(nameof(fact));

        state = NormalizeState(state);
        fact = RewriteQueryFactToCurrentVersions(fact, state);
        if (state.IsContradictory)
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.ProvenTrue,
                ContradictoryStateReason);

        if (SymbolicState.TryEvaluateProofFact(fact, out var factValue))
            return factValue
                ? SymbolicIrProofResult.Syntactic(
                    SymbolicProofStatus.ProvenTrue,
                    "ir_target_fact_syntactic_true")
                : ClassifySyntacticallyFalseImplication(state, "ir_target_fact_syntactic_false");

        if (StateContainsFact(state, fact))
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.ProvenTrue,
                "ir_state_contains_fact");

        if (StateContradictsFact(state, fact))
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.ProvenFalse,
                "ir_state_contradicts_fact");

        return ClassifyWithIrCache(
            "implication-fact:" + state.NormalizedProofKey + "\n" + SymbolicState.CreateProofFactKey(fact),
            () =>
            {
                if (!TryEncodeState(state, out var pathConditions, out var unknownReason))
                    return SymbolicIrProofResult.Unknown(unknownReason);

                if (!TryEncodeFactWithPathState(fact, state, s_syntheticProofNode, out var factFormula))
                    return SymbolicIrProofResult.Unknown(SymbolicUnknownReason.UnsupportedIrEncoding);

                return proofPipeline.ClassifyImplication(
                    pathConditions,
                    factFormula,
                    CreateBudgetInfo,
                    SymbolicProofSupport.Exact);
            });
    }

    public SymbolicIrProofResult ClassifyImplication(SymbolicState state, SymbolicCondition condition)
    {
        if (TryClassifyConditionPreliminarily(
                state,
                condition,
                ConditionClassificationMode.Implication,
                out state,
                out condition,
                out var preliminaryResult))
            return preliminaryResult;

        return ClassifyWithIrCache(
            "implication-condition:" + state.NormalizedProofKey + "\n" +
            SymbolicState.CreateProofConditionKey(condition),
            () =>
            {
                if (!TryEncodeState(state, out var pathConditions, out var unknownReason))
                    return SymbolicIrProofResult.Unknown(unknownReason);

                if (!TryEncodeConditionWithPathState(condition, state, s_syntheticProofNode, out var formula))
                    return SymbolicIrProofResult.Unknown(SymbolicUnknownReason.UnsupportedIrEncoding);

                return proofPipeline.ClassifyImplication(
                    pathConditions,
                    formula,
                    CreateBudgetInfo,
                    SymbolicProofSupport.Exact);
            });
    }

    public SymbolicIrProofResult ClassifyBranchFeasibility(SymbolicState state, SymbolicCondition branchCondition)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        if (branchCondition == null) throw new ArgumentNullException(nameof(branchCondition));

        state = NormalizeState(state);
        branchCondition = RewriteQueryConditionToCurrentVersions(branchCondition, state);
        if (state.IsContradictory)
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.Unreachable,
                ContradictoryStateReason);

        if (TryClassifySyntacticConditionTruth(branchCondition, out var syntacticStatus))
            return syntacticStatus == SymbolicProofStatus.ProvenTrue
                ? ClassifyReachability(state)
                : SymbolicIrProofResult.Syntactic(
                    SymbolicProofStatus.Unreachable,
                    "ir_branch_syntactic_false");

        if (StateContainsCondition(state, branchCondition)) return ClassifyReachability(state);

        if (StateContradictsCondition(state, branchCondition))
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.Unreachable,
                "ir_state_contradicts_branch");

        if (!TryEncodeConditionWithPathState(branchCondition, state, s_syntheticProofNode, out _))
            return SymbolicIrProofResult.Unknown(SymbolicUnknownReason.UnsupportedIrEncoding);

        return ClassifyReachability(state.AddPathCondition(branchCondition));
    }

    public SymbolicIrProofResult ClassifyConditionTruth(SymbolicState state, SymbolicCondition condition)
    {
        if (TryClassifyConditionPreliminarily(
                state,
                condition,
                ConditionClassificationMode.Truth,
                out state,
                out condition,
                out var preliminaryResult))
            return preliminaryResult;

        var reachability = ClassifyReachability(state);
        if (reachability.Info.Status == SymbolicProofStatus.Unreachable) return reachability;

        return ClassifyWithIrCache(
            "condition-truth:" + state.NormalizedProofKey + "\n" + SymbolicState.CreateProofConditionKey(condition),
            () =>
            {
                var trueBranch = ClassifyBranchFeasibility(state, condition);
                if (trueBranch.Info.Status == SymbolicProofStatus.Unreachable)
                    return trueBranch.RawResult != null
                        ? SymbolicIrProofResult.FromConditionTruth(
                            trueBranch.RawResult,
                            SymbolicProofStatus.ProvenFalse,
                            CreateBudgetInfo())
                        : SymbolicIrProofResult.Syntactic(
                            SymbolicProofStatus.ProvenFalse,
                            trueBranch.Info.Reason);
                if (trueBranch.Info.Status == SymbolicProofStatus.Unknown)
                    return trueBranch.WithStatus(
                        SymbolicProofStatus.Unknown,
                        "ir_condition_true_branch_feasibility_unknown");

                var falseBranch = ClassifyBranchFeasibility(state, new SymbolicNotCondition(condition));
                if (falseBranch.Info.Status == SymbolicProofStatus.Unreachable)
                    return falseBranch.RawResult != null
                        ? SymbolicIrProofResult.FromConditionTruth(
                            falseBranch.RawResult,
                            SymbolicProofStatus.ProvenTrue,
                            CreateBudgetInfo())
                        : SymbolicIrProofResult.Syntactic(
                            SymbolicProofStatus.ProvenTrue,
                            falseBranch.Info.Reason);
                if (falseBranch.Info.Status == SymbolicProofStatus.Unknown)
                    return falseBranch.WithStatus(
                        SymbolicProofStatus.Unknown,
                        "ir_condition_false_branch_feasibility_unknown");

                return falseBranch.WithStatus(
                    SymbolicProofStatus.Unknown,
                    "ir_condition_both_branches_feasible");
            });
    }

    private SymbolicIrProofResult ClassifySyntacticallyFalseImplication(
        SymbolicState state,
        string reachableReason)
    {
        var reachability = ClassifyReachability(state);
        return reachability.Info.Status switch
        {
            SymbolicProofStatus.Unreachable => reachability.WithStatus(
                SymbolicProofStatus.ProvenTrue,
                reachability.Info.Reason),
            SymbolicProofStatus.Reachable => reachability.WithStatus(
                SymbolicProofStatus.ProvenFalse,
                reachableReason),
            _ => reachability.WithStatus(
                SymbolicProofStatus.Unknown,
                "ir_false_implication_state_reachability_unknown")
        };
    }

    private bool TryClassifyConditionPreliminarily(
        SymbolicState state,
        SymbolicCondition condition,
        ConditionClassificationMode mode,
        out SymbolicState normalizedState,
        out SymbolicCondition rewrittenCondition,
        out SymbolicIrProofResult result)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (condition == null) throw new ArgumentNullException(nameof(condition));

        normalizedState = NormalizeState(state);
        rewrittenCondition = RewriteQueryConditionToCurrentVersions(condition, normalizedState);
        if (normalizedState.IsContradictory)
        {
            result = SymbolicIrProofResult.Syntactic(
                mode == ConditionClassificationMode.Implication
                    ? SymbolicProofStatus.ProvenTrue
                    : SymbolicProofStatus.Unreachable,
                ContradictoryStateReason);
            return true;
        }

        if (TryClassifySyntacticConditionTruth(rewrittenCondition, out var syntacticStatus))
        {
            if (mode == ConditionClassificationMode.Implication &&
                syntacticStatus == SymbolicProofStatus.ProvenFalse)
                result = ClassifySyntacticallyFalseImplication(
                    normalizedState,
                    "ir_condition_syntactic_false_reachable");
            else
                result = SymbolicIrProofResult.Syntactic(
                    syntacticStatus,
                    mode == ConditionClassificationMode.Implication
                        ? "ir_condition_syntactic_truth"
                        : syntacticStatus == SymbolicProofStatus.ProvenTrue
                            ? "ir_condition_syntactic_true"
                            : "ir_condition_syntactic_false");
            return true;
        }

        if (StateContainsCondition(normalizedState, rewrittenCondition))
        {
            result = SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.ProvenTrue,
                "ir_state_contains_condition");
            return true;
        }

        if (StateContradictsCondition(normalizedState, rewrittenCondition))
        {
            result = SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.ProvenFalse,
                "ir_state_contradicts_condition");
            return true;
        }

        result = null!;
        return false;
    }

    private enum ConditionClassificationMode
    {
        Implication,
        Truth
    }

    public SymbolicIrProofResult ClassifyHazardTrigger(SymbolicState state, SymbolicFact triggerPrecondition)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        if (triggerPrecondition == null) throw new ArgumentNullException(nameof(triggerPrecondition));

        state = NormalizeState(state);
        triggerPrecondition = RewriteQueryFactToCurrentVersions(triggerPrecondition, state);
        if (state.IsContradictory)
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.Unreachable,
                ContradictoryStateReason);

        return ClassifyWithIrCache(
            "hazard-trigger:" + state.NormalizedProofKey + "\n" + SymbolicState.CreateProofFactKey(triggerPrecondition),
            () =>
            {
                var triggerCondition = ClassifyExceptionTriggerCondition(state, triggerPrecondition);
                if (triggerCondition.Info.Status == SymbolicProofStatus.ProvenTrue) return triggerCondition;

                if (triggerCondition.Info.Status is SymbolicProofStatus.ProvenFalse or SymbolicProofStatus.Unreachable)
                    return triggerCondition.Info.Status == SymbolicProofStatus.Unreachable
                        ? triggerCondition
                        : triggerCondition.WithStatus(SymbolicProofStatus.Unreachable);

                var proven = ClassifyImplication(state, triggerPrecondition);
                if (proven.Info.Status == SymbolicProofStatus.ProvenTrue) return proven;

                var triggerFeasibility = ClassifyBranchFeasibility(
                    state,
                    new SymbolicFactCondition(triggerPrecondition));
                return triggerFeasibility.Info.Status == SymbolicProofStatus.Unreachable
                    ? triggerFeasibility
                    : proven;
            });
    }

    private SymbolicIrProofResult ClassifyExceptionTriggerCondition(SymbolicState state,
        SymbolicFact triggerPrecondition)
    {
        if (triggerPrecondition is { Polarity: true, Atom: SymbolicExceptionPreconditionAtom precondition })
            return ClassifyConditionTruth(state, precondition.Trigger);

        return SymbolicIrProofResult.Unknown(SymbolicUnknownReason.UnsupportedIrEncoding);
    }

    private SymbolicIrProofResult ClassifyWithIrCache(
        string key,
        Func<SymbolicIrProofResult> classify)
    {
        var cache = GetProofResultCache();
        if (cache.TryGetResult(key, out var cached)) return cached.WithCacheHit(CreateBudgetInfo());

        var result = classify();
        cache.TryAddResult(key, result);
        return result;
    }

    private ProofResultCache GetProofResultCache()
    {
        return smtAnalysis != null
            ? s_serviceCaches.GetValue(
                smtAnalysis,
                static _ => new ProofResultCache(PerServiceProofCacheEntryLimit))
            : s_fallbackCache;
    }

    private static SymbolicState NormalizeState(SymbolicState state)
    {
        return state.Normalize();
    }

    private static SymbolicTerm RewriteQueryTermToCurrentVersions(SymbolicTerm term, SymbolicState state)
    {
        return state.SymbolVersions.Count == 0
            ? term
            : SymbolicIrVersionRewriter.RewriteToCurrentVersions(term, state.SymbolVersions);
    }

    private static SymbolicCondition RewriteQueryConditionToCurrentVersions(SymbolicCondition condition,
        SymbolicState state)
    {
        return state.SymbolVersions.Count == 0
            ? condition
            : SymbolicIrVersionRewriter.RewriteToCurrentVersions(condition, state.SymbolVersions);
    }

    private static SymbolicFact RewriteQueryFactToCurrentVersions(SymbolicFact fact, SymbolicState state)
    {
        return state.SymbolVersions.Count == 0
            ? fact
            : SymbolicIrVersionRewriter.RewriteToCurrentVersions(fact, state.SymbolVersions);
    }

    private static bool TryClassifySyntacticConditionTruth(
        SymbolicCondition condition,
        out SymbolicProofStatus status)
    {
        switch (SymbolicState.CreateProofConditionKey(condition))
        {
            case "const:true":
                status = SymbolicProofStatus.ProvenTrue;
                return true;
            case "const:false":
                status = SymbolicProofStatus.ProvenFalse;
                return true;
            default:
                status = SymbolicProofStatus.Unknown;
                return false;
        }
    }

    private static bool StateContainsFact(SymbolicState state, SymbolicFact fact)
    {
        var factKey = SymbolicState.CreateProofFactKey(fact);
        var factConditionKey = "fact-condition:" + factKey;
        return state.Facts.Any(candidate => string.Equals(
                   SymbolicState.CreateProofFactKey(candidate),
                   factKey,
                   StringComparison.Ordinal)) ||
               state.PathConditions.Any(candidate =>
                   string.Equals(
                       SymbolicState.CreateProofConditionKey(candidate),
                       factConditionKey,
                       StringComparison.Ordinal) ||
                   SymbolicState.EnumerateProofConditionFactKeys(candidate).Any(conditionFactKey => string.Equals(
                       conditionFactKey,
                       factKey,
                       StringComparison.Ordinal)));
    }

    private static bool StateContradictsFact(SymbolicState state, SymbolicFact fact)
    {
        return StateContainsFact(state, fact.Negate());
    }

    private static bool StateContainsCondition(SymbolicState state, SymbolicCondition condition)
    {
        if (TryEvaluateConditionFromState(state, condition, out var value)) return value;

        if (condition is SymbolicFactCondition factCondition &&
            StateContainsFact(state, factCondition.Fact))
            return true;

        var conditionKey = SymbolicState.CreateProofConditionKey(condition);
        return state.Facts.Any(candidate => string.Equals(
                   "fact-condition:" + SymbolicState.CreateProofFactKey(candidate),
                   conditionKey,
                   StringComparison.Ordinal)) ||
               state.PathConditions.Any(candidate => string.Equals(
                   SymbolicState.CreateProofConditionKey(candidate),
                   conditionKey,
                   StringComparison.Ordinal));
    }

    private static bool StateContradictsCondition(SymbolicState state, SymbolicCondition condition)
    {
        if (TryEvaluateConditionFromState(state, condition, out var value)) return !value;

        return StateContainsCondition(state, new SymbolicNotCondition(condition));
    }

    private static bool TryEvaluateConditionFromState(
        SymbolicState state,
        SymbolicCondition condition,
        out bool value)
    {
        return TryEvaluateConditionFromState(
            state,
            condition,
            new Dictionary<string, bool>(StringComparer.Ordinal),
            out value);
    }

    private static bool TryEvaluateConditionFromState(
        SymbolicState state,
        SymbolicCondition condition,
        IDictionary<string, bool> memo,
        out bool value)
    {
        var conditionKey = SymbolicState.CreateProofConditionKey(condition);
        if (memo.TryGetValue(conditionKey, out value)) return true;

        if (state.Facts.Any(fact => string.Equals(
                "fact-condition:" + SymbolicState.CreateProofFactKey(fact),
                conditionKey,
                StringComparison.Ordinal)) ||
            state.PathConditions.Any(pathCondition => string.Equals(
                SymbolicState.CreateProofConditionKey(pathCondition),
                conditionKey,
                StringComparison.Ordinal)))
        {
            value = true;
            memo[conditionKey] = true;
            return true;
        }

        var negatedConditionKey = SymbolicState.CreateProofConditionKey(new SymbolicNotCondition(condition));
        if (state.PathConditions.Any(pathCondition => string.Equals(
                SymbolicState.CreateProofConditionKey(pathCondition),
                negatedConditionKey,
                StringComparison.Ordinal)))
        {
            value = false;
            memo[conditionKey] = false;
            return true;
        }

        switch (condition)
        {
            case SymbolicConstantCondition constant:
                value = constant.Value;
                memo[conditionKey] = value;
                return true;
            case SymbolicFactCondition factCondition:
                if (StateContainsFact(state, factCondition.Fact))
                {
                    value = true;
                    memo[conditionKey] = value;
                    return true;
                }

                if (StateContradictsFact(state, factCondition.Fact))
                {
                    value = false;
                    memo[conditionKey] = value;
                    return true;
                }

                break;
            case SymbolicNotCondition notCondition:
                if (TryEvaluateConditionFromState(state, notCondition.Operand, memo, out var operandValue))
                {
                    value = !operandValue;
                    memo[conditionKey] = value;
                    return true;
                }

                break;
            case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.And } andCondition:
                var leftAndKnown = TryEvaluateConditionFromState(state, andCondition.Left, memo, out var leftAndValue);
                var rightAndKnown =
                    TryEvaluateConditionFromState(state, andCondition.Right, memo, out var rightAndValue);
                if ((leftAndKnown && !leftAndValue) ||
                    (rightAndKnown && !rightAndValue))
                {
                    value = false;
                    memo[conditionKey] = value;
                    return true;
                }

                if (leftAndKnown && rightAndKnown)
                {
                    value = leftAndValue && rightAndValue;
                    memo[conditionKey] = value;
                    return true;
                }

                break;
            case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or } orCondition:
                var leftOrKnown = TryEvaluateConditionFromState(state, orCondition.Left, memo, out var leftOrValue);
                var rightOrKnown = TryEvaluateConditionFromState(state, orCondition.Right, memo, out var rightOrValue);
                if ((leftOrKnown && leftOrValue) ||
                    (rightOrKnown && rightOrValue))
                {
                    value = true;
                    memo[conditionKey] = value;
                    return true;
                }

                if (leftOrKnown && rightOrKnown)
                {
                    value = leftOrValue || rightOrValue;
                    memo[conditionKey] = value;
                    return true;
                }

                break;
        }

        value = false;
        return false;
    }

    private SymbolicBudgetInfo? CreateBudgetInfo()
    {
        var service = smtAnalysis;
        if (service == null) return null;

        var proofCache = GetProofResultCache();
        var cache = new SymbolicCacheInfo(
            service.CacheHitCount + proofCache.HitCount,
            service.CacheMissCount + proofCache.MissCount,
            service.CacheEntryCount + proofCache.Count,
            service.CacheEvictionCount + proofCache.EvictionCount);
        return new SymbolicBudgetInfo(
            service.Options.MaxPathConditions,
            service.Options.MaxExpressionNodes,
            SymbolicSmtDiagnostics.ToBoundedMilliseconds(service.Options.QueryTimeout),
            SymbolicSmtDiagnostics.ToBoundedMilliseconds(service.Options.MethodBudget),
            service.ExecutedQueryCount,
            cache.Entries,
            cache);
    }

    private bool TryEncodeState(
        SymbolicState state,
        out ImmutableArray<SmtFormula> pathConditions,
        out SymbolicUnknownReason unknownReason)
    {
        var cache = GetProofResultCache();
        if (!cache.TryGetEncodedState(state.NormalizedProofKey, out var entry))
        {
            entry = EncodeStateUncached(state);
            cache.TryAddEncodedState(state.NormalizedProofKey, entry);
        }

        pathConditions = entry.PathConditions;
        unknownReason = entry.UnknownReason;
        return entry.Success;
    }

    private static EncodedStateCacheEntry EncodeStateUncached(SymbolicState state)
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
            return new EncodedStateCacheEntry(
                false,
                ImmutableArray<SmtFormula>.Empty,
                SymbolicUnknownReason.UnsupportedIrEncoding);

        return new EncodedStateCacheEntry(
            true,
            builder.ToImmutable(),
            SymbolicUnknownReason.None);
    }

    private sealed class ProofResultCache
    {
        private const string EncodedStatePrefix = "encoded-state:";
        private const string ResultPrefix = "proof-result:";
        private readonly BoundedConcurrentCache<string, object> _values;

        internal ProofResultCache(int capacity)
        {
            _values = new BoundedConcurrentCache<string, object>(capacity, StringComparer.Ordinal);
        }

        internal int Count => _values.Count;
        internal long HitCount => _values.HitCount;
        internal long MissCount => _values.MissCount;
        internal long EvictionCount => _values.EvictionCount;

        internal bool TryGetResult(string key, out SymbolicIrProofResult result)
        {
            if (_values.TryGetValue(ResultPrefix + key, out var value) &&
                value is SymbolicIrProofResult cached)
            {
                result = cached;
                return true;
            }

            result = null!;
            return false;
        }

        internal void TryAddResult(string key, SymbolicIrProofResult result)
        {
            _values.TryAdd(ResultPrefix + key, result);
        }

        internal bool TryGetEncodedState(string key, out EncodedStateCacheEntry entry)
        {
            if (_values.TryGetValue(EncodedStatePrefix + key, out var value) &&
                value is EncodedStateCacheEntry cached)
            {
                entry = cached;
                return true;
            }

            entry = default;
            return false;
        }

        internal void TryAddEncodedState(string key, EncodedStateCacheEntry entry)
        {
            _values.TryAdd(EncodedStatePrefix + key, entry);
        }
    }

    private readonly record struct EncodedStateCacheEntry(
        bool Success,
        ImmutableArray<SmtFormula> PathConditions,
        SymbolicUnknownReason UnknownReason);
}

internal sealed class SymbolicIrProofResult
{
    private SymbolicIrProofResult(PurityProofResult? rawResult, SymbolicProofInfo info)
    {
        RawResult = rawResult;
        Info = info;
    }

    public PurityProofResult? RawResult { get; }

    public SymbolicProofInfo Info { get; }

    public static SymbolicIrProofResult Unknown(
        SymbolicUnknownReason reason,
        SymbolicProofStage stage = SymbolicProofStage.Lowering,
        SymbolicProofSupport support = SymbolicProofSupport.Unsupported,
        string? detail = null)
    {
        return new SymbolicIrProofResult(
            null,
            new SymbolicProofInfo(
                SymbolicProofStatus.Unknown,
                SymbolicProofBackend.None,
                reason,
                detail ?? reason.ToString(),
                false,
                null,
                stage,
                support));
    }

    public static SymbolicIrProofResult Syntactic(
        SymbolicProofStatus status,
        string reason)
    {
        return new SymbolicIrProofResult(
            null,
            new SymbolicProofInfo(
                status,
                SymbolicProofBackend.Syntactic,
                SymbolicUnknownReason.None,
                reason,
                false,
                null,
                SymbolicProofStage.SyntacticClassification,
                SymbolicProofSupport.Exact));
    }

    internal SymbolicIrProofResult WithCacheHit(SymbolicBudgetInfo? budget)
    {
        return new SymbolicIrProofResult(
            RawResult,
            new SymbolicProofInfo(
                Info.Status,
                Info.Backend,
                Info.UnknownReason,
                Info.Reason,
                true,
                budget ?? Info.Budget,
                Info.Stage,
                Info.Support,
                Info.Target,
                Info.ConditionText,
                Info.DisplayKind));
    }

    internal SymbolicIrProofResult WithStatus(SymbolicProofStatus status, string? reason = null)
    {
        return new SymbolicIrProofResult(
            RawResult,
            new SymbolicProofInfo(
                status,
                Info.Backend,
                status == SymbolicProofStatus.Unknown && Info.UnknownReason == SymbolicUnknownReason.None
                    ? SymbolicUnknownReason.Unknown
                    : Info.UnknownReason,
                reason ?? Info.Reason,
                Info.CacheHit,
                Info.Budget,
                Info.Stage,
                Info.Support,
                Info.Target,
                Info.ConditionText,
                Info.DisplayKind));
    }

    public static SymbolicIrProofResult FromReachability(
        PurityProofResult result,
        SymbolicBudgetInfo? budget,
        SymbolicProofSupport support = SymbolicProofSupport.Exact)
    {
        var status = result.PathCheck.Feasibility switch
        {
            Feasibility.Satisfiable => SymbolicProofStatus.Reachable,
            Feasibility.Unsatisfiable => SymbolicProofStatus.Unreachable,
            _ => SymbolicProofStatus.Unknown
        };

        return FromResult(result, status, budget, support);
    }

    public static SymbolicIrProofResult FromImplication(
        PurityProofResult result,
        SymbolicBudgetInfo? budget,
        SymbolicProofSupport support = SymbolicProofSupport.Exact)
    {
        var status = result.Outcome switch
        {
            PurityProofOutcome.ProvablyPure => SymbolicProofStatus.ProvenTrue,
            PurityProofOutcome.ProvablyImpure => SymbolicProofStatus.ProvenFalse,
            _ => SymbolicProofStatus.Unknown
        };

        return FromResult(result, status, budget, support);
    }

    public static SymbolicIrProofResult FromConditionTruth(
        PurityProofResult result,
        SymbolicProofStatus status,
        SymbolicBudgetInfo? budget,
        SymbolicProofSupport support = SymbolicProofSupport.Exact)
    {
        if (status is not SymbolicProofStatus.ProvenTrue and
            not SymbolicProofStatus.ProvenFalse and
            not SymbolicProofStatus.Unreachable and
            not SymbolicProofStatus.Unknown)
            throw new ArgumentOutOfRangeException(nameof(status), status,
                "Condition truth proofs must be proven true, proven false, unreachable, or unknown.");

        return FromResult(result, status, budget, support);
    }

    private static SymbolicIrProofResult FromResult(
        PurityProofResult result,
        SymbolicProofStatus status,
        SymbolicBudgetInfo? budget,
        SymbolicProofSupport support)
    {
        return new SymbolicIrProofResult(
            result,
            new SymbolicProofInfo(
                status,
                SymbolicProofBackend.Smt,
                MapUnknownReason(result.Reason),
                result.Reason,
                false,
                budget,
                MapStage(result.Reason, status),
                support));
    }

    private static SymbolicProofStage MapStage(string reason, SymbolicProofStatus status)
    {
        if (status != SymbolicProofStatus.Unknown) return SymbolicProofStage.ResultMapping;

        return reason switch
        {
            "smt_method_budget_exceeded" or
                "smt_path_condition_budget_exceeded" or
                "smt_expression_budget_exceeded" => SymbolicProofStage.Budgeting,
            "smt_disabled" => SymbolicProofStage.Budgeting,
            _ => SymbolicProofStage.SmtExecution
        };
    }

    private static SymbolicUnknownReason MapUnknownReason(string reason)
    {
        return reason switch
        {
            "smt_disabled" => SymbolicUnknownReason.SmtDisabled,
            "smt_unavailable" => SymbolicUnknownReason.SmtUnavailable,
            "smt_transient_failure" => SymbolicUnknownReason.SmtUnavailable,
            "smt_timeout" => SymbolicUnknownReason.Timeout,
            "smt_method_budget_exceeded" => SymbolicUnknownReason.MethodBudgetExceeded,
            "smt_path_condition_budget_exceeded" => SymbolicUnknownReason.PathConditionBudgetExceeded,
            "smt_expression_budget_exceeded" => SymbolicUnknownReason.ExpressionBudgetExceeded,
            "smt_encoding_failure" => SymbolicUnknownReason.EncodingFailure,
            _ => SymbolicUnknownReason.None
        };
    }
}
