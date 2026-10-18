namespace SharpProof.Attributes;

/// <summary>Suppresses SharpProof reporting without adding proof evidence.</summary>
[AttributeUsage(
    SharpProofAttributeTargets.Declaration,
    AllowMultiple = true,
    Inherited = false)]
public sealed class SharpProofSuppressAttribute : Attribute
{
    /// <summary>Creates a documented reporting suppression.</summary>
    /// <param name="reason">The nonempty suppression rationale.</param>
    public SharpProofSuppressAttribute(string reason)
    {
        Reason = SharpProofAttributeValidation.RequireReason(
            reason,
            "A suppression reason is required.");
    }

    /// <summary>Gets the suppression rationale.</summary>
    /// <value>The suppression rationale.</value>
    public string Reason
    {
        get;
    }
}
