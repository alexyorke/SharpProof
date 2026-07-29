namespace SharpProof.Attributes;

[AttributeUsage(
    AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface |
    AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property,
    AllowMultiple = true,
    Inherited = false)]
public sealed class SharpProofSuppressAttribute : Attribute
{
    public SharpProofSuppressAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A suppression reason is required.", nameof(reason));
        }

        Reason = reason;
    }

    public string Reason
    {
        get;
    }
}
