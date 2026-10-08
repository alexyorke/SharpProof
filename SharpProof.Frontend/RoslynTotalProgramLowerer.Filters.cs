namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalProgramLowerer
{
    private sealed class RegionFilter(ControlFlowRegion region, ControlFlowRegion caught,
        IrVarId selector, IrBlockId accepted, IrBlockId rejected)
    {
        internal ControlFlowRegion Region { get; } = region;
        internal ControlFlowRegion Catch { get; } = caught;
        internal IrVarId Selector { get; } = selector;
        internal IrBlockId Accepted { get; } = accepted;
        internal IrBlockId Rejected { get; } = rejected;
        internal List<(IrBlockId Accepted, IrBlockId Rejected, OperationId Site)> Transfers { get; } = [];
    }

    private readonly Dictionary<ControlFlowRegion, RegionFilter> _regionFilters = [];

    private RegionFilter? EnclosingRegionFilter(ControlFlowRegion source)
    {
        for (var region = source; region != null; region = region.EnclosingRegion)
        {
            SpendRegion();
            if (_regionFilters.TryGetValue(region, out var filter))
            { return filter; }
        }
        return null;
    }

    private void RegionFilterTerminator(RegionFilter filter, BasicBlock source, IrBlockId block, OperationId site)
    {
        if (source.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Throw)
        {
            if (source.BranchValue != null && CSharpOperationSemantics.IsNullThrow(source.BranchValue))
            { _builder.Throw(block, site, IrExceptionKind.NullReference, filter.Rejected); }
            else
            { ExplicitThrow(source.BranchValue, block, _ => filter.Rejected); }
            return;
        }
        var fall = FilterTarget(source.FallThroughSuccessor);
        if (source.ConditionKind != ControlFlowConditionKind.None && source.BranchValue is { } condition)
        {
            var value = Condition(condition, block);
            var conditional = FilterTarget(source.ConditionalSuccessor);
            var whenTrue = source.ConditionKind == ControlFlowConditionKind.WhenTrue;
            _builder.Branch(value.Continuation, site, value.Value, whenTrue ? conditional : fall, whenTrue ? fall : conditional);
        }
        else
        { _builder.Goto(block, site, fall); }

        IrBlockId FilterTarget(ControlFlowBranch? branch)
        {
            SpendRegion();
            if (branch?.Semantics == ControlFlowBranchSemantics.StructuredExceptionHandling && branch.Destination == null)
            { return filter.Rejected; }
            if (branch?.Semantics != ControlFlowBranchSemantics.Regular || branch.Destination == null || !branch.FinallyRegions.IsEmpty)
            { throw new RegionIncompleteException(); }
            if (Inside(branch.Destination.EnclosingRegion, filter.Catch))
            { return filter.Accepted; }
            if (!Inside(branch.Destination.EnclosingRegion, filter.Region))
            { throw new RegionIncompleteException(); }
            return _blocks[branch.Destination];
        }
    }

    private IrBlockId RegionExceptionSearch(ControlFlowRegion source, RegionExceptionToken token,
        Func<IrBlockId, IrBlockId>? innerUnwind = null)
    {
        // Search all matching filters before leaving any try. Each selected
        // handler owns the precise prefix of finally regions being left.
        var unwind = new List<ControlFlowRegion>();
        var candidates = new List<(ControlFlowRegion Catch, RegionFilter? Filter, ControlFlowRegion[] Unwind, bool Uncertain)>();
        var preserveResult = HasEnclosingRegionFinally(source);
        var complete = false;
        for (var region = source; region != null && !complete; region = region.EnclosingRegion)
        {
            SpendRegion();
            if (region.Kind != ControlFlowRegionKind.Try || region.EnclosingRegion is not { } parent)
            { continue; }
            if (parent.Kind == ControlFlowRegionKind.TryAndFinally)
            {
                unwind.Add(parent.NestedRegions.Single(child => child.Kind == ControlFlowRegionKind.Finally));
                continue;
            }
            if (parent.Kind != ControlFlowRegionKind.TryAndCatch)
            { continue; }
            foreach (var handler in parent.NestedRegions)
            {
                SpendRegion();
                var caught = handler.Kind == ControlFlowRegionKind.FilterAndHandler
                    ? handler.NestedRegions.Single(child => child.Kind == ControlFlowRegionKind.Catch) : handler;
                var catches = caught.Kind == ControlFlowRegionKind.Catch ? Catches(caught, token) : false;
                if (catches == false)
                { continue; }
                RegionFilter? filter = handler.Kind == ControlFlowRegionKind.FilterAndHandler
                    ? _regionFilters[handler.NestedRegions.Single(child => child.Kind == ControlFlowRegionKind.Filter)] : null;
                foreach (var _ in unwind)
                { SpendRegion(); }
                candidates.Add((caught, filter, unwind.ToArray(), catches == null));
                if (filter == null && catches == true)
                { complete = true; break; }
            }
        }

        // Frame search precedes all pending inner unwinds. Own selected
        // handlers stop the outward search; escaping faults continue at the
        // caller's captured lexical point with this immutable unwind prefix.
        foreach (var _ in unwind)
        { SpendRegion(); }
        var escapingUnwind = unwind.ToArray();
        var next = complete ? default : _frame != null
            ? _frame.Search(token.Kind, token.Site, target => Unwind(escapingUnwind, target))
            : Resume(Unwind(escapingUnwind, _regionExceptionalExit));
        for (var ordinal = candidates.Count - 1; ordinal >= 0; ordinal--)
        {
            SpendRegion();
            var candidate = candidates[ordinal];
            var selected = Resume(Unwind(candidate.Unwind, RegionCatchEntry(candidate.Catch, token)));
            var matched = selected;
            if (candidate.Filter is { } filter)
            {
                matched = RegionBlock("filter:entry");
                _builder.Assign(matched, token.Site, filter.Selector, _context.Factory.Integer(RegionInteger, filter.Transfers.Count));
                _builder.Goto(matched, token.Site, _blocks[_regionGraph.Blocks[filter.Region.FirstBlockOrdinal]]);
                filter.Transfers.Add((selected, next, token.Site));
            }
            if (!candidate.Uncertain)
            { next = matched; continue; }
            // Whether the handler's type matches is unknown: an approximation
            // choice, so no refutation depends on it.
            var choice = RegionBlock("catch:uncertain");
            var chosen = _context.Temporary(_context.Factory.BooleanType);
            _builder.Havoc(choice, token.Site, IrHavocKind.Variables, IrHavocOrigin.Approximation, chosen);
            _builder.Branch(choice, token.Site, _context.Factory.Variable(chosen), matched, next);
            next = choice;
        }
        return next;

        IrBlockId Unwind(IReadOnlyList<ControlFlowRegion> prefix, IrBlockId target)
        {
            target = EnterRegionFinallyChain(prefix, target, token.Site, token, preserveResult);
            return innerUnwind == null ? target : innerUnwind(target);
        }

        IrBlockId Resume(IrBlockId target)
        {
            // A fault inside a filter is false, not a replacement exception.
            // Re-establish original kind/site after the complete search.
            var resumed = RegionBlock("exception:searched");
            _builder.Throw(resumed, token.Site, token.Kind, target);
            return resumed;
        }
    }

    private void FinishRegionFilter(RegionFilter filter, OperationId structural)
    {
        Dispatch(filter.Accepted, true);
        Dispatch(filter.Rejected, false);

        void Dispatch(IrBlockId block, bool accepted)
        {
            for (var ordinal = 0; ordinal < filter.Transfers.Count; ordinal++)
            {
                SpendRegion();
                var transfer = filter.Transfers[ordinal];
                var target = accepted ? transfer.Accepted : transfer.Rejected;
                if (ordinal == filter.Transfers.Count - 1)
                { _builder.Goto(block, transfer.Site, target); }
                else
                {
                    var next = RegionBlock("filter:dispatch");
                    var match = _context.Factory.Binary(IrBinaryOperator.Equal, _context.Factory.Variable(filter.Selector),
                        _context.Factory.Integer(RegionInteger, ordinal));
                    _builder.Branch(block, transfer.Site, match, target, next);
                    block = next;
                }
            }
            if (filter.Transfers.Count == 0)
            { Return(block, structural); }
        }
    }
}
