namespace SharpProof.Ir;

// A write to an array the body created and never let escape, or to the
// object a constructor is initializing before it escapes, cannot be observed
// by a caller. It keeps its region, so reads after it remain approximations.
internal static class IrWriteSites
{
    internal const string FreshPrefix = "FreshWrite@";
    // A loop cut's store of an unknown value into a location the loop stores
    // to: it forgets that location only and is no write of the program.
    internal const string LoopCellPrefix = "LoopCell@";

    internal static bool IsLoopCell(IrFactory factory, IrWriteInstruction write)
    {
        return factory.GetOperationInfo(write.Operation).Description is { } id &&
            factory.GetString(id).StartsWith(LoopCellPrefix, StringComparison.Ordinal);
    }

    internal static bool IsObservable(IrFactory factory, IrWriteInstruction write)
    {
        return write.Region != IrWriteRegion.Local &&
            !(factory.GetOperationInfo(write.Operation).Description is { } id &&
                factory.GetString(id).StartsWith(FreshPrefix, StringComparison.Ordinal));
    }
}
