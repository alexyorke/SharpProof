using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;

namespace PurelySharp.Symbolic.Ir
{
    internal static partial class SymbolicIrLowerer
    {
        private static bool TryLowerObjectReferenceEqualsInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol method,
            SymbolicLoweringContext context,
            out SymbolicCondition condition)
        {
            condition = null!;
            if (!method.IsStatic ||
                invocation.ArgumentList.Arguments.Count != 2 ||
                method.Parameters.Length != 2 ||
                !TryLowerTerm(invocation.ArgumentList.Arguments[0].Expression, context, out var left) ||
                !TryLowerTerm(invocation.ArgumentList.Arguments[1].Expression, context, out var right) ||
                !CanCompareTerms(left, right, SymbolicRelationOperator.Equal) ||
                left.Kind != SmtValueKind.Reference && right.Kind != SmtValueKind.Reference)
            {
                return false;
            }

            condition = CreateFactCondition(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    left,
                    right),
                invocation,
                "ir.known-api.object.reference-equals");
            return true;
        }
    }
}
