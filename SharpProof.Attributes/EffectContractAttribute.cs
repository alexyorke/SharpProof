namespace SharpProof.Attributes;

[AttributeUsage(
    AttributeTargets.Assembly | AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property,
    AllowMultiple = true,
    Inherited = false)]
public sealed class EffectContractAttribute : Attribute {
    public EffectContractAttribute(SharpProofEffect effects) => Effects = effects;
    public EffectContractAttribute(string targetMethodKey, SharpProofEffect effects) {
        TargetMethodKey = targetMethodKey ?? throw new ArgumentNullException(nameof(targetMethodKey));
        Effects = effects;
    }
    public string? TargetMethodKey { get; }

    public SharpProofEffect Effects { get; }

    public SharpProofCapability Capabilities { get; set; }

    public Type[] ThrownExceptions { get; set; } = [];

    public bool IsDeterministic { get; set; } = true;

    public bool Complete { get; set; } = true;
}
