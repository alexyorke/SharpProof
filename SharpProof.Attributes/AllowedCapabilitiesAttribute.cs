namespace SharpProof.Attributes;
[AttributeUsage(SharpProofAttributeTargets.Contract, Inherited = false)]
public sealed class AllowedCapabilitiesAttribute(SharpProofCapability capabilities) : Attribute
{
    public SharpProofCapability Capabilities { get; } = capabilities;
}
