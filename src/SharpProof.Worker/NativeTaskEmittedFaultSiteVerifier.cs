namespace SharpProof.Worker;

internal sealed record NativeTaskEmittedFaultSite(int MethodToken, int IlOffset, int OpCode, CompilerTaskPrimitiveFaultKind Faults);

internal sealed record NativeTaskEmittedFaultSiteCheck(ImmutableArray<NativeTaskEmittedFaultSite>? Sites, string? UnknownReason)
{
    internal bool HasSites => Sites is not null;
    internal string? RequiredCompilerModel => Sites is null ? null : NativeTaskEmittedFaultSiteVerifier.Model;
}

// Complete compiler-owned emitted sites only. A static site inventory is not
// an execution count, a runtime helper selection, or a root allocation proof.
internal static class NativeTaskEmittedFaultSiteVerifier
{
    internal const string Model = "owned-emitted-primitive-fault-sites-v1";

    internal static NativeTaskEmittedFaultSiteCheck Check(CompilerForkJoinProjection projection,
        CompilerTotalEntryPreparation root, ResourceRuntimeLayoutContext layout, CancellationToken cancellation)
    {
        projection = ArgumentNullGuard.NotNull(projection, nameof(projection));
        root = ArgumentNullGuard.NotNull(root, nameof(root));
        layout = ArgumentNullGuard.NotNull(layout, nameof(layout));
        cancellation.ThrowIfCancellationRequested();
        if (projection.Carriers?.Shapes is not { } shapes || shapes.EmittedFaultBodies is not { } capture)
        { return new(null, "TaskEmittedFaultBodyEvidenceUnavailable"); }
        if (capture.Inventory is not { } inventory)
        {
            return new(null, !string.IsNullOrWhiteSpace(capture.UnknownReason) && capture.UnknownReason.Length <= 512
                ? capture.UnknownReason : "TaskEmittedFaultBodyEvidenceInvalid");
        }
        if (capture.UnknownReason is not null || projection.RootCallableId != root.CallableId || shapes.RootCallableId != root.CallableId)
        { return new(null, "TaskEmittedFaultBodyEvidenceInvalid"); }
        if (CompilerTaskEmittedFaultBodyOwnership.ValidateAndDecode(inventory, shapes, projection.FrameworkAssemblyIdentity,
                cancellation, out var decodedBodies) is { } failure)
        { return new(null, failure); }
        var source = NativeTaskCarrierStorageVerifier.CheckSourceBodies(shapes, projection, root, layout, cancellation);
        if (source.DirectSourceActivationsUpper is null)
        { return new(null, source.UnknownReason); }
        var sites = ImmutableArray.CreateBuilder<NativeTaskEmittedFaultSite>();
        foreach (var body in decodedBodies)
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (var instruction in body.Instructions)
            {
                cancellation.ThrowIfCancellationRequested();
                if (instruction.Faults != CompilerTaskPrimitiveFaultKind.None)
                { sites.Add(new(body.Body.MethodToken, instruction.Offset, instruction.OpCode, instruction.Faults)); }
            }
        }
        return new(sites.ToImmutable(), null);
    }
}
