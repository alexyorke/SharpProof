namespace SharpProof.Attributes;

/// <summary>Declares the ambient capabilities that a member may use.</summary>
[AttributeUsage(SharpProofAttributeTargets.Contract, Inherited = false)]
public sealed class AllowedCapabilitiesAttribute(SharpProofCapability capabilities) : Attribute
{
    /// <summary>Creates a capability allowance.</summary>
    /// <param name="capabilities">The allowed capability flags.</param>
    public SharpProofCapability Capabilities { get; } = capabilities;
}
