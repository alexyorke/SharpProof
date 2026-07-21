namespace SharpProof.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property, Inherited = false)]
public sealed class AllowedCapabilitiesAttribute : Attribute {
    public AllowedCapabilitiesAttribute(SharpProofCapability capabilities) {
        Capabilities = capabilities;
    }

    public SharpProofCapability Capabilities { get; }
}
