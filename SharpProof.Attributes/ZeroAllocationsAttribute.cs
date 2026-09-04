namespace SharpProof.Attributes;

/// <summary>Requires a member to prove that it performs no managed allocation.</summary>
[AttributeUsage(SharpProofAttributeTargets.Contract, Inherited = false)]
public sealed class ZeroAllocationsAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="ZeroAllocationsAttribute"/> class.</summary>
    public ZeroAllocationsAttribute()
    {
    }
}
