namespace SharpProof.Dataflow;

/// <summary>
/// Reduced interval and congruence domain over signed 64-bit integers.
/// </summary>
public sealed class IntervalDomain : CanonicalAbstractDomain<IntervalValue>
{
    public static IntervalDomain Instance { get; } = new();
    private IntervalDomain()
    {
    }

    public override IntervalValue Bottom => IntervalValue.Bottom;
    public override IntervalValue Top { get; } = new(null, null, 1, 0);

    protected override bool IsCanonical(IntervalValue value)
    {
        // The value constructor is internal and every public factory creates
        // the canonical representation, so every externally supplied value is
        // canonical.
        return true;
    }

    public IntervalValue Constant(long value)
    {
        return new(value, value, 0, value);
    }

    public IntervalValue Range(long? lowerBound, long? upperBound)
    {
        return Create(lowerBound, upperBound, 1, 0);
    }

    public IntervalValue Create(
        long? lowerBound, long? upperBound, BigInteger modulus, BigInteger remainder)
    {
        if (modulus.Sign < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(modulus));
        }

        if (lowerBound.HasValue && upperBound.HasValue &&
            lowerBound.Value > upperBound.Value)
        {
            return Bottom;
        }

        if (modulus.IsZero)
        {
            if (lowerBound.HasValue && remainder < lowerBound.Value ||
                upperBound.HasValue && remainder > upperBound.Value ||
                remainder < long.MinValue ||
                remainder > long.MaxValue)
            {
                return Bottom;
            }

            return Constant((long)remainder);
        }

        var normalizedRemainder = Normalize(remainder, modulus);
        long? adjustedLower = lowerBound == long.MinValue ? null : lowerBound;
        long? adjustedUpper = upperBound == long.MaxValue ? null : upperBound;
        if (!TryCongruentBoundary(adjustedLower ?? long.MinValue,
                modulus, normalizedRemainder, atOrAbove: true, out var first))
        {
            return Bottom;
        }

        if (adjustedLower.HasValue)
        {
            adjustedLower = first;
        }

        if (!TryCongruentBoundary(adjustedUpper ?? long.MaxValue,
                modulus, normalizedRemainder, atOrAbove: false, out var last))
        {
            return Bottom;
        }

        if (adjustedUpper.HasValue)
        {
            adjustedUpper = last;
        }

        if (first > last)
        {
            return Bottom;
        }

        if (first == last)
        {
            return Constant(first);
        }

        return new IntervalValue(adjustedLower, adjustedUpper, modulus, normalizedRemainder);
    }

    public override bool LessThanOrEqual(IntervalValue left, IntervalValue right)
    {
        if (left.IsBottom)
        {
            return true;
        }

        if (right.IsBottom)
        {
            return false;
        }

        if (right.LowerBound.HasValue &&
            (!left.LowerBound.HasValue || left.LowerBound.Value < right.LowerBound.Value))
        {
            return false;
        }

        if (right.UpperBound.HasValue &&
            (!left.UpperBound.HasValue || left.UpperBound.Value > right.UpperBound.Value))
        {
            return false;
        }

        return CongruenceIncludes(right, left);
    }

    public override IntervalValue Join(IntervalValue left, IntervalValue right)
    {
        if (left.IsBottom || right.IsBottom)
        {
            return left.IsBottom ? right : left;
        }

        var lower = left.LowerBound.HasValue && right.LowerBound.HasValue
            ? (long?)Math.Min(left.LowerBound.Value, right.LowerBound.Value)
            : null;
        var upper = left.UpperBound.HasValue && right.UpperBound.HasValue
            ? (long?)Math.Max(left.UpperBound.Value, right.UpperBound.Value)
            : null;
        var (modulus, remainder) = GetCongruenceHull(left, right);
        return Create(lower, upper, modulus, remainder);
    }

    public override IntervalValue Widen(IntervalValue previous, IntervalValue candidate)
    {
        if (previous.IsBottom)
        {
            return candidate;
        }

        if (candidate.IsBottom || LessThanOrEqual(candidate, previous))
        {
            return previous;
        }

        var (modulus, remainder) = GetCongruenceHull(previous, candidate);
        var lower = previous.LowerBound.HasValue &&
                    candidate.LowerBound.HasValue &&
                    candidate.LowerBound.Value >= previous.LowerBound.Value
            ? previous.LowerBound
            : null;
        var upper = previous.UpperBound.HasValue &&
                    candidate.UpperBound.HasValue &&
                    candidate.UpperBound.Value <= previous.UpperBound.Value
            ? previous.UpperBound
            : null;
        return Create(lower, upper, modulus, remainder);
    }

    private static (BigInteger Modulus, BigInteger Remainder) GetCongruenceHull(
        IntervalValue left, IntervalValue right)
    {
        var difference = BigInteger.Abs(left.Remainder - right.Remainder);
        var modulus = BigInteger.GreatestCommonDivisor(
            BigInteger.GreatestCommonDivisor(left.Modulus, right.Modulus), difference);
        var remainder = modulus.IsZero ? left.Remainder : Normalize(left.Remainder, modulus);
        return (modulus, remainder);
    }

    public override IntervalValue Havoc(IntervalValue value)
    {
        return value.IsBottom ? Bottom : Top;
    }

    public IntervalValue AssumeAtLeast(IntervalValue value, long lowerBound)
    {
        return RestrictBound(value, lowerBound, atLeast: true);
    }

    public IntervalValue AssumeAtMost(IntervalValue value, long upperBound)
    {
        return RestrictBound(value, upperBound, atLeast: false);
    }

    private IntervalValue RestrictBound(
        IntervalValue value, long bound, bool atLeast)
    {
        if (value.IsBottom)
        {
            return Bottom;
        }

        var lower = atLeast
            ? value.LowerBound.HasValue
                ? Math.Max(value.LowerBound.Value, bound)
                : bound
            : value.LowerBound;
        var upper = atLeast
            ? value.UpperBound
            : value.UpperBound.HasValue
                ? Math.Min(value.UpperBound.Value, bound)
                : bound;
        return Create(lower, upper, value.Modulus, value.Remainder);
    }

    internal static BigInteger Normalize(BigInteger value, BigInteger modulus)
    {
        var normalized = value % modulus;
        return normalized.Sign < 0 ? normalized + modulus : normalized;
    }

    private static bool CongruenceIncludes(IntervalValue outer, IntervalValue inner)
    {
        if (outer.Modulus.IsOne)
        {
            return true;
        }

        if (outer.Modulus.IsZero)
        {
            return inner.Modulus.IsZero && inner.Remainder == outer.Remainder;
        }

        return (inner.Modulus % outer.Modulus).IsZero &&
               Normalize(inner.Remainder, outer.Modulus) == outer.Remainder;
    }

    private static bool TryCongruentBoundary(
        long boundary,
        BigInteger modulus,
        BigInteger remainder,
        bool atOrAbove,
        out long result)
    {
        var boundaryRemainder = Normalize(boundary, modulus);
        var delta = Normalize(
            atOrAbove
                ? remainder - boundaryRemainder
                : boundaryRemainder - remainder,
            modulus);
        var candidate = atOrAbove ? boundary + delta : boundary - delta;
        if (candidate < long.MinValue || candidate > long.MaxValue)
        {
            result = default;
            return false;
        }
        result = (long)candidate;
        return true;
    }
}
