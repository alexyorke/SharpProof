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
public readonly record struct SequenceCardinalityValue {
    internal SequenceCardinalityValue(SequenceCardinalityKind kind, IntervalValue length) =>
        (Kind, Length) = (kind, length);

    public static SequenceCardinalityValue Bottom => default;
    public static SequenceCardinalityValue Empty => SequenceCardinalityDomain.Instance.Empty;
    public static SequenceCardinalityValue NonEmpty => SequenceCardinalityDomain.Instance.NonEmpty;
    public static SequenceCardinalityValue Top => SequenceCardinalityDomain.Instance.Top;
    public static SequenceCardinalityValue KnownLength(long length) =>
        SequenceCardinalityDomain.Instance.KnownLength(length);

    public SequenceCardinalityKind Kind { get; }
    public IntervalValue Length { get; }
    public bool IsBottom => Kind == SequenceCardinalityKind.Bottom;

    public override string ToString() => IsBottom ? "bottom" : $"{Kind} length={Length}";
}
