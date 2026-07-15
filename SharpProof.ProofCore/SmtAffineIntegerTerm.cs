using System.Numerics;

namespace SharpProof.ProofCore.Smt;

internal delegate bool TryResolveSmtIntegerValue(SmtFormula formula, out long value);

internal readonly struct SmtAffineIntegerTerm
{
    internal SmtAffineIntegerTerm(SmtFormula? baseTerm, long scale, long offset)
    {
        BaseTerm = scale == 0 ? null : baseTerm;
        Scale = BaseTerm == null ? 0 : scale;
        Offset = offset;
    }

    internal SmtFormula? BaseTerm { get; }

    internal long Scale { get; }

    internal long Offset { get; }

    internal static SmtAffineIntegerTerm Constant(long value)
    {
        return new SmtAffineIntegerTerm(null, 0, value);
    }

    internal static SmtAffineIntegerTerm Term(SmtFormula term)
    {
        return new SmtAffineIntegerTerm(term, 1, 0);
    }

    internal static bool TryCreate(
        SmtFormula formula,
        int maxDepth,
        Func<SmtFormula, SmtFormula> normalize,
        TryResolveSmtIntegerValue resolveConstant,
        bool resolveWholeFormula,
        Func<SmtFormula, bool> canUseBaseTerm,
        out SmtAffineIntegerTerm affine)
    {
        if (formula == null) throw new ArgumentNullException(nameof(formula));
        if (normalize == null) throw new ArgumentNullException(nameof(normalize));
        if (resolveConstant == null) throw new ArgumentNullException(nameof(resolveConstant));
        if (canUseBaseTerm == null) throw new ArgumentNullException(nameof(canUseBaseTerm));

        return TryCreateCore(
            formula,
            0,
            maxDepth,
            normalize,
            resolveConstant,
            resolveWholeFormula,
            canUseBaseTerm,
            out affine);
    }

    internal static bool TryAdd(
        SmtAffineIntegerTerm left,
        SmtAffineIntegerTerm right,
        out SmtAffineIntegerTerm result)
    {
        return TryCombine(left, right, false, out result);
    }

    internal static bool TrySubtract(
        SmtAffineIntegerTerm left,
        SmtAffineIntegerTerm right,
        out SmtAffineIntegerTerm result)
    {
        return TryCombine(left, right, true, out result);
    }

    internal static bool TryScale(
        SmtAffineIntegerTerm value,
        long scale,
        out SmtAffineIntegerTerm result)
    {
        result = default;
        if (!SmtIntegerArithmetic.TryMultiply(value.Scale, scale, out var scaledScale) ||
            !SmtIntegerArithmetic.TryMultiply(value.Offset, scale, out var scaledOffset))
            return false;

        result = value.BaseTerm == null || scaledScale == 0
            ? Constant(scaledOffset)
            : new SmtAffineIntegerTerm(value.BaseTerm, scaledScale, scaledOffset);
        return true;
    }

    internal static bool TryNegate(SmtAffineIntegerTerm value, out SmtAffineIntegerTerm result)
    {
        result = default;
        if (!SmtIntegerArithmetic.TryNegate(value.Scale, out var scale) ||
            !SmtIntegerArithmetic.TryNegate(value.Offset, out var offset))
            return false;

        result = value.BaseTerm == null || scale == 0
            ? Constant(offset)
            : new SmtAffineIntegerTerm(value.BaseTerm, scale, offset);
        return true;
    }

    private static bool TryCreateCore(
        SmtFormula formula,
        int depth,
        int maxDepth,
        Func<SmtFormula, SmtFormula> normalize,
        TryResolveSmtIntegerValue resolveConstant,
        bool resolveWholeFormula,
        Func<SmtFormula, bool> canUseBaseTerm,
        out SmtAffineIntegerTerm affine)
    {
        formula = normalize(formula);
        if (depth > maxDepth) return TryCreateBaseTerm(formula, canUseBaseTerm, out affine);

        if (resolveWholeFormula && resolveConstant(formula, out var resolved))
        {
            affine = Constant(resolved);
            return true;
        }

        switch (formula)
        {
            case SmtIntegerConstant constant:
                affine = Constant(constant.Value);
                return true;
            case SmtIntegerUnaryTerm { Operator: SmtIntegerUnaryOperator.Negate } unary:
                if (TryCreateCore(
                        unary.Operand,
                        depth + 1,
                        maxDepth,
                        normalize,
                        resolveConstant,
                        resolveWholeFormula,
                        canUseBaseTerm,
                        out var operand))
                    return TryNegate(operand, out affine);

                affine = default;
                return false;
            case SmtIntegerBinaryTerm binary:
                if (TryCreateBinary(
                        binary,
                        depth,
                        maxDepth,
                        normalize,
                        resolveConstant,
                        resolveWholeFormula,
                        canUseBaseTerm,
                        out affine))
                    return true;

                return TryCreateBaseTerm(formula, canUseBaseTerm, out affine);
            default:
                return TryCreateBaseTerm(formula, canUseBaseTerm, out affine);
        }
    }

    private static bool TryCreateBinary(
        SmtIntegerBinaryTerm binary,
        int depth,
        int maxDepth,
        Func<SmtFormula, SmtFormula> normalize,
        TryResolveSmtIntegerValue resolveConstant,
        bool resolveWholeFormula,
        Func<SmtFormula, bool> canUseBaseTerm,
        out SmtAffineIntegerTerm affine)
    {
        affine = default;
        if (binary.Operator == SmtIntegerBinaryOperator.Multiply)
        {
            if (resolveConstant(binary.Left, out var leftConstant) &&
                TryCreateCore(
                    binary.Right,
                    depth + 1,
                    maxDepth,
                    normalize,
                    resolveConstant,
                    resolveWholeFormula,
                    canUseBaseTerm,
                    out var rightAffine))
                return TryScale(rightAffine, leftConstant, out affine);

            if (resolveConstant(binary.Right, out var rightConstant) &&
                TryCreateCore(
                    binary.Left,
                    depth + 1,
                    maxDepth,
                    normalize,
                    resolveConstant,
                    resolveWholeFormula,
                    canUseBaseTerm,
                    out var leftAffine))
                return TryScale(leftAffine, rightConstant, out affine);

            return false;
        }

        if (binary.Operator is not (SmtIntegerBinaryOperator.Add or SmtIntegerBinaryOperator.Subtract) ||
            !TryCreateCore(
                binary.Left,
                depth + 1,
                maxDepth,
                normalize,
                resolveConstant,
                resolveWholeFormula,
                canUseBaseTerm,
                out var left) ||
            !TryCreateCore(
                binary.Right,
                depth + 1,
                maxDepth,
                normalize,
                resolveConstant,
                resolveWholeFormula,
                canUseBaseTerm,
                out var right))
            return false;

        return binary.Operator == SmtIntegerBinaryOperator.Add
            ? TryAdd(left, right, out affine)
            : TrySubtract(left, right, out affine);
    }

    private static bool TryCreateBaseTerm(
        SmtFormula formula,
        Func<SmtFormula, bool> canUseBaseTerm,
        out SmtAffineIntegerTerm affine)
    {
        if (!canUseBaseTerm(formula))
        {
            affine = default;
            return false;
        }

        affine = Term(formula);
        return true;
    }

    private static bool TryCombine(
        SmtAffineIntegerTerm left,
        SmtAffineIntegerTerm right,
        bool subtractRight,
        out SmtAffineIntegerTerm result)
    {
        result = default;
        var rightScale = right.Scale;
        var rightOffset = right.Offset;
        if (subtractRight &&
            (!SmtIntegerArithmetic.TryNegate(rightScale, out rightScale) ||
             !SmtIntegerArithmetic.TryNegate(rightOffset, out rightOffset)))
            return false;

        if (!SmtIntegerArithmetic.TryAdd(left.Offset, rightOffset, out var offset)) return false;

        if (left.BaseTerm == null && right.BaseTerm == null)
        {
            result = Constant(offset);
            return true;
        }

        if (left.BaseTerm == null)
        {
            result = rightScale == 0 ? Constant(offset) : new SmtAffineIntegerTerm(right.BaseTerm, rightScale, offset);
            return true;
        }

        if (right.BaseTerm == null)
        {
            result = left.Scale == 0 ? Constant(offset) : new SmtAffineIntegerTerm(left.BaseTerm, left.Scale, offset);
            return true;
        }

        if (!left.BaseTerm.Equals(right.BaseTerm) ||
            !SmtIntegerArithmetic.TryAdd(left.Scale, rightScale, out var scale))
            return false;

        result = scale == 0 ? Constant(offset) : new SmtAffineIntegerTerm(left.BaseTerm, scale, offset);
        return true;
    }
}

internal static class SmtIntegerArithmetic
{
    internal static bool TryEvaluateBinary(
        SmtIntegerBinaryOperator op,
        long left,
        long right,
        out long value)
    {
        if (op is not (SmtIntegerBinaryOperator.Add or
            SmtIntegerBinaryOperator.Subtract or
            SmtIntegerBinaryOperator.Multiply or
            SmtIntegerBinaryOperator.Divide or
            SmtIntegerBinaryOperator.Remainder))
        {
            value = default;
            return false;
        }

        try
        {
            value = op switch
            {
                SmtIntegerBinaryOperator.Add => checked(left + right),
                SmtIntegerBinaryOperator.Subtract => checked(left - right),
                SmtIntegerBinaryOperator.Multiply => checked(left * right),
                SmtIntegerBinaryOperator.Divide => checked(left / right),
                SmtIntegerBinaryOperator.Remainder => checked(left % right),
                _ => throw new InvalidOperationException("Unexpected integer operator.")
            };
            return true;
        }
        catch (Exception exception) when (exception is OverflowException or DivideByZeroException)
        {
            value = default;
            return false;
        }
    }

    internal static bool TryAdd(long left, long right, out long value)
    {
        return TryBinary(left, right, static (first, second) => checked(first + second), out value);
    }

    internal static bool TrySubtract(long left, long right, out long value)
    {
        return TryBinary(left, right, static (first, second) => checked(first - second), out value);
    }

    internal static bool TryMultiply(long left, long right, out long value)
    {
        return TryBinary(left, right, static (first, second) => checked(first * second), out value);
    }

    internal static bool TryNegate(long value, out long result)
    {
        if (value == long.MinValue)
        {
            result = default;
            return false;
        }

        result = -value;
        return true;
    }

    internal static BigInteger FloorDivide(BigInteger dividend, BigInteger positiveDivisor)
    {
        var quotient = BigInteger.DivRem(dividend, positiveDivisor, out var remainder);
        return remainder != 0 && dividend.Sign < 0 ? quotient - BigInteger.One : quotient;
    }

    internal static BigInteger CeilingDivide(BigInteger dividend, BigInteger positiveDivisor)
    {
        var quotient = BigInteger.DivRem(dividend, positiveDivisor, out var remainder);
        return remainder != 0 && dividend.Sign > 0 ? quotient + BigInteger.One : quotient;
    }



    private static bool TryBinary(long left, long right, Func<long, long, long> operation, out long value)
    {
        try
        {
            value = operation(left, right);
            return true;
        }
        catch (OverflowException)
        {
            value = default;
            return false;
        }
    }
}
