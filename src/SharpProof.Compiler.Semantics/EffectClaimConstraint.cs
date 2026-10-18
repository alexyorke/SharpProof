namespace SharpProof.Compiler.Semantics;

internal sealed partial record EffectClaimConstraint
{
    internal static EffectClaimConstraint Empty
    {
        get;
    } =
        new(EffectContractKind.None, EffectContractCapabilityKind.None, []);
}
