namespace SharpProof.Dataflow;

/// <summary>
/// Four-point nullness domain.
/// </summary>
public sealed class NullnessDomain : ClosedAbstractDomain<NullnessValue>
{
    public static NullnessDomain Instance { get; } = new();

    private NullnessDomain()
    {
    }

    public override NullnessValue Bottom => NullnessValue.Bottom;
    public override NullnessValue Top => NullnessValue.MaybeNull;

    public override bool LessThanOrEqual(NullnessValue left, NullnessValue right)
    {
        Validate(left);
        Validate(right);
        return left == right || left == NullnessValue.Bottom || right == NullnessValue.MaybeNull;
    }

    public override NullnessValue Join(NullnessValue left, NullnessValue right)
    {
        Validate(left);
        Validate(right);
        if (left == right)
        {
            return left;
        }

        if (left == NullnessValue.Bottom)
        {
            return right;
        }

        if (right == NullnessValue.Bottom)
        {
            return left;
        }

        return NullnessValue.MaybeNull;
    }

    public override NullnessValue Widen(NullnessValue previous, NullnessValue candidate)
    {
        return Join(previous, candidate);
    }

    public override NullnessValue Havoc(NullnessValue value)
    {
        Validate(value);
        return value == NullnessValue.Bottom ? Bottom : Top;
    }

    public NullnessValue AssumeNull(NullnessValue value)
    {
        return Assume(value, NullnessValue.Null);
    }

    public NullnessValue AssumeNonNull(NullnessValue value)
    {
        return Assume(value, NullnessValue.NonNull);
    }

    private NullnessValue Assume(NullnessValue value, NullnessValue expected)
    {
        Validate(value);
        return value switch
        {
            NullnessValue.Bottom => Bottom,
            NullnessValue.MaybeNull => expected,
            _ when value == expected => expected,
            _ => Bottom
        };
    }

    private static void Validate(NullnessValue value)
    {
        _ = ArgumentNullGuard.RequireDefined(value, nameof(value));
    }
}
