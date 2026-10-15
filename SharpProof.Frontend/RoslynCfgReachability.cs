namespace SharpProof.Frontend;

internal static class RoslynCfgReachability
{
    internal static bool TryGetPlainRegularDestination(ControlFlowBranch? branch, out BasicBlock destination)
    {
        destination = null!;
        if (branch?.Semantics != ControlFlowBranchSemantics.Regular ||
            !branch.FinallyRegions.IsEmpty || branch.Destination is not { } target)
        { return false; }
        destination = target;
        return true;
    }

    internal static IEnumerable<BasicBlock> ReachableBlocks(
        ControlFlowGraph graph,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullGuard.NotNull(graph, nameof(graph));
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
        ArgumentNullGuard.NotNull(graph, nameof(graph));
        ArgumentNullGuard.NotNull(block, nameof(block));
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
                    yield return graph.Blocks[ArgumentNullGuard.RequireIndex(handler.FirstBlockOrdinal, graph.Blocks.Length, nameof(graph))];
                }
            }
        }
    }
}
