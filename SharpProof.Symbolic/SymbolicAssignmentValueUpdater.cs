using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Symbolic;

internal static class SymbolicAssignmentValueUpdater
{
    internal static bool TryApplyComputedUpdate(
        ref SymbolicState state,
        ISymbol target,
        ExpressionSyntax source,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SymbolicTerm? previousValueOverride = null)
    {
        if (previousValueOverride is not { } previousValue &&
            !SymbolicStateValueFacts.TryGetCurrentValue(state, target, out previousValue))
            return false;

        SymbolicTerm updatedValue;
        SymbolicComputedUpdateKind updateKind;
        bool isChecked;
        string provenance;
        switch (semanticModel.GetOperation(source, cancellationToken))
        {
            case IIncrementOrDecrementOperation { OperatorMethod: null } increment:
                var delta = increment.Kind == OperationKind.Increment ? 1 : -1;
                isChecked = increment.IsChecked;
                if (!TryCreateIncrementOrDecrement(
                        previousValue,
                        delta,
                        source,
                        target,
                        isChecked,
                        out updatedValue))
                    return false;
                updateKind = delta > 0
                    ? SymbolicComputedUpdateKind.Increment
                    : SymbolicComputedUpdateKind.Decrement;
                provenance = delta > 0
                    ? "ir.path.prior-statement.increment"
                    : "ir.path.prior-statement.decrement";
                break;
            case ICompoundAssignmentOperation { OperatorMethod: null } compound
                when source is AssignmentExpressionSyntax assignment:
                isChecked = compound.IsChecked;
                if (!TryCreateCompoundAssignment(
                        previousValue,
                        assignment,
                        semanticModel,
                        cancellationToken,
                        target,
                        isChecked,
                        out updatedValue))
                    return false;
                updateKind = SymbolicComputedUpdateKind.CompoundAssignment;
                provenance = "ir.path.prior-statement.compound-assignment";
                break;
            default:
                return false;
        }

        var transition = SymbolicOperationTransferAdapter.ApplyComputedUpdate(
            state,
            target,
            updatedValue,
            source,
            semanticModel,
            cancellationToken,
            updateKind,
            isChecked,
            provenance);
        if (!transition.IsExact)
            return false;
        state = transition.State;
        return true;
    }

    private static bool TryCreateIncrementOrDecrement(
        SymbolicTerm previousValue,
        int delta,
        ExpressionSyntax updateExpression,
        ISymbol targetSymbol,
        bool isChecked,
        out SymbolicTerm updatedValue)
    {
        updatedValue = null!;
        if (previousValue.Kind != SmtValueKind.Int ||
            delta is not 1 and not -1 ||
            !TryGetTargetRange(targetSymbol, out var minimum, out var maximum))
            return false;

        if (previousValue is SymbolicIntegerConstantTerm integerConstant)
            return TryCreateConstantResult(
                integerConstant.Value + (BigInteger)delta,
                minimum,
                maximum,
                isChecked,
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
            isChecked);
        return true;
    }

    private static bool TryCreateCompoundAssignment(
        SymbolicTerm previousValue,
        AssignmentExpressionSyntax assignment,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ISymbol targetSymbol,
        bool isChecked,
        out SymbolicTerm updatedValue)
    {
        updatedValue = null!;
        var lowering = SymbolicSemanticPipeline.LowerTerm(
            assignment.Right,
            new SymbolicLoweringContext(semanticModel, cancellationToken));
        var targetName = SymbolicFactFactory.GetSmtVariableName(targetSymbol);
        if (previousValue.Kind != SmtValueKind.Int ||
            !TryGetTargetRange(targetSymbol, out var minimum, out var maximum) ||
            !CSharpSyntaxFacts.TryGetCompoundAssignmentBinaryKind(assignment.Kind(), out var binaryKind) ||
            !SymbolicOperatorLowerer.TryGetBinaryTermOperator(binaryKind, out var binaryOperator) ||
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
                isChecked,
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
                isChecked);
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

}
