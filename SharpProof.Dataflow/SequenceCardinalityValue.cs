namespace SharpProof.Dataflow;

public enum SequenceCardinalityKind {
    Bottom,
    Empty,
    NonEmpty,
    Top
}

/// <summary>
/// Sequence emptiness refined by a non-negative length interval.
/// </summary>
public readonly struct SequenceCardinalityValue : IEquatable<SequenceCardinalityValue> {
    internal SequenceCardinalityValue(SequenceCardinalityKind kind, IntervalValue length) {
        Kind = kind;
        Length = length;
    }

    public static SequenceCardinalityValue Bottom => default;
    public static SequenceCardinalityValue Empty => SequenceCardinalityDomain.Instance.Empty;
    public static SequenceCardinalityValue NonEmpty => SequenceCardinalityDomain.Instance.NonEmpty;
    public static SequenceCardinalityValue Top => SequenceCardinalityDomain.Instance.Top;
    public static SequenceCardinalityValue KnownLength(long length) =>
        SequenceCardinalityDomain.Instance.KnownLength(length);

    public SequenceCardinalityKind Kind { get; }
    public IntervalValue Length { get; }
    public bool IsBottom => Kind == SequenceCardinalityKind.Bottom;

    public bool Equals(SequenceCardinalityValue other) =>
        Kind == other.Kind && Length.Equals(other.Length);

    public override bool Equals(object? obj) =>
        obj is SequenceCardinalityValue other && Equals(other);

    public override int GetHashCode() {
        unchecked {
            return ((int)Kind * 397) ^ Length.GetHashCode();
        }
    }

    public override string ToString() => IsBottom ? "bottom" : $"{Kind} length={Length}";

    public static bool operator ==(
        SequenceCardinalityValue left,
        SequenceCardinalityValue right) =>
        left.Equals(right);

    public static bool operator !=(
        SequenceCardinalityValue left,
        SequenceCardinalityValue right) =>
        !left.Equals(right);
}
