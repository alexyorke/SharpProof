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

    /// <summary>Gets the allowed capability flags.</summary>
    /// <value>The allowed capabilities.</value>
    public SharpProofCapability Capabilities { get; }
}
