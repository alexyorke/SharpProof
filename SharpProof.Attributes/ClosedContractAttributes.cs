namespace SharpProof.Attributes;

[AttributeUsage(
    AttributeTargets.Parameter | AttributeTargets.ReturnValue,
    Inherited = false)]
public sealed class NotNullAttribute : Attribute;

[AttributeUsage(
    AttributeTargets.Parameter | AttributeTargets.ReturnValue,
    Inherited = false)]
public sealed class PositiveAttribute : Attribute;

[AttributeUsage(
    AttributeTargets.Parameter | AttributeTargets.ReturnValue,
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
