namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalProgramLowerer
{
    private readonly Dictionary<BasicBlock, BasicBlock> _compilerElidedPlans = [];
    private readonly HashSet<BasicBlock> _compilerRestoredBlocks = [];
    private readonly HashSet<BasicBlock> _regionSpecificationConditionals = [];
    private readonly Queue<BasicBlock> _compilerRestorePending = [];
    private Dictionary<(SyntaxTree Tree, int Start, int Length), (BasicBlock? Block, int Operation)>? _compilerOwnerBlocks;

    private void PlanCompilerElidedTerminator(BasicBlock source)
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
        _ = ElidedEmissionSuccessor(source,
            allowUnreachableCompilerElision: _compilerRestoredBlocks.Contains(source));
        if (!TryElidedSpecificationContinuation(source, out var continuation,
            compilerContinuations: _compilerElidedContinuations))
        { throw new RegionIncompleteException(); }
        _compilerElidedPlans.Add(source, continuation);
    }

    private bool TryRecoverCompilerElidedOwner(BasicBlock source, HashSet<BasicBlock> omitted,
        out BasicBlock continuation)
    {
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
        while (region.Kind == ControlFlowRegionKind.LocalLifetime)
        {
            SpendRegion();
            region = region.EnclosingRegion!;
        }
        return region;
    }

    private void RestoreCompilerBlock(BasicBlock block)
    {
        if (!block.IsReachable && _compilerRestoredBlocks.Add(block))
        { _compilerRestorePending.Enqueue(block); }
    }

    private void RestoreCompilerElidedReachability(HashSet<ControlFlowRegion> filterRegions)
    {
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
}
