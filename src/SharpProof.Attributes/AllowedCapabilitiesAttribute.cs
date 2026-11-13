namespace SharpProof.Attributes;

/// <summary>Declares the ambient capabilities that a member may use.</summary>
/// <remarks>
/// Combine known flags with bitwise OR; <c>[AllowedCapabilities(SharpProofCapability.None)]</c> selects a claim that no tracked
/// ambient capability is used. The declaration does not grant operating-system permissions or establish other effect properties.
/// Unsupported selected behavior remains incomplete rather than being assumed to satisfy the allowance.
/// </remarks>
[AttributeUsage(SharpProofAttributeTargets.Contract, Inherited = false)]
public sealed class AllowedCapabilitiesAttribute : Attribute
{
    /// <summary>Creates a capability allowance.</summary>
    /// <param name="capabilities">The allowed capability flags.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capabilities"/> contains an unknown flag bit.</exception>
    public AllowedCapabilitiesAttribute(SharpProofCapability capabilities)
    {
        Capabilities = SharpProofAttributeValidation.RequireKnown(capabilities, nameof(capabilities));
    }

    /// <summary>Gets the allowed capability flags.</summary>
    /// <value>The allowed capabilities.</value>
    public SharpProofCapability Capabilities { get; }
}
