namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalProgramLowerer
{
    private sealed class RegionFilter(ControlFlowRegion region, ControlFlowRegion caught,
        IrVarId selector, IrBlockId accepted, IrBlockId rejected)
    {
        internal ControlFlowRegion Region { get; } = (region ?? throw new System.ArgumentNullException(nameof(region)));
        internal ControlFlowRegion Catch { get; } = (caught ?? throw new System.ArgumentNullException(nameof(caught)));
        internal IrVarId Selector { get; } = (selector.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(selector)) : selector);
        internal IrBlockId Accepted { get; } = (accepted.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(accepted)) : accepted);
        internal IrBlockId Rejected { get; } = (rejected.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(rejected)) : rejected);
        internal ImmutableArray<(IrBlockId Accepted, IrBlockId Rejected, OperationId Site)>.Builder Transfers { get; } = ImmutableArray.CreateBuilder<(IrBlockId Accepted, IrBlockId Rejected, OperationId Site)>();
    }

    private readonly ImmutableDictionary<ControlFlowRegion, RegionFilter>.Builder _regionFilters = ImmutableDictionary.CreateBuilder<ControlFlowRegion, RegionFilter>();

    private RegionFilter? EnclosingRegionFilter(ControlFlowRegion source)
    {
        source = RepositoryParameterGuard.NotNull(source, nameof(source));
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
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        RepositoryParameterGuard.ThrowIfInvalid(site.IsDefault, nameof(site));
        filter = RepositoryParameterGuard.NotNull(filter, nameof(filter));
        if (source.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Throw)
        {
            if (source.BranchValue != null && CSharpOperationSemantics.IsNullThrow(source.BranchValue))
            { _builder.Throw(block, site, IrExceptionKind.NullReference, filter.Rejected); }
            else
            { ExplicitThrow(source.BranchValue!, block, _ => filter.Rejected); }
            return;
        }
        var resolver = new RegionFilterEdgeResolver(this, filter.Region, filter.Catch,
            filter.Accepted, filter.Rejected, _blocks.ToImmutable());
        var fall = resolver.Resolve(source.FallThroughSuccessor);
        if (source.ConditionKind != ControlFlowConditionKind.None && source.BranchValue is { } condition)
        {
            var value = Condition(condition, block);
            var conditional = resolver.Resolve(source.ConditionalSuccessor);
            var whenTrue = source.ConditionKind == ControlFlowConditionKind.WhenTrue;
            _builder.Branch(value.Continuation, site, value.Value, whenTrue ? conditional : fall, whenTrue ? fall : conditional);
        }
        else
        { _builder.Goto(block, site, fall); }
    }

    private IrBlockId RegionExceptionSearch(ControlFlowRegion source, RegionExceptionToken token,
        Func<IrBlockId, IrBlockId>? innerUnwind = null)
    {
        RepositoryParameterGuard.RequireInitialized(token.Site.IsDefault, nameof(token), "Value must be initialized.");

        // Search all matching filters before leaving any try. Each selected
        // handler owns the precise prefix of finally regions being left.
        source = RepositoryParameterGuard.NotNull(source, nameof(source));
        var unwind = ImmutableArray.CreateBuilder<ControlFlowRegion>();
        var candidates = ImmutableArray.CreateBuilder<(ControlFlowRegion Catch, RegionFilter? Filter, ImmutableArray<ControlFlowRegion> Unwind, bool Uncertain)>();
        var preserveResult = HasEnclosingRegionFinally(source);
        var complete = false;
        // Declaration ancestors beyond a nested CFG root are not caller frames.
        // Escaping faults continue through _frame.Search after this local search.
        for (var region = source; region != null && region != _regionGraph.Root && !complete; region = region.EnclosingRegion)
        {
            SpendRegion();
            if (region.Kind != ControlFlowRegionKind.Try || region.EnclosingRegion is not { } parent)
            { continue; }
            if (parent.Kind == ControlFlowRegionKind.TryAndFinally)
            {
                unwind.Add(parent.NestedRegions.Single(child => child is null ? throw new System.ArgumentNullException(nameof(child)) : (child.Kind == ControlFlowRegionKind.Finally)));
                continue;
            }
            if (parent.Kind != ControlFlowRegionKind.TryAndCatch)
            { continue; }
            foreach (var handler in parent.NestedRegions)
            {
                SpendRegion();
                var caught = handler.Kind == ControlFlowRegionKind.FilterAndHandler
                    ? handler.NestedRegions.Single(child => child is null ? throw new System.ArgumentNullException(nameof(child)) : (child.Kind == ControlFlowRegionKind.Catch)) : handler;
                var catches = caught.Kind == ControlFlowRegionKind.Catch ? Catches(caught, token) : false;
                if (catches == false)
                { continue; }
                RegionFilter? filter = handler.Kind == ControlFlowRegionKind.FilterAndHandler
                    ? _regionFilters[handler.NestedRegions.Single(child => child is null ? throw new System.ArgumentNullException(nameof(child)) : (child.Kind == ControlFlowRegionKind.Filter))] : null;
                foreach (var _ in unwind)
                { SpendRegion(); }
                candidates.Add((caught, filter, unwind.ToImmutable(), catches == null));
                if (filter == null && catches == true)
                { complete = true; break; }
            }
        }

        // Frame search precedes all pending inner unwinds. Own selected
        // handlers stop the outward search; escaping faults continue at the
        // caller's captured lexical point with this immutable unwind prefix.
        foreach (var _ in unwind)
        { SpendRegion(); }
        var escapingUnwind = unwind.ToImmutable();
        var next = complete ? default : _frame != null
            ? _frame.Search(token.Kind, token.Site, target => target.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(target)) : (Unwind(escapingUnwind, target)))
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

        IrBlockId Unwind(ImmutableArray<ControlFlowRegion> prefix, IrBlockId target)
        {
            RepositoryParameterGuard.ThrowIfInvalid(target.IsDefault, nameof(target));
            if (prefix.IsDefault)
            { throw new ArgumentNullException(nameof(prefix)); }
            target = EnterRegionFinallyChain(prefix, target, token.Site, token, preserveResult);
            return innerUnwind == null ? target : innerUnwind(target);
        }

        IrBlockId Resume(IrBlockId target)
        {
            // A fault inside a filter is false, not a replacement exception.
            // Re-establish original kind/site after the complete search.
            RepositoryParameterGuard.ThrowIfInvalid(target.IsDefault, nameof(target));
            var resumed = RegionBlock("exception:searched");
            _builder.Throw(resumed, token.Site, token.Kind, target);
            return resumed;
        }
    }

    private void FinishRegionFilter(RegionFilter filter, OperationId structural)
    {
        RepositoryParameterGuard.ThrowIfInvalid(structural.IsDefault, nameof(structural));
        filter = RepositoryParameterGuard.NotNull(filter, nameof(filter));
        Dispatch(filter.Accepted, true);
        Dispatch(filter.Rejected, false);

        void Dispatch(IrBlockId block, bool accepted)
        {
            RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
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
