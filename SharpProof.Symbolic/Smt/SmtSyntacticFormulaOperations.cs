using System.Collections.Immutable;
using SharpProof.ProofCore.Smt;

namespace SharpProof.Symbolic.Smt;

internal static class SmtSyntacticFormulaOperations
{
    private const int MaxSyntacticFormulaNodes = 2048;

    internal static bool ExceedsFormulaNodeBudget(
        SmtFormula triggerCondition,
        ImmutableArray<SmtFormula> pathConditions,
        out bool containsOpaqueIntegerOperation)
    {
        containsOpaqueIntegerOperation = false;
        var remaining = MaxSyntacticFormulaNodes;
        if (!TryConsumeFormulaNodes(triggerCondition, ref remaining, ref containsOpaqueIntegerOperation)) return true;

        foreach (var pathCondition in pathConditions)
            if (!TryConsumeFormulaNodes(pathCondition, ref remaining, ref containsOpaqueIntegerOperation))
                return true;

        return false;
    }

    internal static bool TryConsumeFormulaNodes(
        SmtFormula root,
        ref int remaining,
        ref bool containsOpaqueIntegerOperation)
    {
        var pending = new Stack<SmtFormula>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            if (remaining-- == 0) return false;

            switch (pending.Pop())
            {
                case SmtUnaryFormula unary:
                    pending.Push(unary.Operand);
                    break;
                case SmtBinaryFormula binary:
                    pending.Push(binary.Right);
                    pending.Push(binary.Left);
                    break;
                case SmtIntegerUnaryTerm unary:
                    pending.Push(unary.Operand);
                    break;
                case SmtIntegerBinaryTerm binary:
                    pending.Push(binary.Right);
                    pending.Push(binary.Left);
                    break;
                case SmtOpaqueIntegerBinaryTerm binary:
                    containsOpaqueIntegerOperation = true;
                    pending.Push(binary.Right);
                    pending.Push(binary.Left);
                    break;
                case SmtStringLengthTerm length:
                    pending.Push(length.Value);
                    break;
                case SmtStringConcatTerm concat:
                    pending.Push(concat.Right);
                    pending.Push(concat.Left);
                    break;
                case SmtStringContainsFormula contains:
                    pending.Push(contains.Search);
                    pending.Push(contains.Value);
                    break;
                case SmtStringStartsWithFormula startsWith:
                    pending.Push(startsWith.Prefix);
                    pending.Push(startsWith.Value);
                    break;
                case SmtStringEndsWithFormula endsWith:
                    pending.Push(endsWith.Suffix);
                    pending.Push(endsWith.Value);
                    break;
                case SmtRegexMatchFormula regex:
                    pending.Push(regex.Value);
                    break;
                case SmtRuntimeTypeTestFormula runtimeType:
                    pending.Push(runtimeType.Value);
                    break;
                case SmtConditionalFormula conditional:
                    pending.Push(conditional.WhenFalse);
                    pending.Push(conditional.WhenTrue);
                    pending.Push(conditional.Condition);
                    break;
            }
        }

        return true;
    }

    internal static IEnumerable<SmtFormula> EnumerateConjuncts(SmtFormula formula)
    {
        if (formula is SmtBinaryFormula { Operator: SmtBinaryOperator.And } binary)
        {
            foreach (var left in EnumerateConjuncts(binary.Left)) yield return left;

            foreach (var right in EnumerateConjuncts(binary.Right)) yield return right;

            yield break;
        }

        yield return formula;
    }

    internal static IEnumerable<SmtFormula> EnumerateConditionalConditions(IEnumerable<SmtFormula> formulas)
    {
        var seen = new HashSet<SmtFormula>();
        foreach (var formula in formulas)
            foreach (var condition in EnumerateConditionalConditions(formula))
                if (seen.Add(condition))
                    yield return condition;
    }

    internal static IEnumerable<SmtFormula> EnumerateConditionalConditions(SmtFormula formula)
    {
        switch (formula)
        {
            case SmtUnaryFormula unary:
                foreach (var condition in EnumerateConditionalConditions(unary.Operand)) yield return condition;

                break;
            case SmtBinaryFormula binary:
                foreach (var condition in EnumerateConditionalConditions(binary.Left)) yield return condition;

                foreach (var condition in EnumerateConditionalConditions(binary.Right)) yield return condition;

                break;
            case SmtIntegerUnaryTerm unary:
                foreach (var condition in EnumerateConditionalConditions(unary.Operand)) yield return condition;

                break;
            case SmtIntegerBinaryTerm binary:
                foreach (var condition in EnumerateConditionalConditions(binary.Left)) yield return condition;

                foreach (var condition in EnumerateConditionalConditions(binary.Right)) yield return condition;

                break;
            case SmtOpaqueIntegerBinaryTerm binary:
                foreach (var condition in EnumerateConditionalConditions(binary.Left)) yield return condition;

                foreach (var condition in EnumerateConditionalConditions(binary.Right)) yield return condition;

                break;
            case SmtStringLengthTerm stringLength:
                foreach (var condition in EnumerateConditionalConditions(stringLength.Value)) yield return condition;

                break;
            case SmtStringConcatTerm stringConcat:
                foreach (var condition in EnumerateConditionalConditions(stringConcat.Left)) yield return condition;

                foreach (var condition in EnumerateConditionalConditions(stringConcat.Right)) yield return condition;

                break;
            case SmtStringContainsFormula contains:
                foreach (var condition in EnumerateConditionalConditions(contains.Value)) yield return condition;

                foreach (var condition in EnumerateConditionalConditions(contains.Search)) yield return condition;

                break;
            case SmtStringStartsWithFormula startsWith:
                foreach (var condition in EnumerateConditionalConditions(startsWith.Value)) yield return condition;

                foreach (var condition in EnumerateConditionalConditions(startsWith.Prefix)) yield return condition;

                break;
            case SmtStringEndsWithFormula endsWith:
                foreach (var condition in EnumerateConditionalConditions(endsWith.Value)) yield return condition;

                foreach (var condition in EnumerateConditionalConditions(endsWith.Suffix)) yield return condition;

                break;
            case SmtRegexMatchFormula regexMatch:
                foreach (var condition in EnumerateConditionalConditions(regexMatch.Value)) yield return condition;

                break;
            case SmtRuntimeTypeTestFormula runtimeTypeTest:
                foreach (var condition in EnumerateConditionalConditions(runtimeTypeTest.Value)) yield return condition;

                break;
            case SmtConditionalFormula conditional:
                yield return conditional.Condition;
                foreach (var condition in EnumerateConditionalConditions(conditional.Condition)) yield return condition;

                foreach (var condition in EnumerateConditionalConditions(conditional.WhenTrue)) yield return condition;

                foreach (var condition in EnumerateConditionalConditions(conditional.WhenFalse)) yield return condition;

                break;
        }
    }

    internal static bool TryGetIntegerComparison(
        SmtFormula formula,
        out SmtFormula term,
        out SmtBinaryOperator op,
        out long constant)
    {
        term = null!;
        op = default;
        constant = default;
        if (formula is not SmtBinaryFormula binary ||
            !SmtComparisonOperatorFacts.IsComparison(binary.Operator))
            return TryGetNegatedIntegerComparison(formula, out term, out op, out constant);

        if (binary.Left.Kind == SmtValueKind.Int &&
            binary.Right is SmtIntegerConstant rightConstant)
        {
            term = binary.Left;
            op = binary.Operator;
            constant = rightConstant.Value;
            return true;
        }

        if (binary.Left is SmtIntegerConstant leftConstant &&
            binary.Right.Kind == SmtValueKind.Int)
        {
            term = binary.Right;
            op = SmtComparisonOperatorFacts.Reverse(binary.Operator);
            constant = leftConstant.Value;
            return true;
        }

        return false;
    }

    internal static bool TryGetNegatedIntegerComparison(
        SmtFormula formula,
        out SmtFormula term,
        out SmtBinaryOperator op,
        out long constant)
    {
        term = null!;
        op = default;
        constant = default;

        if (formula is not SmtUnaryFormula { Operator: SmtUnaryOperator.Not } negated ||
            negated.Operand is not SmtBinaryFormula comparison ||
            !TryGetIntegerComparison(comparison, out term, out op, out constant))
            return false;

        op = SmtComparisonOperatorFacts.Negate(op);
        return true;
    }

    internal static bool AreSyntacticComplements(SmtFormula left, SmtFormula right)
    {
        if (left is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } leftNot &&
            leftNot.Operand.Equals(right))
            return true;

        if (right is SmtUnaryFormula { Operator: SmtUnaryOperator.Not } rightNot &&
            rightNot.Operand.Equals(left))
            return true;

        if (left is not SmtBinaryFormula leftBinary ||
            right is not SmtBinaryFormula rightBinary)
            return false;

        if (!HaveSameOperands(leftBinary, rightBinary)) return false;

        return AreComplementaryOperators(leftBinary.Operator, rightBinary.Operator);
    }

    internal static bool HaveSameOperands(SmtBinaryFormula left, SmtBinaryFormula right)
    {
        if (left.Left.Equals(right.Left) && left.Right.Equals(right.Right)) return true;

        return IsSymmetricComparison(left.Operator) &&
               IsSymmetricComparison(right.Operator) &&
               left.Left.Equals(right.Right) &&
               left.Right.Equals(right.Left);
    }

    internal static bool IsSymmetricComparison(SmtBinaryOperator op)
    {
        return op is SmtBinaryOperator.Equal or SmtBinaryOperator.NotEqual;
    }

    internal static bool AreComplementaryOperators(SmtBinaryOperator left, SmtBinaryOperator right)
    {
        return (left, right) switch
        {
            (SmtBinaryOperator.Equal, SmtBinaryOperator.NotEqual) => true,
            (SmtBinaryOperator.NotEqual, SmtBinaryOperator.Equal) => true,
            (SmtBinaryOperator.LessThan, SmtBinaryOperator.GreaterThanOrEqual) => true,
            (SmtBinaryOperator.GreaterThanOrEqual, SmtBinaryOperator.LessThan) => true,
            (SmtBinaryOperator.LessThanOrEqual, SmtBinaryOperator.GreaterThan) => true,
            (SmtBinaryOperator.GreaterThan, SmtBinaryOperator.LessThanOrEqual) => true,
            _ => false
        };
    }

}
