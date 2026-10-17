namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalProgramLowerer
{
    internal readonly struct RegionFilterEdgeResolver(RoslynTotalProgramLowerer lowerer,
        ControlFlowRegion region, ControlFlowRegion caught, IrBlockId accepted, IrBlockId rejected,
        IReadOnlyDictionary<BasicBlock, IrBlockId> blocks)
    {
        private readonly RoslynTotalProgramLowerer _lowerer = lowerer ?? throw new System.ArgumentNullException(nameof(lowerer));
        private readonly ControlFlowRegion _region = region ?? throw new System.ArgumentNullException(nameof(region));
        private readonly ControlFlowRegion _caught = caught ?? throw new System.ArgumentNullException(nameof(caught));
        private readonly IrBlockId _accepted = accepted.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(accepted)) : accepted;
        private readonly IrBlockId _rejected = rejected.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(rejected)) : rejected;
        private readonly IReadOnlyDictionary<BasicBlock, IrBlockId> _blocks = blocks ?? throw new System.ArgumentNullException(nameof(blocks));
        internal IrBlockId Resolve(ControlFlowBranch? branch)
        {
            _lowerer.SpendRegion();
            if (branch?.Semantics == ControlFlowBranchSemantics.StructuredExceptionHandling && branch.Destination == null)
            { return _rejected; }
            if (branch?.Semantics == ControlFlowBranchSemantics.Regular && branch.Destination != null && branch.FinallyRegions.IsEmpty)
            {
                if (_lowerer.Inside(branch.Destination.EnclosingRegion, _caught))
                { return _accepted; }
                if (_lowerer.Inside(branch.Destination.EnclosingRegion, _region))
                { return _blocks[branch.Destination]; }
            }
            throw new RegionIncompleteException();
        }
    }
}
