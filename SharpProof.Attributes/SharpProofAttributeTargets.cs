namespace SharpProof.Attributes;

internal static class SharpProofAttributeTargets
{
    internal const AttributeTargets Contract =
        AttributeTargets.Constructor |
        AttributeTargets.Method |
        AttributeTargets.Property;

    internal const AttributeTargets Declaration =
        AttributeTargets.Assembly |
        AttributeTargets.Class |
        AttributeTargets.Struct |
        AttributeTargets.Interface |
        Contract;
}
