namespace SharpProof.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ContractForAttribute(Type targetType) : Attribute
{
    public Type TargetType { get; } = targetType ?? throw new ArgumentNullException(nameof(targetType));
}
