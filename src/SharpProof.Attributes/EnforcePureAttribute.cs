namespace SharpProof.Attributes;

/// <summary>Requires a member to prove observable purity.</summary>
/// <remarks>
/// This selects SharpProof's modeled observable-effect obligation. It does not independently claim zero allocations,
/// no exceptions, termination, or a particular return value; express those requirements with their separate contracts.
/// The attribute describes static behavior and does not isolate or sandbox the running method.
/// </remarks>
[AttributeUsage(SharpProofAttributeTargets.Contract, Inherited = false)]
public sealed class EnforcePureAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="EnforcePureAttribute"/> class.</summary>
    public EnforcePureAttribute()
    {
    }
}
