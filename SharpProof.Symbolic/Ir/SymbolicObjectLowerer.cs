using static SharpProof.Symbolic.Ir.SymbolicIrLowerer;
namespace SharpProof.Symbolic.Ir;
internal static class SymbolicObjectLowerer {
    internal static bool TryLowerObjectReferenceEqualsInvocation(
        InvocationExpressionSyntax invocation,
        IInvocationOperation operation,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        var method = operation.TargetMethod;
        if (!method.IsStatic ||
            invocation.ArgumentList.Arguments.Count != 2 ||
            method.Parameters.Length != 2 ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpressionByOrdinal(operation, 0, out var leftExpression) ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpressionByOrdinal(operation, 1, out var rightExpression) ||
            LowerTerm(leftExpression, context) is not { } left ||
            LowerTerm(rightExpression, context) is not { } right ||
            !SymbolicOperatorLowerer.CanCompareTerms(left, right, SymbolicRelationOperator.Equal) ||
            (left.Kind != SmtValueKind.Reference && right.Kind != SmtValueKind.Reference))
            return false;
        condition = CreateFactCondition(
            new SymbolicRelationAtom(SymbolicRelationOperator.Equal, left, right),
            invocation,
            "ir.known-api.object.reference-equals");
        return true;
    }
}
