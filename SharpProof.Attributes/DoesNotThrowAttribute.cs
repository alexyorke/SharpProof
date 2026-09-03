namespace SharpProof.Attributes;

/// <summary>Requires a member to prove that no modeled synchronous exception can escape.</summary>
[AttributeUsage(SharpProofAttributeTargets.Contract, Inherited = false)]
public sealed class DoesNotThrowAttribute : Attribute
{
    /// <summary>Creates a no-escaping-modeled-exceptions contract.</summary>
    public DoesNotThrowAttribute()
    {
    }
}
