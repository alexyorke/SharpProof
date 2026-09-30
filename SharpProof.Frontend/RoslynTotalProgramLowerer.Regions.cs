namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalProgramLowerer
{
    private const int MaximumRegionSteps = 4096;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1032", Justification = "Private bounded lowering control flow.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1064", Justification = "Private bounded lowering control flow never escapes the lowering session.")]
    private sealed class RegionIncompleteException : Exception;
    private readonly struct RegionExceptionToken(IrExceptionKind kind, OperationId site, int ordinal)
    {
        internal IrExceptionKind Kind { get; } = kind;
        internal OperationId Site { get; } = site;
        internal int Ordinal { get; } = ordinal;
    }
    private sealed class RegionFinally(ControlFlowRegion region, IrVarId selector, IrBlockId dispatch)
    {
        internal ControlFlowRegion Region { get; } = region;
        internal IrVarId Selector { get; } = selector;
        internal IrBlockId Dispatch { get; } = dispatch;
        internal List<(IrBlockId Entry, IrBlockId Target, OperationId Site, RegionExceptionToken? Exception)> Transfers { get; } = [];
        internal Dictionary<(IrBlockId Target, OperationId Site, IrExceptionKind? Kind, bool Return), IrBlockId> Entries { get; } = [];
    }
    private readonly Dictionary<ControlFlowRegion, ImmutableArray<IrExceptionKind>> _regionCatchKinds = [];
    private readonly Dictionary<ControlFlowRegion, IrVarId> _regionCaught = [];
    private readonly Dictionary<(ControlFlowRegion Catch, int Token), IrBlockId> _regionCatchEntries = [];
    private readonly Dictionary<(IrExceptionKind Kind, OperationId Site), RegionExceptionToken> _regionTokens = [];
    private readonly List<(IrBlockId Block, ControlFlowRegion Source, ControlFlowRegion Catch, OperationId Site)> _regionRethrows = [];
    private readonly List<ILocalSymbol> _regionLocals = [];
    private ControlFlowGraph _regionGraph = null!;
    private BasicBlock _regionSource = null!;
    private RegionFinally? _regionFinally;
    private IrBlockId _regionExceptionalExit;
    private int _regionRemaining = MaximumRegionSteps;

    private FrontendProgramLoweringResult LowerRegions(ControlFlowGraph graph, IrBlockId entry, OperationId structural)
    {
        _regionGraph = graph;
        try
        {
            if (graph.Blocks.Length > MaximumRegionSteps || _context.Parameters.Length > MaximumRegionSteps / 2)
            { throw new RegionIncompleteException(); }
            var regions = new Stack<ControlFlowRegion>();
            regions.Push(graph.Root);
            while (regions.Count != 0)
            {
                SpendRegion();
                var region = regions.Pop();
                for (var ordinal = 0; ordinal < region.Locals.Length; ordinal++)
                {
                    SpendRegion();
                    if (!region.Locals[ordinal].IsConst)
                    { _regionLocals.Add(region.Locals[ordinal]); }
                }
                if (region.Kind is not (ControlFlowRegionKind.Root or ControlFlowRegionKind.LocalLifetime or
                        ControlFlowRegionKind.Try or ControlFlowRegionKind.TryAndCatch or ControlFlowRegionKind.Catch or
                        ControlFlowRegionKind.TryAndFinally or ControlFlowRegionKind.Finally) ||
                    region.Locals.Any(local => local.IsImplicitlyDeclared || string.IsNullOrEmpty(local.Name) ||
                        local.RefKind != RefKind.None || !CSharpOperationSemantics.IsScalar(local.Type)))
                { throw new RegionIncompleteException(); }
                if (region.Kind == ControlFlowRegionKind.Catch)
                {
                    if (graph.OriginalOperation.SemanticModel?.Compilation is not { } compilation ||
                        !CSharpOperationSemantics.TryCatchKinds(compilation, region.ExceptionType, out var kinds))
                    { throw new RegionIncompleteException(); }
                    _regionCatchKinds.Add(region, kinds);
                    _regionCaught.Add(region, _context.Temporary(RegionInteger));
                }
                if (region.Kind == ControlFlowRegionKind.Finally)
                {
                    if (_regionFinally != null)
                    { throw new RegionIncompleteException(); }
                    _regionFinally = new(region, _context.Temporary(RegionInteger), RegionBlock("finally:dispatch"));
                }
                foreach (var child in region.NestedRegions.Reverse())
                { SpendRegion(); regions.Push(child); }
            }
            foreach (var binding in _context.Parameters)
            {
                SpendRegion();
                _builder.Assign(entry, structural, binding.Current, _context.Factory.Variable(binding.Entry));
                _builder.Assign(entry, structural, binding.PreState, _context.Factory.Variable(binding.Entry));
            }
            foreach (var block in graph.Blocks)
            { _blocks.Add(block, RegionBlock("cfg:" + block.Ordinal.ToString(CultureInfo.InvariantCulture))); }
            _regionExceptionalExit = RegionBlock("exceptional:exit");
            _builder.ExceptionalExit(_regionExceptionalExit, structural);
            _expressions = new(_context, _builder, _regionExceptionalExit)
            {
                Spend = SpendRegion,
                ExceptionTarget = (kind, site) => RegionExceptionTarget(_regionSource.EnclosingRegion, Token(kind, site))
            };
            _builder.Goto(entry, structural, _blocks[graph.Blocks[0]]);
            var localInitialization = _regionLocals.Count == 0 ? default : RegionLocalInitialization();
            foreach (var source in graph.Blocks)
            {
                SpendRegion();
                _regionSource = source;
                var block = _blocks[source];
                for (var ordinal = 0; ordinal < source.Operations.Length; ordinal++)
                {
                    SpendRegion();
                    if (localInitialization == (source, ordinal))
                    { InitializeRegionLocals(block, structural); }
                    block = Statement(source.Operations[ordinal], block);
                }
                if (localInitialization == (source, source.Operations.Length))
                { InitializeRegionLocals(block, structural); }
                RegionTerminator(source, block, structural);
            }
            ResolveRegionRethrows();
            FinishRegionFinally(structural);
            var program = _builder.Build();
            var instructionCount = 0;
            foreach (var block in program.Blocks)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                instructionCount += block.Instructions.Length;
                if (instructionCount > MaximumRegionSteps)
                { throw new RegionIncompleteException(); }
            }
            _ = IrBlockOrder.TryCreateAcyclicOrder(program, _ =>
            {
                SpendRegion();
                return true;
            }, out var failure);
            if (failure != IrAcyclicOrderFailure.None)
            { throw new RegionIncompleteException(); }
            return Result(program);
        }
        catch (RegionIncompleteException)
        {
            return IncompleteRegion(structural);
        }
    }

    private (BasicBlock? Block, int Operation) RegionLocalInitialization()
    {
        // Follow the same elided prologue edges as RegionTerminator. Initialize
        // only after all point filters and before the first runtime body action.
        var source = _regionGraph.Blocks[0];
        var visited = new HashSet<BasicBlock>();
        while (visited.Add(source))
        {
            SpendRegion();
            for (var ordinal = 0; ordinal < source.Operations.Length; ordinal++)
            {
                SpendRegion();
                var operation = source.Operations[ordinal];
                var expression = operation is IExpressionStatementOperation statement ? statement.Operation : operation;
                if (!_context.IsSpecificationOperation(expression) && operation is not IEmptyOperation)
                { return (source, ordinal); }
            }
            var branch = source.FallThroughSuccessor;
            if (source.BranchValue is { } value && !_context.IsSpecificationOperation(value) ||
                branch?.Semantics != ControlFlowBranchSemantics.Regular || !branch.FinallyRegions.IsEmpty ||
                branch.Destination == null)
            { return (source, source.Operations.Length); }
            source = branch.Destination;
        }
        throw new RegionIncompleteException();
    }

    private void InitializeRegionLocals(IrBlockId block, OperationId structural)
    {
        // Valid C# definite assignment makes these initial storage values
        // unobservable. They retain normal-only assignments across a shared
        // finally's exceptional predecessor without changing any source write.
        foreach (var local in _regionLocals)
        {
            SpendRegion();
            var variable = _context.Variable(local);
            var type = _context.Factory.GetVariableInfo(variable).Type;
            IrTerm initial = type == _context.Factory.BooleanType ? _context.Factory.Boolean(false) : _context.Factory.Integer(type, 0);
            _builder.Assign(block, structural, variable, initial);
        }
    }

    private FrontendProgramLoweringResult IncompleteRegion(OperationId structural)
    {
        // Partial construction can leave unfinished dispatch blocks.
        var closed = new IrProgramBuilder(_context.Factory);
        var start = closed.CreateBlock("incomplete:candidate");
        closed.SetEntry(start);
        closed.Return(start, structural);
        _abstentions.Add(new(structural, FrontendAbstention.UnsupportedControlFlow));
        return new(closed.Build(), FrontendSubsetClassification.Abstain(FrontendAbstention.UnsupportedControlFlow),
            _context.Variables, _context.Captures, [.. _abstentions], _context.Origin);
    }

    private void RegionTerminator(BasicBlock source, IrBlockId block, OperationId structural)
    {
        var site = source.BranchValue == null ? structural : _context.Site(source.BranchValue);
        var branch = source.FallThroughSuccessor;
        if (source.BranchValue is { } specification && _context.IsSpecificationOperation(specification) &&
            branch?.Destination != null)
        { _builder.Goto(block, structural, RegionNormalTarget(branch, structural)); }
        else if (branch?.Semantics == ControlFlowBranchSemantics.Return)
        {
            if (source.BranchValue is { } expression && _context.Result is { } result)
            {
                var value = Value(expression, block);
                _builder.Assign(value.Continuation, site, result, value.Value);
                block = value.Continuation;
            }
            if (branch.FinallyRegions.IsEmpty)
            { _builder.Return(block, site, _context.Result is { } returned ? _context.Factory.Variable(returned) : null); }
            else
            {
                var returned = RegionBlock("return:captured");
                _builder.Return(returned, site, _context.Result is { } resultValue ? _context.Factory.Variable(resultValue) : null);
                _builder.Goto(block, site, EnterRegionFinally(returned, site, null, preserveResult: true));
            }
        }
        else if (branch?.Semantics == ControlFlowBranchSemantics.Throw)
        {
            if (!CSharpOperationSemantics.IsNullThrow(source.BranchValue))
            { throw new RegionIncompleteException(); }
            _builder.Throw(block, site, IrExceptionKind.NullReference,
                RegionExceptionTarget(source.EnclosingRegion, Token(IrExceptionKind.NullReference, site)));
        }
        else if (branch?.Semantics == ControlFlowBranchSemantics.Rethrow)
        {
            var caught = source.EnclosingRegion;
            while (caught != null && caught.Kind != ControlFlowRegionKind.Catch)
            {
                SpendRegion();
                caught = caught.EnclosingRegion;
            }
            if (caught == null)
            { throw new RegionIncompleteException(); }
            _regionRethrows.Add((block, source.EnclosingRegion, caught, site));
        }
        else if (branch?.Semantics == ControlFlowBranchSemantics.StructuredExceptionHandling)
        {
            if (_regionFinally == null || !Inside(source.EnclosingRegion, _regionFinally.Region))
            { throw new RegionIncompleteException(); }
            _builder.Goto(block, site, _regionFinally.Dispatch);
        }
        else if (source.Kind == BasicBlockKind.Exit)
        { _builder.Return(block, site); }
        else if (source.ConditionKind != ControlFlowConditionKind.None && source.BranchValue is { } condition &&
                 branch?.Destination != null && source.ConditionalSuccessor?.Destination != null)
        {
            var value = Value(condition, block);
            var fall = RegionNormalTarget(branch, site);
            var conditional = RegionNormalTarget(source.ConditionalSuccessor, site);
            var whenTrue = source.ConditionKind == ControlFlowConditionKind.WhenTrue;
            _builder.Branch(value.Continuation, site, value.Value, whenTrue ? conditional : fall, whenTrue ? fall : conditional);
        }
        else if (branch?.Destination != null)
        { _builder.Goto(block, site, RegionNormalTarget(branch, site)); }
        else
        { throw new RegionIncompleteException(); }
    }

    private IrBlockId RegionNormalTarget(ControlFlowBranch branch, OperationId site)
    {
        SpendRegion();
        if (branch.Semantics != ControlFlowBranchSemantics.Regular || branch.Destination == null ||
            branch.FinallyRegions.Length > 1)
        { throw new RegionIncompleteException(); }
        var target = _blocks[branch.Destination];
        if (branch.FinallyRegions.IsEmpty)
        { return target; }
        if (_regionFinally?.Region != branch.FinallyRegions[0])
        { throw new RegionIncompleteException(); }
        return EnterRegionFinally(target, site, null);
    }

    private IrBlockId RegionExceptionTarget(ControlFlowRegion source, RegionExceptionToken token)
    {
        var throughFinally = false;
        var target = _regionExceptionalExit;
        for (var region = source; region != null; region = region.EnclosingRegion)
        {
            SpendRegion();
            if (region.Kind == ControlFlowRegionKind.Try && region.EnclosingRegion is { } parent)
            {
                if (parent.Kind == ControlFlowRegionKind.TryAndCatch)
                {
                    var handler = parent.NestedRegions.FirstOrDefault(candidate =>
                        candidate.Kind == ControlFlowRegionKind.Catch && _regionCatchKinds[candidate].Contains(token.Kind));
                    if (handler != null)
                    {
                        target = RegionCatchEntry(handler, token);
                        break;
                    }
                }
                else if (parent.Kind == ControlFlowRegionKind.TryAndFinally)
                { throughFinally = true; }
            }
        }
        return throughFinally ? EnterRegionFinally(target, token.Site, token) : target;
    }

    private IrBlockId RegionCatchEntry(ControlFlowRegion caught, RegionExceptionToken token)
    {
        SpendRegion();
        var key = (caught, token.Ordinal);
        if (_regionCatchEntries.TryGetValue(key, out var entry))
        { return entry; }
        entry = RegionBlock("catch:entry");
        _builder.Assign(entry, token.Site, _regionCaught[caught], _context.Factory.Integer(RegionInteger, token.Ordinal));
        _builder.Goto(entry, token.Site, _blocks[_regionGraph.Blocks[caught.FirstBlockOrdinal]]);
        _regionCatchEntries.Add(key, entry);
        return entry;
    }

    private IrBlockId EnterRegionFinally(IrBlockId target, OperationId site, RegionExceptionToken? exception, bool preserveResult = false)
    {
        SpendRegion();
        var state = _regionFinally ?? throw new RegionIncompleteException();
        var key = (target, site, exception?.Kind, preserveResult);
        if (state.Entries.TryGetValue(key, out var entry))
        { return entry; }
        entry = RegionBlock("finally:entry");
        if (!preserveResult && _context.Result is { } result)
        {
            // All incoming edges define the hidden return storage for SSA.
            // Only a return continuation observes it, and that edge captured
            // the actual value before entering finally.
            var type = _context.Factory.GetVariableInfo(result).Type;
            IrTerm filler = type == _context.Factory.BooleanType ? _context.Factory.Boolean(false) : _context.Factory.Integer(type, 0);
            _builder.Assign(entry, site, result, filler);
        }
        _builder.Assign(entry, site, state.Selector, _context.Factory.Integer(RegionInteger, state.Transfers.Count));
        _builder.Goto(entry, site, _blocks[_regionGraph.Blocks[state.Region.FirstBlockOrdinal]]);
        state.Entries.Add(key, entry);
        state.Transfers.Add((entry, target, site, exception));
        return entry;
    }

    private void ResolveRegionRethrows()
    {
        var tokens = _regionTokens.Values.OrderBy(token => token.Ordinal).ToArray();
        foreach (var token in tokens)
        {
            SpendRegion();
        }
        foreach (var request in _regionRethrows)
        {
            SpendRegion();
            var block = request.Block;
            var selected = new List<RegionExceptionToken>();
            foreach (var token in tokens)
            {
                SpendRegion();
                if (_regionCatchKinds[request.Catch].Contains(token.Kind))
                {
                    selected.Add(token);
                }
            }
            for (var index = 0; index < selected.Count; index++)
            {
                SpendRegion();
                var token = selected[index];
                var thrown = RegionBlock("rethrow:original");
                _builder.Throw(thrown, token.Site, token.Kind, RegionExceptionTarget(request.Source, token));
                if (index == selected.Count - 1)
                { _builder.Goto(block, request.Site, thrown); }
                else
                {
                    var next = RegionBlock("rethrow:dispatch");
                    var match = _context.Factory.Binary(IrBinaryOperator.Equal, _context.Factory.Variable(_regionCaught[request.Catch]),
                        _context.Factory.Integer(RegionInteger, token.Ordinal));
                    _builder.Branch(block, request.Site, match, thrown, next);
                    block = next;
                }
            }
            if (selected.Count == 0)
            { _builder.Return(block, request.Site); }
        }
    }

    private void FinishRegionFinally(OperationId structural)
    {
        if (_regionFinally is not { } state)
        { return; }
        var block = state.Dispatch;
        for (var index = 0; index < state.Transfers.Count; index++)
        {
            SpendRegion();
            var transfer = state.Transfers[index];
            var continued = RegionBlock("finally:continue");
            if (transfer.Exception is { } exception)
            {
                // A mixed normal/exceptional join has no unconditional pending
                // exception. Resume the captured original kind AND operation.
                _builder.Throw(continued, exception.Site, exception.Kind, transfer.Target);
            }
            else
            { _builder.Goto(continued, transfer.Site, transfer.Target); }
            if (index == state.Transfers.Count - 1)
            { _builder.Goto(block, structural, continued); }
            else
            {
                var next = RegionBlock("finally:dispatch");
                var match = _context.Factory.Binary(IrBinaryOperator.Equal, _context.Factory.Variable(state.Selector),
                    _context.Factory.Integer(RegionInteger, index));
                _builder.Branch(block, structural, match, continued, next);
                block = next;
            }
        }
        if (state.Transfers.Count == 0)
        { _builder.Return(block, structural); }
    }

    private RegionExceptionToken Token(IrExceptionKind kind, OperationId site)
    {
        SpendRegion();
        var key = (kind, site);
        if (!_regionTokens.TryGetValue(key, out var token))
        {
            token = new(kind, site, _regionTokens.Count);
            _regionTokens.Add(key, token);
        }
        return token;
    }
    private IrTypeId RegionInteger => _context.Factory.GetOrCreateIntegerType(32, true);
    private IrBlockId RegionBlock(string name)
    {
        SpendRegion();
        return _builder.CreateBlock(name);
    }
    private void SpendRegion()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (--_regionRemaining < 0)
        { throw new RegionIncompleteException(); }
    }
    private bool Inside(ControlFlowRegion candidate, ControlFlowRegion expected)
    {
        for (var region = candidate; region != null; region = region.EnclosingRegion)
        {
            SpendRegion();
            if (region == expected)
            { return true; }
        }
        return false;
    }
}
