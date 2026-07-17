using System.Numerics;
namespace SharpProof.ProofCore.Smt;

internal static partial class SmtSyntacticClassifier
{
    internal sealed partial class SyntacticFactSet
    {
        internal bool TryGetKnownInteger(SmtFormula formula, out long value)
        {
            formula = NormalizeAliases(formula);
            if (_integerIntervals.TryGetValue(formula, out var interval) &&
                interval.ExactValue.HasValue)
            {
                value = interval.ExactValue.Value;
                return true;
            }

            if (TryCreateIntrinsicIntegerInterval(formula, out var intrinsicInterval) &&
                intrinsicInterval.ExactValue.HasValue)
            {
                value = intrinsicInterval.ExactValue.Value;
                return true;
            }

            switch (formula)
            {
                case SmtIntegerConstant integerConstant:
                    value = integerConstant.Value;
                    return true;
                case SmtStringLengthTerm stringLength
                    when TryGetKnownStringLength(stringLength.Value, out var length):
                    value = length;
                    return true;
                case SmtConditionalFormula conditional
                    when conditional.Kind == SmtValueKind.Int &&
                         TryEvaluateBoolean(conditional.Condition, out var conditionValue):
                    return TryGetKnownInteger(conditionValue ? conditional.WhenTrue : conditional.WhenFalse, out value);
                case SmtIntegerUnaryTerm { Operator: SmtIntegerUnaryOperator.Negate } unary
                    when TryGetKnownInteger(unary.Operand, out var operand):
                    value = -operand;
                    return true;
                case SmtIntegerBinaryTerm binary
                    when TryGetKnownInteger(binary.Left, out var left) &&
                         TryGetKnownInteger(binary.Right, out var right):
                    return SmtIntegerArithmetic.TryEvaluateBinary(binary.Operator, left, right, out value);
                default:
                    value = 0;
                    return false;
            }
        }

        private bool TryAddIntegerIntervalFact(
            SmtFormula formula,
            out bool hasContradiction)
        {
            hasContradiction = false;
            if (!SmtComparisonOperatorFacts.TryGetIntegerComparison(
                    formula,
                    out var term,
                    out var op,
                    out var constant))
                return TryAddAffineIntegerComparisonFact(formula, out hasContradiction);

            term = NormalizeAliases(term);
            var added = AddIntegerIntervalFact(term, op, constant, out hasContradiction);
            if (hasContradiction) return true;

            if (!TryNormalizeAffineIntegerComparison(
                    term,
                    op,
                    constant,
                    out var normalizedTerm,
                    out var normalizedOp,
                    out var normalizedConstant,
                    out var affineContradiction,
                    out var affineTautology))
                return added;

            if (affineContradiction)
            {
                hasContradiction = true;
                return true;
            }

            if (affineTautology) return added;

            if (normalizedTerm.Equals(term) &&
                normalizedOp == op &&
                normalizedConstant == constant)
                return added;

            added |= AddIntegerIntervalFact(normalizedTerm, normalizedOp, normalizedConstant,
                out var normalizedContradiction);
            hasContradiction |= normalizedContradiction;
            return added;
        }

        private bool TryAddAffineIntegerComparisonFact(
            SmtFormula formula,
            out bool hasContradiction)
        {
            hasContradiction = false;
            if (!TryGetIntegerBinaryComparison(formula, out var left, out var op, out var right)) return false;

            left = NormalizeAliases(left);
            right = NormalizeAliases(right);
            if (TryGetKnownInteger(left, out var leftKnown) &&
                TryGetKnownInteger(right, out var rightKnown))
                return TryClassifyConstantComparison(
                    leftKnown,
                    op,
                    rightKnown,
                    out hasContradiction);

            if (TryGetKnownInteger(right, out var rightConstant) &&
                TryGetAffineIntegerTerm(left, 0, out var leftAffine))
                return AddAffineIntegerComparisonFact(
                    leftAffine,
                    op,
                    rightConstant,
                    out hasContradiction);

            if (TryGetKnownInteger(left, out var leftConstant) &&
                TryGetAffineIntegerTerm(right, 0, out var rightAffine))
                return AddAffineIntegerComparisonFact(
                    rightAffine,
                    SmtComparisonOperatorFacts.Reverse(op),
                    leftConstant,
                    out hasContradiction);

            if (!TryGetAffineIntegerTerm(left, 0, out var leftTerm) ||
                !TryGetAffineIntegerTerm(right, 0, out var rightTerm) ||
                !SmtAffineIntegerTerm.TrySubtract(leftTerm, rightTerm, out var difference))
                return false;

            return AddAffineIntegerComparisonFact(
                difference,
                op,
                0,
                out hasContradiction);
        }

        private bool AddAffineIntegerComparisonFact(
            SmtAffineIntegerTerm term,
            SmtBinaryOperator op,
            long constant,
            out bool hasContradiction)
        {
            hasContradiction = false;
            if (term.BaseTerm == null ||
                term.Scale == 0)
                return TryClassifyConstantComparison(
                    term.Offset,
                    op,
                    constant,
                    out hasContradiction);

            var formula = CreateAffineTerm(term);
            if (!TryNormalizeAffineIntegerComparison(
                    formula,
                    op,
                    constant,
                    out var normalizedTerm,
                    out var normalizedOp,
                    out var normalizedConstant,
                    out var affineContradiction,
                    out var affineTautology))
                return false;

            if (affineContradiction)
            {
                hasContradiction = true;
                return true;
            }

            if (affineTautology) return false;

            return AddIntegerIntervalFact(
                normalizedTerm,
                normalizedOp,
                normalizedConstant,
                out hasContradiction);
        }

        private static bool TryClassifyConstantComparison(
            long left,
            SmtBinaryOperator op,
            long right,
            out bool hasContradiction)
        {
            if (!TryEvaluateConstantComparison(
                    left,
                    op,
                    right,
                    out var constantContradiction,
                    out var constantTautology))
            {
                hasContradiction = false;
                return false;
            }

            hasContradiction = constantContradiction;
            return constantContradiction || !constantTautology;
        }

        private bool AddIntegerIntervalFact(
            SmtFormula term,
            SmtBinaryOperator op,
            long constant,
            out bool hasContradiction)
        {
            term = NormalizeAliases(term);
            var interval = TryCreateIntrinsicIntegerInterval(term, out var intrinsicInterval)
                ? intrinsicInterval
                : SmtIntegerInterval.Unbounded;
            if (_integerIntervals.TryGetValue(term, out var existing)) interval = interval.Intersect(existing);

            interval = interval.Apply(op, constant);
            hasContradiction = interval.IsContradictory;
            _integerIntervals[term] = interval;
            return true;
        }

        private bool TryCreateIntrinsicIntegerInterval(SmtFormula term, out SmtIntegerInterval interval)
        {
            term = NormalizeAliases(term);
            interval = SmtIntegerInterval.Unbounded;
            if (term is not SmtStringLengthTerm stringLength) return false;

            interval = interval.Apply(SmtBinaryOperator.GreaterThanOrEqual, 0);
            if (TryGetKnownStringLength(stringLength.Value, out var length))
                interval = interval.Apply(SmtBinaryOperator.Equal, length);

            return true;
        }

        private bool TryNormalizeAffineIntegerComparison(
            SmtFormula term,
            SmtBinaryOperator op,
            long constant,
            out SmtFormula normalizedTerm,
            out SmtBinaryOperator normalizedOp,
            out long normalizedConstant,
            out bool hasContradiction,
            out bool isTautology)
        {
            normalizedTerm = term;
            normalizedOp = op;
            normalizedConstant = constant;
            hasContradiction = false;
            isTautology = false;

            if (!TryGetAffineIntegerTerm(term, 0, out var affine)) return false;

            if (affine.BaseTerm == null ||
                affine.Scale == 0)
                return TryEvaluateConstantComparison(
                    affine.Offset,
                    op,
                    constant,
                    out hasContradiction,
                    out isTautology);

            var scale = affine.Scale;
            var adjusted = (BigInteger)constant - affine.Offset;
            if (scale < 0)
            {
                adjusted = BigInteger.Negate(adjusted);
                op = SmtComparisonOperatorFacts.Reverse(op);
                var positiveScale = BigInteger.Negate(new BigInteger(scale));
                if (!TryInvertPositiveScaleComparison(
                        op,
                        adjusted,
                        positiveScale,
                        out normalizedOp,
                        out normalizedConstant,
                        out hasContradiction,
                        out isTautology))
                    return false;

                normalizedTerm = NormalizeAliases(affine.BaseTerm);
                return true;
            }

            if (scale <= 0 || adjusted < long.MinValue || adjusted > long.MaxValue) return false;

            if (!TryInvertPositiveScaleComparison(
                    op,
                    (long)adjusted,
                    scale,
                    out normalizedOp,
                    out normalizedConstant,
                    out hasContradiction,
                    out isTautology))
                return false;

            normalizedTerm = NormalizeAliases(affine.BaseTerm);
            return true;
        }

        private static bool TryEvaluateConstantComparison(
            long left,
            SmtBinaryOperator op,
            long right,
            out bool hasContradiction,
            out bool isTautology)
        {
            if (!SmtIntegerComparisonFacts.TryEvaluate(op, left, right, out var value))
            {
                hasContradiction = false;
                isTautology = false;
                return false;
            }

            hasContradiction = !value;
            isTautology = value;
            return true;
        }

        private static bool TryInvertPositiveScaleComparison(
            SmtBinaryOperator op,
            long adjustedConstant,
            long positiveScale,
            out SmtBinaryOperator normalizedOp,
            out long normalizedConstant,
            out bool hasContradiction,
            out bool isTautology)
        {
            return TryInvertPositiveScaleComparison(
                op,
                new BigInteger(adjustedConstant),
                new BigInteger(positiveScale),
                out normalizedOp,
                out normalizedConstant,
                out hasContradiction,
                out isTautology);
        }

        private static bool TryInvertPositiveScaleComparison(
            SmtBinaryOperator op,
            BigInteger adjustedConstant,
            BigInteger positiveScale,
            out SmtBinaryOperator normalizedOp,
            out long normalizedConstant,
            out bool hasContradiction,
            out bool isTautology)
        {
            normalizedOp = op;
            normalizedConstant = 0;
            hasContradiction = false;
            isTautology = false;
            BigInteger value;

            switch (op)
            {
                case SmtBinaryOperator.Equal:
                    if (adjustedConstant % positiveScale != 0)
                    {
                        hasContradiction = true;
                        return true;
                    }

                    value = adjustedConstant / positiveScale;
                    break;
                case SmtBinaryOperator.NotEqual:
                    if (adjustedConstant % positiveScale != 0)
                    {
                        isTautology = true;
                        return true;
                    }

                    value = adjustedConstant / positiveScale;
                    break;
                case SmtBinaryOperator.GreaterThan:
                case SmtBinaryOperator.LessThanOrEqual:
                    value = SmtIntegerArithmetic.FloorDivide(adjustedConstant, positiveScale);
                    break;
                case SmtBinaryOperator.GreaterThanOrEqual:
                case SmtBinaryOperator.LessThan:
                    value = SmtIntegerArithmetic.CeilingDivide(adjustedConstant, positiveScale);
                    break;
                default:
                    return false;
            }

            if (value < long.MinValue || value > long.MaxValue) return false;

            normalizedConstant = (long)value;
            return true;
        }

        private bool TryGetAffineIntegerTerm(
            SmtFormula formula,
            int depth,
            out SmtAffineIntegerTerm affine)
        {
            return SmtAffineIntegerTerm.TryCreate(
                formula,
                MaxAffineExpansionDepth - depth,
                NormalizeAliases,
                TryGetKnownInteger,
                false,
                static candidate => candidate.Kind == SmtValueKind.Int,
                out affine);
        }

    }
}
