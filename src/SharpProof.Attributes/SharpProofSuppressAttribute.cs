namespace SharpProof.Attributes;

/// <summary>Suppresses SharpProof reporting without adding proof evidence.</summary>
/// <remarks>
/// Use a nonempty reason that identifies the reporting decision and its review. Suppression does not convert Unknown or
/// incomplete coverage into Proven, discharge a caller obligation, or disable the verifier's policy checks.
/// Prefer fixing an invalid declaration or call before suppressing its diagnostic.
/// </remarks>
[AttributeUsage(
    SharpProofAttributeTargets.Declaration,
    AllowMultiple = true,
    Inherited = false)]
public sealed class SharpProofSuppressAttribute : Attribute
{
    /// <summary>Creates a documented reporting suppression.</summary>
    /// <param name="reason">The nonempty suppression rationale.</param>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is null, empty or whitespace.</exception>
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
