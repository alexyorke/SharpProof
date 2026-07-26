namespace SharpProof.Attributes;

[AttributeUsage(
    AttributeTargets.Parameter | AttributeTargets.ReturnValue | AttributeTargets.Property | AttributeTargets.Field,
    Inherited = false)]
public sealed class NotNullAttribute : Attribute;

[AttributeUsage(
    AttributeTargets.Parameter | AttributeTargets.ReturnValue | AttributeTargets.Property | AttributeTargets.Field,
    Inherited = false)]
public sealed class PositiveAttribute : Attribute;

[AttributeUsage(
    AttributeTargets.Parameter | AttributeTargets.ReturnValue | AttributeTargets.Property | AttributeTargets.Field,
    Inherited = false)]
public sealed class InRangeAttribute : Attribute {
    public InRangeAttribute(long minimum, long maximum) {
        if (minimum > maximum) throw new ArgumentOutOfRangeException(nameof(minimum), "Minimum cannot exceed maximum.");
        Minimum = minimum;
        Maximum = maximum;
    }

    public long Minimum { get; }
    public long Maximum { get; }
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, Inherited = false)]
public sealed class PureAttribute : Attribute;
