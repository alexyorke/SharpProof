namespace SharpProof.Frontend;

internal static class RoslynCfgReachability
{
    internal static IEnumerable<BasicBlock> ReachableBlocks(
        ControlFlowGraph graph,
        CancellationToken cancellationToken = default)
    {
        foreach (var block in graph.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (block.IsReachable)
            {
                yield return block;
            }
        }
    }

    internal static IEnumerable<BasicBlock> ExceptionalSuccessors(
        ControlFlowGraph graph,
        BasicBlock block,
        CancellationToken cancellationToken = default)
    {
        var yielded = new HashSet<int>();
        for (var region = block.EnclosingRegion;
             region != null;
             region = region.EnclosingRegion)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (region.Kind != ControlFlowRegionKind.Try ||
                region.EnclosingRegion is not { } owner)
            {
                continue;
            }

            foreach (var handler in owner.NestedRegions.Where(candidate =>
                         candidate.Kind is ControlFlowRegionKind.Filter or
                             ControlFlowRegionKind.Catch or
                             ControlFlowRegionKind.FilterAndHandler or
                             ControlFlowRegionKind.Finally))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (yielded.Add(handler.FirstBlockOrdinal))
                {
                    yield return graph.Blocks[handler.FirstBlockOrdinal];
                }
            }
        }
    }
}
