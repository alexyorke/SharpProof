namespace SharpProof.Attributes;

/// <summary>Marks an explicitly reviewed trust boundary that still requires a complete contract.</summary>
[AttributeUsage(
    SharpProofAttributeTargets.Declaration,
    Inherited = false)]
public sealed class SharpProofTrustedAttribute : Attribute
{
    /// <summary>Creates a documented trust declaration.</summary>
    /// <param name="reason">The nonempty trust rationale.</param>
    public SharpProofTrustedAttribute(string reason)
    {
        Reason = SharpProofAttributeValidation.RequireReason(
            reason,
            "A trust reason is required.");
    }

    /// <summary>Gets the trust rationale.</summary>
    /// <value>The trust rationale.</value>
    public string Reason
    {
        get;
    }
}
