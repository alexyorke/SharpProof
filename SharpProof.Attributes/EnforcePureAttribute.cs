namespace SharpProof.Attributes;

/// <summary>Requires a member to prove observable purity.</summary>
[AttributeUsage(SharpProofAttributeTargets.Contract, Inherited = false)]
public sealed class EnforcePureAttribute : Attribute
{
}
