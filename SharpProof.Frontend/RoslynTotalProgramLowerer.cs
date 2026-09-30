namespace SharpProof.Frontend;

// Candidate scalar CFG only. Regions, cycles, calls and heap effects remain
// explicitly incomplete until their exact routing/semantics are implemented.
internal sealed class RoslynTotalProgramLowerer(TotalLoweringContext context)
{
    private readonly TotalLoweringContext _context = context;
    private readonly IrProgramBuilder _builder = new(context.Factory);
    private readonly List<FrontendProgramAbstention> _abstentions = [];
    private readonly Dictionary<BasicBlock, IrBlockId> _blocks = [];
    private RoslynTotalExpressionLowerer _expressions = null!;

    internal FrontendProgramLoweringResult Lower(ControlFlowGraph graph)
    {
        var structural = _context.Factory.CreateOperation("candidate:cfg");
        var entry = _builder.CreateBlock("entry");
        _builder.SetEntry(entry);
        if (!_context.HasScalarSignature || !_context.OwnsBody(graph.OriginalOperation) ||
            HasUnsupportedRegion(graph.Root) || !TrySelect(graph.Blocks[0], out var selected))
        {
            _abstentions.Add(new(structural, _context.HasScalarSignature ? FrontendAbstention.UnsupportedControlFlow : FrontendAbstention.UnsupportedType));
            _builder.Return(entry, structural);
            return Result();
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
        _builder.ExceptionalExit(exceptionalExit, structural);
        _expressions = new(_context, _builder, exceptionalExit);
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
                    _builder.Return(value.Continuation, site, _context.Factory.Variable(result));
                }
                else
                {
                    _builder.Return(block, site);
                }
            }
            else if (source.Kind == BasicBlockKind.Exit)
            {
                _builder.Return(block, site);
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
                _builder.Return(block, site);
            }
        }
        return Result();
    }

    private IrBlockId Statement(IOperation operation, IrBlockId block)
    {
        if (_context.IsSpecificationOperation(operation))
        {
            return block;
        }
        switch (operation)
        {
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

    private FrontendProgramLoweringResult Result()
    {
        return new(_builder.Build(), _abstentions.Count == 0 ? FrontendSubsetClassification.Exact : FrontendSubsetClassification.Abstain(_abstentions[0].Reason),
            _context.Variables, _context.Captures, [.. _abstentions]);
    }

    private static bool HasUnsupportedRegion(ControlFlowRegion region)
    {
        return region.Kind is not (ControlFlowRegionKind.Root or ControlFlowRegionKind.LocalLifetime) || region.NestedRegions.Any(HasUnsupportedRegion);
    }

    private static bool TrySelect(BasicBlock entry, out ImmutableArray<BasicBlock> selected)
    {
        var visited = new HashSet<BasicBlock>();
        var active = new HashSet<BasicBlock>();
        var order = new List<BasicBlock>();
        var valid = Visit(entry);
        order.Reverse();
        selected = [.. order];
        return valid;

        bool Visit(BasicBlock block)
        {
            if (active.Contains(block))
            { return false; }
            if (!visited.Add(block))
            { return true; }
            active.Add(block);
            foreach (var branch in new[] { block.FallThroughSuccessor, block.ConditionalSuccessor })
            {
                if (branch == null)
                { continue; }
                if (!branch.FinallyRegions.IsEmpty || branch.Semantics is not (ControlFlowBranchSemantics.Regular or ControlFlowBranchSemantics.Return))
                {
                    return false;
                }
                if (branch.Destination is { } destination && !Visit(destination))
                { return false; }
            }
            active.Remove(block);
            order.Add(block);
            return true;
        }
    }
}
