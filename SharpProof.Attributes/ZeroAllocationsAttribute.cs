namespace SharpProof.Attributes;

/// <summary>Requires a member to prove that it performs no managed allocation.</summary>
[AttributeUsage(SharpProofAttributeTargets.Contract, Inherited = false)]
public sealed class ZeroAllocationsAttribute : Attribute
{
    /// <summary>Creates a zero-allocation contract.</summary>
    public ZeroAllocationsAttribute()
    {
    }
}
