namespace SharpProof.Attributes;

/// <summary>Declares the ambient capabilities that a member may use.</summary>
[AttributeUsage(SharpProofAttributeTargets.Contract, Inherited = false)]
public sealed class AllowedCapabilitiesAttribute : Attribute
{
    /// <summary>Creates a capability allowance.</summary>
    /// <param name="capabilities">The allowed capability flags.</param>
    public AllowedCapabilitiesAttribute(SharpProofCapability capabilities)
    {
        Capabilities = capabilities;
    }

    public SharpProofCapability Capabilities { get; }
}
