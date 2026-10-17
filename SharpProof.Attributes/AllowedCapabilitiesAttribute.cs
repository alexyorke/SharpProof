namespace SharpProof.Attributes;

/// <summary>Declares the ambient capabilities that a member may use.</summary>
[AttributeUsage(SharpProofAttributeTargets.Contract, Inherited = false)]
public sealed class AllowedCapabilitiesAttribute : Attribute
{
    /// <summary>Creates a capability allowance.</summary>
    /// <param name="capabilities">The allowed capability flags.</param>
    public AllowedCapabilitiesAttribute(SharpProofCapability capabilities)
    {
        const SharpProofCapability known = SharpProofCapability.IO |
            SharpProofCapability.FileRead | SharpProofCapability.FileWrite |
            SharpProofCapability.Network | SharpProofCapability.Console |
            SharpProofCapability.Process | SharpProofCapability.Environment |
            SharpProofCapability.Registry | SharpProofCapability.Clock |
            SharpProofCapability.Randomness | SharpProofCapability.Reflection |
            SharpProofCapability.Synchronization | SharpProofCapability.NativeInterop;
        if ((capabilities & ~known) != SharpProofCapability.None)
        {
            throw new ArgumentOutOfRangeException(nameof(capabilities));
        }

        Capabilities = capabilities;
    }

    /// <summary>Gets the allowed capability flags.</summary>
    /// <value>The allowed capabilities.</value>
    public SharpProofCapability Capabilities { get; }
}
