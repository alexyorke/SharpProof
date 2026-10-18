namespace SharpProof.Attributes;

/// <summary>Requires a member to prove that it performs no managed allocation.</summary>
/// <remarks>
/// The obligation includes modeled allocations in the selected body and admitted calls. It does not claim zero native
/// allocation, zero memory use, purity, or termination. Unknown call effects remain incomplete instead of counting as no allocation.
/// </remarks>
[AttributeUsage(SharpProofAttributeTargets.Contract, Inherited = false)]
public sealed class ZeroAllocationsAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="ZeroAllocationsAttribute"/> class.</summary>
    public ZeroAllocationsAttribute()
    {
    }
}
