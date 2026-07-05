using Microsoft.CodeAnalysis.CSharp;
using SearchLib.Smt;

namespace SharpProof.Symbolic
{
    internal static class SymbolicMutationFactFactory
    {
        internal static bool TryCreateCompoundAssignmentFact(
            SmtFormula targetFormula,
            SmtFormula previousValue,
            bool previousValueReferencesTarget,
            bool rightReferencesTarget,
            SyntaxKind assignmentKind,
            SmtFormula? rightValue,
            out SmtFormula fact)
        {
            fact = null!;
            if (targetFormula.Kind != SmtValueKind.Int ||
                previousValue.Kind != SmtValueKind.Int ||
                previousValueReferencesTarget ||
                rightReferencesTarget ||
                rightValue is not { Kind: SmtValueKind.Int } ||
                !TryCreateCompoundAssignmentValue(assignmentKind, previousValue, rightValue, out var updatedValue))
            {
                return false;
            }

            fact = new SmtBinaryFormula(SmtBinaryOperator.Equal, targetFormula, updatedValue);
            return true;
        }

        internal static bool TryCreateIncrementOrDecrementFact(
            SmtFormula targetFormula,
            SmtFormula previousValue,
            bool previousValueReferencesTarget,
            int delta,
            out SmtFormula fact)
        {
            fact = null!;
            if (targetFormula.Kind != SmtValueKind.Int ||
                previousValue.Kind != SmtValueKind.Int ||
                previousValueReferencesTarget)
            {
                return false;
            }

            var updatedValue = delta > 0
                ? new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, previousValue, new SmtIntegerConstant(delta))
                : new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, previousValue, new SmtIntegerConstant(-delta));
            fact = new SmtBinaryFormula(SmtBinaryOperator.Equal, targetFormula, updatedValue);
            return true;
        }

        private static bool TryCreateCompoundAssignmentValue(
            SyntaxKind assignmentKind,
            SmtFormula previousValue,
            SmtFormula rightValue,
            out SmtFormula updatedValue)
        {
            switch (assignmentKind)
            {
                case SyntaxKind.AddAssignmentExpression:
                    updatedValue = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, previousValue, rightValue);
                    return true;
                case SyntaxKind.SubtractAssignmentExpression:
                    updatedValue = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, previousValue, rightValue);
                    return true;
                case SyntaxKind.MultiplyAssignmentExpression
                    when previousValue is SmtIntegerConstant || rightValue is SmtIntegerConstant:
                    updatedValue = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Multiply, previousValue, rightValue);
                    return true;
                default:
                    updatedValue = null!;
                    return false;
            }
        }
    }
}
