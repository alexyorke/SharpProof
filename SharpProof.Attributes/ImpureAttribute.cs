namespace SharpProof.Attributes;

[AttributeUsage(
    AttributeTargets.Assembly | AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property,
    Inherited = false)]
public sealed class ImpureAttribute : Attribute
{
}