namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalProgramLowerer
{
    internal const int MaximumRegionSteps = 4096;
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
    private readonly Dictionary<OperationId, INamedTypeSymbol> _explicitThrowTypes = [];
    private readonly List<(IrBlockId Block, ControlFlowRegion Source, ControlFlowRegion Catch, OperationId Site)> _regionRethrows = [];
    private readonly List<ILocalSymbol> _regionLocals = [];
    private ControlFlowGraph _regionGraph = null!;
    private BasicBlock _regionSource = null!;
    private readonly Dictionary<ControlFlowRegion, RegionFinally> _regionFinallys = [];
    private IrBlockId _regionExceptionalExit;
    private int _regionRemaining = MaximumRegionSteps;

    private void LowerRegions(ControlFlowGraph graph, IrBlockId entry, OperationId structural)
    {
        _regionGraph = graph;
        if (graph.Blocks.Length > MaximumRegionSteps || _context.Parameters.Length > MaximumRegionSteps / 2)
        { _constructionLimitExceeded = true; throw new RegionIncompleteException(); }
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
                    ControlFlowRegionKind.TryAndFinally or ControlFlowRegionKind.Finally or
                    ControlFlowRegionKind.FilterAndHandler or ControlFlowRegionKind.Filter) ||
                region.Locals.Any(local => (local.IsImplicitlyDeclared || string.IsNullOrEmpty(local.Name)) && !IsSynchronizationLocal(graph, local) ||
                    local.RefKind != RefKind.None || !CSharpOperationSemantics.IsValueDomain(local.Type)))
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
                _regionFinallys.Add(region, new(region, _context.Temporary(RegionInteger), RegionBlock("finally:dispatch")));
            }
            if (region.Kind == ControlFlowRegionKind.Filter)
            {
                var parent = region.EnclosingRegion ?? throw new RegionIncompleteException();
                var caught = parent.NestedRegions.Single(child => child.Kind == ControlFlowRegionKind.Catch);
                _regionFilters.Add(region, new(region, caught, _context.Temporary(RegionInteger),
                    RegionBlock("filter:accepted"), RegionBlock("filter:rejected")));
            }
            foreach (var child in region.NestedRegions.Reverse())
            { SpendRegion(); regions.Push(child); }
        }
        foreach (var binding in _context.Inputs)
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
            SourceCall = InlineSourceCall,
            SourceGetter = InlineSourceGetter,
            SourceConstruction = InlineSourceConstruction,
            SourceSetter = InlineSourceSetter,
            AllowOpaqueCalls = _preserveSourceCall == null && _calls?.OpaqueCalls == true,
            OpaqueEffects = _calls?.OpaqueEffects,
            ApproximateElementReads = _calls?.ApproximateElementReads == true,
            PinElementReads = _calls?.PinElementReads == true,
            ExceptionTarget = (kind, site) => EnclosingRegionFilter(_regionSource.EnclosingRegion) is { } filter
                ? filter.Rejected : RegionExceptionTarget(_regionSource.EnclosingRegion, Token(kind, site))
        };
        _builder.Goto(entry, structural, _blocks[graph.Blocks[0]]);
        var localInitialization = _regionLocals.Count == 0 ? default : RegionLocalInitialization();
        foreach (var source in graph.Blocks)
        {
            SpendRegion();
            _regionSource = source;
            var block = _blocks[source];
            // A block Roslyn finds unreachable (after a throw expression) has
            // an invalid placeholder for the thrown value; it never runs.
            if (!source.IsReachable)
            { Return(block, structural); continue; }
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
        foreach (var filter in _regionFilters.Values)
        { FinishRegionFilter(filter, structural); }
        foreach (var state in _regionFinallys.Values)
        { FinishRegionFinally(state, structural); }
    }

    private void ValidateRegionOrder(IrProgram program)
    {
        _ = IrBlockOrder.TryCreateAcyclicOrder(program, _ =>
        {
            SpendRegion();
            return true;
        }, out var failure);
        if (failure is not (IrAcyclicOrderFailure.None or IrAcyclicOrderFailure.CyclicControlFlow))
        { throw new RegionIncompleteException(); }
    }

    private static bool IsSynchronizationLocal(ControlFlowGraph graph, ILocalSymbol local)
    {
        // Admit only the compiler's lockTaken flag used by implicit Monitor.Enter.
        return local.IsImplicitlyDeclared && local.Type.SpecialType == SpecialType.System_Boolean &&
            graph.Blocks.Any(block => block.Operations.Any(operation =>
                (operation is IExpressionStatementOperation statement ? statement.Operation : operation) is IInvocationOperation invocation &&
                invocation.IsImplicit && CSharpOperationSemantics.IsMonitorAttempt(invocation) && invocation.Arguments.Length == 2 &&
                invocation.Arguments[1].Value is ILocalReferenceOperation reference &&
                SymbolEqualityComparer.Default.Equals(reference.Local, local)));
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
            IrTerm initial = CSharpOperationSemantics.DefaultValue(_context.Factory, type);
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
            _context.Variables, _context.Captures, [.. _abstentions], _context.Origin)
        {
            ConstructionLimitExceeded = _constructionLimitExceeded || _calls?.ConstructionLimitExceeded == true,
            IsShadowCallSkeleton = _preserveSourceCall != null
        };
    }

    private void RegionTerminator(BasicBlock source, IrBlockId block, OperationId structural)
    {
        var site = source.BranchValue == null ? structural : _context.Site(source.BranchValue);
        var branch = source.FallThroughSuccessor;
        if (EnclosingRegionFilter(source.EnclosingRegion) is { } filter)
        {
            RegionFilterTerminator(filter, source, block, site);
            return;
        }
        if (source.BranchValue is { } specification && (_context.IsSpecificationOperation(specification) || _emission?.IsElided(specification) == true) &&
            branch?.Destination != null)
        { _builder.Goto(block, structural, RegionNormalTarget(branch, structural)); }
        else if (source.ConditionKind != ControlFlowConditionKind.None && source.BranchValue is { } condition &&
                 branch != null && source.ConditionalSuccessor is { } conditionalBranch)
        {
            // Roslyn may fold a rethrow/finally completion into one edge of a
            // conditional block. Its guard (and mutations) must execute before
            // selecting that terminal edge; it is not an unconditional leave.
            var value = Value(condition, block);
            var fall = RegionConditionalTarget(source, branch, site);
            var conditional = RegionConditionalTarget(source, conditionalBranch, site);
            var whenTrue = source.ConditionKind == ControlFlowConditionKind.WhenTrue;
            _builder.Branch(value.Continuation, site, value.Value, whenTrue ? conditional : fall, whenTrue ? fall : conditional);
        }
        else if (branch?.Semantics == ControlFlowBranchSemantics.Return)
        {
            if (source.BranchValue is { } expression && _context.Result is { } result)
            {
                var value = Value(expression, block);
                _builder.Assign(value.Continuation, site, result, value.Value);
                block = value.Continuation;
            }
            if (branch.FinallyRegions.IsEmpty)
            { Return(block, site, _context.Result is { } returned ? _context.Factory.Variable(returned) : null); }
            else
            {
                var returned = RegionBlock("return:captured");
                Return(returned, site, _context.Result is { } resultValue ? _context.Factory.Variable(resultValue) : null);
                _builder.Goto(block, site, EnterRegionFinallyChain(branch.FinallyRegions, returned, site, null, preserveResult: true));
            }
        }
        else if (branch?.Semantics == ControlFlowBranchSemantics.Throw)
        {
            if (!CSharpOperationSemantics.IsNullThrow(source.BranchValue))
            { ExplicitThrow(source.BranchValue, block, token => RegionExceptionTarget(source.EnclosingRegion, token)); }
            else
            {
                _builder.Throw(block, site, IrExceptionKind.NullReference,
                    RegionExceptionTarget(source.EnclosingRegion, Token(IrExceptionKind.NullReference, site)));
            }
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
            var state = EnclosingRegionFinally(source.EnclosingRegion);
            _builder.Goto(block, site, state.Dispatch);
        }
        else if (source.Kind == BasicBlockKind.Exit)
        { Return(block, site); }
        else if (branch?.Destination != null)
        { _builder.Goto(block, site, RegionNormalTarget(branch, site)); }
        else
        { throw new RegionIncompleteException(); }
    }

    private IrBlockId RegionConditionalTarget(BasicBlock source, ControlFlowBranch branch, OperationId site)
    {
        SpendRegion();
        if (branch.Semantics == ControlFlowBranchSemantics.Regular)
        { return RegionNormalTarget(branch, site); }
        if (branch.Semantics == ControlFlowBranchSemantics.StructuredExceptionHandling)
        { return EnclosingRegionFinally(source.EnclosingRegion).Dispatch; }
        if (branch.Semantics == ControlFlowBranchSemantics.Return && _context.Result == null)
        {
            var returned = RegionBlock("return:conditional");
            Return(returned, site);
            return branch.FinallyRegions.IsEmpty ? returned
                : EnterRegionFinallyChain(branch.FinallyRegions, returned, site, null, preserveResult: true);
        }
        if (branch.Semantics == ControlFlowBranchSemantics.Rethrow)
        {
            var caught = source.EnclosingRegion;
            while (caught != null && caught.Kind != ControlFlowRegionKind.Catch)
            { SpendRegion(); caught = caught.EnclosingRegion; }
            if (caught == null)
            { throw new RegionIncompleteException(); }
            var rethrown = RegionBlock("rethrow:conditional");
            _regionRethrows.Add((rethrown, source.EnclosingRegion, caught, site));
            return rethrown;
        }
        // A conditional CFG edge has no separate thrown operand here. Do not
        // infer throw-null or a captured return value from the Boolean guard.
        throw new RegionIncompleteException();
    }

    private IrBlockId RegionNormalTarget(ControlFlowBranch branch, OperationId site)
    {
        SpendRegion();
        if (branch.Semantics != ControlFlowBranchSemantics.Regular || branch.Destination == null)
        { throw new RegionIncompleteException(); }
        var target = _blocks[branch.Destination];
        if (branch.FinallyRegions.IsEmpty)
        { return target; }
        return EnterRegionFinallyChain(branch.FinallyRegions, target, site, null,
            preserveResult: HasEnclosingRegionFinally(branch.Source.EnclosingRegion));
    }

    private IrBlockId RegionExceptionTarget(ControlFlowRegion source, RegionExceptionToken token)
    {
        if (_regionFilters.Count != 0 || _frame != null)
        { return RegionExceptionSearch(source, token); }
        var throughFinally = new List<ControlFlowRegion>();
        var target = _regionExceptionalExit;
        for (var region = source; region != null; region = region.EnclosingRegion)
        {
            SpendRegion();
            if (region.Kind == ControlFlowRegionKind.Try && region.EnclosingRegion is { } parent)
            {
                if (parent.Kind == ControlFlowRegionKind.TryAndCatch)
                {
                    ControlFlowRegion? handler = null;
                    foreach (var candidate in parent.NestedRegions)
                    {
                        if (candidate.Kind != ControlFlowRegionKind.Catch)
                        { continue; }
                        var catches = Catches(candidate, token);
                        if (catches == null)
                        { return RegionExceptionSearch(source, token); }
                        if (catches == true)
                        { handler = candidate; break; }
                    }
                    if (handler != null)
                    {
                        target = RegionCatchEntry(handler, token);
                        break;
                    }
                }
                else if (parent.Kind == ControlFlowRegionKind.TryAndFinally)
                { throughFinally.Add(parent.NestedRegions.Single(child => child.Kind == ControlFlowRegionKind.Finally)); }
            }
        }
        return EnterRegionFinallyChain(throughFinally, target, token.Site, token,
            preserveResult: HasEnclosingRegionFinally(source));
    }

    // `throw e`: a null `e` throws NullReferenceException instead. The thrown
    // exception's static type selects handlers.
    private void ExplicitThrow(IOperation? thrown, IrBlockId block, Func<RegionExceptionToken, IrBlockId> target)
    {
        if (thrown == null || _regionGraph.OriginalOperation.SemanticModel?.Compilation is not { } compilation)
        { throw new RegionIncompleteException(); }
        thrown = CSharpOperationSemantics.ThrownOperand(thrown);
        if (CSharpOperationSemantics.ThrownType(compilation, thrown) is not { } type)
        { throw new RegionIncompleteException(); }
        // A thrown `new X(...)` has exact runtime type X and is never null.
        var exact = thrown is IObjectCreationOperation;
        var value = Value(thrown, block);
        block = value.Continuation;
        var site = _context.ThrowSite(thrown, type, exact);
        _explicitThrowTypes[site] = type;
        if (!exact)
        {
            var isNull = _context.Factory.Binary(IrBinaryOperator.Equal, value.Value, _context.Factory.Null(value.Value.Type));
            var nullThrow = RegionBlock("throw:null");
            var thrownBlock = RegionBlock("throw:explicit");
            _builder.Branch(block, site, isNull, nullThrow, thrownBlock);
            _builder.Throw(nullThrow, site, IrExceptionKind.NullReference, target(Token(IrExceptionKind.NullReference, site)));
            block = thrownBlock;
        }
        _builder.Throw(block, site, IrExceptionKind.Explicit, target(Token(IrExceptionKind.Explicit, site)));
    }

    // An Unknown exception is caught by Exception/Object handlers; whether a
    // narrower handler catches it is not known (null), and the search then
    // chooses either way. An explicit exception of static type S is caught by
    // a handler for T when S derives from T, and may be when T derives from S.
    private bool? Catches(ControlFlowRegion handler, RegionExceptionToken token)
    {
        var kinds = _regionCatchKinds[handler];
        if (token.Kind == IrExceptionKind.Explicit && _explicitThrowTypes.TryGetValue(token.Site, out var thrown))
        {
            var caught = handler.ExceptionType;
            if (caught == null || caught.SpecialType == SpecialType.System_Object || CSharpOperationSemantics.DerivesFrom(thrown, caught))
            { return true; }
            if (CSharpOperationSemantics.DerivesFrom(caught, thrown))
            { return null; }
            return false;
        }
        if (token.Kind is IrExceptionKind.Unknown or IrExceptionKind.Explicit && !kinds.Contains(IrExceptionKind.Unknown))
        { return null; }
        return kinds.Contains(token.Kind);
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

    private IrBlockId EnterRegionFinally(RegionFinally state, IrBlockId target, OperationId site, RegionExceptionToken? exception, bool preserveResult = false)
    {
        SpendRegion();
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
            IrTerm filler = CSharpOperationSemantics.DefaultValue(_context.Factory, type);
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
                if (Catches(request.Catch, token) != false)
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
            { Return(block, request.Site); }
        }
    }

    private void FinishRegionFinally(RegionFinally state, OperationId structural)
    {
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
        { Return(block, structural); }
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

    private IrBlockId EnterRegionFinallyChain(IReadOnlyList<ControlFlowRegion> regions, IrBlockId target,
        OperationId site, RegionExceptionToken? exception, bool preserveResult = false)
    {
        for (var ordinal = regions.Count - 1; ordinal >= 0; ordinal--)
        {
            SpendRegion();
            if (!_regionFinallys.TryGetValue(regions[ordinal], out var state))
            { throw new RegionIncompleteException(); }
            target = EnterRegionFinally(state, target, site, exception, preserveResult);
        }
        return target;
    }

    private RegionFinally EnclosingRegionFinally(ControlFlowRegion source)
    {
        for (var region = source; region != null; region = region.EnclosingRegion)
        {
            SpendRegion();
            if (_regionFinallys.TryGetValue(region, out var state))
            { return state; }
        }
        throw new RegionIncompleteException();
    }

    private bool HasEnclosingRegionFinally(ControlFlowRegion source)
    {
        // Every executing finally entry defined hidden Result by return
        // capture or the typed filler. Nested leaves must retain that value,
        // including an inner fault caught before the outer finally completes.
        for (var region = source; region != null; region = region.EnclosingRegion)
        {
            SpendRegion();
            if (_regionFinallys.ContainsKey(region))
            { return true; }
        }
        return false;
    }
    private IrBlockId RegionBlock(string name)
    {
        SpendRegion();
        return _builder.CreateBlock(name);
    }
    private void SpendRegion()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (_calls != null ? !_calls.Spend() : --_regionRemaining < 0)
        { _constructionLimitExceeded = true; throw new RegionIncompleteException(); }
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
