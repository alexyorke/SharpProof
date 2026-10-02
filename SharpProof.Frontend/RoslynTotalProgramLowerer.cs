namespace SharpProof.Frontend;

// Candidate scalar CFG. The bounded region route is separate from the
// unchanged ordinary CFG path; unsupported forms remain incomplete.
internal sealed partial class RoslynTotalProgramLowerer(TotalLoweringContext context, CancellationToken cancellationToken,
    TotalSourceCallSession? calls = null, bool externalFilterSearch = false)
{
    private readonly TotalLoweringContext _context = context;
    private IrProgramBuilder _builder = new(context.Factory);
    private readonly List<FrontendProgramAbstention> _abstentions = [];
    private readonly Dictionary<BasicBlock, IrBlockId> _blocks = [];
    private RoslynTotalExpressionLowerer _expressions = null!;
    private readonly CancellationToken _cancellationToken = cancellationToken;
    private readonly TotalSourceCallSession? _calls = calls;
    private readonly bool _externalFilterSearch = externalFilterSearch;
    private SourceCallFrame? _frame;
    private OperationId? _regionStructural;
    private IrBlockId _ordinaryExceptionalExit;
    private bool _constructionLimitExceeded;

    internal FrontendProgramLoweringResult Lower(ControlFlowGraph graph)
    {
        if (_calls != null && !_calls.Enter(_context.Target))
        { return IncompleteRegion(_context.Factory.CreateOperation("candidate:cfg")); }
        try
        {
            LowerCore(graph);
            var result = Result();
            if (_regionGraph != null)
            { ValidateRegionOrder(result.Program); }
            return result;
        }
        catch (RegionIncompleteException)
        { return IncompleteRegion(_regionStructural ?? _context.Factory.CreateOperation("candidate:cfg")); }
        finally
        { _calls?.Leave(_context.Target); }
    }

    private void LowerCore(ControlFlowGraph graph)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (graph.Blocks.Length > MaximumRegionSteps || _context.Parameters.Length > MaximumRegionSteps / 2)
        { _constructionLimitExceeded = true; throw new RegionIncompleteException(); }
        var structural = _context.Factory.CreateOperation("candidate:cfg");
        var entry = _builder.CreateBlock("entry");
        if (_frame == null)
        { _builder.SetEntry(entry); }
        else
        { _frame.Entry = entry; }
        if (_context.HasScalarSignature && _context.OwnsBody(graph.OriginalOperation) &&
            (HasUnsupportedRegion(graph.Root) || graph.Blocks.Any(block =>
                block.FallThroughSuccessor?.Semantics is ControlFlowBranchSemantics.Throw or ControlFlowBranchSemantics.Rethrow)))
        { _regionStructural = structural; LowerRegions(graph, entry, structural); return; }
        if (!_context.HasScalarSignature || !_context.OwnsBody(graph.OriginalOperation) ||
            HasUnsupportedRegion(graph.Root) || !TrySelect(graph.Blocks[0], out var selected))
        {
            _abstentions.Add(new(structural, _context.HasScalarSignature ? FrontendAbstention.UnsupportedControlFlow : FrontendAbstention.UnsupportedType));
            Return(entry, structural);
            return;
        }
        foreach (var binding in _context.Parameters)
        {
            _builder.Assign(entry, structural, binding.Current, _context.Factory.Variable(binding.Entry));
            _builder.Assign(entry, structural, binding.PreState, _context.Factory.Variable(binding.Entry));
        }
        foreach (var block in selected)
        {
            _blocks.Add(block, _builder.CreateBlock("cfg:" + block.Ordinal.ToString(CultureInfo.InvariantCulture)));
        }
        var exceptionalExit = _builder.CreateBlock("exceptional:exit");
        _ordinaryExceptionalExit = exceptionalExit;
        _builder.ExceptionalExit(exceptionalExit, structural);
        _expressions = new(_context, _builder, exceptionalExit)
        {
            Spend = SpendRegion,
            SourceCall = InlineSourceCall,
            ExceptionTarget = _frame == null ? null : (kind, site) => _frame.Search(kind, site, static target => target)
        };
        _builder.Goto(entry, structural, _blocks[graph.Blocks[0]]);
        foreach (var source in selected)
        {
            var block = _blocks[source];
            foreach (var operation in source.Operations)
            {
                block = Statement(operation, block);
            }
            var site = source.BranchValue == null ? structural : _context.Site(source.BranchValue);
            var branch = source.FallThroughSuccessor;
            if (source.BranchValue is { } specification && _context.IsSpecificationOperation(specification) &&
                branch?.Destination is { } specificationContinuation)
            {
                _builder.Goto(block, structural, _blocks[specificationContinuation]);
            }
            else if (branch?.Semantics == ControlFlowBranchSemantics.Return)
            {
                if (source.BranchValue is { } returnOperation && _context.Result is { } result)
                {
                    var value = Value(returnOperation, block);
                    _builder.Assign(value.Continuation, site, result, value.Value);
                    Return(value.Continuation, site, _context.Factory.Variable(result));
                }
                else
                {
                    Return(block, site);
                }
            }
            else if (source.Kind == BasicBlockKind.Exit)
            {
                // Nonvoid C# fallthrough is rejected by the compiler. A
                // constant loop can still expose its impossible exit in CFG;
                // keep that structural return typed, behind its false edge.
                IrTerm? filler = _context.Result is { } exitResult && source.Predecessors.Any(predecessor => predecessor.Semantics == ControlFlowBranchSemantics.Regular)
                    ? CSharpOperationSemantics.DefaultValue(_context.Factory, _context.Factory.GetVariableInfo(exitResult).Type)
                    : null;
                Return(block, site, filler);
            }
            else if (source.ConditionKind != ControlFlowConditionKind.None && source.BranchValue is { } condition &&
                branch?.Destination is { } fallThrough && source.ConditionalSuccessor?.Destination is { } conditional)
            {
                var value = Value(condition, block);
                var whenTrue = source.ConditionKind == ControlFlowConditionKind.WhenTrue;
                _builder.Branch(value.Continuation, site, value.Value,
                    _blocks[whenTrue ? conditional : fallThrough], _blocks[whenTrue ? fallThrough : conditional]);
            }
            else if (branch?.Destination is { } destination)
            {
                _builder.Goto(block, site, _blocks[destination]);
            }
            else
            {
                _abstentions.Add(new(site, FrontendAbstention.UnsupportedControlFlow));
                Return(block, site);
            }
        }
    }

    private void Return(IrBlockId block, OperationId site, IrTerm? value = null)
    {
        if (_frame == null)
        { _builder.Return(block, site, value); }
        else
        { _frame.Return(block, site, value); }
    }

    private IrBlockId Statement(IOperation operation, IrBlockId block)
    {
        SpendRegion();
        if (_context.TryGetSpecificationAssumption(operation, out var assumption, out var site))
        {
            // Elided arguments have no runtime evaluation. This is the owning
            // static filter, including the clause's definedness domain.
            _builder.Assume(block, site, assumption);
            return block;
        }
        if (_context.IsSpecificationOperation(operation))
        {
            return block;
        }
        switch (operation)
        {
            case IExpressionStatementOperation
            {
                Operation: IObjectCreationOperation
                {
                    Type.SpecialType: SpecialType.System_Object,
                    Constructor.Parameters.Length: 0,
                    Arguments.Length: 0,
                    Initializer: null
                } creation
            }:
                // The core Object constructor has no managed body effects.
                // The discarded identity never enters the value domain.
                _builder.Allocate(block, _context.Site(creation), _context.Factory.ObjectType);
                return block;
            case IExpressionStatementOperation { Operation: IInvocationOperation invocation } when _context.IsSpecificationOperation(invocation):
                return block;
            case IExpressionStatementOperation expression:
                return Value(expression.Operation, block).Continuation;
            case ISimpleAssignmentOperation or IIncrementOrDecrementOperation or ICompoundAssignmentOperation:
                return Value(operation, block).Continuation;
            case IFlowCaptureOperation capture:
                {
                    var value = Value(capture.Value, block);
                    _builder.Assign(value.Continuation, _context.Site(operation), _context.Capture(capture.Id, capture.Value.Type), value.Value);
                    return value.Continuation;
                }
            case IVariableDeclaratorOperation declarator:
                {
                    if (declarator.Symbol.RefKind != RefKind.None)
                    {
                        _abstentions.Add(new(_context.Site(operation), FrontendAbstention.UnsupportedMutation));
                        return block;
                    }
                    if (declarator.Initializer == null)
                    { return block; }
                    var value = Value(declarator.Initializer.Value, block);
                    _builder.Assign(value.Continuation, _context.Site(operation), _context.Variable(declarator.Symbol), value.Value);
                    return value.Continuation;
                }
            case IBlockOperation or IVariableDeclarationGroupOperation or IVariableDeclarationOperation:
                foreach (var child in operation.ChildOperations)
                {
                    block = Statement(child, block);
                }
                return block;
            case IEmptyOperation:
                return block;
            default:
                _abstentions.Add(new(_context.Site(operation), FrontendAbstention.UnsupportedStatement));
                return block;
        }
    }

    private TotalBodyValue Value(IOperation operation, IrBlockId block)
    {
        var value = _expressions.LowerBodyValue(operation, block);
        if (!value.Classification.IsExact)
        {
            _abstentions.Add(new(_context.Site(operation), value.Classification.Abstention));
        }
        return value;
    }

    private FrontendProgramLoweringResult Result(IrProgram? program = null)
    {
        program ??= _builder.Build();
        var instructions = 0;
        foreach (var block in program.Blocks)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            instructions += block.Instructions.Length;
            if (instructions > MaximumRegionSteps)
            {
                _constructionLimitExceeded = true;
                throw new RegionIncompleteException();
            }
        }
        return new(program, _abstentions.Count == 0 ? FrontendSubsetClassification.Exact : FrontendSubsetClassification.Abstain(_abstentions[0].Reason),
            _context.Variables, _context.Captures, [.. _abstentions], _context.Origin);
    }

    private bool HasUnsupportedRegion(ControlFlowRegion region)
    {
        var pending = new Stack<ControlFlowRegion>();
        pending.Push(region);
        var remaining = MaximumRegionSteps;
        while (pending.Count != 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_calls != null)
            { SpendRegion(); }
            if (--remaining < 0)
            { return true; }
            var current = pending.Pop();
            if (current.Kind is not (ControlFlowRegionKind.Root or ControlFlowRegionKind.LocalLifetime))
            { return true; }
            foreach (var child in current.NestedRegions)
            {
                if (pending.Count >= MaximumRegionSteps)
                {
                    return true;
                }
                pending.Push(child);
            }
        }
        return false;
    }

    private bool TrySelect(BasicBlock entry, out ImmutableArray<BasicBlock> selected)
    {
        var visited = new HashSet<BasicBlock>();
        var active = new HashSet<BasicBlock>();
        var order = new List<BasicBlock>();
        var pending = new Stack<(BasicBlock Block, bool Exit)>();
        pending.Push((entry, false));
        selected = default;
        while (pending.Count != 0)
        {
            SpendRegion();
            var (block, exit) = pending.Pop();
            if (exit)
            {
                active.Remove(block);
                order.Add(block);
                continue;
            }
            // Ordinary scalar cycles remain owned original IR. The worker
            // derives loop proof/search encodings; region cycles stay closed.
            if (active.Contains(block))
            { continue; }
            if (!visited.Add(block))
            { continue; }
            if (visited.Count > MaximumRegionSteps)
            { throw new RegionIncompleteException(); }
            active.Add(block);
            pending.Push((block, true));
            // LIFO reproduces the old fall-through-then-conditional DFS order.
            foreach (var branch in new[] { block.ConditionalSuccessor, block.FallThroughSuccessor })
            {
                if (branch == null)
                { continue; }
                if (!branch.FinallyRegions.IsEmpty || branch.Semantics is not (ControlFlowBranchSemantics.Regular or ControlFlowBranchSemantics.Return))
                { return false; }
                if (branch.Destination is { } destination)
                { pending.Push((destination, false)); }
            }
        }
        order.Reverse();
        selected = [.. order];
        return true;
    }
}
