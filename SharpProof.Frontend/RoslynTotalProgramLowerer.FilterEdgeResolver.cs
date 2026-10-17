namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalProgramLowerer
{
    internal readonly struct RegionFilterEdgeResolver(RoslynTotalProgramLowerer lowerer,
        ControlFlowRegion region, ControlFlowRegion caught, IrBlockId accepted, IrBlockId rejected,
        IReadOnlyDictionary<BasicBlock, IrBlockId> blocks)
    {
        internal IrBlockId Resolve(ControlFlowBranch? branch)
        {
            lowerer.SpendRegion();
            if (branch?.Semantics == ControlFlowBranchSemantics.StructuredExceptionHandling && branch.Destination == null)
            { return rejected; }
            if (branch?.Semantics == ControlFlowBranchSemantics.Regular && branch.Destination != null && branch.FinallyRegions.IsEmpty)
            {
                if (lowerer.Inside(branch.Destination.EnclosingRegion, caught))
                { return accepted; }
                if (lowerer.Inside(branch.Destination.EnclosingRegion, region))
                { return blocks[branch.Destination]; }
            }
            throw new RegionIncompleteException();
        }
    }
}
