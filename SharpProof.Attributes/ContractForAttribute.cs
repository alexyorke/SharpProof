namespace SharpProof.Attributes;

/// <summary>Associates a static contract companion class with its target type.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ContractForAttribute(Type targetType) : Attribute
{
    /// <summary>Creates a contract-companion association.</summary>
    /// <param name="targetType">The interface or class described by the companion.</param>
    public Type TargetType { get; } = targetType ?? throw new ArgumentNullException(nameof(targetType));
}
