namespace SharpProof.CompilerArtifact;

internal sealed partial record ManifestCallableTarget
{
    // Manifest sealing canonicalizes the wire list. Lowering still needs the
    // source clause order so each assumption ID remains attached to its clause.
    internal ImmutableArray<WorkerAssumptionEvidence> SourceOrderedAssumptions
    {
        get;
        init;
    } = [];
}
