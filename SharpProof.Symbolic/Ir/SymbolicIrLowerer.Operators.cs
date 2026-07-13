using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.ProofCore.Smt;

namespace SharpProof.Symbolic.Ir;

internal static partial class SymbolicIrLowerer
{
    private static bool TryLowerBuiltInBooleanBitwiseCondition(
        BinaryExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (expression.Kind() is not (SyntaxKind.BitwiseAndExpression or
            SyntaxKind.BitwiseOrExpression or
            SyntaxKind.ExclusiveOrExpression) ||
            context.SemanticModel.GetOperation(expression, context.CancellationToken) is not
                Microsoft.CodeAnalysis.Operations.IBinaryOperation
                {
                    OperatorMethod: null,
                    Type.SpecialType: SpecialType.System_Boolean
                } ||
            !TryLowerCondition(expression.Left, context, out var left) ||
            !TryLowerCondition(expression.Right, context, out var right))
            return false;

        condition = expression.Kind() switch
        {
            SyntaxKind.BitwiseAndExpression =>
                new SymbolicBinaryCondition(SymbolicConditionOperator.And, left, right),
            SyntaxKind.BitwiseOrExpression =>
                new SymbolicBinaryCondition(SymbolicConditionOperator.Or, left, right),
            _ => new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    left,
                    new SymbolicNotCondition(right)),
                new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    new SymbolicNotCondition(left),
                    right))
        };
        return true;
    }

    internal static bool CanCompareTerms(SymbolicTerm left, SymbolicTerm right, SymbolicRelationOperator op)
    {
        if (op is not SymbolicRelationOperator.Equal and not SymbolicRelationOperator.NotEqual &&
            left.Kind != SmtValueKind.Int)
            return false;

        return left.Kind == right.Kind ||
               (left is SymbolicNullTerm && right.Kind == SmtValueKind.Reference) ||
               (right is SymbolicNullTerm && left.Kind == SmtValueKind.Reference);
    }

    private static bool IsEqualityExpression(BinaryExpressionSyntax binaryExpression)
    {
        return binaryExpression.IsKind(SyntaxKind.EqualsExpression) ||
               binaryExpression.IsKind(SyntaxKind.NotEqualsExpression);
    }

    internal static bool TryGetRelationOperator(SyntaxKind kind, out SymbolicRelationOperator op)
    {
        switch (kind)
        {
            case SyntaxKind.EqualsExpression:
                op = SymbolicRelationOperator.Equal;
                return true;
            case SyntaxKind.NotEqualsExpression:
                op = SymbolicRelationOperator.NotEqual;
                return true;
            case SyntaxKind.LessThanExpression:
                op = SymbolicRelationOperator.LessThan;
                return true;
            case SyntaxKind.LessThanOrEqualExpression:
                op = SymbolicRelationOperator.LessThanOrEqual;
                return true;
            case SyntaxKind.GreaterThanExpression:
                op = SymbolicRelationOperator.GreaterThan;
                return true;
            case SyntaxKind.GreaterThanOrEqualExpression:
                op = SymbolicRelationOperator.GreaterThanOrEqual;
                return true;
            default:
                op = default;
                return false;
        }
    }

    private static bool TryGetBinaryTermOperator(SyntaxKind kind, out SymbolicBinaryTermOperator op)
    {
        switch (kind)
        {
            case SyntaxKind.AddExpression:
                op = SymbolicBinaryTermOperator.Add;
                return true;
            case SyntaxKind.SubtractExpression:
                op = SymbolicBinaryTermOperator.Subtract;
                return true;
            case SyntaxKind.MultiplyExpression:
                op = SymbolicBinaryTermOperator.Multiply;
                return true;
            case SyntaxKind.DivideExpression:
                op = SymbolicBinaryTermOperator.Divide;
                return true;
            case SyntaxKind.ModuloExpression:
                op = SymbolicBinaryTermOperator.Remainder;
                return true;
            default:
                op = default;
                return false;
        }
    }

    private static bool TryGetBinaryTermOperator(SmtIntegerBinaryOperator smtOperator, out SymbolicBinaryTermOperator op)
    {
        switch (smtOperator)
        {
            case SmtIntegerBinaryOperator.Add:
                op = SymbolicBinaryTermOperator.Add;
                return true;
            case SmtIntegerBinaryOperator.Subtract:
                op = SymbolicBinaryTermOperator.Subtract;
                return true;
            case SmtIntegerBinaryOperator.Multiply:
                op = SymbolicBinaryTermOperator.Multiply;
                return true;
            case SmtIntegerBinaryOperator.Divide:
                op = SymbolicBinaryTermOperator.Divide;
                return true;
            case SmtIntegerBinaryOperator.Remainder:
                op = SymbolicBinaryTermOperator.Remainder;
                return true;
            default:
                op = default;
                return false;
        }
    }
}
