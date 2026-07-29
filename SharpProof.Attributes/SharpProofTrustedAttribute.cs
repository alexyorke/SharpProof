namespace SharpProof.Attributes;

[AttributeUsage(
    AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface |
    AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property,
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
