namespace SharpProof.Frontend;

internal sealed partial class RoslynTotalProgramLowerer
{
    private sealed class SourceCallFrame(Action<IrBlockId, OperationId, IrTerm?> returned,
        Func<IrExceptionKind, OperationId, Func<IrBlockId, IrBlockId>, IrBlockId> search)
    {
        internal IrBlockId Entry { get; set; }
        internal void Return(IrBlockId block, OperationId site, IrTerm? value)
        { returned(block, site, value); }
        internal IrBlockId Search(IrExceptionKind kind, OperationId site, Func<IrBlockId, IrBlockId> unwind)
        { return search(kind, site, unwind); }
    }

    // A shadow call records an effect boundary, not executable Total semantics.
    // In particular, it supplies no exception edge or completion guarantee.
    private TotalBodyValue? PreserveSourceCall(IInvocationOperation invocation, IrBlockId block, int depth)
    {
        var method = invocation.TargetMethod;
        if (invocation.Instance != null || method.MethodKind != MethodKind.Ordinary ||
            !method.IsStatic || method.IsAsync || method.IsExtern || method.IsVirtual || method.IsAbstract || method.IsOverride ||
            method.Arity != 0 || method.ContainingType.IsGenericType || method.ReducedFrom != null ||
            method.ReturnsByRef || method.ReturnsByRefReadonly ||
            method.PartialDefinitionPart != null || method.PartialImplementationPart != null ||
            !SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, _context.Target.ContainingAssembly) ||
            method.DeclaringSyntaxReferences.Length != 1 || invocation.Arguments.Length != method.Parameters.Length ||
            method.Parameters.Any(parameter => parameter.RefKind != RefKind.None ||
                !CSharpOperationSemantics.IsValueDomain(parameter.Type)) ||
            !method.ReturnsVoid && !CSharpOperationSemantics.IsValueDomain(method.ReturnType))
        { return null; }
        var ordinals = new HashSet<int>();
        foreach (var argument in invocation.Arguments)
        {
            SpendRegion();
            if (argument.Parameter is not { } parameter ||
                !SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, method) ||
                parameter.Ordinal < 0 || parameter.Ordinal >= method.Parameters.Length || !ordinals.Add(parameter.Ordinal) ||
                argument.ArgumentKind is not (ArgumentKind.Explicit or ArgumentKind.DefaultValue or ArgumentKind.ParamArray) ||
                argument.ArgumentKind == ArgumentKind.ParamArray && !parameter.IsParams ||
                TotalSourceCallSession.IsEmptyParamsArray(argument) ||
                argument.ArgumentKind == ArgumentKind.DefaultValue &&
                    (!parameter.HasExplicitDefaultValue || !argument.Value.ConstantValue.HasValue))
            { return null; }
        }
        if (!_preserveSourceCall!(method.OriginalDefinition))
        { return null; }
        var resultType = _context.Type(method.ReturnType);
        var target = method.ReturnsVoid ? (IrVarId?)null : _context.Temporary(resultType);
        IrTerm value = target is { } returned ? _context.Factory.Variable(returned) : _context.Factory.Boolean(false);
        if (target is { } storage)
        { _builder.Assign(block, _context.Site(invocation), storage, CSharpOperationSemantics.DefaultValue(_context.Factory, resultType)); }
        var arguments = new IrTerm[method.Parameters.Length];
        foreach (var argument in invocation.Arguments)
        {
            SpendRegion();
            var lowered = _expressions.LowerBodyValue(argument.Value, block, depth + 1);
            block = lowered.Continuation;
            if (!lowered.Classification.IsExact)
            { return new(value, block, lowered.Classification); }
            arguments[argument.Parameter!.Ordinal] = lowered.Value;
        }
        var member = _context.Factory.GetOrCreateMember(
            CompilerIdentityBridge.InternSymbol(_context.Factory, method), _context.Type(method.ContainingType),
            "shadow-call:" + CompilerIdentityBridge.CreateSymbolDisplay(method), resultType, true,
            [.. method.Parameters.Select(parameter => _context.Type(parameter.Type))]);
        var call = _builder.Call(block, _context.Site(invocation), target, member, null, arguments);
        _preservedSourceCalls.Add(call, method.OriginalDefinition);
        return new(value, block, FrontendSubsetClassification.Exact);
    }

    private TotalBodyValue? InlineSourceCall(IInvocationOperation invocation, IrBlockId block, int depth)
    {
        if (_preserveSourceCall != null)
        { return PreserveSourceCall(invocation, block, depth); }
        if (_calls?.PrepareScalarCall(invocation) is { } model)
        { return _expressions.LowerScalarCall(invocation, model, block, depth); }
        if (invocation.TargetMethod.DeclaringSyntaxReferences.IsEmpty && _calls?.PrepareIl(invocation.TargetMethod) is { } body)
        { return InlineMetadataCall(invocation, body, block, depth); }
        if (_calls == null || !_calls.TryPrepare(_context, invocation, out var frame, out var graph))
        { return null; }
        var callee = frame!;
        // Nested lowering has its own region state. Capture the caller's lexical
        // search/unwind context before expanding arguments or the callee body.
        var callerRegion = _regionGraph == null ? null : _regionSource.EnclosingRegion;
        var callerFilter = callerRegion == null ? null : EnclosingRegionFilter(callerRegion);
        var externalFilterSearch = _externalFilterSearch || callerFilter != null || HasEnclosingCatchFilter(callerRegion);
        var site = _context.Site(invocation);
        var returned = callee.Result is { } result ? _context.Temporary(_context.Factory.GetVariableInfo(result).Type) : (IrVarId?)null;
        IrTerm marker = returned is { } resultStorage ? _context.Factory.Variable(resultStorage) : _context.Factory.Boolean(false);
        if (returned is { } initialized)
        {
            // A fault never reads a call result. Keeping its internal storage
            // initialized preserves normal-only writes across shared finally.
            var type = _context.Factory.GetVariableInfo(initialized).Type;
            _builder.Assign(block, site, initialized, CSharpOperationSemantics.DefaultValue(_context.Factory, type));
        }
        var arguments = new IrTerm[callee.Parameters.Length];
        foreach (var argument in invocation.Arguments)
        {
            SpendRegion();
            if (TotalSourceCallSession.IsEmptyParamsArray(argument))
            {
                if (_calls.PrepareEmptyParamsArray(argument) is not { Classification.IsExact: true, Throws.IsEmpty: true } empty)
                { return new(marker, block, FrontendSubsetClassification.Abstain(FrontendAbstention.UnsupportedInvocationShape)); }
                arguments[argument.Parameter!.Ordinal] = empty.Value;
                continue;
            }
            var value = _expressions.LowerBodyValue(argument.Value, block, depth + 1);
            block = value.Continuation;
            if (!value.Classification.IsExact)
            { return new(marker, block, value.Classification); }
            arguments[argument.Parameter!.Ordinal] = value.Value;
        }
        if (externalFilterSearch)
        {
            if (!_calls.Spend(callee.Parameters.Length + 2))
            { throw new RegionIncompleteException(); }
            var continued = _builder.CreateBlock("call:continued");
            var composed = new RoslynTotalProgramLowerer(callee, _cancellationToken, _calls, externalFilterSearch)
            {
                _builder = _builder,
                _frame = new((source, operation, value) =>
                {
                    if (returned is { } storage && value != null)
                    { _builder.Assign(source, operation, storage, value); }
                    _builder.Goto(source, operation, continued);
                }, (kind, operation, unwind) => ContinueSourceException(callerRegion, callerFilter, kind, operation, unwind))
            };
            composed.LowerSharedFrame(graph!);
            foreach (var parameter in callee.Parameters)
            { _builder.Assign(block, site, parameter.Entry, arguments[parameter.Parameter.Ordinal]); }
            _builder.Goto(block, site, composed._frame.Entry);
            return new(marker, continued, FrontendSubsetClassification.Exact);
        }
        var lowering = new RoslynTotalProgramLowerer(callee, _cancellationToken, _calls, externalFilterSearch).Lower(graph!);
        if (!lowering.IsExact)
        { return new(marker, block, lowering.Classification); }
        var program = lowering.Program;
        var instructionCount = 0;
        foreach (var source in program.Blocks)
        {
            SpendRegion();
            instructionCount += source.Instructions.Length;
        }
        if (!_calls.Spend(program.Blocks.Length + instructionCount + callee.Parameters.Length + 2))
        { throw new RegionIncompleteException(); }
        var continuation = _builder.CreateBlock("call:continued");
        var blocks = program.Blocks.ToDictionary(source => source.Id, source => _builder.CreateBlock("call:body"));
        foreach (var parameter in callee.Parameters)
        { _builder.Assign(block, site, parameter.Entry, arguments[parameter.Parameter.Ordinal]); }
        _builder.Goto(block, site, blocks[program.Entry]);
        foreach (var source in program.Blocks)
        {
            foreach (var instruction in source.Instructions)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var destination = blocks[source.Id];
                switch (instruction)
                {
                    case IrAllocationInstruction allocation:
                        _builder.Allocate(destination, allocation.Operation, allocation.AllocatedType, allocation.Target, allocation.Length, allocation.InitialValues);
                        break;
                    case IrLockInstruction synchronization:
                        _builder.Lock(destination, synchronization.Operation, synchronization.Receiver);
                        break;
                    case IrWriteInstruction write:
                        _builder.Write(destination, write.Operation, write.Region);
                        break;
                    case IrAssignInstruction assign:
                        _builder.Assign(destination, assign.Operation, assign.Target, assign.Value);
                        break;
                    case IrBranchInstruction branch:
                        _builder.Branch(destination, branch.Operation, branch.Condition, Target(branch.WhenTrue), Target(branch.WhenFalse));
                        break;
                    case IrGotoInstruction go:
                        _builder.Goto(destination, go.Operation, Target(go.Target));
                        break;
                    case IrThrowInstruction thrown:
                        var exceptionTarget = program.GetBlock(thrown.Target).Terminator is IrExceptionalExitInstruction
                            ? callerFilter != null ? callerFilter.Rejected : callerRegion == null ? _ordinaryExceptionalExit
                                : RegionExceptionTarget(callerRegion, Token(thrown.ExceptionKind, thrown.Operation))
                            : blocks[thrown.Target];
                        _builder.Throw(destination, thrown.Operation, thrown.ExceptionKind, exceptionTarget);
                        break;
                    case IrExceptionalExitInstruction exceptional:
                        _builder.ExceptionalExit(destination, exceptional.Operation);
                        break;
                    case IrReturnInstruction normal:
                        if (returned is { } resultTarget && normal.Value != null)
                        { _builder.Assign(destination, normal.Operation, resultTarget, normal.Value); }
                        _builder.Goto(destination, normal.Operation, continuation);
                        break;
                    default:
                        throw new RegionIncompleteException();
                }
            }
        }
        return new(marker, continuation, FrontendSubsetClassification.Exact);

        IrBlockId Target(IrBlockId target)
        {
            // Escaping exceptions must retain an actual kind/site-bearing
            // Throw. A generic edge cannot invent the caller's pending context.
            if (program.GetBlock(target).Terminator is IrExceptionalExitInstruction)
            { throw new RegionIncompleteException(); }
            return blocks[target];
        }
    }

    private void LowerSharedFrame(ControlFlowGraph graph)
    {
        if (_calls == null || !_calls.Enter(_context.Target))
        { throw new RegionIncompleteException(); }
        try
        {
            LowerCore(graph);
            if (_abstentions.Count != 0)
            { throw new RegionIncompleteException(); }
        }
        finally
        { _calls.Leave(_context.Target); }
    }

    private IrBlockId ContinueSourceException(ControlFlowRegion? callerRegion, RegionFilter? callerFilter,
        IrExceptionKind kind, OperationId site, Func<IrBlockId, IrBlockId> unwind)
    {
        SpendRegion();
        if (callerFilter != null)
        { return unwind(callerFilter.Rejected); }
        if (callerRegion != null)
        { return RegionExceptionSearch(callerRegion, Token(kind, site), unwind); }
        return _frame != null ? _frame.Search(kind, site, unwind) : unwind(_ordinaryExceptionalExit);
    }

    private bool HasEnclosingCatchFilter(ControlFlowRegion? region)
    {
        for (; region != null; region = region.EnclosingRegion)
        {
            SpendRegion();
            if (region.Kind != ControlFlowRegionKind.Try || region.EnclosingRegion is not { Kind: ControlFlowRegionKind.TryAndCatch } parent)
            { continue; }
            foreach (var handler in parent.NestedRegions)
            {
                SpendRegion();
                if (handler.Kind == ControlFlowRegionKind.FilterAndHandler)
                { return true; }
            }
        }
        return false;
    }

}
