namespace SharpProof.Symbolic;
internal static class SymbolicProofEncoder {
    private static readonly ExpressionSyntax s_syntheticProofNode = SyntaxFactory.IdentifierName("__symbolic_proof__");
    internal static bool TryEncodeConditionWithPathState(SymbolicCondition condition, SymbolicState state, out SmtFormula formula) =>
        TryEncodeConditionWithPathState(condition, state, s_syntheticProofNode, true, out formula);
    internal static bool TryEncodeConditionWithPathState(
        SymbolicCondition condition,
        SymbolicState state,
        SyntaxNode sourceNode,
        out SmtFormula formula) => TryEncodeConditionWithPathState(condition, state, sourceNode, true, out formula);
    private static bool TryEncodeConditionWithPathState(
        SymbolicCondition condition,
        SymbolicState state,
        SyntaxNode sourceNode,
        bool rewriteQueryVersions,
        out SmtFormula formula) {
        if (condition == null) throw new ArgumentNullException(nameof(condition));
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (sourceNode == null) throw new ArgumentNullException(nameof(sourceNode));
        state = SymbolicProofStateFacts.NormalizeState(state);
        if (rewriteQueryVersions) condition = SymbolicProofStateFacts.RewriteQueryConditionToCurrentVersions(condition, state);
        if (state.IsContradictory) return SymbolicIrFormulaEncoder.TryEncode(condition, out formula);
        if (!HasSafeIntegerDivisors(condition, state, sourceNode)) {
            formula = null!;
            return false;
        }
        return SymbolicIrFormulaEncoder.TryEncode(condition, out formula);
    }
    internal static bool TryEncodeFactWithPathState(SymbolicFact fact, SymbolicState state, out SmtFormula formula) =>
        TryEncodeFactWithPathState(fact, state, s_syntheticProofNode, out formula);
    internal static bool TryEncodeFactWithPathState(SymbolicFact fact, SymbolicState state, SyntaxNode sourceNode, out SmtFormula formula) {
        if (fact == null) throw new ArgumentNullException(nameof(fact));
        return TryEncodeConditionWithPathState(new SymbolicFactCondition(fact), state, sourceNode, false, out formula);
    }
    private static bool HasSafeIntegerDivisors(SymbolicCondition condition, SymbolicState state, SyntaxNode sourceNode)
        => HasSafeIntegerDivisorsCore(condition, state, sourceNode);
    private static bool HasSafeIntegerDivisorsCore(
        SymbolicTerm term,
        SymbolicState state,
        SyntaxNode sourceNode) {
        switch (term) {
            case SymbolicConditionalTerm conditional:
                if (!HasSafeIntegerDivisorsCore(conditional.Condition, state, sourceNode)) return false;
                var whenTrue = AssumePathCondition(state, conditional.Condition);
                if (!whenTrue.IsContradictory &&
                    !HasSafeIntegerDivisorsCore(conditional.WhenTrue, whenTrue, sourceNode))
                    return false;
                var whenFalse = AssumePathCondition(state, new SymbolicNotCondition(conditional.Condition));
                return whenFalse.IsContradictory ||
                       HasSafeIntegerDivisorsCore(conditional.WhenFalse, whenFalse, sourceNode);
            case SymbolicBinaryTerm binary:
                return HasSafeIntegerDivisorsCore(binary.Left, state, sourceNode) &&
                       HasSafeIntegerDivisorsCore(binary.Right, state, sourceNode) &&
                       (binary.Operator is not (SymbolicBinaryTermOperator.Divide
                            or SymbolicBinaryTermOperator.Remainder) ||
                        IsTermProvablyNonZero(binary.Right, state, sourceNode));
            default:
                return HasSafeIntegerDivisorsInChildren(SymbolicIrChildren.OfTerm(term), state, sourceNode);
        }
    }
    private static bool HasSafeIntegerDivisorsInChildren(
        SymbolicIrChildren children,
        SymbolicState state,
        SyntaxNode sourceNode) {
        if (children.First != null &&
            !HasSafeIntegerDivisorsCore(children.First, state, sourceNode))
            return false;
        if (children.Second != null &&
            !HasSafeIntegerDivisorsCore(children.Second, state, sourceNode))
            return false;
        if (!children.Rest.IsDefaultOrEmpty)
            foreach (var index in children.Rest)
                if (!HasSafeIntegerDivisorsCore(index, state, sourceNode))
                    return false;
        return children.Condition == null ||
               HasSafeIntegerDivisorsCore(children.Condition, state, sourceNode);
    }
    private static bool HasSafeIntegerDivisorsCore(
        SymbolicCondition condition,
        SymbolicState state,
        SyntaxNode sourceNode) {
        switch (condition) {
            case SymbolicFactCondition factCondition:
                return HasSafeIntegerDivisorsCore(factCondition.Fact.Atom, state, sourceNode);
            case SymbolicNotCondition notCondition:
                return HasSafeIntegerDivisorsCore(notCondition.Operand, state, sourceNode);
            case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.And } andCondition:
                return HasSafeIntegerDivisorsInShortCircuitRight(
                    andCondition.Left,
                    andCondition.Right,
                    state,
                    sourceNode,
                    leftMustBeTrue: true);
            case SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or } orCondition:
                return HasSafeIntegerDivisorsInShortCircuitRight(
                    orCondition.Left,
                    orCondition.Right,
                    state,
                    sourceNode,
                    leftMustBeTrue: false);
            case SymbolicBinaryCondition binaryCondition:
                return HasSafeIntegerDivisorsCore(binaryCondition.Left, state, sourceNode) &&
                       HasSafeIntegerDivisorsCore(binaryCondition.Right, state, sourceNode);
            default:
                return true;
        }
    }
    private static bool HasSafeIntegerDivisorsInShortCircuitRight(
        SymbolicCondition left,
        SymbolicCondition right,
        SymbolicState state,
        SyntaxNode sourceNode,
        bool leftMustBeTrue) {
        if (!HasSafeIntegerDivisorsCore(left, state, sourceNode)) return false;
        var rightState = AssumePathCondition(
            state, leftMustBeTrue ? left : new SymbolicNotCondition(left));
        return rightState.IsContradictory ||
               HasSafeIntegerDivisorsCore(right, rightState, sourceNode);
    }
    private static bool HasSafeIntegerDivisorsCore(
        SymbolicAtom atom,
        SymbolicState state,
        SyntaxNode sourceNode) =>
        HasSafeIntegerDivisorsInChildren(SymbolicIrChildren.OfAtom(atom), state, sourceNode);
    private static bool IsTermProvablyNonZero(SymbolicTerm term, SymbolicState state, SyntaxNode sourceNode) {
        if (term is SymbolicIntegerConstantTerm integerConstant) return integerConstant.Value != 0;
        var zero = new SymbolicIntegerConstantTerm(0);
        foreach (var relationOperator in new[] {
                     SymbolicRelationOperator.NotEqual,
                     SymbolicRelationOperator.GreaterThan,
                     SymbolicRelationOperator.LessThan
                 }) {
            var nonZeroFact = SymbolicFact.Exact(
                new SymbolicRelationAtom(relationOperator, term, zero),
                sourceNode,
                "ir.safe-divisor.non-zero");
            if (SymbolicProofStateFacts.StateContainsFact(state, nonZeroFact)) return true;
        }
        var zeroCondition = SymbolicIrLowerer.CreateIntegerZeroCondition(term, sourceNode, "ir.safe-divisor.zero");
        if (zeroCondition is SymbolicFactCondition factCondition) {
            if (SymbolicProofStateFacts.StateContradictsFact(state, factCondition.Fact)) return true;
            if (SymbolicProofStateFacts.StateContainsFact(state, factCondition.Fact)) return false;
        }
        if (SymbolicProofStateFacts.TryEvaluateConditionFromState(state, zeroCondition, out var value)) return !value;
        return SymbolicProofStateFacts.StateContradictsCondition(state, zeroCondition);
    }
    private static SymbolicState AssumePathCondition(SymbolicState state, SymbolicCondition condition) =>
        SymbolicProofStateFacts.NormalizeState(state.AddPathCondition(condition));
    internal static SymbolicEncodedState EncodeState(SymbolicState state) {
        var builder = ImmutableArray.CreateBuilder<SmtFormula>(state.Facts.Length + state.PathConditions.Length);
        var skippedUnsupported = false;
        foreach (var fact in state.Facts) {
            if (!TryEncodeFactWithPathState(fact, state, s_syntheticProofNode, out var formula)) {
                skippedUnsupported = true;
                continue;
            }
            builder.Add(formula);
        }
        foreach (var condition in state.PathConditions) {
            if (!TryEncodeConditionWithPathState(condition, state, s_syntheticProofNode, false, out var formula)) {
                skippedUnsupported = true;
                continue;
            }
            builder.Add(formula);
        }
        if (skippedUnsupported)
            return new SymbolicEncodedState(false, [], SymbolicUnknownReason.UnsupportedIrEncoding);
        return new SymbolicEncodedState(true, builder.ToImmutable(), SymbolicUnknownReason.None);
    }
}
