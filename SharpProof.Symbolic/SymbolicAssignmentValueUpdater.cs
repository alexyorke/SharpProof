using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Symbolic;

internal static class SymbolicAssignmentValueUpdater
{
    internal static bool TryCreateIncrementOrDecrement(
        SymbolicTerm previousValue,
        int delta,
        ExpressionSyntax updateExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ISymbol targetSymbol,
        out SymbolicTerm updatedValue)
    {
        updatedValue = null!;
        if (previousValue.Kind != SmtValueKind.Int ||
            delta is not 1 and not -1 ||
            semanticModel.GetOperation(updateExpression, cancellationToken) is not IIncrementOrDecrementOperation
            {
                OperatorMethod: null
            } operation ||
            !TryGetTargetRange(targetSymbol, out var minimum, out var maximum))
            return false;

        if (previousValue is SymbolicIntegerConstantTerm integerConstant)
            return TryCreateConstantResult(
                integerConstant.Value + (BigInteger)delta,
                minimum,
                maximum,
                operation.IsChecked,
                out updatedValue);

        var mathematicalTerm = new SymbolicBinaryTerm(
            delta > 0
                ? SymbolicBinaryTermOperator.Add
                : SymbolicBinaryTermOperator.Subtract,
            previousValue,
            new SymbolicIntegerConstantTerm(1));
        updatedValue = SymbolicIrLowerer.CreateOverflowAwareBinaryTerm(
            mathematicalTerm,
            minimum,
            maximum,
            updateExpression,
            "ir.path.prior-statement.update",
            operation.IsChecked);
        return true;
    }

    internal static bool TryCreateCompoundAssignment(
        SymbolicTerm previousValue,
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ISymbol targetSymbol,
        out SymbolicTerm updatedValue)
    {
        updatedValue = null!;
        var lowering = SymbolicSemanticPipeline.LowerTerm(
            assignment.Right,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        var targetName = SymbolicFactFactory.GetSmtVariableName(targetSymbol);
        if (previousValue.Kind != SmtValueKind.Int ||
            semanticModel.GetOperation(assignment, cancellationToken) is not ICompoundAssignmentOperation
            {
                OperatorMethod: null
            } operation ||
            !TryGetTargetRange(targetSymbol, out var minimum, out var maximum) ||
            !TryGetOperator(assignment.Kind(), out var binaryOperator) ||
            lowering is not { IsExact: true, Value: { } rightTerm } ||
            rightTerm.Kind != SmtValueKind.Int ||
            SymbolicIrReferenceScanner.ContainsVariableOrMember(previousValue, targetName) ||
            SymbolicIrReferenceScanner.ContainsVariableOrMember(rightTerm, targetName))
            return false;

        if (previousValue is SymbolicIntegerConstantTerm leftConstant &&
            rightTerm is SymbolicIntegerConstantTerm rightConstant)
            return TryCreateConstantBinaryResult(
                leftConstant.Value,
                rightConstant.Value,
                binaryOperator,
                minimum,
                maximum,
                operation.IsChecked,
                out updatedValue);

        if (binaryOperator is SymbolicBinaryTermOperator.Divide or SymbolicBinaryTermOperator.Remainder &&
            rightTerm is SymbolicIntegerConstantTerm { Value: 0 })
            return false;

        var mathematicalTerm = new SymbolicBinaryTerm(binaryOperator, previousValue, rightTerm);
        if (binaryOperator is SymbolicBinaryTermOperator.Add or
            SymbolicBinaryTermOperator.Subtract or
            SymbolicBinaryTermOperator.Multiply)
        {
            updatedValue = SymbolicIrLowerer.CreateOverflowAwareBinaryTerm(
                mathematicalTerm,
                minimum,
                maximum,
                assignment,
                "ir.path.prior-statement.compound-assignment",
                operation.IsChecked);
            return true;
        }

        if (minimum < 0)
        {
            var overflowCondition = SymbolicIrLowerer.CreateSignedDivisionOverflowCondition(
                previousValue,
                rightTerm,
                minimum,
                assignment,
                "ir.path.prior-statement.compound-assignment.signed-division-overflow");
            updatedValue = new SymbolicConditionalTerm(
                overflowCondition,
                mathematicalTerm with { MayOverflow = true },
                mathematicalTerm);
            return true;
        }

        updatedValue = mathematicalTerm;
        return true;
    }

    private static bool TryCreateConstantBinaryResult(
        long left,
        long right,
        SymbolicBinaryTermOperator binaryOperator,
        long minimum,
        long maximum,
        bool isChecked,
        out SymbolicTerm updatedValue)
    {
        updatedValue = null!;
        if (binaryOperator is SymbolicBinaryTermOperator.Divide or SymbolicBinaryTermOperator.Remainder)
        {
            if (right == 0 || minimum < 0 && left == minimum && right == -1) return false;
        }

        var leftValue = (BigInteger)left;
        var rightValue = (BigInteger)right;
        var result = binaryOperator switch
        {
            SymbolicBinaryTermOperator.Add => leftValue + rightValue,
            SymbolicBinaryTermOperator.Subtract => leftValue - rightValue,
            SymbolicBinaryTermOperator.Multiply => leftValue * rightValue,
            SymbolicBinaryTermOperator.Divide => leftValue / rightValue,
            SymbolicBinaryTermOperator.Remainder => leftValue % rightValue,
            _ => throw new ArgumentOutOfRangeException(nameof(binaryOperator), binaryOperator, null)
        };
        return TryCreateConstantResult(result, minimum, maximum, isChecked, out updatedValue);
    }

    private static bool TryCreateConstantResult(
        BigInteger result,
        long minimum,
        long maximum,
        bool isChecked,
        out SymbolicTerm updatedValue)
    {
        updatedValue = null!;
        if (result < minimum || result > maximum)
        {
            if (isChecked) return false;

            var modulus = (BigInteger)maximum - minimum + 1;
            result = ((result - minimum) % modulus + modulus) % modulus + minimum;
        }

        updatedValue = new SymbolicIntegerConstantTerm((long)result);
        return true;
    }

    private static bool TryGetTargetRange(ISymbol targetSymbol, out long minimum, out long maximum)
    {
        var targetType = SymbolicFactFactory.GetTrackedSymbolType(targetSymbol);
        if (targetType is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlyingType })
            targetType = underlyingType;

        return SymbolicTypeFacts.TryGetBoundedIntegralRange(targetType, out minimum, out maximum);
    }

    private static bool TryGetOperator(
        SyntaxKind kind,
        out SymbolicBinaryTermOperator binaryOperator)
    {
        switch (kind)
        {
            case SyntaxKind.AddAssignmentExpression:
                binaryOperator = SymbolicBinaryTermOperator.Add;
                return true;
            case SyntaxKind.SubtractAssignmentExpression:
                binaryOperator = SymbolicBinaryTermOperator.Subtract;
                return true;
            case SyntaxKind.MultiplyAssignmentExpression:
                binaryOperator = SymbolicBinaryTermOperator.Multiply;
                return true;
            case SyntaxKind.DivideAssignmentExpression:
                binaryOperator = SymbolicBinaryTermOperator.Divide;
                return true;
            case SyntaxKind.ModuloAssignmentExpression:
                binaryOperator = SymbolicBinaryTermOperator.Remainder;
                return true;
            default:
                binaryOperator = default;
                return false;
        }
    }
}
