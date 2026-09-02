namespace SharpProof.Attributes;

[AttributeUsage(
    SharpProofAttributeTargets.Declaration,
    Inherited = false)]
public sealed class SharpProofTrustedAttribute : Attribute
{
    public SharpProofTrustedAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A trust reason is required.", nameof(reason));
        }

        Reason = reason;
    }

    public string Reason
    {
        get;
    }
}
