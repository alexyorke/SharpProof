using System.Collections.Immutable;

namespace SharpProof.Symbolic;

internal static class SymbolicCapabilityFacts
{
    internal const SymbolicCapability AllKnown =
        SymbolicCapability.IO |
        SymbolicCapability.FileRead |
        SymbolicCapability.FileWrite |
        SymbolicCapability.Network |
        SymbolicCapability.Console |
        SymbolicCapability.Process |
        SymbolicCapability.Environment |
        SymbolicCapability.Registry |
        SymbolicCapability.Clock |
        SymbolicCapability.Randomness |
        SymbolicCapability.Reflection |
        SymbolicCapability.Synchronization |
        SymbolicCapability.NativeInterop;

    internal static readonly ImmutableArray<SymbolicCapability> Ordered = ImmutableArray.Create(
        SymbolicCapability.IO,
        SymbolicCapability.FileRead,
        SymbolicCapability.FileWrite,
        SymbolicCapability.Network,
        SymbolicCapability.Console,
        SymbolicCapability.Process,
        SymbolicCapability.Environment,
        SymbolicCapability.Registry,
        SymbolicCapability.Clock,
        SymbolicCapability.Randomness,
        SymbolicCapability.Reflection,
        SymbolicCapability.Synchronization,
        SymbolicCapability.NativeInterop);

    internal const int NoneMask = (int)SymbolicCapability.None;

    internal const int AllKnownMask = (int)AllKnown;

    internal static readonly ImmutableArray<int> OrderedMasks = Ordered
        .Select(static capability => (int)capability)
        .ToImmutableArray();

    internal static SymbolicCapability Normalize(SymbolicCapability capabilities)
    {
        if ((capabilities & (SymbolicCapability.FileRead |
                             SymbolicCapability.FileWrite |
                             SymbolicCapability.Network |
                             SymbolicCapability.Console |
                             SymbolicCapability.Registry)) != 0)
            capabilities |= SymbolicCapability.IO;

        return capabilities;
    }

    internal static SymbolicCapability ExpandAllowed(SymbolicCapability capabilities)
    {
        if ((capabilities & SymbolicCapability.IO) != 0)
            capabilities |= SymbolicCapability.FileRead |
                            SymbolicCapability.FileWrite |
                            SymbolicCapability.Network |
                            SymbolicCapability.Console |
                            SymbolicCapability.Registry;

        return Normalize(capabilities);
    }

    internal static string Format(SymbolicCapability capabilities)
    {
        capabilities = Normalize(capabilities);
        if (capabilities == SymbolicCapability.None) return "None";

        var values = Ordered
            .Where(value => capabilities.HasFlag(value))
            .Select(static value => value.ToString())
            .ToArray();
        return values.Length == 0 ? "None" : string.Join(", ", values);
    }

    internal static int GetMask(SymbolicCapabilityResult result)
    {
        return (int)result.Capabilities;
    }

    internal static int GetMask(SymbolicCapabilitySite site)
    {
        return (int)site.Capabilities;
    }

    internal static int NormalizeMask(int capabilities)
    {
        return (int)Normalize((SymbolicCapability)capabilities);
    }

    internal static int ExpandAllowedMask(int capabilities)
    {
        return (int)ExpandAllowed((SymbolicCapability)capabilities);
    }

    internal static string FormatMask(int capabilities)
    {
        return Format((SymbolicCapability)capabilities);
    }

    internal static string GetName(int capability)
    {
        return ((SymbolicCapability)capability).ToString();
    }
}
