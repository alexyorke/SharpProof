namespace SharpProof.Ir;

// What an opaque call may do. The frontend records it in the call site's
// description; a call without an API specification may do all of it.
[Flags]
internal enum IrOpaqueCallEffects
{
    None = 0,
    Throws = 1,
    Allocates = 2,
    Writes = 4,
    Synchronizes = 8,
    InputOutput = 16,
    NativeCode = 32,
    Reflection = 64,
    Nondeterminism = 128,
    Reads = 256,
    All = 511,
    // Reads ambient state such as a clock or the environment; Reads covers it.
    ReadsAmbient = 512,
    // A callee whose trusted contract declares its capabilities carries them,
    // as SharpProofCapability flags, from bit CapabilityShift up.
    DeclaredCapabilities = 1 << 15
}

internal static class IrOpaqueCallSite
{
    private const string Prefix = "opaque-call:";
    private const int CapabilityShift = 16;
    private const int CapabilityMask = 0x1FFF;

    internal static IrOpaqueCallEffects WithCapabilities(IrOpaqueCallEffects effects, int capabilities)
    {
        return (effects & (IrOpaqueCallEffects.All | IrOpaqueCallEffects.ReadsAmbient)) | IrOpaqueCallEffects.DeclaredCapabilities |
            (IrOpaqueCallEffects)((capabilities & CapabilityMask) << CapabilityShift);
    }

    // Observable purity excludes writes, synchronization, ambient reads, I/O,
    // native code, reflection, nondeterminism and any capability.
    internal static bool IsObservablyImpure(IrOpaqueCallEffects effects)
    {
        const IrOpaqueCallEffects Impure = IrOpaqueCallEffects.Writes | IrOpaqueCallEffects.Synchronizes |
            IrOpaqueCallEffects.InputOutput | IrOpaqueCallEffects.NativeCode | IrOpaqueCallEffects.Reflection |
            IrOpaqueCallEffects.Nondeterminism | IrOpaqueCallEffects.ReadsAmbient;
        return effects == IrOpaqueCallEffects.All || (effects & Impure) != 0 || Capabilities(effects) is not (null or 0);
    }

    // The declared capabilities, or null when they follow from the effects.
    internal static int? Capabilities(IrOpaqueCallEffects effects)
    {
        return (effects & IrOpaqueCallEffects.DeclaredCapabilities) != 0
            ? ((int)effects >> CapabilityShift) & CapabilityMask : null;
    }

    internal static string Describe(IrOpaqueCallEffects effects, string member)
    { return Prefix + ((int)effects).ToString(CultureInfo.InvariantCulture) + ":" + member; }

    // A call site that does not carry this description may do anything.
    internal static IrOpaqueCallEffects Effects(IrFactory factory, OperationId site)
    {
        var description = factory.GetOperationInfo(site).Description is { } id ? factory.GetString(id) : "";
        if (!description.StartsWith(Prefix, StringComparison.Ordinal))
        { return IrOpaqueCallEffects.All; }
        var end = description.IndexOf(':', Prefix.Length);
        return end > Prefix.Length && int.TryParse(description.Substring(Prefix.Length, end - Prefix.Length),
            NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
            (value & ~((int)IrOpaqueCallEffects.All | (int)IrOpaqueCallEffects.ReadsAmbient |
                (int)IrOpaqueCallEffects.DeclaredCapabilities | CapabilityMask << CapabilityShift)) == 0
            ? (IrOpaqueCallEffects)value : IrOpaqueCallEffects.All;
    }
}
