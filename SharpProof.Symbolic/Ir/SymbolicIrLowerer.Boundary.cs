using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Ir;

internal static partial class SymbolicIrLowerer
{
    internal static SymbolicTerm? LowerTerm(ExpressionSyntax expression, SymbolicLoweringContext context)
    {
        return TryLowerTerm(expression, context, out var term) ? term : null;
    }

    internal static SymbolicCondition? LowerCondition(ExpressionSyntax expression, SymbolicLoweringContext context)
    {
        return TryLowerCondition(expression, context, out var condition) ? condition : null;
    }

    internal static SymbolicCondition? LowerPatternCondition(
        SymbolicTerm value,
        ITypeSymbol? valueType,
        PatternSyntax pattern,
        SyntaxNode source,
        SymbolicLoweringContext context)
    {
        return TryLowerPatternCondition(value, valueType, pattern, source, context, out var condition)
            ? condition
            : null;
    }

    internal static SymbolicCondition? LowerPatternCondition(
        SymbolicTerm value,
        PatternSyntax pattern,
        SyntaxNode source,
        SymbolicLoweringContext context)
    {
        return TryLowerPatternCondition(value, pattern, source, context, out var condition)
            ? condition
            : null;
    }

    internal static SymbolicTerm? LowerReferenceTerm(ExpressionSyntax expression, SymbolicLoweringContext context)
    {
        return TryLowerReferenceTerm(expression, context, out var term) ? term : null;
    }

    internal static SymbolicTerm? LowerStringTerm(ExpressionSyntax expression, SymbolicLoweringContext context)
    {
        return TryLowerStringTerm(expression, context, out var term) ? term : null;
    }

    internal static SymbolicTerm? LowerBooleanValueTerm(ExpressionSyntax expression, SymbolicLoweringContext context)
    {
        return TryLowerBooleanValueTerm(expression, context, out var term) ? term : null;
    }

    internal static SymbolicTerm? LowerBuiltInLengthTerm(ExpressionSyntax expression, SymbolicLoweringContext context)
    {
        return TryLowerBuiltInLengthTerm(expression, context, out var term) ? term : null;
    }

    internal static SymbolicTerm? ProjectBuiltInLengthTerm(ITypeSymbol? receiverType, SymbolicTerm receiver)
    {
        return TryCreateBuiltInLengthReferenceTerm(receiverType, receiver, out var term) ? term : null;
    }

    internal static SymbolicTerm? ProjectStringContentTerm(SymbolicTerm receiver)
    {
        return TryCreateStringContentReferenceTerm(receiver, out var term) ? term : null;
    }

    internal static SymbolicTerm? LowerArrayDimensionLengthTerm(
        ExpressionSyntax expression,
        int dimension,
        SymbolicLoweringContext context)
    {
        return TryLowerArrayDimensionLengthTerm(expression, dimension, context, out var term) ? term : null;
    }

    internal static SymbolicTerm? LowerNullableHasValueTerm(ExpressionSyntax expression, SymbolicLoweringContext context)
    {
        return TryLowerNullableHasValueTerm(expression, context, out var term) ? term : null;
    }

    internal static SymbolicTerm? LowerNullableValueTerm(ExpressionSyntax expression, SymbolicLoweringContext context)
    {
        return TryLowerNullableValueTerm(expression, context, out var term) ? term : null;
    }

    internal static SymbolicCondition? LowerStringNonNullCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        return TryLowerStringNonNullCondition(expression, context, out var condition) ? condition : null;
    }

    internal static SymbolicTerm? LowerNotNullIfNotNullAssignedResultTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        return TryLowerNotNullIfNotNullResultNonNullTerm(
                expression,
                context,
                true,
                out var term)
            ? term
            : null;
    }

    internal static SymbolicCondition? LowerArrayElementBoundsCondition(
        ExpressionSyntax arrayExpression,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        SyntaxNode source,
        string provenance,
        SymbolicLoweringContext context)
    {
        return TryCreateArrayElementBoundsCondition(
            arrayExpression,
            indexExpressions,
            source,
            provenance,
            context,
            out var condition,
            out _)
            ? condition
            : null;
    }

    internal static SymbolicCondition? LowerBuiltInElementAccessInRangeCondition(
        ExpressionSyntax receiverExpression,
        ExpressionSyntax indexExpression,
        SyntaxNode source,
        string provenance,
        SymbolicLoweringContext context)
    {
        return TryCreateBuiltInElementAccessInRangeCondition(
            receiverExpression,
            indexExpression,
            source,
            provenance,
            context,
            out var condition)
            ? condition
            : null;
    }

    internal static SymbolicCondition? LowerSubsequenceInRangeCondition(
        ExpressionSyntax receiverExpression,
        ExpressionSyntax startExpression,
        ExpressionSyntax? lengthExpression,
        SyntaxNode source,
        string provenance,
        SymbolicLoweringContext context,
        bool oneArgumentUpperBoundIsInclusive)
    {
        return TryCreateSubsequenceInRangeCondition(
            receiverExpression,
            startExpression,
            lengthExpression,
            source,
            provenance,
            context,
            oneArgumentUpperBoundIsInclusive,
            out var condition)
            ? condition
            : null;
    }

    internal static SymbolicBinaryTermOperator? GetBinaryTermOperator(SmtIntegerBinaryOperator smtOperator)
    {
        return TryGetBinaryTermOperator(smtOperator, out var binaryOperator) ? binaryOperator : null;
    }
}
