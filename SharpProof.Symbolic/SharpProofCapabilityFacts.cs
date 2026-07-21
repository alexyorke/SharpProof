using SharpProof.Attributes;

namespace SharpProof.Symbolic;

internal static class SharpProofCapabilityFacts {
    internal const SharpProofCapability AllKnown =
        SharpProofCapability.IO |
        SharpProofCapability.FileRead |
        SharpProofCapability.FileWrite |
        SharpProofCapability.Network |
        SharpProofCapability.Console |
        SharpProofCapability.Process |
        SharpProofCapability.Environment |
        SharpProofCapability.Registry |
        SharpProofCapability.Clock |
        SharpProofCapability.Randomness |
        SharpProofCapability.Reflection |
        SharpProofCapability.Synchronization |
        SharpProofCapability.NativeInterop;

    internal static readonly ImmutableArray<SharpProofCapability> Ordered = ImmutableArray.Create(
        SharpProofCapability.IO,
        SharpProofCapability.FileRead,
        SharpProofCapability.FileWrite,
        SharpProofCapability.Network,
        SharpProofCapability.Console,
        SharpProofCapability.Process,
        SharpProofCapability.Environment,
        SharpProofCapability.Registry,
        SharpProofCapability.Clock,
        SharpProofCapability.Randomness,
        SharpProofCapability.Reflection,
        SharpProofCapability.Synchronization,
        SharpProofCapability.NativeInterop);

    internal const int NoneMask = (int)SharpProofCapability.None;

    internal const int AllKnownMask = (int)AllKnown;

    internal static readonly ImmutableArray<int> OrderedMasks = Ordered
        .Select(static capability => (int)capability)
        .ToImmutableArray();

    internal static SharpProofCapability Normalize(SharpProofCapability capabilities) {
        if ((capabilities & (SharpProofCapability.FileRead |
                             SharpProofCapability.FileWrite |
                             SharpProofCapability.Network |
                             SharpProofCapability.Console |
                             SharpProofCapability.Registry)) != 0)
            capabilities |= SharpProofCapability.IO;

        return capabilities;
    }

    internal static int NormalizeMask(int capabilities) =>
        (int)Normalize((SharpProofCapability)capabilities);

    internal static string GetName(int capability) =>
        ((SharpProofCapability)capability).ToString();
}
