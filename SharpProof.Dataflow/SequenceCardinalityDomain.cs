namespace SharpProof.Dataflow;

/// <summary>
/// Product domain for sequence emptiness and length.
/// </summary>
public sealed class SequenceCardinalityDomain : CanonicalAbstractDomain<SequenceCardinalityValue>
{
    private readonly IntervalDomain _intervals = IntervalDomain.Instance;

    public static SequenceCardinalityDomain Instance { get; } = new();

    private SequenceCardinalityDomain()
    {
        Empty = new SequenceCardinalityValue(SequenceCardinalityKind.Empty, _intervals.Constant(0));
        NonEmpty = new SequenceCardinalityValue(SequenceCardinalityKind.NonEmpty, _intervals.Range(1, null));
        Top = new SequenceCardinalityValue(SequenceCardinalityKind.Top, _intervals.Range(0, null));
    }

    public override SequenceCardinalityValue Bottom => SequenceCardinalityValue.Bottom;
    public SequenceCardinalityValue Empty
    {
        get;
    }
    public SequenceCardinalityValue NonEmpty
    {
        get;
    }
    public override SequenceCardinalityValue Top
    {
        get;
    }

    protected override bool IsCanonical(SequenceCardinalityValue value)
    {
        // The value constructor is internal and every public factory creates
        // the canonical kind/length pair.
        return true;
    }

    public SequenceCardinalityValue KnownLength(long length)
    {
        length = ArgumentNullGuard.RequireNonnegative(length, nameof(length));
        return Create(SequenceCardinalityKind.Top, _intervals.Constant(length));
    }

    public SequenceCardinalityValue Create(SequenceCardinalityKind kind, IntervalValue length)
    {
        Validate(kind);
        if (kind == SequenceCardinalityKind.Bottom || length.IsBottom)
        {
            return Bottom;
        }

        var minimumLength = kind == SequenceCardinalityKind.NonEmpty ? 1 : 0;
        var restricted = _intervals.AssumeAtLeast(length, minimumLength);
        if (restricted.IsBottom)
        {
            return Bottom;
        }

        switch (kind)
        {
            case SequenceCardinalityKind.Empty:
                return restricted.Contains(0) ? Empty : Bottom;
            case SequenceCardinalityKind.NonEmpty:
                break;
            case SequenceCardinalityKind.Top:
                break;
        }

        var canonicalKind = restricted.IsSingleton && restricted.SingletonValue == 0
            ? SequenceCardinalityKind.Empty
            : restricted.LowerBound >= 1
                ? SequenceCardinalityKind.NonEmpty
                : SequenceCardinalityKind.Top;
        return new SequenceCardinalityValue(canonicalKind, restricted);
    }

    public override bool LessThanOrEqual(
        SequenceCardinalityValue left, SequenceCardinalityValue right)
    {
        Validate(left.Kind);
        Validate(right.Kind);
        return LessThanOrEqualValidated(left, right);
    }

    private bool LessThanOrEqualValidated(
        SequenceCardinalityValue left, SequenceCardinalityValue right)
    {
        if (left.IsBottom)
        {
            return true;
        }

        if (right.IsBottom)
        {
            return false;
        }

        return _intervals.LessThanOrEqual(left.Length, right.Length);
    }

    public override SequenceCardinalityValue Join(
        SequenceCardinalityValue left, SequenceCardinalityValue right)
    {
        Validate(left.Kind);
        Validate(right.Kind);
        if (left.IsBottom || right.IsBottom)
        {
            return left.IsBottom ? right : left;
        }

        return Create(
            SequenceCardinalityKind.Top,
            _intervals.Join(left.Length, right.Length));
    }

    public override SequenceCardinalityValue Widen(
        SequenceCardinalityValue previous, SequenceCardinalityValue candidate)
    {
        Validate(previous.Kind);
        Validate(candidate.Kind);
        if (previous.IsBottom)
        {
            return candidate;
        }

        if (candidate.IsBottom || LessThanOrEqualValidated(candidate, previous))
        {
            return previous;
        }

        return Create(
            JoinKind(previous.Kind, candidate.Kind),
            _intervals.Widen(previous.Length, candidate.Length));
    }

    public override SequenceCardinalityValue Havoc(SequenceCardinalityValue value)
    {
        Validate(value.Kind);
        return value.IsBottom ? Bottom : Top;
    }

    private static SequenceCardinalityKind JoinKind(
        SequenceCardinalityKind left, SequenceCardinalityKind right)
    {
        return left == right ? left : SequenceCardinalityKind.Top;
    }

    private static void Validate(SequenceCardinalityKind kind)
    {
        _ = ArgumentNullGuard.RequireDefined(kind, nameof(kind));
    }
}
