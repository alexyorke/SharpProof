namespace SharpProof.Ir;

// A write to an array the body created and never let escape cannot be
// observed by a caller. It stays an element write, so element reads after it
// remain approximations.
internal static class IrWriteSites
{
    internal const string FreshElementPrefix = "FreshElementWrite@";

    internal static bool IsObservable(IrFactory factory, IrWriteInstruction write)
    {
        return write.Region != IrWriteRegion.Local &&
            !(factory.GetOperationInfo(write.Operation).Description is { } id &&
                factory.GetString(id).StartsWith(FreshElementPrefix, StringComparison.Ordinal));
    }
}
