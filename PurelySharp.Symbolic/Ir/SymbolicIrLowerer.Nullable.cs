using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;

namespace PurelySharp.Symbolic.Ir
{
    internal static partial class SymbolicIrLowerer
    {
        private static readonly ImmutableArray<KnownApiTermLoweringDescriptor> KnownApiTermLowerings =
            ImmutableArray.Create(
                new KnownApiTermLoweringDescriptor(
                    SpecialType.System_Nullable_T,
                    nameof(Nullable<int>.GetValueOrDefault),
                    TryLowerNullableGetValueOrDefaultInvocation));

        private static bool TryLowerNullableGetValueOrDefaultInvocation(
            InvocationExpressionSyntax invocation,
            IMethodSymbol method,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                invocation.ArgumentList.Arguments.Count is not 0 and not 1 ||
                method.Parameters.Length != invocation.ArgumentList.Arguments.Count ||
                method.ContainingType?.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T ||
                !TryLowerNullableHasValueTerm(memberAccess.Expression, context, out var hasValueTerm) ||
                !TryLowerNullableValueTerm(memberAccess.Expression, context, out var valueTerm))
            {
                return false;
            }

            SymbolicTerm fallbackTerm;
            if (invocation.ArgumentList.Arguments.Count == 0)
            {
                if (!TryCreateDefaultTerm(method.ReturnType, out fallbackTerm))
                {
                    return false;
                }
            }
            else if (!TryLowerTerm(invocation.ArgumentList.Arguments[0].Expression, context, out fallbackTerm) ||
                fallbackTerm.Kind != valueTerm.Kind)
            {
                return false;
            }

            term = new SymbolicConditionalTerm(
                CreateFactCondition(
                    new SymbolicTruthAtom(hasValueTerm),
                    invocation,
                    "ir.known-api.nullable.get-value-or-default.has-value"),
                valueTerm,
                fallbackTerm);
            return true;
        }

        private static bool TryCreateDefaultTerm(ITypeSymbol type, out SymbolicTerm term)
        {
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                term = new SymbolicBooleanConstantTerm(false);
                return true;
            }

            if (TryGetValueKind(type, out var kind) &&
                kind == SmtValueKind.Int)
            {
                term = new SymbolicIntegerConstantTerm(0);
                return true;
            }

            term = null!;
            return false;
        }
    }
}
