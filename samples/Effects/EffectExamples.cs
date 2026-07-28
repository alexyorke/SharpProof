using SharpProof.Attributes;

namespace SharpProof.Samples.Effects;

public static class EffectExamples {
    [EnforcePure]
    [ZeroAllocations]
    [DoesNotThrow]
    [AllowedCapabilities(SharpProofCapability.None)]
    public static int Identity(int value) => value;
}
