namespace SharpProof.Attributes;

[AttributeUsage(
    SharpProofAttributeTargets.Declaration,
    Inherited = false)]
public sealed class SharpProofTrustedAttribute : Attribute
{
    public SharpProofTrustedAttribute(string reason)
    {
        Reason = SharpProofAttributeValidation.RequireReason(
            reason,
            "A trust reason is required.");
    }

    public string Reason
    {
        get;
    }
}
