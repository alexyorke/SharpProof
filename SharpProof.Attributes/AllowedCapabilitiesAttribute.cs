namespace SharpProof.Attributes;
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, Inherited = false)]
public sealed class AllowedCapabilitiesAttribute(SharpProofCapability capabilities) : Attribute {
    public SharpProofCapability Capabilities { get; } = capabilities;
}
