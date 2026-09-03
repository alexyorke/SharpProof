namespace SharpProof.Attributes;

/// <summary>Associates a static contract companion class with its target type.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ContractForAttribute : Attribute
{
    /// <summary>Creates a contract-companion association.</summary>
    /// <param name="targetType">The interface or class described by the companion.</param>
    public ContractForAttribute(Type targetType)
    {
        TargetType = targetType ?? throw new ArgumentNullException(nameof(targetType));
    }

    public Type TargetType { get; }
}
