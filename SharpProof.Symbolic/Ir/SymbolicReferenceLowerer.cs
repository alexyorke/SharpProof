using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Smt;

namespace SharpProof.Symbolic.Ir;

internal static class SymbolicReferenceLowerer
{
    internal static bool TryLowerReferenceTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        expression = SymbolicIrLowerer.UnwrapExpression(expression);
        var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        var expressionType = typeInfo.ConvertedType ?? typeInfo.Type;
        if (expressionType is not { IsReferenceType: true })
        {
            term = null!;
            return false;
        }

        if (SymbolicNullableLowerer.IsNullConstant(expression, context))
        {
            term = new SymbolicNullTerm();
            return true;
        }

        if (expression is ConditionalAccessExpressionSyntax conditionalAccess &&
            TryLowerReferenceConditionalAccessTerm(conditionalAccess, context, out term))
            return true;

        if (expression is ConditionalExpressionSyntax conditionalExpression &&
            SymbolicIrLowerer.TryLowerCondition(conditionalExpression.Condition, context, out var condition) &&
            TryLowerReferenceTerm(conditionalExpression.WhenTrue, context, out var whenTrue) &&
            TryLowerReferenceTerm(conditionalExpression.WhenFalse, context, out var whenFalse))
        {
            term = new SymbolicConditionalTerm(condition, whenTrue, whenFalse);
            return true;
        }

        if (expression is BinaryExpressionSyntax coalesceExpression &&
            coalesceExpression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.CoalesceExpression) &&
            TryLowerReferenceTerm(coalesceExpression.Left, context, out var coalesceLeft) &&
            TryLowerReferenceTerm(coalesceExpression.Right, context, out var coalesceRight))
        {
            term = new SymbolicConditionalTerm(
                SymbolicIrLowerer.CreateReferenceNullCondition(
                    coalesceLeft,
                    false,
                    coalesceExpression.Left,
                    "ir.reference.coalesce.left-not-null"),
                coalesceLeft,
                coalesceRight);
            return true;
        }

        if (expression is BinaryExpressionSyntax asExpression &&
            asExpression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AsExpression) &&
            SymbolicConversionLowerer.TryLowerReferenceAsTerm(asExpression, context, out term))
            return true;

        if (expression is MemberAccessExpressionSyntax memberAccess &&
            SymbolicMemberLowerer.TryLowerMemberTerm(memberAccess, context, out term) &&
            term.Kind == SmtValueKind.Reference)
            return true;

        if (expression is ElementAccessExpressionSyntax elementAccess &&
            SymbolicIrLowerer.TryLowerElementAccessTerm(elementAccess, context, out term) &&
            term.Kind == SmtValueKind.Reference)
            return true;

        if (expression is ThisExpressionSyntax)
        {
            term = context.ImplicitThis;
            return true;
        }

        if (SymbolicMemberLowerer.TryLowerImplicitThisMemberTerm(expression, context, out term) &&
            term.Kind == SmtValueKind.Reference)
            return true;

        var symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
        if (symbol != null && context.TryGetSubstitution(symbol, out term) &&
            term.Kind == SmtValueKind.Reference)
            return true;

        if (symbol is ILocalSymbol or IParameterSymbol)
        {
            term = new SymbolicVariableTerm(context.GetVariableName(symbol), SmtValueKind.Reference);
            return true;
        }

        term = null!;
        return false;
    }

    internal static bool TryLowerReferenceConditionalAccessTerm(
        ConditionalAccessExpressionSyntax conditionalAccess,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        var resultType =
            context.SemanticModel.GetTypeInfo(conditionalAccess, context.CancellationToken).ConvertedType ??
            context.SemanticModel.GetTypeInfo(conditionalAccess, context.CancellationToken).Type;
        if (resultType is not { IsReferenceType: true } ||
            !SymbolicIrLowerer.TryLowerTerm(conditionalAccess.Expression, context, out var receiver) ||
            receiver.Kind != SmtValueKind.Reference ||
            !TryLowerConditionalAccessWhenNotNullReferenceTerm(
                conditionalAccess,
                receiver,
                resultType,
                context,
                out var whenNotNull))
            return false;

        term = new SymbolicConditionalTerm(
            SymbolicIrLowerer.CreateReferenceNullCondition(
                receiver,
                false,
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
            if (!SymbolicMemberLowerer.TryGetInstanceMemberSymbol(memberBinding, context, out var memberSymbol) ||
                !SymbolicTypeLowerer.TryGetSymbolType(memberSymbol, out var memberType) ||
                !SymbolEqualityComparer.Default.Equals(memberType, expectedType) ||
                !SymbolicTypeLowerer.TryGetValueKind(memberType, out var memberKind) ||
                memberKind != SmtValueKind.Reference)
                return false;

            term = new SymbolicMemberTerm(receiver, memberSymbol.Name, memberKind);
            return true;
        }

        if (conditionalAccess.WhenNotNull is ElementBindingExpressionSyntax elementBinding &&
            elementBinding.ArgumentList.Arguments.Count == 1 &&
            context.SemanticModel.GetTypeInfo(conditionalAccess.Expression, context.CancellationToken).Type is
                IArrayTypeSymbol { Rank: 1 } arrayType &&
            SymbolEqualityComparer.Default.Equals(arrayType.ElementType, expectedType) &&
            SymbolicTypeLowerer.TryGetValueKind(arrayType.ElementType, out var elementKind) &&
            elementKind == SmtValueKind.Reference &&
            SymbolicIrLowerer.TryLowerTerm(elementBinding.ArgumentList.Arguments[0].Expression, context, out var index) &&
            index.Kind == SmtValueKind.Int)
        {
            term = new SymbolicElementTerm(receiver, index, elementKind);
            return true;
        }

        return false;
    }
}
