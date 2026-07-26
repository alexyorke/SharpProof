namespace SharpProof.Dataflow;

/// <summary>
/// Four-point nullness domain.
/// </summary>
public sealed class NullnessDomain : ClosedAbstractDomain<NullnessValue> {
    public static NullnessDomain Instance { get; } = new();

    private NullnessDomain() {
    }

    public override NullnessValue Bottom => NullnessValue.Bottom;
    public override NullnessValue Top => NullnessValue.MaybeNull;

    public override bool LessThanOrEqual(NullnessValue left, NullnessValue right) {
        Validate(left);
        Validate(right);
        return left == right ||
               left == NullnessValue.Bottom ||
               right == NullnessValue.MaybeNull;
    }

    public override NullnessValue Join(NullnessValue left, NullnessValue right) {
        Validate(left);
        Validate(right);
        if (left == right) return left;
        if (left == NullnessValue.Bottom) return right;
        if (right == NullnessValue.Bottom) return left;
        return NullnessValue.MaybeNull;
    }

    public override NullnessValue Widen(NullnessValue previous, NullnessValue next) =>
        Join(previous, next);

    public override NullnessValue Havoc(NullnessValue value) {
        Validate(value);
        return value == NullnessValue.Bottom ? Bottom : Top;
    }

    public NullnessValue AssumeNull(NullnessValue value) {
        Validate(value);
        return value switch {
            NullnessValue.Bottom => Bottom,
            NullnessValue.Null => NullnessValue.Null,
            NullnessValue.NonNull => Bottom,
            NullnessValue.MaybeNull => NullnessValue.Null,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }

    public NullnessValue AssumeNonNull(NullnessValue value) {
        Validate(value);
        return value switch {
            NullnessValue.Bottom => Bottom,
            NullnessValue.Null => Bottom,
            NullnessValue.NonNull => NullnessValue.NonNull,
            NullnessValue.MaybeNull => NullnessValue.NonNull,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }

    private static void Validate(NullnessValue value) {
        if (value < NullnessValue.Bottom || value > NullnessValue.MaybeNull)
            throw new ArgumentOutOfRangeException(nameof(value));
    }
}
