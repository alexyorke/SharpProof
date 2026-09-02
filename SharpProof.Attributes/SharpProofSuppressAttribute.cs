namespace SharpProof.Attributes;

[AttributeUsage(
    SharpProofAttributeTargets.Declaration,
    AllowMultiple = true,
    Inherited = false)]
public sealed class SharpProofSuppressAttribute : Attribute
{
    public SharpProofSuppressAttribute(string reason)
    {
        Reason = SharpProofAttributeValidation.RequireReason(
            reason,
            "A suppression reason is required.");
    }

    public string Reason
    {
        get;
    }
}
