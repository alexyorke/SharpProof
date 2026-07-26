namespace SharpProof.Dataflow;

/// <summary>
/// An interval of signed 64-bit integers refined by a congruence.
/// A modulus of zero denotes an exact singleton and a modulus of one denotes no
/// congruence restriction.
/// </summary>
public readonly struct IntervalValue : IEquatable<IntervalValue> {
    private readonly bool _hasValue;

    internal IntervalValue(
        long? lowerBound,
        long? upperBound,
        BigInteger modulus,
        BigInteger remainder) {
        _hasValue = true;
        LowerBound = lowerBound;
        UpperBound = upperBound;
        Modulus = modulus;
        Remainder = remainder;
    }

    public static IntervalValue Bottom => default;
    public static IntervalValue Top => IntervalDomain.Instance.Top;
    public static IntervalValue Constant(long value) => IntervalDomain.Instance.Constant(value);
    public static IntervalValue Range(long? lowerBound, long? upperBound) =>
        IntervalDomain.Instance.Range(lowerBound, upperBound);
    public static IntervalValue Congruent(
        long? lowerBound,
        long? upperBound,
        BigInteger modulus,
        BigInteger remainder) =>
        IntervalDomain.Instance.Create(lowerBound, upperBound, modulus, remainder);

    public bool IsBottom => !_hasValue;
    public long? LowerBound { get; }
    public long? UpperBound { get; }
    public BigInteger Modulus { get; }
    public BigInteger Remainder { get; }
    public bool IsSingleton =>
        _hasValue &&
        LowerBound.HasValue &&
        UpperBound.HasValue &&
        LowerBound.Value == UpperBound.Value;

    public long SingletonValue {
        get {
            if (!IsSingleton) throw new InvalidOperationException("The interval is not a singleton.");
            return LowerBound!.Value;
        }
    }

    public bool Contains(long value) {
        if (IsBottom) return false;
        if (LowerBound.HasValue && value < LowerBound.Value) return false;
        if (UpperBound.HasValue && value > UpperBound.Value) return false;
        if (Modulus.IsZero) return value == Remainder;
        return IntervalDomain.Normalize(value, Modulus) == Remainder;
    }

    public bool Equals(IntervalValue other) =>
        _hasValue == other._hasValue &&
        (!_hasValue ||
         LowerBound == other.LowerBound &&
         UpperBound == other.UpperBound &&
         Modulus == other.Modulus &&
         Remainder == other.Remainder);

    public override bool Equals(object? obj) => obj is IntervalValue other && Equals(other);

    public override int GetHashCode() {
        if (IsBottom) return 0;
        unchecked {
            var hash = 17;
            hash = hash * 31 + LowerBound.GetHashCode();
            hash = hash * 31 + UpperBound.GetHashCode();
            hash = hash * 31 + Modulus.GetHashCode();
            hash = hash * 31 + Remainder.GetHashCode();
            return hash;
        }
    }

    public override string ToString() {
        if (IsBottom) return "bottom";
        var lower = LowerBound?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "-inf";
        var upper = UpperBound?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "+inf";
        return Modulus switch {
            var value when value.IsZero => $"[{lower}, {upper}]",
            var value when value.IsOne => $"[{lower}, {upper}]",
            _ => $"[{lower}, {upper}] mod {Modulus} = {Remainder}"
        };
    }

    public static bool operator ==(IntervalValue left, IntervalValue right) => left.Equals(right);
    public static bool operator !=(IntervalValue left, IntervalValue right) => !left.Equals(right);
}
