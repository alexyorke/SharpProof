namespace SharpProof.Frontend;

// Candidate scalar CFG. The bounded region route is separate from the
// unchanged ordinary CFG path; unsupported forms remain incomplete.
internal sealed partial class RoslynTotalProgramLowerer(TotalLoweringContext context, CancellationToken cancellationToken,
    TotalSourceCallSession? calls = null, bool externalFilterSearch = false,
    Func<IMethodSymbol, bool>? preserveSourceCall = null, ImmutableArray<TotalShadowCallHop> shadowAncestry = default, bool? captureShadowCallAncestry = null)
{
    private readonly TotalLoweringContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly ImmutableArray<TotalShadowCallHop> _shadowAncestry = shadowAncestry.IsDefault ? [] : shadowAncestry;
    private readonly bool _captureShadowCallAncestry = captureShadowCallAncestry ?? context.CaptureShadowCallAncestry;
    private IrProgramBuilder _builder = new(context.Factory);
    private readonly ImmutableArray<FrontendProgramAbstention>.Builder _abstentions = ImmutableArray.CreateBuilder<FrontendProgramAbstention>();
    private ImmutableDictionary<BasicBlock, IrBlockId> _blocks = ImmutableDictionary<BasicBlock, IrBlockId>.Empty;
    private RoslynTotalExpressionLowerer _expressions = null!;
    private readonly CancellationToken _cancellationToken = cancellationToken;
    private readonly TotalSourceCallSession? _calls = calls;
    private readonly bool _externalFilterSearch = externalFilterSearch;
    private readonly Func<IMethodSymbol, bool>? _preserveSourceCall = preserveSourceCall;
    private ImmutableDictionary<IrCallInstruction, IMethodSymbol> _preservedSourceCalls = ImmutableDictionary<IrCallInstruction, IMethodSymbol>.Empty;
    private ImmutableDictionary<IrAssignInstruction, TotalCallPrecondition> _callPreconditions = ImmutableDictionary<IrAssignInstruction, TotalCallPrecondition>.Empty;
    private SourceCallFrame? _frame;
    private OperationId? _regionStructural;
    private IrBlockId _ordinaryExceptionalExit;
    private bool _constructionLimitExceeded;
    private CSharpInvocationEmissionPolicy? _emission;
    private Compilation? _emissionCompilation;
    private CompilerElisionContinuationPlanner _compilerElision = null!;
    private readonly TotalRegionWork _regionWork = new(cancellationToken, calls);

    internal FrontendProgramLoweringResult Lower(ControlFlowGraph graph)
    {
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
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
        catch (FrontendLoweringRejectionException incomplete)
        { return IncompleteRegion(_regionStructural ?? _context.Factory.CreateOperation("candidate:cfg"), incomplete.Reason); }
        finally
        { _calls?.Leave(_context.Target); }
    }

    private void LowerCore(ControlFlowGraph graph)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (CSharpOperationSemantics.HasSynchronizedEntry(_context.Target))
        { throw new RegionIncompleteException(); }
        if (HasOwnIteratorBody(graph.OriginalOperation))
        { throw new RegionIncompleteException(FrontendAbstention.UnsupportedStatement); }
        _emissionCompilation = _context.Compilation ??= graph.OriginalOperation.SemanticModel?.Compilation;
        _emission = _emissionCompilation == null ? null : new(_emissionCompilation);
        _compilerElision = new(graph, _context, _emission, _regionWork, _emissionCompilation);
        _context.TrackArrayReceivers(graph);
        _context.TrackDefaultComparers(graph);
        _context.FreshReceiver = _context.Target.MethodKind == MethodKind.Constructor &&
            graph.OriginalOperation.Descendants().OfType<IInstanceReferenceOperation>()
                .All(static receiver => receiver is null ? throw new System.ArgumentNullException(nameof(receiver)) : (receiver.Parent is IFieldReferenceOperation field && field.Instance == receiver ||
                    CSharpOperationSemantics.IsObjectConstructorCall(receiver.Parent)));
        // Roslyn captures a local or parameter that a branching right-hand
        // side assigns; every capture of that id names the same variable.
        foreach (var captures in graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations)).OfType<IFlowCaptureOperation>().GroupBy(capture => capture is null ? throw new System.ArgumentNullException(nameof(capture)) : (capture.Id)))
        {
            var symbols = captures.Select(capture => capture is null ? throw new System.ArgumentNullException(nameof(capture)) : (capture.Value switch
            {
                IParameterReferenceOperation parameter when parameter.Parameter.RefKind == RefKind.None && _context.OwnsParameter(parameter.Parameter) =>
                    (ISymbol)parameter.Parameter,
                ILocalReferenceOperation { Local.RefKind: RefKind.None } local => local.Local,
                _ => null
            })).Distinct(SymbolEqualityComparer.Default).ToArray();
            if (symbols.Length == 1 && symbols[0] is { } symbol)
            { _context.RecordStorageCapture(captures.Key, symbol); }
        }
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
                block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.FallThroughSuccessor?.Semantics is ControlFlowBranchSemantics.Throw or ControlFlowBranchSemantics.Rethrow))))
        { _regionStructural = structural; LowerRegions(graph, entry, structural); return; }
        if (!_context.HasScalarSignature || !_context.OwnsBody(graph.OriginalOperation) ||
            HasUnsupportedRegion(graph.Root) || !TrySelect(graph.Blocks[0], out var selected))
        {
            _abstentions.Add(new(structural, _context.HasScalarSignature ? FrontendAbstention.UnsupportedControlFlow : FrontendAbstention.UnsupportedType));
            Return(entry, structural);
            return;
        }
        foreach (var binding in _context.Inputs)
        {
            _builder.Assign(entry, structural, binding.Current, _context.Factory.Variable(binding.Entry));
            _builder.Assign(entry, structural, binding.PreState, _context.Factory.Variable(binding.Entry));
        }
        EmitEntrySpecificationAssumptions(entry);
        foreach (var block in selected)
        {
            _blocks = _blocks.AddUnique(block, _builder.CreateBlock("cfg:" + block.Ordinal.ToString(CultureInfo.InvariantCulture)));
        }
        var exceptionalExit = _builder.CreateBlock("exceptional:exit");
        _ordinaryExceptionalExit = exceptionalExit;
        _builder.ExceptionalExit(exceptionalExit, structural);
        _expressions = new(_context, _builder, exceptionalExit)
        {
            Spend = SpendRegion,
            SourceCall = InlineSourceCall,
            SourceGetter = InlineSourceGetter,
            SourceConstruction = InlineSourceConstruction,
            SourceSetter = InlineSourceSetter,
            AllowOpaqueCalls = _preserveSourceCall == null && _calls?.OpaqueCalls == true,
            ShadowCallSkeleton = _preserveSourceCall != null,
            OpaqueEffects = _calls?.OpaqueEffects,
            ApproximateElementReads = _calls?.ApproximateElementReads == true,
            PinElementReads = _calls?.PinElementReads == true,
            ExceptionTarget = _frame == null ? null : (kind, site) => site.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(site)) : (_frame.Search(kind, site, static target => target.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(target)) : (target)))
        };
        if (graph.OriginalOperation.Language != LanguageNames.VisualBasic)
        { _builder.Goto(entry, structural, _blocks[graph.Blocks[0]]); }
        foreach (var source in selected)
        {
            var block = _blocks[source];
            if (!source.IsReachable && !_captureShadowCallAncestry && _preserveSourceCall == null)
            {
                // Roslyn retains constant-folded dead arms in the CFG. Keep
                // their IR blocks typed, but never lower their side effects.
                // Shadow call skeletons retain dead precondition markers.
                Return(block, structural, ImpossibleResult(source));
                continue;
            }
            foreach (var operation in source.Operations)
            {
                block = Statement(operation, block);
            }
            var site = source.BranchValue == null ? structural : _context.Site(source.BranchValue);
            var branch = source.FallThroughSuccessor;
            if (source.BranchValue is { } specification && _context.IsSpecificationOperation(specification) &&
                source.ConditionKind != ControlFlowConditionKind.None)
            {
                if (!_compilerElision.TryElidedSpecificationContinuation(source, out var continuation))
                { throw new RegionIncompleteException(); }
                var target = branch?.Destination is { IsReachable: true } reachable ? reachable : continuation;
                _builder.Goto(block, structural, _blocks[target]);
            }
            else if (source.BranchValue is { } elided && _compilerElision.IsElidedSpecificationPart(elided) &&
                branch?.Destination is { } specificationContinuation)
            {
                var target = CompilerElisionContinuationPlanner.ElidedEmissionSuccessor(source,
                    allowUnreachableCompilerElision: _captureShadowCallAncestry).Destination!;
                _builder.Goto(block, structural, _blocks[target]);
            }
            else if (branch?.Semantics == ControlFlowBranchSemantics.Return)
            {
                if (source.BranchValue is { } returnOperation && _context.Result is { } result)
                {
                    var value = Value(returnOperation, block);
                    _builder.Assign(value.Continuation, site, result, Bridge(value.Value, _context.Factory.GetVariableInfo(result).Type));
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
                IrTerm? filler = _context.Result is { } exitResult && source.Predecessors.Any(predecessor => predecessor is null ? throw new System.ArgumentNullException(nameof(predecessor)) : (predecessor.Semantics == ControlFlowBranchSemantics.Regular))
                    ? CSharpOperationSemantics.DefaultValue(_context.Factory, _context.Factory.GetVariableInfo(exitResult).Type)
                    : null;
                Return(block, site, filler);
            }
            else if (source.ConditionKind != ControlFlowConditionKind.None && source.BranchValue is { } condition &&
                branch?.Destination is { } fallThrough && source.ConditionalSuccessor?.Destination is { } conditional)
            {
                var value = Condition(condition, block);
                var whenTrue = source.ConditionKind == ControlFlowConditionKind.WhenTrue;
                _builder.Branch(value.Continuation, site, value.Value,
                    _blocks[whenTrue ? conditional : fallThrough], _blocks[whenTrue ? fallThrough : conditional]);
            }
            else
            {
                _builder.Goto(block, site, _blocks[branch!.Destination!]);
            }
        }
        if (graph.OriginalOperation.Language == LanguageNames.VisualBasic)
        {
            InitializeVisualBasicLocalStorage(graph, entry, structural);
            _builder.Goto(entry, structural, _blocks[graph.Blocks[0]]);
        }
    }

    private void InitializeVisualBasicLocalStorage(ControlFlowGraph graph, IrBlockId entry, OperationId structural)
    {
        // Only storage actually referenced during lowering needs an initial
        // value. Unused, unreachable and compiler-elided declarations keep
        // their existing admission. VB initializes ordinary local storage
        // once per invocation, including locals declared inside loops.
        RepositoryParameterGuard.ThrowIfInvalid(entry.IsDefault, nameof(entry));
        RepositoryParameterGuard.ThrowIfInvalid(structural.IsDefault, nameof(structural));
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
        var bindings = _context.Variables.Where(binding => binding.Symbol is ILocalSymbol).ToImmutableArray();
        if (bindings.Length == 0)
        { return; }
        var owned = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
        var pending = ImmutableStack<ControlFlowRegion>.Empty;
        pending = pending.Push(graph.Root);
        while (!pending.IsEmpty)
        {
            SpendRegion();
            var region = pending.Peek();
            pending = pending.Pop();
            foreach (var local in region.Locals)
            {
                SpendRegion();
                owned.Add(local);
            }
            foreach (var nested in region.NestedRegions)
            { pending = pending.Push(nested); }
        }
        foreach (var binding in bindings)
        {
            SpendRegion();
            var local = (ILocalSymbol)binding.Symbol;
            if (!owned.Contains(local))
            { throw new RegionIncompleteException(); }
            if (local.IsStatic || local.RefKind != RefKind.None)
            { throw new RegionIncompleteException(FrontendAbstention.UnsupportedMutation); }
            _builder.Assign(entry, structural, binding.Variable,
                RoslynValueTypeValidation.LocalStorageDefault(_context.Factory, local.Type, binding.Variable));
        }
    }

    private bool HasOwnIteratorBody(IOperation root)
    {
        root = RepositoryParameterGuard.NotNull(root, nameof(root));
        var pending = ImmutableStack<IOperation>.Empty;
        pending = pending.Push(root);
        while (!pending.IsEmpty)
        {
            var operation = pending.Peek();
            pending = pending.Pop();
            _cancellationToken.ThrowIfCancellationRequested();
            if (operation != root &&
                (operation is ILocalFunctionOperation or IAnonymousFunctionOperation))
            { continue; }
            if (operation.Kind is OperationKind.YieldBreak or OperationKind.YieldReturn)
            { return true; }
            foreach (var child in operation.ChildOperations)
            {
                pending = pending.Push(child);
            }
        }
        return false;
    }

    private void Return(IrBlockId block, OperationId site, IrTerm? value = null)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        RepositoryParameterGuard.ThrowIfInvalid(site.IsDefault, nameof(site));
        if (_frame == null)
        { _builder.Return(block, site, value); }
        else
        { _frame.Return(block, site, value); }
    }

    private IIncrementOrDecrementOperation? TryDiscardedStaticFieldMutation(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        var discarded = operation is IExpressionStatementOperation statement ? statement.Operation : operation;
        var conversionAllowed = false;
        if (discarded is ISimpleAssignmentOperation { IsRef: false, Target: IDiscardOperation } assignment)
        {
            discarded = assignment.Value;
            conversionAllowed = true;
        }
        var depth = 0;
        while (discarded is IParenthesizedOperation or IConversionOperation)
        {
            if (++depth >= 256)
            { return null; }
            SpendRegion();
            if (discarded is IParenthesizedOperation parenthesized)
            {
                discarded = parenthesized.Operand;
                continue;
            }
            var conversion = (IConversionOperation)discarded;
            if (!conversionAllowed || conversion.IsChecked || conversion.OperatorMethod != null || !conversion.Conversion.IsNumeric ||
                !CSharpOperationSemantics.TryGetScalarInteger(conversion.Type?.SpecialType ?? SpecialType.None, out _) ||
                !CSharpOperationSemantics.TryGetScalarInteger(conversion.Operand.Type?.SpecialType ?? SpecialType.None, out _))
            { return null; }
            discarded = conversion.Operand;
        }
        return discarded is IIncrementOrDecrementOperation { IsChecked: false, OperatorMethod: null, Target: IFieldReferenceOperation { Field.IsStatic: true, Field.Type.SpecialType: SpecialType.System_Int32, Instance: null } field } increment &&
            CSharpOperationSemantics.IsSupportedFieldWrite(field.Field, _context.Target.ContainingAssembly) ? increment : null;
    }

    private IIncrementOrDecrementOperation? TryDiscardedInstanceFieldMutation(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        var discarded = operation is IExpressionStatementOperation statement ? statement.Operation : operation;
        if (discarded is ISimpleAssignmentOperation { IsRef: false, Target: IDiscardOperation } assignment)
        { discarded = assignment.Value; }
        var depth = 0;
        while (discarded is IParenthesizedOperation parenthesized)
        {
            if (++depth >= 256)
            { return null; }
            SpendRegion();
            discarded = parenthesized.Operand;
        }
        return discarded is IIncrementOrDecrementOperation { IsChecked: false, OperatorMethod: null, Target: IFieldReferenceOperation { Field.IsStatic: false, Field.Type.SpecialType: SpecialType.System_Int32 } field } increment &&
            CSharpOperationSemantics.IsSupportedFieldWrite(field.Field, _context.Target.ContainingAssembly) ? increment : null;
    }
    private void EmitEntrySpecificationAssumptions(IrBlockId entry)
    {
        RepositoryParameterGuard.ThrowIfInvalid(entry.IsDefault, nameof(entry));
        foreach (var assumption in _context.EntrySpecificationAssumptions)
        {
            SpendRegion();
            _builder.Assume(entry, assumption.Site, assumption.Condition);
        }
    }

    private IrBlockId Statement(IOperation operation, IrBlockId block)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
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
        if (_emission?.IsElided(operation) == true)
        { return block; }
        if (TryDiscardedStaticFieldMutation(operation) is { } discardedMutation)
        {
            _builder.Write(block, _context.Site(discardedMutation), IrWriteRegion.Static);
            return block;
        }
        if (TryDiscardedInstanceFieldMutation(operation) is { } discardedInstanceMutation)
        {
            var value = _expressions.LowerDiscardedInstanceFieldMutation(discardedInstanceMutation, block);
            if (!value.Classification.IsExact)
            { _abstentions.Add(new(_context.Site(operation), value.Classification.Abstention)); }
            return value.Continuation;
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
            case IInvocationOperation invocation when CSharpOperationSemantics.IsMonitorAttempt(invocation):
                return Value(invocation, block).Continuation;
            // A compiler-generated call statement, such as the Dispose a
            // foreach or using runs in its finally, is a value discarded.
            case IInvocationOperation { IsImplicit: true } implicitCall:
                return Value(implicitCall, block).Continuation;
            case ISimpleAssignmentOperation or IIncrementOrDecrementOperation or ICompoundAssignmentOperation:
                return Value(operation, block).Continuation;
            case IFlowCaptureOperation { Value: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } } thisCapture
                when _expressions.IsImplicitThis(thisCapture.Value):
                _context.RecordThisCapture(thisCapture.Id);
                return block;
            case IFlowCaptureOperation { Value: IFieldReferenceOperation field } fieldCapture
                when CSharpOperationSemantics.IsSupportedFieldRead(field.Field) && _expressions.IsImplicitThis(field.Instance):
                _context.RecordFieldCapture(fieldCapture.Id, field);
                return block;
            case IFlowCaptureOperation capture:
                {
                    var value = _expressions.LowerReceiverValue(capture.Value, block, 0);
                    if (!value.Classification.IsExact)
                    { _abstentions.Add(new(_context.Site(capture.Value), value.Classification.Abstention)); }
                    _context.RecordConcatenationOperands(capture.Id, value.ConcatenationOperands);
                    _builder.Assign(value.Continuation, _context.Site(operation), _context.Capture(capture.Id, capture.Value.Type), Bridge(value.Value, _context.Factory.GetVariableInfo(_context.Capture(capture.Id, capture.Value.Type)).Type));
                    return value.Continuation;
                }
            default:
                _abstentions.Add(new(_context.Site(operation), FrontendAbstention.UnsupportedStatement));
                return block;
        }
    }

    private TotalBodyValue Condition(IOperation operation, IrBlockId block)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        var value = Value(operation, block);
        // Unsupported handler success captures can be untyped; their
        // approximation is not a Boolean branch condition.
        if (value.Value.Type != _context.Factory.BooleanType)
        { throw new RegionIncompleteException(); }
        return value;
    }

    private TotalBodyValue Value(IOperation operation, IrBlockId block)
    {
        RepositoryParameterGuard.ThrowIfInvalid(block.IsDefault, nameof(block));
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
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
        program = InvalidateReferenceInputs(program);
        var instructions = 0;
        foreach (var block in program.Blocks)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (block.Instructions.Length > MaximumRegionSteps - instructions)
            {
                _constructionLimitExceeded = true;
                throw new RegionIncompleteException();
            }
            instructions += block.Instructions.Length;
        }
        return new(program, _abstentions.Count == 0 ? FrontendSubsetClassification.Exact : FrontendSubsetClassification.Abstain(_abstentions[0].Reason),
            _context.Variables, _context.Captures, _abstentions.ToImmutable(), _context.Origin)
        {
            SourceInitializationEffectsComplete = _calls?.SourceInitializationEffectsComplete ?? true,
            IsShadowCallSkeleton = _preserveSourceCall != null,
            PreservedSourceCalls = _preservedSourceCalls,
            CallPreconditions = _callPreconditions
        };
    }

    private bool HasUnsupportedRegion(ControlFlowRegion region)
    {
        region = RepositoryParameterGuard.NotNull(region, nameof(region));
        var pending = ImmutableStack<ControlFlowRegion>.Empty;
        pending = pending.Push(region);
        var remaining = MaximumRegionSteps;
        while (!pending.IsEmpty)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (_calls != null)
            { SpendRegion(); }
            if (--remaining < 0)
            { return true; }
            var current = pending.Peek();
            pending = pending.Pop();
            if (current.Kind is not (ControlFlowRegionKind.Root or ControlFlowRegionKind.LocalLifetime))
            { return true; }
            foreach (var child in current.NestedRegions)
            {
                pending = pending.Push(child);
            }
        }
        return false;
    }

    private bool TrySelect(BasicBlock entry, out ImmutableArray<BasicBlock> selected)
    {
        entry = RepositoryParameterGuard.NotNull(entry, nameof(entry));
        var visited = ImmutableHashSet.CreateBuilder<BasicBlock>();
        var active = ImmutableHashSet.CreateBuilder<BasicBlock>();
        var order = ImmutableArray.CreateBuilder<BasicBlock>();
        var pending = ImmutableStack<(BasicBlock Block, bool Exit)>.Empty;
        pending = pending.Push((entry, false));
        selected = default;
        while (!pending.IsEmpty)
        {
            SpendRegion();
            var (block, exit) = pending.Peek();
            pending = pending.Pop();
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
            active.Add(block);
            pending = pending.Push((block, true));
            // LIFO reproduces the old fall-through-then-conditional DFS order.
            foreach (var branch in ImmutableArray.Create(block.ConditionalSuccessor, block.FallThroughSuccessor))
            {
                if (branch == null)
                { continue; }
                if (!branch.FinallyRegions.IsEmpty || branch.Semantics is not (ControlFlowBranchSemantics.Regular or ControlFlowBranchSemantics.Return))
                { return false; }
                if (branch.Destination is { } destination)
                { pending = pending.Push((destination, false)); }
            }
        }
        selected = order.ToImmutable().Reverse().ToImmutableArray();
        return true;
    }
}
