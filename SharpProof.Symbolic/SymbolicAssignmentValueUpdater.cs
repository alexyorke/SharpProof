using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Symbolic;

internal static class SymbolicAssignmentValueUpdater
{
    internal static bool TryCreateIncrementOrDecrement(
        SymbolicTerm previousValue,
        int delta,
        out SymbolicTerm updatedValue)
    {
        updatedValue = null!;
        if (previousValue.Kind != SmtValueKind.Int || delta is not 1 and not -1) return false;

        if (previousValue is SymbolicIntegerConstantTerm integerConstant)
        {
            updatedValue = new SymbolicIntegerConstantTerm(integerConstant.Value + delta);
            return true;
        }

        updatedValue = new SymbolicBinaryTerm(
            delta > 0
                ? SymbolicBinaryTermOperator.Add
                : SymbolicBinaryTermOperator.Subtract,
            previousValue,
            new SymbolicIntegerConstantTerm(1));
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
            !TryGetOperator(assignment.Kind(), out var binaryOperator) ||
            lowering is not { IsExact: true, Value: { } rightTerm } ||
            rightTerm.Kind != SmtValueKind.Int ||
            SymbolicIrReferenceScanner.ContainsVariableOrMember(previousValue, targetName) ||
            SymbolicIrReferenceScanner.ContainsVariableOrMember(rightTerm, targetName))
            return false;

        if (previousValue is SymbolicIntegerConstantTerm leftConstant &&
            rightTerm is SymbolicIntegerConstantTerm rightConstant)
            switch (binaryOperator)
            {
                case SymbolicBinaryTermOperator.Add:
                    updatedValue = new SymbolicIntegerConstantTerm(leftConstant.Value + rightConstant.Value);
                    return true;
                case SymbolicBinaryTermOperator.Subtract:
                    updatedValue = new SymbolicIntegerConstantTerm(leftConstant.Value - rightConstant.Value);
                    return true;
                case SymbolicBinaryTermOperator.Multiply:
                    updatedValue = new SymbolicIntegerConstantTerm(leftConstant.Value * rightConstant.Value);
                    return true;
                case SymbolicBinaryTermOperator.Divide:
                    if (rightConstant.Value == 0) return false;

                    updatedValue = new SymbolicIntegerConstantTerm(leftConstant.Value / rightConstant.Value);
                    return true;
                case SymbolicBinaryTermOperator.Remainder:
                    if (rightConstant.Value == 0) return false;

                    updatedValue = new SymbolicIntegerConstantTerm(leftConstant.Value % rightConstant.Value);
                    return true;
            }

        if (binaryOperator is SymbolicBinaryTermOperator.Divide or SymbolicBinaryTermOperator.Remainder &&
            rightTerm is SymbolicIntegerConstantTerm { Value: 0 })
            return false;

        updatedValue = new SymbolicBinaryTerm(binaryOperator, previousValue, rightTerm);
        return true;
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
