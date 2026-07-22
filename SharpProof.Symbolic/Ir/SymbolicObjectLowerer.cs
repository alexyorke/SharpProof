using static SharpProof.Symbolic.Ir.SymbolicIrLowerer;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicObjectLowerer {
    internal static bool TryLowerObjectReferenceEqualsInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (!method.IsStatic ||
            invocation.ArgumentList.Arguments.Count != 2 ||
            method.Parameters.Length != 2 ||
            LowerTerm(invocation.ArgumentList.Arguments[0].Expression, context) is not { } left ||
            LowerTerm(invocation.ArgumentList.Arguments[1].Expression, context) is not { } right ||
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
