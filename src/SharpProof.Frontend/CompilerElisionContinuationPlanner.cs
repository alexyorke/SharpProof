namespace SharpProof.Frontend;

// Owns continuation planning and validation over authentic compiler CFG objects.
// The IR lowerer shares its actual work meter and consumes accepted plans.
internal sealed class CompilerElisionContinuationPlanner(ControlFlowGraph graph,
    TotalLoweringContext context, CSharpInvocationEmissionPolicy? emission, TotalRegionWork work,
    Compilation? emissionCompilation)
{
    private readonly TotalRegionWork _work = work ?? throw new System.ArgumentNullException(nameof(work));
    private readonly ControlFlowGraph _regionGraph = graph ?? throw new System.ArgumentNullException(nameof(graph));
    private readonly TotalLoweringContext _context = context ?? throw new System.ArgumentNullException(nameof(context));
    private readonly CSharpInvocationEmissionPolicy? _emission = emission;
    private readonly Compilation? _emissionCompilation = emissionCompilation;
    private readonly Dictionary<BasicBlock, BasicBlock> _compilerElidedContinuations = [];
    private readonly Dictionary<BasicBlock, BasicBlock> _compilerElidedPlans = [];
    private readonly HashSet<BasicBlock> _compilerRestoredBlocks = [];
    private readonly HashSet<BasicBlock> _regionSpecificationConditionals = [];
    private readonly Queue<BasicBlock> _compilerRestorePending = [];
    private Dictionary<(SyntaxTree Tree, int Start, int Length), (BasicBlock? Block, int Operation)>? _compilerOwnerBlocks;

    internal TotalLoweringContext.ExactSpecificationContinuation RegionSpecificationContinuation(BasicBlock source)
    {
        if (_context.TryGetExactSpecificationContinuation(_regionGraph, _emissionCompilation, source, out var cached))
        {
            // Exact scans spend once per pending pop, including duplicate pops.
            // Replay those checks so reuse preserves cancellation and exhaustion.
            for (var ordinal = 0; ordinal < cached.TraversalWork; ordinal++)
            { SpendRegion(); }
            return cached;
        }
        if (!TryElidedSpecificationContinuation(source, out var continuation, out var work, requireSameRegion: true) ||
            !ReferenceEquals(source.EnclosingRegion, continuation.EnclosingRegion))
        { throw new RoslynTotalProgramLowerer.RegionIncompleteException(); }
        var validated = new TotalLoweringContext.ExactSpecificationContinuation(continuation, work);
        _context.CacheExactSpecificationContinuation(_regionGraph, _emissionCompilation, source, validated);
        return validated;
    }

    internal void PlanCompilerElidedTerminator(BasicBlock source)
    {
        if (_compilerElidedPlans.ContainsKey(source) || source.BranchValue is not { } value)
        { return; }
        if (_context.IsSpecificationOperation(value) && source.ConditionKind != ControlFlowConditionKind.None)
        { _regionSpecificationConditionals.Add(source); return; }
        if (!IsElidedSpecificationPart(value) || source.FallThroughSuccessor?.Destination == null)
        { return; }
        if (_compilerElidedContinuations.TryGetValue(source, out var cached))
        {
            _compilerElidedPlans.Add(source, cached);
            return;
        }
        _compilerElidedPlans.Add(source, RequireCompilerContinuation(source));
    }

    // Validate a proposed compiler continuation and own its rejection action.
    internal BasicBlock RequireCompilerContinuation(BasicBlock source)
    {
        _ = ElidedEmissionSuccessor(source,
            allowUnreachableCompilerElision: _compilerRestoredBlocks.Contains(source));
        if (!TryElidedSpecificationContinuation(source, out var continuation, compilerMode: true))
        { throw new RoslynTotalProgramLowerer.RegionIncompleteException(); }
        return continuation;
    }

    internal bool TryRecoverCompilerElidedOwner(BasicBlock source, HashSet<BasicBlock> omitted,
        out BasicBlock continuation)
    {
        omitted = RepositoryParameterGuard.NotNull(omitted, nameof(omitted));
        SpendRegion();
        continuation = null!;
        var value = source.BranchValue!;
        var owner = _emission!.OwningInvocation(value)!;
        foreach (var block in omitted)
        {
            SpendRegion();
            foreach (var operation in block.Operations)
            {
                SpendRegion();
                if (!BelongsToCompilerElidedOwner(operation, owner))
                { return false; }
            }
            if (block.BranchValue is { } branch)
            {
                SpendRegion();
                if (!BelongsToCompilerElidedOwner(branch, owner))
                { return false; }
            }
        }
        IndexCompilerElidedOwners();
        var syntax = owner.Syntax;
        if (!_compilerOwnerBlocks!.TryGetValue((syntax.SyntaxTree, syntax.SpanStart, syntax.Span.Length), out var candidate) ||
            candidate.Block == null ||
            !ReferenceEquals(CompilerElisionBoundary(source.EnclosingRegion), CompilerElisionBoundary(candidate.Block.EnclosingRegion)))
        { return false; }
        for (var ordinal = 0; ordinal <= candidate.Operation; ordinal++)
        {
            SpendRegion();
            if (!BelongsToCompilerElidedOwner(candidate.Block.Operations[ordinal], owner))
            { return false; }
        }
        continuation = candidate.Block;
        RestoreCompilerBlock(continuation);
        return true;
    }

    private bool BelongsToCompilerElidedOwner(IOperation operation, IInvocationOperation owner)
    {
        owner = RepositoryParameterGuard.NotNull(owner, nameof(owner));
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        var expression = operation is IExpressionStatementOperation statement ? statement.Operation : operation;
        return !_context.IsSpecificationOperation(expression) && _emission?.IsElided(expression) == true &&
            _emission.OwningInvocation(expression) is { } actual &&
            ReferenceEquals(actual.Syntax.SyntaxTree, owner.Syntax.SyntaxTree) && actual.Syntax.Span == owner.Syntax.Span;
    }

    private void IndexCompilerElidedOwners()
    {
        if (_compilerOwnerBlocks != null)
        { return; }
        _compilerOwnerBlocks = [];
        foreach (var block in _regionGraph.Blocks)
        {
            SpendRegion();
            for (var ordinal = 0; ordinal < block.Operations.Length; ordinal++)
            {
                SpendRegion();
                var operation = block.Operations[ordinal];
                var expression = operation is IExpressionStatementOperation statement ? statement.Operation : operation;
                if (expression is not IInvocationOperation invocation)
                { continue; }
                var syntax = invocation.Syntax;
                var key = (syntax.SyntaxTree, syntax.SpanStart, syntax.Span.Length);
                _compilerOwnerBlocks[key] = _compilerOwnerBlocks.ContainsKey(key) ? (null, 0) : (block, ordinal);
            }
        }
    }

    private ControlFlowRegion CompilerElisionBoundary(ControlFlowRegion region)
    {
        region = RepositoryParameterGuard.NotNull(region, nameof(region));
        while (region.Kind == ControlFlowRegionKind.LocalLifetime)
        {
            SpendRegion();
            region = region.EnclosingRegion!;
        }
        return region;
    }

    private void RestoreCompilerBlock(BasicBlock block)
    {
        block = RepositoryParameterGuard.NotNull(block, nameof(block));
        if (!block.IsReachable && _compilerRestoredBlocks.Add(block))
        { _compilerRestorePending.Enqueue(block); }
    }

    internal void RestoreCompilerElidedReachability(HashSet<ControlFlowRegion> filterRegions)
    {
        filterRegions = RepositoryParameterGuard.NotNull(filterRegions, nameof(filterRegions));
        while (_compilerRestorePending.Count != 0)
        {
            SpendRegion();
            var source = _compilerRestorePending.Dequeue();
            if (!filterRegions.Contains(source.EnclosingRegion))
            { PlanCompilerElidedTerminator(source); }
            if (_compilerElidedPlans.TryGetValue(source, out var plan))
            { RestoreCompilerBlock(plan); }
            else if (source.ConditionKind != ControlFlowConditionKind.None &&
                source.BranchValue is { ConstantValue: { HasValue: true, Value: bool constant } })
            {
                RestoreCompilerEdge(constant == (source.ConditionKind == ControlFlowConditionKind.WhenTrue)
                    ? source.ConditionalSuccessor : source.FallThroughSuccessor);
            }
            else
            {
                RestoreCompilerEdge(source.FallThroughSuccessor);
                RestoreCompilerEdge(source.ConditionalSuccessor);
            }
            for (var region = source.EnclosingRegion; region != null; region = region.EnclosingRegion)
            {
                SpendRegion();
                if (region.Kind != ControlFlowRegionKind.Try || region.EnclosingRegion is not { } owner)
                { continue; }
                foreach (var handler in owner.NestedRegions)
                {
                    SpendRegion();
                    if (handler.Kind is ControlFlowRegionKind.Filter or ControlFlowRegionKind.Catch or
                        ControlFlowRegionKind.FilterAndHandler or ControlFlowRegionKind.Finally)
                    { RestoreCompilerBlock(_regionGraph.Blocks[handler.FirstBlockOrdinal]); }
                }
            }
        }
    }

    private void RestoreCompilerEdge(ControlFlowBranch? edge)
    {
        if (edge == null)
        { return; }
        SpendRegion();
        foreach (var region in edge.FinallyRegions)
        {
            SpendRegion();
            RestoreCompilerBlock(_regionGraph.Blocks[region.FirstBlockOrdinal]);
        }
        if (edge.Semantics == ControlFlowBranchSemantics.Regular && edge.Destination is { } destination)
        { RestoreCompilerBlock(destination); }
    }

    internal static ControlFlowBranch ElidedEmissionSuccessor(BasicBlock source,
        bool allowUnreachableCompilerElision = false)
    {
        var fall = source.FallThroughSuccessor!;
        if (source.ConditionKind == ControlFlowConditionKind.None || fall.Destination!.IsReachable ||
            allowUnreachableCompilerElision && !source.IsReachable)
        {
            if (fall.Semantics != ControlFlowBranchSemantics.Regular || !fall.FinallyRegions.IsEmpty)
            { throw new RoslynTotalProgramLowerer.RegionIncompleteException(); }
            return fall;
        }
        var alternative = source.ConditionalSuccessor;
        if (alternative?.Destination is not { IsReachable: true } ||
            alternative.Semantics != ControlFlowBranchSemantics.Regular || !alternative.FinallyRegions.IsEmpty)
        { throw new RoslynTotalProgramLowerer.RegionIncompleteException(); }
        return alternative;
    }
    internal bool IsElidedSpecificationPart(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return _context.IsSpecificationOperation(operation) || _emission?.IsElided(operation) == true;
    }

    private bool IsCompilerElidedThrow(BasicBlock? block)
    {
        return block is { ConditionKind: ControlFlowConditionKind.None, ConditionalSuccessor: null, BranchValue: { } thrown, FallThroughSuccessor.Semantics: ControlFlowBranchSemantics.Throw } &&
            _emission?.IsElided(thrown) == true;
    }

    internal bool TryElidedSpecificationContinuation(BasicBlock source, out BasicBlock continuation,
        bool requireSameRegion = false, bool compilerMode = false)
    {
        source = RepositoryParameterGuard.NotNull(source, nameof(source));
        return TryElidedSpecificationContinuation(source, out continuation, out _, requireSameRegion, compilerMode);
    }

    internal bool TryElidedSpecificationContinuation(BasicBlock source, out BasicBlock continuation, out int traversalWork,
        bool requireSameRegion = false, bool compilerMode = false)
    {
        var compilerContinuations = compilerMode ? _compilerElidedContinuations : null;
        traversalWork = 0;
        // Short-circuit operands of a compiler-elided contract can have two
        // CFG successors. Both must reach the same owning call (or first
        // runtime block) without executing any non-elided operation.
        continuation = null!;
        var pending = new Stack<BasicBlock>();
        var visited = new HashSet<BasicBlock>();
        var stops = new HashSet<BasicBlock>();
        if (source.FallThroughSuccessor?.Destination == null || source.ConditionalSuccessor?.Destination == null ||
            !EnqueueElidedEdge(source.FallThroughSuccessor, pending) || !EnqueueElidedEdge(source.ConditionalSuccessor, pending))
        { return false; }
        while (pending.Count != 0)
        {
            SpendRegion();
            traversalWork++;
            var current = pending.Pop();
            if (!visited.Add(current))
            { continue; }
            if (compilerContinuations != null && compilerContinuations.TryGetValue(current, out var cached))
            {
                stops.Add(cached);
                if (stops.Count > 1 && !TryMergeCompilerElidedStops(stops))
                { return false; }
                continue;
            }
            if (requireSameRegion && !ReferenceEquals(source.EnclosingRegion, current.EnclosingRegion))
            { return false; }
            var hasElidedPart = false;
            var stop = false;
            foreach (var operation in current.Operations)
            {
                var expression = operation is IExpressionStatementOperation statement ? statement.Operation : operation;
                if (_context.IsExcludedSpecificationCall(expression) ||
                    expression is IInvocationOperation && _emission?.IsElided(expression, requireOwningInvocation: compilerContinuations != null) == true ||
                    !IsElidedSpecificationPart(expression))
                {
                    stop = true;
                    break;
                }
                hasElidedPart = true;
            }
            if (!stop && current.BranchValue is { } value)
            {
                if (!IsElidedSpecificationPart(value))
                { stop = true; }
                else
                { hasElidedPart = true; }
            }
            if (stop || !hasElidedPart)
            {
                stops.Add(current);
                if (stops.Count > 1 && (compilerContinuations == null || !TryMergeCompilerElidedStops(stops)))
                { return false; }
                continue;
            }
            // Compiler-omitted throw arms have no runtime successor. Their
            // other omitted arms must still identify one continuation.
            if (compilerContinuations != null && IsCompilerElidedThrow(current) &&
                current.FallThroughSuccessor is { Destination: null } thrown && thrown.FinallyRegions.IsEmpty)
            { continue; }
            if (!EnqueueElidedEdge(current.FallThroughSuccessor, pending) || !EnqueueElidedEdge(current.ConditionalSuccessor, pending))
            { return false; }
        }
        if (stops.Count == 0 && compilerContinuations != null && _regionGraph != null &&
            TryRecoverCompilerElidedOwner(source, visited, out var owner))
        { stops.Add(owner); }
        if (stops.Count != 1)
        { return false; }
        continuation = stops.Single();
        if (compilerContinuations != null)
        {
            compilerContinuations[source] = continuation;
            foreach (var current in visited)
            {
                // A terminal arm alone cannot establish a continuation. Do
                // not let a later scan mistake it for a converging path.
                if (!stops.Contains(current) && current.FallThroughSuccessor?.Semantics != ControlFlowBranchSemantics.Throw)
                { compilerContinuations[current] = continuation; }
            }
        }
        return true;
    }

    private bool TryMergeCompilerElidedStops(HashSet<BasicBlock> stops)
    {
        // Only repair a previously rejected pair: an entirely omitted call
        // block can fall directly into the other stop without executing it.
        var redundant = stops.FirstOrDefault(source =>
        {
            SpendRegion();
            return source.ConditionKind == ControlFlowConditionKind.None && source.BranchValue == null &&
                source.ConditionalSuccessor == null &&
                source.FallThroughSuccessor is { Semantics: ControlFlowBranchSemantics.Regular, Destination: { } destination } edge &&
                edge.FinallyRegions.IsEmpty && !ReferenceEquals(source, destination) && stops.Contains(destination) &&
                source.Operations.All(operation =>
                {
                    operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
                    SpendRegion();
                    var expression = operation is IExpressionStatementOperation statement ? statement.Operation : operation;
                    return !_context.IsSpecificationOperation(expression) && _emission?.IsElided(expression) == true;
                }) &&
                source.Operations.Any(operation =>
                {
                    operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
                    SpendRegion();
                    var expression = operation is IExpressionStatementOperation statement ? statement.Operation : operation;
                    return expression is IInvocationOperation && _emission?.IsElided(expression, requireOwningInvocation: true) == true;
                });
        });
        return redundant != null && stops.Remove(redundant);
    }

    private static bool EnqueueElidedEdge(ControlFlowBranch? edge, Stack<BasicBlock> pending)
    {
        pending = RepositoryParameterGuard.NotNull(pending, nameof(pending));
        if (edge == null)
        { return true; }
        if (edge.Semantics != ControlFlowBranchSemantics.Regular ||
            !edge.FinallyRegions.IsEmpty || edge.Destination == null)
        { return false; }
        pending.Push(edge.Destination);
        return true;
    }

    internal bool IsRestored(BasicBlock block)
    {
        block = RepositoryParameterGuard.NotNull(block, nameof(block));
        return _compilerRestoredBlocks.Contains(block);
    }
    internal bool IsSpecificationConditional(BasicBlock block)
    {
        block = RepositoryParameterGuard.NotNull(block, nameof(block));
        return _regionSpecificationConditionals.Contains(block);
    }
    internal bool TryGetPlan(BasicBlock block, out BasicBlock target)
    {
        block = RepositoryParameterGuard.NotNull(block, nameof(block));
        return _compilerElidedPlans.TryGetValue(block, out target!);
    }
    private void SpendRegion()
    { _work.Spend(); }
}
