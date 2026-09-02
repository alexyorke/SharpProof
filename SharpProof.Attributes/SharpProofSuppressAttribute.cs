namespace SharpProof.Attributes;

[AttributeUsage(
    SharpProofAttributeTargets.Declaration,
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
