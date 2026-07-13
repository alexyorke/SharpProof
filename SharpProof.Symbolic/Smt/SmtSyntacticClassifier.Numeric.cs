using System.Collections.Immutable;
using System.Numerics;
using SharpProof.ProofCore.Smt;

namespace SharpProof.Symbolic.Smt;

internal static partial class SmtSyntacticClassifier
{
    private readonly struct IntegerInterval
    {
        private IntegerInterval(
            long? lowerBound,
            long? upperBound,
            ImmutableHashSet<long> excludedValues,
            bool isImpossible)
        {
            LowerBound = lowerBound;
            UpperBound = upperBound;
            ExcludedValues = excludedValues;
            IsImpossible = isImpossible;
        }

        public static IntegerInterval Unbounded { get; } = new(
            null,
            null,
            ImmutableHashSet<long>.Empty,
            false);

        public long? LowerBound { get; }
        public long? UpperBound { get; }
        public ImmutableHashSet<long> ExcludedValues { get; }
        public bool IsImpossible { get; }

        public bool IsContradictory =>
            IsImpossible ||
            (LowerBound.HasValue &&
             UpperBound.HasValue &&
             LowerBound.Value > UpperBound.Value) ||
            (LowerBound.HasValue &&
             UpperBound.HasValue &&
             LowerBound.Value == UpperBound.Value &&
             ExcludedValues.Contains(LowerBound.Value));

        public long? ExactValue =>
            !IsContradictory &&
            LowerBound.HasValue &&
            UpperBound.HasValue &&
            LowerBound.Value == UpperBound.Value
                ? LowerBound.Value
                : null;

        public IntegerInterval Apply(SmtBinaryOperator op, long constant)
        {
            return op switch
            {
                SmtBinaryOperator.Equal => WithExactValue(constant),
                SmtBinaryOperator.NotEqual => new IntegerInterval(
                    LowerBound,
                    UpperBound,
                    ExcludedValues.Add(constant),
                    IsImpossible),
                SmtBinaryOperator.GreaterThan => constant == long.MaxValue
                    ? Impossible()
                    : WithLowerBound(constant + 1),
                SmtBinaryOperator.GreaterThanOrEqual => WithLowerBound(constant),
                SmtBinaryOperator.LessThan => constant == long.MinValue
                    ? Impossible()
                    : WithUpperBound(constant - 1),
                SmtBinaryOperator.LessThanOrEqual => WithUpperBound(constant),
                _ => this
            };
        }

        public IntegerInterval Intersect(IntegerInterval other)
        {
            var interval = this;
            if (other.IsImpossible) interval = interval.Impossible();

            if (other.LowerBound.HasValue) interval = interval.WithLowerBound(other.LowerBound.Value);

            if (other.UpperBound.HasValue) interval = interval.WithUpperBound(other.UpperBound.Value);

            foreach (var excludedValue in other.ExcludedValues)
                interval = interval.Apply(SmtBinaryOperator.NotEqual, excludedValue);

            return interval;
        }

        private IntegerInterval WithLowerBound(long lowerBound)
        {
            return new IntegerInterval(
                LowerBound.HasValue ? Math.Max(LowerBound.Value, lowerBound) : lowerBound,
                UpperBound,
                ExcludedValues,
                IsImpossible);
        }

        private IntegerInterval WithUpperBound(long upperBound)
        {
            return new IntegerInterval(
                LowerBound,
                UpperBound.HasValue ? Math.Min(UpperBound.Value, upperBound) : upperBound,
                ExcludedValues,
                IsImpossible);
        }

        private IntegerInterval WithExactValue(long value)
        {
            return new IntegerInterval(
                value,
                value,
                ExcludedValues,
                IsImpossible ||
                (LowerBound.HasValue && value < LowerBound.Value) ||
                (UpperBound.HasValue && value > UpperBound.Value));
        }

        private IntegerInterval Impossible()
        {
            return new IntegerInterval(
                LowerBound,
                UpperBound,
                ExcludedValues,
                true);
        }
    }

    private sealed partial class SyntacticFactSet
    {
        private bool TryGetKnownInteger(SmtFormula formula, out long value)
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
                    return TryEvaluateIntegerBinaryTerm(binary.Operator, left, right, out value);
                default:
                    value = 0;
                    return false;
            }
        }

        private static bool TryEvaluateIntegerBinaryTerm(
            SmtIntegerBinaryOperator op,
            long left,
            long right,
            out long value)
        {
            try
            {
                checked
                {
                    switch (op)
                    {
                        case SmtIntegerBinaryOperator.Add:
                            value = left + right;
                            return true;
                        case SmtIntegerBinaryOperator.Subtract:
                            value = left - right;
                            return true;
                        case SmtIntegerBinaryOperator.Multiply:
                            value = left * right;
                            return true;
                        case SmtIntegerBinaryOperator.Divide when right != 0:
                            value = left / right;
                            return true;
                        case SmtIntegerBinaryOperator.Remainder when right != 0:
                            value = left % right;
                            return true;
                    }
                }
            }
            catch (OverflowException)
            {
            }

            value = 0;
            return false;
        }

        private bool TryAddIntegerIntervalFact(
            SmtFormula formula,
            out bool hasContradiction)
        {
            hasContradiction = false;
            if (!SmtSyntacticFormulaOperations.TryGetIntegerComparison(
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
                !TrySubtract(leftTerm, rightTerm, out var difference))
                return false;

            return AddAffineIntegerComparisonFact(
                difference,
                op,
                0,
                out hasContradiction);
        }

        private bool AddAffineIntegerComparisonFact(
            AffineIntegerTerm term,
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
                : IntegerInterval.Unbounded;
            if (_integerIntervals.TryGetValue(term, out var existing)) interval = interval.Intersect(existing);

            interval = interval.Apply(op, constant);
            hasContradiction = interval.IsContradictory;
            _integerIntervals[term] = interval;
            return true;
        }

        private bool TryCreateIntrinsicIntegerInterval(SmtFormula term, out IntegerInterval interval)
        {
            term = NormalizeAliases(term);
            interval = IntegerInterval.Unbounded;
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
                    value = FloorDiv(adjustedConstant, positiveScale);
                    break;
                case SmtBinaryOperator.GreaterThanOrEqual:
                case SmtBinaryOperator.LessThan:
                    value = CeilingDiv(adjustedConstant, positiveScale);
                    break;
                default:
                    return false;
            }

            if (value < long.MinValue || value > long.MaxValue) return false;

            normalizedConstant = (long)value;
            return true;
        }

        private static BigInteger FloorDiv(BigInteger dividend, BigInteger positiveDivisor)
        {
            var quotient = BigInteger.DivRem(dividend, positiveDivisor, out var remainder);
            return remainder != 0 && dividend.Sign < 0
                ? quotient - BigInteger.One
                : quotient;
        }

        private static BigInteger CeilingDiv(BigInteger dividend, BigInteger positiveDivisor)
        {
            var quotient = BigInteger.DivRem(dividend, positiveDivisor, out var remainder);
            return remainder != 0 && dividend.Sign > 0
                ? quotient + BigInteger.One
                : quotient;
        }

        private bool TryGetAffineIntegerTerm(
            SmtFormula formula,
            int depth,
            out AffineIntegerTerm affine)
        {
            formula = NormalizeAliases(formula);
            if (depth > MaxAffineExpansionDepth) return TryCreateUnitAffineTerm(formula, out affine);

            switch (formula)
            {
                case SmtIntegerConstant constant:
                    affine = AffineIntegerTerm.Constant(constant.Value);
                    return true;
                case SmtIntegerUnaryTerm { Operator: SmtIntegerUnaryOperator.Negate } unary
                    when TryGetAffineIntegerTerm(unary.Operand, depth + 1, out var operand) &&
                         TryNegate(operand, out affine):
                    return true;
                case SmtIntegerBinaryTerm binary:
                    return TryGetAffineIntegerBinaryTerm(binary, depth, out affine) ||
                           TryCreateUnitAffineTerm(formula, out affine);
                default:
                    return TryCreateUnitAffineTerm(formula, out affine);
            }
        }

        private bool TryGetAffineIntegerBinaryTerm(
            SmtIntegerBinaryTerm binary,
            int depth,
            out AffineIntegerTerm affine)
        {
            affine = default;
            if (binary.Operator == SmtIntegerBinaryOperator.Multiply)
            {
                if (TryGetKnownInteger(binary.Left, out var leftConstant) &&
                    TryGetAffineIntegerTerm(binary.Right, depth + 1, out var rightAffine))
                    return TryScale(rightAffine, leftConstant, out affine);

                if (TryGetKnownInteger(binary.Right, out var rightConstant) &&
                    TryGetAffineIntegerTerm(binary.Left, depth + 1, out var leftAffine))
                    return TryScale(leftAffine, rightConstant, out affine);

                return false;
            }

            if (binary.Operator is not (SmtIntegerBinaryOperator.Add or SmtIntegerBinaryOperator.Subtract) ||
                !TryGetAffineIntegerTerm(binary.Left, depth + 1, out var left) ||
                !TryGetAffineIntegerTerm(binary.Right, depth + 1, out var right))
                return false;

            return binary.Operator == SmtIntegerBinaryOperator.Add
                ? TryAdd(left, right, out affine)
                : TrySubtract(left, right, out affine);
        }

        private static bool TryCreateUnitAffineTerm(SmtFormula formula, out AffineIntegerTerm affine)
        {
            if (formula.Kind != SmtValueKind.Int)
            {
                affine = default;
                return false;
            }

            affine = AffineIntegerTerm.Term(formula);
            return true;
        }

        private static bool TryAdd(
            AffineIntegerTerm left,
            AffineIntegerTerm right,
            out AffineIntegerTerm result)
        {
            return TryCombine(left, right, false, out result);
        }

        private static bool TrySubtract(
            AffineIntegerTerm left,
            AffineIntegerTerm right,
            out AffineIntegerTerm result)
        {
            return TryCombine(left, right, true, out result);
        }

        private static bool TryCombine(
            AffineIntegerTerm left,
            AffineIntegerTerm right,
            bool subtractRight,
            out AffineIntegerTerm result)
        {
            result = default;
            var rightScale = right.Scale;
            var rightOffset = right.Offset;
            if (subtractRight &&
                (!TryNegate(rightScale, out rightScale) ||
                 !TryNegate(rightOffset, out rightOffset)))
                return false;

            try
            {
                checked
                {
                    if (left.BaseTerm == null &&
                        right.BaseTerm == null)
                    {
                        result = AffineIntegerTerm.Constant(left.Offset + rightOffset);
                        return true;
                    }

                    if (left.BaseTerm == null)
                    {
                        var offset = left.Offset + rightOffset;
                        result = rightScale == 0
                            ? AffineIntegerTerm.Constant(offset)
                            : new AffineIntegerTerm(right.BaseTerm, rightScale, offset);
                        return true;
                    }

                    if (right.BaseTerm == null)
                    {
                        var offset = left.Offset + rightOffset;
                        result = left.Scale == 0
                            ? AffineIntegerTerm.Constant(offset)
                            : new AffineIntegerTerm(left.BaseTerm, left.Scale, offset);
                        return true;
                    }

                    if (!left.BaseTerm.Equals(right.BaseTerm)) return false;

                    var scale = left.Scale + rightScale;
                    var combinedOffset = left.Offset + rightOffset;
                    result = scale == 0
                        ? AffineIntegerTerm.Constant(combinedOffset)
                        : new AffineIntegerTerm(left.BaseTerm, scale, combinedOffset);
                    return true;
                }
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool TryScale(
            AffineIntegerTerm value,
            long scale,
            out AffineIntegerTerm result)
        {
            result = default;
            try
            {
                checked
                {
                    var scaledScale = value.Scale * scale;
                    var scaledOffset = value.Offset * scale;
                    result = value.BaseTerm == null || scaledScale == 0
                        ? AffineIntegerTerm.Constant(scaledOffset)
                        : new AffineIntegerTerm(value.BaseTerm, scaledScale, scaledOffset);
                    return true;
                }
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static bool TryNegate(AffineIntegerTerm value, out AffineIntegerTerm result)
        {
            result = default;
            if (!TryNegate(value.Scale, out var scale) ||
                !TryNegate(value.Offset, out var offset))
                return false;

            result = value.BaseTerm == null || scale == 0
                ? AffineIntegerTerm.Constant(offset)
                : new AffineIntegerTerm(value.BaseTerm, scale, offset);
            return true;
        }

        private static bool TrySubtract(long left, long right, out long result)
        {
            try
            {
                checked
                {
                    result = left - right;
                }

                return true;
            }
            catch (OverflowException)
            {
                result = default;
                return false;
            }
        }

        private static bool TryNegate(long value, out long result)
        {
            if (value == long.MinValue)
            {
                result = default;
                return false;
            }

            result = -value;
            return true;
        }

        private static long FloorDiv(long dividend, long positiveDivisor)
        {
            var quotient = dividend / positiveDivisor;
            var remainder = dividend % positiveDivisor;
            return remainder != 0 && dividend < 0
                ? quotient - 1
                : quotient;
        }

        private static long CeilingDiv(long dividend, long positiveDivisor)
        {
            var quotient = dividend / positiveDivisor;
            var remainder = dividend % positiveDivisor;
            return remainder != 0 && dividend > 0
                ? quotient + 1
                : quotient;
        }

        private readonly struct AffineIntegerTerm
        {
            internal AffineIntegerTerm(SmtFormula? baseTerm, long scale, long offset)
            {
                BaseTerm = scale == 0 ? null : baseTerm;
                Scale = BaseTerm == null ? 0 : scale;
                Offset = offset;
            }

            internal SmtFormula? BaseTerm { get; }
            internal long Scale { get; }
            internal long Offset { get; }

            internal static AffineIntegerTerm Constant(long value)
            {
                return new AffineIntegerTerm(null, 0, value);
            }

            internal static AffineIntegerTerm Term(SmtFormula term)
            {
                return new AffineIntegerTerm(term, 1, 0);
            }
        }
    }
}
