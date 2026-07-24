namespace SharpProof.Symbolic;
internal static class SymbolicProofEncoder {
    private static readonly ExpressionSyntax s_proofNode = SyntaxFactory.IdentifierName("__symbolic_proof__");
    private static readonly SymbolicRelationOperator[] s_nonZeroRelations = [
        SymbolicRelationOperator.NotEqual,
        SymbolicRelationOperator.GreaterThan,
        SymbolicRelationOperator.LessThan
    ];
    internal static bool TryEncodeConditionWithPathState(
        SymbolicCondition condition,
        SymbolicState state,
        out SmtFormula formula) =>
        TryEncode(condition, state, s_proofNode, true, out formula);
    internal static bool TryEncodeConditionWithPathState(
        SymbolicCondition condition,
        SymbolicState state,
        SyntaxNode sourceNode,
        out SmtFormula formula) =>
        TryEncode(condition, state, sourceNode, true, out formula);
    internal static bool TryEncodeFactWithPathState(
        SymbolicFact fact,
        SymbolicState state,
        out SmtFormula formula) =>
        TryEncodeFactWithPathState(fact, state, s_proofNode, out formula);
    internal static bool TryEncodeFactWithPathState(
        SymbolicFact fact,
        SymbolicState state,
        SyntaxNode sourceNode,
        out SmtFormula formula) {
        if (fact == null) throw new ArgumentNullException(nameof(fact));
        return TryEncode(new SymbolicFactCondition(fact), state, sourceNode, false, out formula);
    }
    private static bool TryEncode(
        SymbolicCondition condition,
        SymbolicState state,
        SyntaxNode sourceNode,
        bool rewriteVersions,
        out SmtFormula formula) {
        if (condition == null) throw new ArgumentNullException(nameof(condition));
        if (state == null) throw new ArgumentNullException(nameof(state));
        if (sourceNode == null) throw new ArgumentNullException(nameof(sourceNode));
        state = state.Normalize();
        if (rewriteVersions)
            condition = SymbolicProofStateFacts.RewriteQueryConditionToCurrentVersions(condition, state);
        if (!state.IsContradictory && !HasSafeDivisors(condition, state, sourceNode)) {
            formula = null!;
            return false;
        }
        return SymbolicIrFormulaEncoder.TryEncode(condition, out formula);
    }
    private static bool HasSafeDivisors(SymbolicTerm term, SymbolicState state, SyntaxNode source) {
        if (term is SymbolicConditionalTerm conditional) {
            if (!HasSafeDivisors(conditional.Condition, state, source)) return false;
            var whenTrue = Assume(state, conditional.Condition);
            var whenFalse = Assume(state, new SymbolicNotCondition(conditional.Condition));
            return (whenTrue.IsContradictory || HasSafeDivisors(conditional.WhenTrue, whenTrue, source)) &&
                   (whenFalse.IsContradictory || HasSafeDivisors(conditional.WhenFalse, whenFalse, source));
        }
        if (term is SymbolicBinaryTerm binary &&
            (!HasSafeDivisors(binary.Left, state, source) ||
             !HasSafeDivisors(binary.Right, state, source) ||
             binary.Operator is SymbolicBinaryTermOperator.Divide or SymbolicBinaryTermOperator.Remainder &&
             !IsNonZero(binary.Right, state, source)))
            return false;
        return term is SymbolicBinaryTerm || HasSafeDivisors(SymbolicIrChildren.Of(term), state, source);
    }
    private static bool HasSafeDivisors(SymbolicCondition condition, SymbolicState state, SyntaxNode source) =>
        condition switch {
            SymbolicFactCondition fact => HasSafeDivisors(fact.Fact.Atom, state, source),
            SymbolicNotCondition not => HasSafeDivisors(not.Operand, state, source),
            SymbolicBinaryCondition { Operator: SymbolicConditionOperator.And } and =>
                HasSafeShortCircuit(and.Left, and.Right, state, source, true),
            SymbolicBinaryCondition { Operator: SymbolicConditionOperator.Or } or =>
                HasSafeShortCircuit(or.Left, or.Right, state, source, false),
            SymbolicBinaryCondition binary =>
                HasSafeDivisors(binary.Left, state, source) && HasSafeDivisors(binary.Right, state, source),
            _ => true
        };
    private static bool HasSafeShortCircuit(
        SymbolicCondition left,
        SymbolicCondition right,
        SymbolicState state,
        SyntaxNode source,
        bool leftMustBeTrue) {
        if (!HasSafeDivisors(left, state, source)) return false;
        var rightState = Assume(state, leftMustBeTrue ? left : new SymbolicNotCondition(left));
        return rightState.IsContradictory || HasSafeDivisors(right, rightState, source);
    }
    private static bool HasSafeDivisors(SymbolicAtom atom, SymbolicState state, SyntaxNode source) =>
        HasSafeDivisors(SymbolicIrChildren.Of(atom), state, source);
    private static bool HasSafeDivisors(SymbolicIrChildren children, SymbolicState state, SyntaxNode source) =>
        (children.First == null || HasSafeDivisors(children.First, state, source)) &&
        (children.Second == null || HasSafeDivisors(children.Second, state, source)) &&
        (children.Rest.IsDefaultOrEmpty || children.Rest.All(term => HasSafeDivisors(term, state, source))) &&
        (children.Condition == null || HasSafeDivisors(children.Condition, state, source));
    private static bool IsNonZero(SymbolicTerm term, SymbolicState state, SyntaxNode source) {
        if (term is SymbolicIntegerConstantTerm constant) return constant.Value != 0;
        var zero = new SymbolicIntegerConstantTerm(0);
        foreach (var op in s_nonZeroRelations)
            if (state.ProofIndex.ContainsFact(SymbolicFact.Exact(
                    new SymbolicRelationAtom(op, term, zero),
                    source,
                    "ir.safe-divisor.non-zero")))
                return true;
        var condition = SymbolicIrLowerer.CreateIntegerZeroCondition(term, source, "ir.safe-divisor.zero");
        return condition is SymbolicFactCondition fact && state.ProofIndex.ContainsFact(fact.Fact)
            ? false
            : SymbolicProofStateFacts.TryEvaluateConditionFromState(state, condition, out var value)
                ? !value
                : SymbolicProofStateFacts.StateContradictsCondition(state, condition);
    }
    private static SymbolicState Assume(SymbolicState state, SymbolicCondition condition) =>
        state.AddPathCondition(condition).Normalize();
    internal static SymbolicEncodedState EncodeState(SymbolicState state) {
        var formulas = ImmutableArray.CreateBuilder<SmtFormula>(state.Facts.Length + state.PathConditions.Length);
        foreach (var fact in state.Facts)
            if (TryEncodeFactWithPathState(fact, state, s_proofNode, out var formula))
                formulas.Add(formula);
            else
                return new SymbolicEncodedState(false, [], SymbolicUnknownReason.UnsupportedIrEncoding);
        foreach (var condition in state.PathConditions)
            if (TryEncode(condition, state, s_proofNode, false, out var formula))
                formulas.Add(formula);
            else
                return new SymbolicEncodedState(false, [], SymbolicUnknownReason.UnsupportedIrEncoding);
        return new SymbolicEncodedState(true, formulas.ToImmutable(), SymbolicUnknownReason.None);
    }
}
