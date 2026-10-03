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
    All = 255
}

internal static class IrOpaqueCallSite
{
    private const string Prefix = "opaque-call:";

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
            NumberStyles.None, CultureInfo.InvariantCulture, out var value) && (value & ~(int)IrOpaqueCallEffects.All) == 0
            ? (IrOpaqueCallEffects)value : IrOpaqueCallEffects.All;
    }
}
