namespace SharpProof.Attributes;

[AttributeUsage(AttributeTargets.All, Inherited = false)]
public sealed class AllowedCapabilitiesAttribute : Attribute {
    public AllowedCapabilitiesAttribute(SharpProofCapability capabilities) {
        Capabilities = capabilities;
    }

    public SharpProofCapability Capabilities { get; }
}