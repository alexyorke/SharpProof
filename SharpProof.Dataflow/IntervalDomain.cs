namespace SharpProof.Dataflow;

/// <summary>
/// Reduced interval and congruence domain over signed 64-bit integers.
/// </summary>
public sealed class IntervalDomain : ClosedAbstractDomain<IntervalValue> {
    public static IntervalDomain Instance { get; } = new();
    private IntervalDomain() {
    }

    public override IntervalValue Bottom => IntervalValue.Bottom;
    public override IntervalValue Top { get; } = new(null, null, 1, 0);

    public IntervalValue Constant(long value) => new(value, value, 0, value);
    public IntervalValue Range(long? lowerBound, long? upperBound) =>
        Create(lowerBound, upperBound, 1, 0);

    public IntervalValue Create(
        long? lowerBound, long? upperBound, BigInteger modulus, BigInteger remainder) {
        if (modulus.Sign < 0) throw new ArgumentOutOfRangeException(nameof(modulus));
        if (lowerBound.HasValue && upperBound.HasValue && lowerBound.Value > upperBound.Value)
            return Bottom;

        if (modulus.IsZero) {
            if (lowerBound.HasValue && remainder < lowerBound.Value ||
                upperBound.HasValue && remainder > upperBound.Value ||
                remainder < long.MinValue ||
                remainder > long.MaxValue)
                return Bottom;
            return Constant((long)remainder);
        }

        var normalizedRemainder = Normalize(remainder, modulus);
        var adjustedLower = lowerBound;
        var adjustedUpper = upperBound;
        if (!TryCongruentBoundary(
                adjustedLower ?? long.MinValue,
                modulus,
                normalizedRemainder,
                atOrAbove: true,
                out var first))
            return Bottom;
        if (adjustedLower.HasValue)
            adjustedLower = first;

        if (!TryCongruentBoundary(
                adjustedUpper ?? long.MaxValue,
                modulus,
                normalizedRemainder,
                atOrAbove: false,
                out var last))
            return Bottom;
        if (adjustedUpper.HasValue)
            adjustedUpper = last;

        if (first > last)
            return Bottom;
        if (first == last)
            return Constant(first);
        return new IntervalValue(adjustedLower, adjustedUpper, modulus, normalizedRemainder);
    }

    public override bool LessThanOrEqual(IntervalValue left, IntervalValue right) {
        if (left.IsBottom) return true;
        if (right.IsBottom) return false;
        if (right.LowerBound.HasValue &&
            (!left.LowerBound.HasValue || left.LowerBound.Value < right.LowerBound.Value))
            return false;
        if (right.UpperBound.HasValue &&
            (!left.UpperBound.HasValue || left.UpperBound.Value > right.UpperBound.Value))
            return false;
        return CongruenceIncludes(right, left);
    }

    public override IntervalValue Join(IntervalValue left, IntervalValue right) {
        if (left.IsBottom) return right;
        if (right.IsBottom) return left;

        var lower = HullLower(left.LowerBound, right.LowerBound);
        var upper = HullUpper(left.UpperBound, right.UpperBound);
        var difference = BigInteger.Abs(left.Remainder - right.Remainder);
        var modulus = BigInteger.GreatestCommonDivisor(
            BigInteger.GreatestCommonDivisor(left.Modulus, right.Modulus),
            difference);
        var remainder = modulus.IsZero ? left.Remainder : Normalize(left.Remainder, modulus);
        return Create(lower, upper, modulus, remainder);
    }

    public override IntervalValue Widen(IntervalValue previous, IntervalValue candidate) {
        if (previous.IsBottom) return candidate;
        if (candidate.IsBottom || LessThanOrEqual(candidate, previous)) return previous;

        var joined = Join(previous, candidate);
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
        return Create(lower, upper, joined.Modulus, joined.Remainder);
    }

    public override IntervalValue Havoc(IntervalValue value) => value.IsBottom ? Bottom : Top;

    public IntervalValue AddConstant(IntervalValue value, long addend) =>
        Add(value, Constant(addend));

    public IntervalValue Add(IntervalValue left, IntervalValue right) {
        if (left.IsBottom || right.IsBottom) return Bottom;
        if (left.IsSingleton && right.IsSingleton) {
            try {
                return Constant(checked(left.SingletonValue + right.SingletonValue));
            }
            catch (OverflowException) {
                return Top;
            }
        }

        if (!TryAddBounds(left, right, out var lower, out var upper))
            return Top;
        var modulus = BigInteger.GreatestCommonDivisor(left.Modulus, right.Modulus);
        var remainder = modulus.IsZero
            ? BigInteger.Zero
            : Normalize(left.Remainder + right.Remainder, modulus);
        return Create(lower, upper, modulus, remainder);
    }

    public IntervalValue AssumeAtLeast(IntervalValue value, long lowerBound) {
        if (value.IsBottom) return Bottom;
        var restricted = value.LowerBound.HasValue
            ? Math.Max(value.LowerBound.Value, lowerBound)
            : lowerBound;
        return Create(restricted, value.UpperBound, value.Modulus, value.Remainder);
    }

    public IntervalValue AssumeAtMost(IntervalValue value, long upperBound) {
        if (value.IsBottom) return Bottom;
        var restricted = value.UpperBound.HasValue
            ? Math.Min(value.UpperBound.Value, upperBound)
            : upperBound;
        return Create(value.LowerBound, restricted, value.Modulus, value.Remainder);
    }

    internal static BigInteger Normalize(BigInteger value, BigInteger modulus) {
        var normalized = value % modulus;
        return normalized.Sign < 0 ? normalized + modulus : normalized;
    }

    private static bool CongruenceIncludes(IntervalValue outer, IntervalValue inner) {
        if (outer.Modulus.IsOne) return true;
        if (outer.Modulus.IsZero)
            return inner.Modulus.IsZero && inner.Remainder == outer.Remainder;
        if (inner.Modulus.IsZero)
            return Normalize(inner.Remainder, outer.Modulus) == outer.Remainder;
        return (inner.Modulus % outer.Modulus).IsZero &&
               Normalize(inner.Remainder, outer.Modulus) == outer.Remainder;
    }

    private static long? HullLower(long? left, long? right) =>
        left.HasValue && right.HasValue ? Math.Min(left.Value, right.Value) : null;

    private static long? HullUpper(long? left, long? right) =>
        left.HasValue && right.HasValue ? Math.Max(left.Value, right.Value) : null;

    private static bool TryAddBounds(IntervalValue left, IntervalValue right,
        out long? lower, out long? upper) {
        var minimum = new BigInteger(left.LowerBound ?? long.MinValue) +
            new BigInteger(right.LowerBound ?? long.MinValue);
        var maximum = new BigInteger(left.UpperBound ?? long.MaxValue) +
            new BigInteger(right.UpperBound ?? long.MaxValue);
        var valid = minimum >= long.MinValue && maximum <= long.MaxValue;
        lower = valid && left.LowerBound.HasValue && right.LowerBound.HasValue
            ? (long)minimum : null;
        upper = valid && left.UpperBound.HasValue && right.UpperBound.HasValue
            ? (long)maximum : null;
        return valid;
    }

    private static bool TryCongruentBoundary(
        long boundary,
        BigInteger modulus,
        BigInteger remainder,
        bool atOrAbove,
        out long result) {
        var boundaryRemainder = Normalize(boundary, modulus);
        var delta = atOrAbove
            ? remainder >= boundaryRemainder
                ? remainder - boundaryRemainder
                : modulus - (boundaryRemainder - remainder)
            : boundaryRemainder >= remainder
                ? boundaryRemainder - remainder
                : modulus - (remainder - boundaryRemainder);
        var candidate = atOrAbove ? boundary + delta : boundary - delta;
        if (candidate < long.MinValue || candidate > long.MaxValue) {
            result = default;
            return false;
        }
        result = (long)candidate;
        return true;
    }
}
