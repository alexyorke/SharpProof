namespace SharpProof.Attributes;
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property,
    AllowMultiple = true,
    Inherited = false)]
public sealed class EffectContractAttribute(SharpProofEffect effects) : Attribute {
    public SharpProofEffect Effects { get; } = effects;
    public SharpProofCapability Capabilities { get; set; }
    public Type[] ThrownExceptions { get; set; } = [];
    public bool IsDeterministic { get; set; } = true;
    public bool Complete { get; set; } = true;
}
