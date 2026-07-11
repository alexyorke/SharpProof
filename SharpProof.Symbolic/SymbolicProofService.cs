using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Purity;
using SearchLib.Smt;
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

    internal static bool TryEncodeDerivedFormulaTerm(
        SmtFormula formula,
        SymbolicTermTransformer transform,
        out SmtFormula encoded)
    {
        if (formula == null) throw new ArgumentNullException(nameof(formula));

        if (transform == null) throw new ArgumentNullException(nameof(transform));

        if (!SymbolicSmtFormulaLowerer.TryLowerTerm(formula, out var term) ||
            !transform(term, out var transformed))
        {
            encoded = null!;
            return false;
        }

        return SymbolicIrFormulaEncoder.TryEncodeTerm(transformed, out encoded);
    }

    internal static bool TryEncodeDerivedFormulaCondition(
        SmtFormula formula,
        SymbolicConditionTransformer transform,
        out SmtFormula encoded)
    {
        if (formula == null) throw new ArgumentNullException(nameof(formula));

        if (transform == null) throw new ArgumentNullException(nameof(transform));

        if (!SymbolicSmtFormulaLowerer.TryLowerTerm(formula, out var term) ||
            !transform(term, out var transformed))
        {
            encoded = null!;
            return false;
        }

        return SymbolicIrFormulaEncoder.TryEncode(transformed, out encoded);
    }

    internal static bool TryEncodeDerivedFormulaFacts(
        SmtFormula formula,
        SymbolicFactTransformer transform,
        out ImmutableArray<SmtFormula> encodedFacts)
    {
        if (formula == null) throw new ArgumentNullException(nameof(formula));

        if (transform == null) throw new ArgumentNullException(nameof(transform));

        if (!SymbolicSmtFormulaLowerer.TryLowerTerm(formula, out var term))
        {
            encodedFacts = ImmutableArray<SmtFormula>.Empty;
            return false;
        }

        var facts = new List<SymbolicFact>();
        if (!transform(term, facts) || facts.Count == 0)
        {
            encodedFacts = ImmutableArray<SmtFormula>.Empty;
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<SmtFormula>(facts.Count);
        foreach (var fact in facts)
        {
            if (!SymbolicIrFormulaEncoder.TryEncode(fact, out var encodedFact))
            {
                encodedFacts = ImmutableArray<SmtFormula>.Empty;
                return false;
            }

            builder.Add(encodedFact);
        }

        encodedFacts = builder.MoveToImmutable();
        return true;
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

    internal static SymbolicState CreateStateFromFormulaPath(
        IEnumerable<SmtFormula> pathConditions,
        SyntaxNode sourceNode)
    {
        var state = new SymbolicState();
        foreach (var pathCondition in pathConditions)
            if (SymbolicSmtFormulaLowerer.TryLowerCondition(
                    pathCondition,
                    sourceNode,
                    "legacy_path_condition",
                    "legacy-path-condition",
                    out var condition))
                state = state.AddPathCondition(condition);

        return state;
    }

    internal static SymbolicState AddLoweredFormulaPathCondition(
        SymbolicState state,
        SmtFormula formula,
        SyntaxNode sourceNode,
        string provenance,
        string evidenceKey)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        if (formula == null) throw new ArgumentNullException(nameof(formula));

        if (sourceNode == null) throw new ArgumentNullException(nameof(sourceNode));

        return SymbolicSmtFormulaLowerer.TryLowerCondition(
            formula,
            sourceNode,
            provenance,
            evidenceKey,
            out var condition)
            ? state.AddPathCondition(condition)
            : state;
    }

    internal static bool TryCreateStateFromFormulaPath(
        IEnumerable<SmtFormula> pathConditions,
        SyntaxNode sourceNode,
        string provenance,
        string evidenceKey,
        out SymbolicState state)
    {
        state = new SymbolicState();
        foreach (var pathCondition in pathConditions)
        {
            if (!SymbolicSmtFormulaLowerer.TryLowerCondition(
                    pathCondition,
                    sourceNode,
                    provenance,
                    evidenceKey,
                    out var condition))
            {
                state = new SymbolicState();
                return false;
            }

            state = state.AddPathCondition(condition);
        }

        return true;
    }

    private static bool HasSafeIntegerDivisors(
        SymbolicTerm term,
        SymbolicState state,
        SyntaxNode sourceNode)
    {
        switch (term)
        {
            case SymbolicConditionalTerm conditional:
                if (!HasSafeIntegerDivisors(conditional.Condition, state, sourceNode)) return false;

                var whenTrueState = AssumePathCondition(state, conditional.Condition);
                if (!whenTrueState.IsContradictory &&
                    !HasSafeIntegerDivisors(conditional.WhenTrue, whenTrueState, sourceNode))
                    return false;

                var whenFalseState = AssumePathCondition(state, new SymbolicNotCondition(conditional.Condition));
                return whenFalseState.IsContradictory ||
                       HasSafeIntegerDivisors(conditional.WhenFalse, whenFalseState, sourceNode);
            case SymbolicBinaryTerm binary:
                return HasSafeIntegerDivisors(binary.Left, state, sourceNode) &&
                       HasSafeIntegerDivisors(binary.Right, state, sourceNode) &&
                       (binary.Operator is not (SymbolicBinaryTermOperator.Divide
                            or SymbolicBinaryTermOperator.Remainder) ||
                        IsTermProvablyNonZero(binary.Right, state, sourceNode));
            case SymbolicMemberTerm member:
                return HasSafeIntegerDivisors(member.Receiver, state, sourceNode);
            case SymbolicElementTerm element:
                return HasSafeIntegerDivisors(element.Receiver, state, sourceNode) &&
                       HasSafeIntegerDivisors(element.Index, state, sourceNode);
            case SymbolicStringContentTerm stringContent:
                return HasSafeIntegerDivisors(stringContent.Reference, state, sourceNode);
            case SymbolicStringConcatTerm stringConcat:
                return HasSafeIntegerDivisors(stringConcat.Left, state, sourceNode) &&
                       HasSafeIntegerDivisors(stringConcat.Right, state, sourceNode);
            case SymbolicLengthTerm length:
                return HasSafeIntegerDivisors(length.Value, state, sourceNode);
            case SymbolicArrayDimensionLengthTerm arrayLength:
                return HasSafeIntegerDivisors(arrayLength.Value, state, sourceNode);
            case SymbolicCountTerm count:
                return HasSafeIntegerDivisors(count.Value, state, sourceNode);
            default:
                return true;
        }
    }

    private static bool HasSafeIntegerDivisors(
        SymbolicCondition condition,
        SymbolicState state,
        SyntaxNode sourceNode)
    {
        switch (condition)
        {
            case SymbolicFactCondition factCondition:
                return HasSafeIntegerDivisors(factCondition.Fact, state, sourceNode);
            case SymbolicNotCondition notCondition:
                return HasSafeIntegerDivisors(notCondition.Operand, state, sourceNode);
            case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.And } andCondition:
                if (!HasSafeIntegerDivisors(andCondition.Left, state, sourceNode)) return false;

                var andRightState = AssumePathCondition(state, andCondition.Left);
                return andRightState.IsContradictory ||
                       HasSafeIntegerDivisors(andCondition.Right, andRightState, sourceNode);
            case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or } orCondition:
                if (!HasSafeIntegerDivisors(orCondition.Left, state, sourceNode)) return false;

                var orRightState = AssumePathCondition(state, new SymbolicNotCondition(orCondition.Left));
                return orRightState.IsContradictory ||
                       HasSafeIntegerDivisors(orCondition.Right, orRightState, sourceNode);
            case SymbolicBinaryCondition binaryCondition:
                return HasSafeIntegerDivisors(binaryCondition.Left, state, sourceNode) &&
                       HasSafeIntegerDivisors(binaryCondition.Right, state, sourceNode);
            default:
                return true;
        }
    }

    private static bool HasSafeIntegerDivisors(
        SymbolicFact fact,
        SymbolicState state,
        SyntaxNode sourceNode)
    {
        return HasSafeIntegerDivisors(fact.Atom, state, sourceNode);
    }

    private static bool HasSafeIntegerDivisors(
        SymbolicAtom atom,
        SymbolicState state,
        SyntaxNode sourceNode)
    {
        switch (atom)
        {
            case SymbolicTruthAtom truth:
                return HasSafeIntegerDivisors(truth.Condition, state, sourceNode);
            case SymbolicRelationAtom relation:
                return HasSafeIntegerDivisors(relation.Left, state, sourceNode) &&
                       HasSafeIntegerDivisors(relation.Right, state, sourceNode);
            case SymbolicStringPredicateAtom predicate:
                return HasSafeIntegerDivisors(predicate.Value, state, sourceNode) &&
                       HasSafeIntegerDivisors(predicate.Argument, state, sourceNode);
            case SymbolicBoundsAtom bounds:
                return HasSafeIntegerDivisors(bounds.Index, state, sourceNode) &&
                       HasSafeIntegerDivisors(bounds.Length, state, sourceNode);
            case SymbolicFreshnessAtom freshness:
                return HasSafeIntegerDivisors(freshness.Value, state, sourceNode);
            case SymbolicOwnershipAtom ownership:
                return HasSafeIntegerDivisors(ownership.Value, state, sourceNode);
            case SymbolicAliasAtom alias:
                return HasSafeIntegerDivisors(alias.Source, state, sourceNode) &&
                       HasSafeIntegerDivisors(alias.Target, state, sourceNode);
            case SymbolicBorrowAtom borrow:
                return HasSafeIntegerDivisors(borrow.Owner, state, sourceNode) &&
                       HasSafeIntegerDivisors(borrow.Borrow, state, sourceNode);
            case SymbolicEscapeAtom escape:
                return HasSafeIntegerDivisors(escape.Value, state, sourceNode);
            case SymbolicReturnedOwnershipAtom returnedOwnership:
                return HasSafeIntegerDivisors(returnedOwnership.Value, state, sourceNode);
            case SymbolicMutationAtom mutation:
                return HasSafeIntegerDivisors(mutation.Target, state, sourceNode);
            case SymbolicDisposalAtom disposal:
                return HasSafeIntegerDivisors(disposal.Resource, state, sourceNode);
            case SymbolicResourceLifetimeAtom lifetime:
                return HasSafeIntegerDivisors(lifetime.Resource, state, sourceNode);
            case SymbolicTypeTestAtom typeTest:
                return HasSafeIntegerDivisors(typeTest.Value, state, sourceNode);
            case SymbolicExceptionPreconditionAtom precondition:
                return (precondition.Subject == null ||
                        HasSafeIntegerDivisors(precondition.Subject, state, sourceNode)) &&
                       HasSafeIntegerDivisors(precondition.Trigger, state, sourceNode);
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

    internal static SymbolicIrProofResult ClassifyFormulaConditionTruthWithIrFirst(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula conditionFormula,
        SyntaxNode sourceNode,
        SmtAnalysisService? smtAnalysis,
        string provenance,
        string evidenceKey)
    {
        var pathConditionList = pathConditions as IReadOnlyCollection<SmtFormula> ?? pathConditions.ToArray();
        if (SymbolicSmtFormulaLowerer.TryLowerCondition(
                conditionFormula,
                sourceNode,
                provenance,
                evidenceKey,
                out var condition) &&
            TryCreateStateFromFormulaPath(pathConditionList, sourceNode, provenance, evidenceKey, out var state))
        {
            var proof = new SymbolicProofService(smtAnalysis).ClassifyConditionTruth(state, condition);
            if (proof.Info.Status != SymbolicProofStatus.Unknown) return proof;
        }

        return new SymbolicProofService(smtAnalysis).ClassifyFormulaConditionTruth(pathConditionList, conditionFormula);
    }

    internal static SymbolicIrProofResult ClassifyFormulaConditionTruthWithIrFallback(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula conditionFormula,
        SyntaxNode sourceNode,
        SmtAnalysisService? smtAnalysis,
        string provenance,
        string evidenceKey)
    {
        var pathConditionList = pathConditions as IReadOnlyCollection<SmtFormula> ?? pathConditions.ToArray();
        var proofService = new SymbolicProofService(smtAnalysis);
        var formulaProof = proofService.ClassifyFormulaConditionTruth(pathConditionList, conditionFormula);
        if (formulaProof.Info.Status != SymbolicProofStatus.Unknown) return formulaProof;

        SymbolicIrProofResult? irProof = null;
        if (SymbolicSmtFormulaLowerer.TryLowerCondition(
                conditionFormula,
                sourceNode,
                provenance,
                evidenceKey,
                out var condition) &&
            TryCreateStateFromFormulaPath(pathConditionList, sourceNode, provenance, evidenceKey, out var state))
        {
            irProof = proofService.ClassifyConditionTruth(state, condition);
            if (irProof.Info.Status != SymbolicProofStatus.Unknown) return irProof;
        }

        return irProof ?? formulaProof;
    }

    internal static bool TryClassifyFormulaConditionTruthWithIr(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula conditionFormula,
        SyntaxNode sourceNode,
        SmtAnalysisService? smtAnalysis,
        string provenance,
        string evidenceKey,
        out SymbolicProofStatus status)
    {
        status = SymbolicProofStatus.Unknown;
        if (!SymbolicSmtFormulaLowerer.TryLowerCondition(
                conditionFormula,
                sourceNode,
                provenance,
                evidenceKey,
                out var condition))
            return false;

        if (!TryCreateStateFromFormulaPath(pathConditions, sourceNode, provenance, evidenceKey, out var state))
            return false;

        status = new SymbolicProofService(smtAnalysis).ClassifyConditionTruth(state, condition).Info.Status;
        return status != SymbolicProofStatus.Unknown;
    }

    internal static bool TryClassifyFormulaPathFeasibilityWithIr(
        IEnumerable<SmtFormula> pathConditions,
        SyntaxNode sourceNode,
        SmtAnalysisService? smtAnalysis,
        string provenance,
        string evidenceKey,
        out SymbolicProofStatus status)
    {
        status = SymbolicProofStatus.Unknown;
        if (!TryCreateStateFromFormulaPath(pathConditions, sourceNode, provenance, evidenceKey, out var state))
            return false;

        status = new SymbolicProofService(smtAnalysis).ClassifyReachability(state).Info.Status;
        return status is SymbolicProofStatus.Reachable or SymbolicProofStatus.Unreachable;
    }

    internal static bool TryClassifyBranchConditionTruthWithIr(
        IEnumerable<SmtFormula> pathConditions,
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService? smtAnalysis,
        string provenance,
        string evidenceKey,
        out SymbolicProofStatus status)
    {
        status = SymbolicProofStatus.Unknown;
        if (!SymbolicIrLowerer.TryLowerCondition(
                condition,
                new SymbolicLoweringContext(semanticModel, cancellationToken),
                out var symbolicCondition) ||
            !TryCreateStateFromFormulaPath(pathConditions, condition, provenance, evidenceKey, out var state))
            return false;

        if (!branchWhenTrue) symbolicCondition = new SymbolicNotCondition(symbolicCondition);

        status = new SymbolicProofService(smtAnalysis).ClassifyConditionTruth(state, symbolicCondition).Info.Status;
        return status != SymbolicProofStatus.Unknown;
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
            return SymbolicIrProofResult.Syntactic(
                factValue ? SymbolicProofStatus.ProvenTrue : SymbolicProofStatus.ProvenFalse,
                factValue ? "ir_target_fact_syntactic_true" : "ir_target_fact_syntactic_false");

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
        if (state == null) throw new ArgumentNullException(nameof(state));

        if (condition == null) throw new ArgumentNullException(nameof(condition));

        state = NormalizeState(state);
        condition = RewriteQueryConditionToCurrentVersions(condition, state);
        if (state.IsContradictory)
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.ProvenTrue,
                ContradictoryStateReason);

        if (TryClassifySyntacticConditionTruth(condition, out var syntacticStatus) &&
            syntacticStatus == SymbolicProofStatus.ProvenTrue)
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.ProvenTrue,
                "ir_condition_syntactic_truth");

        if (syntacticStatus == SymbolicProofStatus.ProvenFalse)
        {
            var reachability = ClassifyReachability(state);
            if (reachability.Info.Backend == SymbolicProofBackend.Syntactic)
                return reachability.Info.Status == SymbolicProofStatus.Unreachable
                    ? SymbolicIrProofResult.Syntactic(
                        SymbolicProofStatus.ProvenTrue,
                        reachability.Info.Reason)
                    : SymbolicIrProofResult.Syntactic(
                        SymbolicProofStatus.ProvenFalse,
                        "ir_condition_syntactic_false_reachable");
        }

        if (StateContainsCondition(state, condition))
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.ProvenTrue,
                "ir_state_contains_condition");

        if (StateContradictsCondition(state, condition))
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.ProvenFalse,
                "ir_state_contradicts_condition");

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
        if (state == null) throw new ArgumentNullException(nameof(state));

        if (condition == null) throw new ArgumentNullException(nameof(condition));

        state = NormalizeState(state);
        condition = RewriteQueryConditionToCurrentVersions(condition, state);
        if (state.IsContradictory)
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.Unreachable,
                ContradictoryStateReason);

        if (TryClassifySyntacticConditionTruth(condition, out var syntacticStatus))
            return SymbolicIrProofResult.Syntactic(
                syntacticStatus,
                "ir_condition_syntactic_truth");

        if (StateContainsCondition(state, condition))
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.ProvenTrue,
                "ir_state_contains_condition");

        if (StateContradictsCondition(state, condition))
            return SymbolicIrProofResult.Syntactic(
                SymbolicProofStatus.ProvenFalse,
                "ir_state_contradicts_condition");

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

                return falseBranch;
            });
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

    internal SymbolicIrProofResult ClassifyFormulaReachability(IEnumerable<SmtFormula> pathConditions)
    {
        return proofPipeline.ClassifyReachability(
            pathConditions,
            CreateBudgetInfo,
            SymbolicProofSupport.Approximate);
    }

    internal SymbolicIrProofResult ClassifyFormulaConditionTruth(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula conditionFormula)
    {
        return proofPipeline.ClassifyConditionTruth(
            pathConditions,
            conditionFormula,
            CreateBudgetInfo,
            SymbolicProofSupport.Approximate);
    }

    internal PurityProofResult ClassifyFormulaImplication(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula factFormula)
    {
        return proofPipeline.ClassifyRawImplication(pathConditions, factFormula);
    }

    internal PurityProofResult ClassifyFormulaBranchReachability(
        IEnumerable<SmtFormula> pathConditions,
        SmtFormula branchCondition)
    {
        return proofPipeline.ClassifyBranchReachability(pathConditions, branchCondition);
    }

    private PurityProofResult ClassifyFormulaPathFeasibility(IEnumerable<SmtFormula> pathConditions)
    {
        return proofPipeline.ClassifyPathFeasibility(pathConditions);
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

    internal delegate bool SymbolicTermTransformer(SymbolicTerm input, out SymbolicTerm output);

    internal delegate bool SymbolicConditionTransformer(SymbolicTerm input, out SymbolicCondition output);

    internal delegate bool SymbolicFactTransformer(SymbolicTerm input, ICollection<SymbolicFact> output);

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

    internal SymbolicIrProofResult WithStatus(SymbolicProofStatus status)
    {
        return new SymbolicIrProofResult(
            RawResult,
            new SymbolicProofInfo(
                status,
                Info.Backend,
                Info.UnknownReason,
                Info.Reason,
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
