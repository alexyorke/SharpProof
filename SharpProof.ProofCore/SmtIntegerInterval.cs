namespace SharpProof.ProofCore.Smt;

internal readonly struct SmtIntegerInterval : IEquatable<SmtIntegerInterval>
{
    private SmtIntegerInterval(
        long? lowerBound,
        long? upperBound,
        long[] excludedValues,
        bool isImpossible)
    {
        LowerBound = lowerBound;
        UpperBound = upperBound;
        ExcludedValues = excludedValues;
        IsImpossible = isImpossible;
    }

    internal static SmtIntegerInterval Unbounded { get; } = new(
        null,
        null,
        Array.Empty<long>(),
        false);

    internal long? LowerBound { get; }

    internal long? UpperBound { get; }

    private long[] ExcludedValues { get; }

    internal bool IsImpossible { get; }

    internal bool IsContradictory =>
        IsImpossible ||
        (LowerBound.HasValue && UpperBound.HasValue && LowerBound.Value > UpperBound.Value) ||
        (LowerBound.HasValue &&
         UpperBound.HasValue &&
         LowerBound.Value == UpperBound.Value &&
         ExcludedValues.Contains(LowerBound.Value));

    internal long? ExactValue =>
        !IsContradictory &&
        LowerBound.HasValue &&
        UpperBound.HasValue &&
        LowerBound.Value == UpperBound.Value
            ? LowerBound.Value
            : null;

    internal bool Excludes(long value) =>
        ExcludedValues.Contains(value) ||
        (LowerBound.HasValue && value < LowerBound.Value) ||
        (UpperBound.HasValue && value > UpperBound.Value);

    internal SmtIntegerInterval Apply(SmtBinaryOperator op, long constant)
    {
        return op switch
        {
            SmtBinaryOperator.Equal => WithExactValue(constant),
            SmtBinaryOperator.NotEqual => Exclude(constant),
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

    internal SmtIntegerInterval Intersect(SmtIntegerInterval other)
    {
        var interval = this;
        if (other.IsImpossible) interval = interval.Impossible();
        if (other.LowerBound.HasValue) interval = interval.WithLowerBound(other.LowerBound.Value);
        if (other.UpperBound.HasValue) interval = interval.WithUpperBound(other.UpperBound.Value);

        foreach (var excludedValue in other.ExcludedValues)
            interval = interval.Apply(SmtBinaryOperator.NotEqual, excludedValue);

        return interval;
    }

    public bool Equals(SmtIntegerInterval other)
    {
        return LowerBound == other.LowerBound &&
               UpperBound == other.UpperBound &&
               IsImpossible == other.IsImpossible &&
               ExcludedValues.SequenceEqual(other.ExcludedValues);
    }

    public override bool Equals(object? obj)
    {
        return obj is SmtIntegerInterval other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = LowerBound.GetHashCode();
            hash = (hash * 397) ^ UpperBound.GetHashCode();
            hash = (hash * 397) ^ IsImpossible.GetHashCode();
            foreach (var value in ExcludedValues) hash = (hash * 397) ^ value.GetHashCode();
            return hash;
        }
    }

    private SmtIntegerInterval Exclude(long value)
    {
        if (Array.IndexOf(ExcludedValues, value) >= 0) return this;

        var excludedValues = new long[ExcludedValues.Length + 1];
        Array.Copy(ExcludedValues, excludedValues, ExcludedValues.Length);
        excludedValues[excludedValues.Length - 1] = value;
        return new SmtIntegerInterval(LowerBound, UpperBound, excludedValues, IsImpossible);
    }

    private SmtIntegerInterval WithLowerBound(long lowerBound)
    {
        return new SmtIntegerInterval(
            LowerBound.HasValue ? Math.Max(LowerBound.Value, lowerBound) : lowerBound,
            UpperBound,
            ExcludedValues,
            IsImpossible);
    }

    private SmtIntegerInterval WithUpperBound(long upperBound)
    {
        return new SmtIntegerInterval(
            LowerBound,
            UpperBound.HasValue ? Math.Min(UpperBound.Value, upperBound) : upperBound,
            ExcludedValues,
            IsImpossible);
    }

    private SmtIntegerInterval WithExactValue(long value)
    {
        return new SmtIntegerInterval(
            value,
            value,
            ExcludedValues,
            IsImpossible ||
            (LowerBound.HasValue && value < LowerBound.Value) ||
            (UpperBound.HasValue && value > UpperBound.Value));
    }

    private SmtIntegerInterval Impossible()
    {
        return new SmtIntegerInterval(LowerBound, UpperBound, ExcludedValues, true);
    }
}
