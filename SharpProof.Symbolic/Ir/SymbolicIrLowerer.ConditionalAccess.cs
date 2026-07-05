using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Ir
{
    internal static partial class SymbolicIrLowerer
    {
        private static bool TryLowerReferenceConditionalAccessTerm(
            ConditionalAccessExpressionSyntax conditionalAccess,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;
            var resultType = context.SemanticModel.GetTypeInfo(conditionalAccess, context.CancellationToken).ConvertedType ??
                context.SemanticModel.GetTypeInfo(conditionalAccess, context.CancellationToken).Type;
            if (resultType is not { IsReferenceType: true } ||
                !TryLowerTerm(conditionalAccess.Expression, context, out var receiver) ||
                receiver.Kind != SmtValueKind.Reference ||
                !TryLowerConditionalAccessWhenNotNullReferenceTerm(
                    conditionalAccess,
                    receiver,
                    resultType,
                    context,
                    out var whenNotNull))
            {
                return false;
            }

            term = new SymbolicConditionalTerm(
                CreateReferenceNullCondition(
                    receiver,
                    equalToNull: false,
                    conditionalAccess.Expression,
                    "ir.conditional-access.receiver-not-null"),
                whenNotNull,
                new SymbolicNullTerm());
            return true;
        }

        private static bool TryLowerConditionalAccessWhenNotNullReferenceTerm(
            ConditionalAccessExpressionSyntax conditionalAccess,
            SymbolicTerm receiver,
            ITypeSymbol expectedType,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;
            if (conditionalAccess.WhenNotNull is MemberBindingExpressionSyntax memberBinding)
            {
                if (!TryGetInstanceMemberSymbol(memberBinding, context, out var memberSymbol) ||
                    !TryGetSymbolType(memberSymbol, out var memberType) ||
                    !SymbolEqualityComparer.Default.Equals(memberType, expectedType) ||
                    !TryGetValueKind(memberType, out var memberKind) ||
                    memberKind != SmtValueKind.Reference)
                {
                    return false;
                }

                term = new SymbolicMemberTerm(receiver, memberSymbol.Name, memberKind);
                return true;
            }

            if (conditionalAccess.WhenNotNull is ElementBindingExpressionSyntax elementBinding &&
                elementBinding.ArgumentList.Arguments.Count == 1 &&
                context.SemanticModel.GetTypeInfo(conditionalAccess.Expression, context.CancellationToken).Type is IArrayTypeSymbol { Rank: 1 } arrayType &&
                SymbolEqualityComparer.Default.Equals(arrayType.ElementType, expectedType) &&
                TryGetValueKind(arrayType.ElementType, out var elementKind) &&
                elementKind == SmtValueKind.Reference &&
                TryLowerTerm(elementBinding.ArgumentList.Arguments[0].Expression, context, out var index) &&
                index.Kind == SmtValueKind.Int)
            {
                term = new SymbolicElementTerm(receiver, index, elementKind);
                return true;
            }

            return false;
        }
    }
}
