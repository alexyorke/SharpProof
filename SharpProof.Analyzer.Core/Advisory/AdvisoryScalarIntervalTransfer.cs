using SharpProof.Dataflow;
using SharpProof.Ir;
using System.Numerics;

#if SHARPPROOF_WORKER_ADVISORY_TRANSFER
namespace SharpProof.Advisory.Worker;
#else
namespace SharpProof.Advisory.Core;
#endif

// Advisory transfer for Total integer terms. These facts do not enter VCs.
// UInt64 cannot be represented by the signed Int64 interval carrier.
internal static class CoreIrScalarIntervalTransfer
{
    internal static bool TryTypeRange(IrTypeInfo type, out IntervalValue range)
    {
        range = IntervalValue.Bottom;
        if (type.Kind != IrTypeKind.Integer ||
            type.Width is not (8 or 16 or 32 or 64) ||
            type.Width == 64 && !type.Signed)
        {
            return false;
        }
        var magnitude = BigInteger.One << (type.Signed ? type.Width - 1 : type.Width);
        range = IntervalValue.Range(type.Signed ? (long)-magnitude : 0, (long)(magnitude - 1));
        return true;
    }

    internal static IntervalValue Binary(IrBinaryOperator operation, IrTypeInfo type,
        IntervalValue left, IntervalValue right)
    {
        var range = TypeRange(type);
        left = Restrict(left, range);
        right = Restrict(right, range);
        if (left.IsBottom || right.IsBottom)
        {
            return IntervalValue.Bottom;
        }
        if (operation is not (IrBinaryOperator.Add or IrBinaryOperator.Subtract or
            IrBinaryOperator.Multiply or IrBinaryOperator.Divide or IrBinaryOperator.Remainder))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }
        if (left.IsSingleton && right.IsSingleton)
        {
            BigInteger first = left.SingletonValue;
            BigInteger second = right.SingletonValue;
            var value = operation switch
            {
                IrBinaryOperator.Add => first + second,
                IrBinaryOperator.Subtract => first - second,
                IrBinaryOperator.Multiply => first * second,
                IrBinaryOperator.Divide when second.IsZero =>
                    type.Signed && first.Sign < 0 ? BigInteger.One : (BigInteger.One << type.Width) - 1,
                IrBinaryOperator.Divide => first / second,
                IrBinaryOperator.Remainder when second.IsZero => first,
                _ => first % second
            };
            return IntervalValue.Constant(Wrap(value, type));
        }
        BigInteger lowerLeft = left.LowerBound ?? range.LowerBound ?? long.MinValue;
        BigInteger upperLeft = left.UpperBound ?? range.UpperBound ?? long.MaxValue;
        BigInteger lowerRight = right.LowerBound ?? range.LowerBound ?? long.MinValue;
        BigInteger upperRight = right.UpperBound ?? range.UpperBound ?? long.MaxValue;
        if (operation == IrBinaryOperator.Add)
        {
            return Bounded(lowerLeft + lowerRight, upperLeft + upperRight, range);
        }
        if (operation == IrBinaryOperator.Subtract)
        {
            return Bounded(lowerLeft - upperRight, upperLeft - lowerRight, range);
        }
        if (operation == IrBinaryOperator.Multiply)
        {
            var first = lowerLeft * lowerRight;
            var second = lowerLeft * upperRight;
            var third = upperLeft * lowerRight;
            var fourth = upperLeft * upperRight;
            return Bounded(BigInteger.Min(BigInteger.Min(first, second), BigInteger.Min(third, fourth)),
                BigInteger.Max(BigInteger.Max(first, second), BigInteger.Max(third, fourth)), range);
        }
        return range;
    }

    internal static IntervalValue Negate(IrTypeInfo type, IntervalValue operand)
    {
        var range = TypeRange(type);
        operand = Restrict(operand, range);
        if (operand.IsBottom)
        {
            return operand;
        }
        if (operand.IsSingleton)
        {
            return IntervalValue.Constant(Wrap(-(BigInteger)operand.SingletonValue, type));
        }
        return Bounded(-(BigInteger)(operand.UpperBound ?? range.UpperBound ?? long.MaxValue),
            -(BigInteger)(operand.LowerBound ?? range.LowerBound ?? long.MinValue), range);
    }

    internal static IntervalValue Cast(IrTypeInfo source, IrTypeInfo target, IntervalValue operand)
    {
        var sourceRange = TypeRange(source);
        var targetRange = TypeRange(target);
        operand = Restrict(operand, sourceRange);
        if (operand.IsBottom)
        {
            return operand;
        }
        if (operand.IsSingleton)
        {
            return IntervalValue.Constant(Wrap(operand.SingletonValue, target));
        }
        var lower = operand.LowerBound ?? sourceRange.LowerBound ?? long.MinValue;
        var upper = operand.UpperBound ?? sourceRange.UpperBound ?? long.MaxValue;
        return lower >= (targetRange.LowerBound ?? long.MinValue) &&
            upper <= (targetRange.UpperBound ?? long.MaxValue)
            ? operand : targetRange;
    }

    private static IntervalValue TypeRange(IrTypeInfo type)
    {
        return TryTypeRange(type, out var range) ? range :
            throw new ArgumentException("The type has no signed Int64 interval representation.", nameof(type));
    }

    private static IntervalValue Restrict(IntervalValue value, IntervalValue range)
    {
        return IntervalDomain.Instance.AssumeAtMost(
            IntervalDomain.Instance.AssumeAtLeast(value, range.LowerBound ?? long.MinValue),
            range.UpperBound ?? long.MaxValue);
    }

    private static IntervalValue Bounded(BigInteger lower, BigInteger upper, IntervalValue range)
    {
        return lower >= (range.LowerBound ?? long.MinValue) &&
            upper <= (range.UpperBound ?? long.MaxValue)
            ? IntervalValue.Range((long)lower, (long)upper) : range;
    }

    private static long Wrap(BigInteger value, IrTypeInfo type)
    {
        var modulus = BigInteger.One << type.Width;
        value %= modulus;
        if (value.Sign < 0)
        {
            value += modulus;
        }
        if (type.Signed && value >= (modulus >> 1))
        {
            value -= modulus;
        }
        return (long)value;
    }
}
