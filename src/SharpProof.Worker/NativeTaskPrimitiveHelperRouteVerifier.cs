namespace SharpProof.Worker;

internal enum NativeTaskPrimitiveHelperAlternative
{
    NewArrayDirect, NewArrayValueDynamic, ReadyToRunNewArray,
    OverflowThrow, DivideByZeroThrow, NullReferenceThrow, RangeCheckThrow, JitOrHardwareFault
}

internal sealed record NativeTaskPrimitiveHelperRouteCheck(string? UnknownReason)
{
    // Outstanding candidate routes, not a complete runtime-selection proof.
    // Invalid inventories publish no candidates, counts, or model identities.
    internal ImmutableArray<NativeTaskPrimitiveHelperAlternative> UnprovedAlternatives { get; init; } = [];
}

internal static class NativeTaskPrimitiveHelperRouteVerifier
{
    internal static NativeTaskPrimitiveHelperRouteCheck Check(CompilerForkJoinProjection projection,
        CompilerTotalEntryPreparation root, ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        projection = ArgumentNullGuard.NotNull(projection, nameof(projection));
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        var emitted = NativeTaskEmittedFaultSiteVerifier.Check(projection, root, layout, cancellation);
        if (emitted.Sites is not { } sites)
        { return new(emitted.UnknownReason); }
        var alternatives = new HashSet<NativeTaskPrimitiveHelperAlternative>();
        foreach (var site in sites)
        {
            cancellation.ThrowIfCancellationRequested();
            if ((site.Faults & CompilerTaskPrimitiveFaultKind.NewArray) != 0)
            {
                // Pinned importer.cpp:9378-9445 and jitinterface.cpp:5947-6005:
                // Int32 ordinary-JIT allocation can select DIRECT or dynamic
                // VC. The source-owned ordinary method is not a cctor, but
                // emitted IL does not exclude ReadyToRun execution. A reviewed
                // portable VC entry is not proof of the dynamic slot binding.
                alternatives.Add(NativeTaskPrimitiveHelperAlternative.NewArrayDirect);
                alternatives.Add(NativeTaskPrimitiveHelperAlternative.NewArrayValueDynamic);
                alternatives.Add(NativeTaskPrimitiveHelperAlternative.ReadyToRunNewArray);
            }
            if ((site.Faults & CompilerTaskPrimitiveFaultKind.Overflow) != 0)
            { alternatives.Add(NativeTaskPrimitiveHelperAlternative.OverflowThrow); }
            if ((site.Faults & CompilerTaskPrimitiveFaultKind.DivideByZero) != 0)
            { alternatives.Add(NativeTaskPrimitiveHelperAlternative.DivideByZeroThrow); }
            if ((site.Faults & CompilerTaskPrimitiveFaultKind.NullReference) != 0)
            { alternatives.Add(NativeTaskPrimitiveHelperAlternative.NullReferenceThrow); }
            if ((site.Faults & CompilerTaskPrimitiveFaultKind.RangeCheck) != 0)
            { alternatives.Add(NativeTaskPrimitiveHelperAlternative.RangeCheckThrow); }
            if ((site.Faults & ~CompilerTaskPrimitiveFaultKind.NewArray) != 0)
            {
                // jithelpers.h:119-122 lists explicit throw entries. It does
                // not establish all JIT lowering/hardware-fault alternatives.
                alternatives.Add(NativeTaskPrimitiveHelperAlternative.JitOrHardwareFault);
            }
        }
        var reason = alternatives.Contains(NativeTaskPrimitiveHelperAlternative.NewArrayValueDynamic)
            ? "TaskPrimitiveHelperDynamicArrayRouteUnproved" : alternatives.Count > 0
                ? "TaskPrimitiveHelperOpcodeRouteUnproved" : "TaskRuntimeFaultFrameCompositionUnproved";
        return new(reason) { UnprovedAlternatives = [.. alternatives.OrderBy(static alternative => alternative)] };
    }
}
