namespace SharpProof.Frontend;

public sealed class RoslynProgramLowerer(
    IrFactory factory, Func<IMethodSymbol, bool>? isKnownPure = null)
{
    private readonly IrFactory _factory =
        ArgumentNullGuard.NotNull(factory, nameof(factory));
    private readonly Func<IMethodSymbol, bool> _isKnownPure = isKnownPure ?? (static _ => false);

    public FrontendProgramLoweringResult Lower(ControlFlowGraph graph)
    {
        graph = ArgumentNullGuard.NotNull(graph, nameof(graph));

        return new LoweringSession(_factory, graph, _isKnownPure, graph.Blocks[0], 0, static _ => false).Lower().Lowering;
    }

    internal SelectedProgramLoweringResult LowerSelected(
        ControlFlowGraph graph,
        BasicBlock entry,
        int firstOperation,
        Func<IOperation, bool> exclude)
    {
        graph = ArgumentNullGuard.NotNull(graph, nameof(graph));
        entry = ArgumentNullGuard.NotNull(entry, nameof(entry));
        exclude = ArgumentNullGuard.NotNull(exclude, nameof(exclude));

        if (!graph.Blocks.Contains(entry) || firstOperation < 0 || firstOperation > entry.Operations.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(firstOperation));
        }

        return new LoweringSession(_factory, graph, _isKnownPure, entry, firstOperation, exclude).Lower();
    }

    internal static bool IsDirectInvocation(IInvocationOperation invocation)
    {
        if (invocation.TargetMethod.ReducedFrom != null || invocation.Arguments.Length != invocation.TargetMethod.Parameters.Length)
        {
            return false;
        }

        var ordinals = new HashSet<int>();
        foreach (var argument in invocation.Arguments)
        {
            var ordinal = argument.Parameter?.Ordinal ?? -1;
            if (argument.ArgumentKind != ArgumentKind.Explicit ||
                ordinal < 0 ||
                ordinal >= invocation.TargetMethod.Parameters.Length ||
                !ordinals.Add(ordinal))
            {
                return false;
            }
        }

        return ordinals.Count == invocation.TargetMethod.Parameters.Length;
    }

    private sealed class LoweringSession(
        IrFactory factory,
        ControlFlowGraph graph,
        Func<IMethodSymbol, bool> isKnownPure,
        BasicBlock entry,
        int firstOperation,
        Func<IOperation, bool> exclude)
    {
        private readonly IrFactory _factory = factory;
        private readonly ControlFlowGraph _graph = graph;
        private readonly BasicBlock _entry = entry;
        private readonly int _firstOperation = firstOperation;
        private readonly Func<IOperation, bool> _exclude = exclude;
        private readonly IrProgramBuilder _builder = new(factory);
        private readonly RoslynOperationLowerer _expressions = new(factory, isKnownPure);
        private readonly Func<IMethodSymbol, bool> _isKnownPure = isKnownPure;
        private readonly Dictionary<BasicBlock, IrBlockId> _blocks = [];
        private readonly List<FrontendProgramAbstention> _abstentions = [];
        private readonly HashSet<(int Operation, FrontendAbstention Reason)> _seenAbstentions = [];
        private readonly Dictionary<IrCallInstruction, IInvocationOperation> _calls = [];
        private int _nextTemporary;

        internal SelectedProgramLoweringResult Lower()
        {
            var selection = SelectBlocks();
            var selected = selection.Selected;
            var omittedHandler = selection.OmittedHandler;
            if (omittedHandler != null)
            {
                Abstain(
                    CreateOperation(
                        omittedHandler,
                        ordinal: -1,
                        kind: OperationKind.None),
                    FrontendAbstention.UnsupportedControlFlow);
            }
            foreach (var block in selected)
            {
                _blocks.Add(block, _builder.CreateBlock(
                    "cfg:" + block.Ordinal.ToString(CultureInfo.InvariantCulture) + ":" + block.Kind));
            }
            _builder.SetEntry(_blocks[_entry]);
            foreach (var block in selected)
            {
                LowerBlock(block);
            }

            var firstReason = _abstentions.Count == 0 ? FrontendAbstention.None : _abstentions[0].Reason;
            var lowering = new FrontendProgramLoweringResult(
                _builder.Build(), firstReason == FrontendAbstention.None
                    ? FrontendSubsetClassification.Exact
                    : FrontendSubsetClassification.Abstain(firstReason),
                _expressions.CreateVariableBindings(), _expressions.CreateCaptureBindings(),
                [.. _abstentions]);
            return new SelectedProgramLoweringResult(lowering, _calls.ToImmutableDictionary());
        }

        private void LowerBlock(BasicBlock source)
        {
            var block = _blocks[source];
            var operationOrdinal = 0;
            foreach (var operation in source.Operations)
            {
                var identity = CreateOperation(source, operationOrdinal++, operation.Kind);
                if ((source == _entry && operationOrdinal <= _firstOperation) || _exclude(operation))
                {
                    continue;
                }

                if (LowerStatement(block, identity, operation))
                {
                    return;
                }
            }
            LowerTerminator(source, block, CreateOperation(source, operationOrdinal, OperationKind.None));
        }

        private bool LowerStatement(
            IrBlockId block, OperationId operation, IOperation statement)
        {
            switch (statement)
            {
                case IVariableDeclarationGroupOperation group:
                    foreach (var declaration in group.Declarations)
                    {
                        LowerDeclaration(block, operation, declaration);
                    }

                    return false;
                case IVariableDeclarationOperation declaration:
                    LowerDeclaration(block, operation, declaration);
                    return false;
                case IVariableDeclaratorOperation declarator:
                    LowerDeclarator(block, operation, declarator);
                    return false;
                case IExpressionStatementOperation expression:
                    return LowerStatement(block, operation, expression.Operation);
                case ISimpleAssignmentOperation assignment:
                    LowerAssignment(block, operation, assignment);
                    return false;
                case IFlowCaptureOperation capture:
                    LowerCapture(block, operation, capture);
                    return false;
                case IInvocationOperation invocation:
                    LowerInvocation(block, operation, invocation, wantsResult: false);
                    return false;
                case IReturnOperation returned:
                    LowerReturn(block, operation, returned.ReturnedValue);
                    return true;
                case IEmptyOperation:
                    return false;
                case IIncrementOrDecrementOperation mutation:
                    LowerUnsupportedMutation(block, operation, mutation.Target);
                    return false;
                case ICompoundAssignmentOperation mutation:
                    LowerUnsupportedMutation(
                        block,
                        operation,
                        mutation.Target,
                        mutation.Value);
                    return false;
                default:
                    Abstain(operation, FrontendAbstention.UnsupportedStatement);
                    HavocKnownState(block, operation);
                    return false;
            }
        }

        private void LowerDeclaration(
            IrBlockId block, OperationId operation, IVariableDeclarationOperation declaration)
        {
            foreach (var declarator in declaration.Declarators)
            {
                LowerDeclarator(block, operation, declarator);
            }
        }

        private void LowerDeclarator(
            IrBlockId block, OperationId operation, IVariableDeclaratorOperation declarator)
        {
            var target = _expressions.GetVariable(declarator.Symbol, declarator.Symbol.Type);
            if (declarator.Symbol.RefKind != RefKind.None)
            {
                if (declarator.Initializer != null)
                {
                    _ = LowerValue(
                        block,
                        operation,
                        declarator.Initializer.Value);
                }
                Abstain(operation, FrontendAbstention.UnsupportedMutation);
                HavocKnownState(block, operation);
                return;
            }
            if (declarator.Initializer == null)
            {
                Havoc(block, operation, IrHavocKind.Variables, target.Variable);
                return;
            }
            var value = LowerValue(block, operation, declarator.Initializer.Value);
            AssignOrHavoc(block, operation, target.Variable, value);
        }

        private void LowerCapture(
            IrBlockId block, OperationId operation, IFlowCaptureOperation capture)
        {
            var target = _expressions.GetCapture(capture.Id, capture.Value.Type);
            var value = LowerValue(block, operation, capture.Value);
            AssignOrHavoc(block, operation, target.Variable, value);
        }

        private void LowerAssignment(
            IrBlockId block, OperationId operation, ISimpleAssignmentOperation assignment)
        {
            if (assignment.Target is IFlowCaptureReferenceOperation)
            {
                _ = LowerValue(block, operation, assignment.Value);
                Abstain(operation, FrontendAbstention.UnsupportedMutation);
                HavocKnownState(block, operation);
                return;
            }

            if (assignment.IsRef ||
                assignment.Target is ILocalReferenceOperation
                {
                    Local.RefKind: not RefKind.None
                })
            {
                LowerUnsupportedMutation(
                    block,
                    operation,
                    assignment.Target,
                    assignment.Value);
                return;
            }

            var variable = _expressions.GetReferencedVariable(assignment.Target, unwrapConversions: false);
            if (variable.HasValue)
            {
                var directValue = LowerValue(
                    block,
                    operation,
                    assignment.Value);
                AssignOrHavoc(
                    block,
                    operation,
                    variable.Value,
                    directValue);
                return;
            }

            var location = LowerLocation(block, operation, assignment.Target);
            if (location.Location == null)
            {
                _ = LowerValue(block, operation, assignment.Value);
                Abstain(operation, location.Abstention);
                HavocKnownState(block, operation);
                return;
            }
            var value = LowerValue(block, operation, assignment.Value);
            if (location.Location.Type != value.Type)
            {
                Abstain(operation, FrontendAbstention.UnsupportedType);
                Havoc(block, operation, IrHavocKind.Memory);
                return;
            }
            _builder.Store(block, operation, location.Location, value);
        }

        private IrTerm LowerValue(IrBlockId block, OperationId operation, IOperation value)
        {
            switch (value)
            {
                case IInvocationOperation invocation:
                    return LowerInvocation(block, operation, invocation, wantsResult: true)!;
                case IIncrementOrDecrementOperation mutation:
                    return LowerUnsupportedMutationResult(
                        block, operation, mutation.Target, mutation.Type);
                case ICompoundAssignmentOperation mutation:
                    return LowerUnsupportedMutationResult(
                        block,
                        operation,
                        mutation.Target,
                        mutation.Type,
                        mutation.Value);
                case IFieldReferenceOperation:
                case IArrayElementReferenceOperation:
                    var location = LowerLocation(block, operation, value);
                    if (location.Location != null)
                    {
                        var target = CreateTemporary("load", location.Location.Type);
                        _builder.Load(block, operation, target, location.Location);
                        return _factory.Variable(target);
                    }
                    Abstain(operation, location.Abstention);
                    break;
                default:
                    var nestedValues = new Dictionary<IOperation, IrTerm>();
                    LowerNestedOperations(
                        block,
                        operation,
                        value,
                        nestedValues);
                    if (nestedValues.Count == 0)
                    {
                        break;
                    }

                    var priorCustomLowering =
                        _expressions.CustomLowering;
                    _expressions.CustomLowering = candidate =>
                        nestedValues.TryGetValue(
                            candidate,
                            out var replacement)
                            ? (true, replacement)
                            : priorCustomLowering(candidate);
                    try
                    {
                        var nestedLowered = _expressions.LowerTerm(value);
                        Observe(operation, nestedLowered.Classification);
                        return nestedLowered.Term;
                    }
                    finally
                    {
                        _expressions.CustomLowering =
                            priorCustomLowering;
                    }
            }

            var lowered = _expressions.LowerTerm(value);
            Observe(operation, lowered.Classification);
            return lowered.Term;
        }

        private void LowerNestedOperations(
            IrBlockId block,
            OperationId operation,
            IOperation value,
            Dictionary<IOperation, IrTerm> nestedValues)
        {
            switch (value)
            {
                case IInvocationOperation invocation:
                    LowerInvocation(block, operation, invocation, wantsResult: false);
                    return;
                case IArrayElementReferenceOperation:
                    nestedValues.Add(
                        value,
                        LowerValue(block, operation, value));
                    return;
                case IAnonymousFunctionOperation:
                case ILocalFunctionOperation:
                case INameOfOperation:
                    return;
            }

            foreach (var child in value.ChildOperations)
            {
                LowerNestedOperations(
                    block,
                    operation,
                    child,
                    nestedValues);
            }
        }

        private IrVariableTerm? LowerInvocation(
            IrBlockId block, OperationId operation, IInvocationOperation invocation,
            bool wantsResult)
        {
            var receiver = LowerOptionalValue(block, operation, invocation.Instance);
            var loweredArguments = LowerInvocationArguments(
                block,
                operation,
                invocation);
            var arguments = loweredArguments.Arguments;
            var mutated = loweredArguments.Mutated;
            var resultType = _expressions.GetTypeId(invocation.Type);
            var member = _expressions.GetMember(invocation.TargetMethod, ref receiver, "call:", resultType, arguments);
            var isDirect = loweredArguments.IsDirect;
            if (!isDirect)
            {
                Abstain(operation, FrontendAbstention.UnsupportedInvocationShape);
            }

            IrVarId? target = null;
            if (wantsResult && !invocation.TargetMethod.ReturnsVoid &&
                CompilerIdentityBridge.IsSupportedValueDomain(invocation.Type))
            {
                target = CreateTemporary("call", resultType);
            }

            var call = _builder.Call(block, operation, target, member, receiver, arguments);
            _calls.Add(call, invocation);

            if (mutated.Length != 0 || !isDirect ||
                !IsStaticallyBound(invocation.TargetMethod) ||
                !_isKnownPure(invocation.TargetMethod))
            {
                if (IsClosureInvocation(invocation.TargetMethod))
                {
                    mutated = [.. mutated
                        .Concat(CreateKnownStateVariables())];
                }
                Havoc(block, operation, mutated.Length == 0 ? IrHavocKind.Memory : IrHavocKind.VariablesAndMemory, mutated);
            }

            if (target.HasValue)
            {
                return _factory.Variable(target.Value);
            }

            if (wantsResult)
            {
                Abstain(operation, FrontendAbstention.UnsupportedType);
                var missing = CreateTemporary("void-call", _factory.ObjectType);
                Havoc(block, operation, IrHavocKind.Variables, missing);
                return _factory.Variable(missing);
            }
            return null;
        }

        private (IrTerm[] Arguments, bool IsDirect, IrVarId[] Mutated)
            LowerInvocationArguments(
            IrBlockId block,
            OperationId operation,
            IInvocationOperation invocation)
        {
            var isDirect = invocation.TargetMethod.ReducedFrom == null &&
                invocation.Arguments.Length ==
                invocation.TargetMethod.Parameters.Length;
            var ordinals = isDirect ? new HashSet<int>() : null;
            HashSet<IrVarId>? mutated = null;
            var lowered = new List<(
                int Ordinal, IrTerm Value)>(invocation.Arguments.Length);
            foreach (var argument in invocation.Arguments)
            {
                var ordinal = argument.Parameter?.Ordinal ?? -1;
                var value = LowerValue(block, operation, argument.Value);
                lowered.Add((argument.Parameter?.Ordinal ?? int.MaxValue, value));
                if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                    _expressions.GetReferencedVariable(argument.Value) is { } variable)
                {
                    (mutated ??= []).Add(variable);
                }
                if (isDirect &&
                    (argument.ArgumentKind != ArgumentKind.Explicit ||
                     ordinal < 0 ||
                     ordinal >= invocation.TargetMethod.Parameters.Length ||
                     !ordinals!.Add(ordinal)))
                {
                    isDirect = false;
                }
            }

            if (isDirect &&
                ordinals!.Count != invocation.TargetMethod.Parameters.Length)
            {
                isDirect = false;
            }

            return ([.. lowered
                .OrderBy(static argument => argument.Ordinal)
                .Select(static argument => argument.Value)],
                isDirect,
                mutated?.ToArray() ?? []);
        }

        private LocationLowering LowerLocation(
            IrBlockId block, OperationId operation, IOperation target)
        {
            try
            {
                switch (target)
                {
                    case IFieldReferenceOperation field:
                        var fieldReceiver = LowerOptionalValue(block, operation, field.Instance);
                        if (!CompilerIdentityBridge.IsSupportedValueDomain(field.Type))
                        {
                            return LocationLowering.Abstain(
                                FrontendAbstention.UnsupportedType);
                        }
                        var fieldMember = _expressions.GetMember(field.Field, ref fieldReceiver, "field:", field.Type);
                        return LocationLowering.FromLocation(_builder.MemberLocation(fieldMember, fieldReceiver));
                    case IInvocationOperation invocation:
                        _ = LowerInvocation(
                            block,
                            operation,
                            invocation,
                            wantsResult: false);
                        return LocationLowering.Abstain(
                            FrontendAbstention.UnsupportedMutation);
                    case IPropertyReferenceOperation property:
                        _ = LowerOptionalValue(block, operation, property.Instance);
                        foreach (var argument in property.Arguments)
                        {
                            _ = LowerValue(block, operation, argument.Value);
                        }
                        return LocationLowering.Abstain(
                            FrontendAbstention.UnsupportedMemberAccess);
                    case IArrayElementReferenceOperation element
                        when element.Indices.Length == 1:
                        return LocationLowering.FromLocation(_builder.SequenceLocation(
                            LowerValue(block, operation, element.ArrayReference),
                            LowerValue(block, operation, element.Indices[0])));
                    case IArrayElementReferenceOperation element:
                        _ = LowerValue(block, operation, element.ArrayReference);
                        foreach (var index in element.Indices)
                        {
                            _ = LowerValue(block, operation, index);
                        }
                        return LocationLowering.Abstain(FrontendAbstention.UnsupportedMemberAccess);
                    default:
                        return LocationLowering.Abstain(FrontendAbstention.UnsupportedMutation);
                }
            }
            catch (ArgumentException)
            {
                return LocationLowering.Abstain(FrontendAbstention.UnsupportedType);
            }
        }

        private void LowerTerminator(
            BasicBlock source, IrBlockId block, OperationId operation)
        {
            if (source.Kind == BasicBlockKind.Exit)
            {
                _builder.Return(block, operation);
                return;
            }

            var fallThrough = source.FallThroughSuccessor;
            var conditional = source.ConditionalSuccessor;
            if (HasMandatoryFinally(fallThrough) ||
                HasMandatoryFinally(conditional))
            {
                Abstain(operation, FrontendAbstention.UnsupportedControlFlow);
                HavocKnownState(block, operation);
                _builder.Return(block, operation);
                return;
            }
            if (fallThrough?.Semantics == ControlFlowBranchSemantics.Return)
            {
                LowerReturn(block, operation, source.BranchValue);
                return;
            }
            if (IsExceptional(fallThrough?.Semantics) || IsExceptional(conditional?.Semantics))
            {
                // The branch value is the throw operand for exceptional
                // terminators. Lower it before abandoning control-flow
                // modeling so calls, reads, and other observable evaluation
                // effects are not silently omitted from the trace.
                if (source.BranchValue != null)
                {
                    _ = LowerValue(block, operation, source.BranchValue);
                }
                Abstain(operation, FrontendAbstention.UnsupportedControlFlow);
                Havoc(block, operation, IrHavocKind.Memory);
                _builder.Return(block, operation);
                return;
            }

            if (source.ConditionKind != ControlFlowConditionKind.None && source.BranchValue != null &&
                conditional?.Destination != null && fallThrough?.Destination != null)
            {
                var condition = LowerValue(block, operation, source.BranchValue);
                if (condition.Type != _factory.BooleanType)
                {
                    Abstain(operation, FrontendAbstention.UnsupportedType);
                    condition = CreateHavocTemporary(block, operation, "condition", _factory.BooleanType);
                }
                var conditionalTarget = _blocks[conditional.Destination];
                var fallThroughTarget = _blocks[fallThrough.Destination];
                var branchWhenTrue = source.ConditionKind == ControlFlowConditionKind.WhenTrue;
                _builder.Branch(block, operation, condition,
                    branchWhenTrue ? conditionalTarget : fallThroughTarget,
                    branchWhenTrue ? fallThroughTarget : conditionalTarget);
                return;
            }

            var destination = fallThrough?.Destination ?? conditional?.Destination;
            if (destination != null)
            {
                if (fallThrough?.Semantics is not (null or ControlFlowBranchSemantics.Regular) ||
                    conditional?.Semantics is not (null or ControlFlowBranchSemantics.Regular))
                {
                    Abstain(operation, FrontendAbstention.UnsupportedControlFlow);
                }

                _builder.Goto(block, operation, _blocks[destination]);
                return;
            }

            Abstain(operation, FrontendAbstention.UnsupportedControlFlow);
            _builder.Return(block, operation);
        }

        private void AssignOrHavoc(
            IrBlockId block, OperationId operation, IrVarId target, IrTerm value)
        {
            if (_factory.GetVariableInfo(target).Type == value.Type)
            {
                _builder.Assign(block, operation, target, value);
                return;
            }
            Abstain(operation, FrontendAbstention.UnsupportedType);
            Havoc(block, operation, IrHavocKind.Variables, target);
        }

        private void LowerUnsupportedMutation(
            IrBlockId block,
            OperationId operation,
            IOperation target,
            IOperation? value = null)
        {
            var variable = _expressions.GetReferencedVariable(target);
            if (!variable.HasValue)
            {
                _ = LowerLocation(block, operation, target);
            }
            if (value != null)
            {
                _ = LowerValue(block, operation, value);
            }
            Abstain(operation, FrontendAbstention.UnsupportedMutation);
            HavocKnownState(block, operation);
        }

        private IrVariableTerm LowerUnsupportedMutationResult(
            IrBlockId block,
            OperationId operation,
            IOperation target,
            ITypeSymbol? type,
            IOperation? value = null)
        {
            LowerUnsupportedMutation(block, operation, target, value);
            return CreateHavocTemporary(
                block,
                operation,
                "mutation-result",
                _expressions.GetTypeId(type));
        }

        private void HavocKnownState(IrBlockId block, OperationId operation)
        {
            var variables = CreateKnownStateVariables();
            Havoc(block, operation,
                variables.Length == 0 ? IrHavocKind.Memory : IrHavocKind.VariablesAndMemory, variables);
        }

        private IrVarId[] CreateKnownStateVariables()
        {
            return [.. _expressions.CreateVariableBindings()
                .Select(static binding => binding.Variable)
                .Concat(_expressions.CreateCaptureBindings())];
        }

        private void LowerReturn(
            IrBlockId block, OperationId operation, IOperation? value)
        {
            _builder.Return(block, operation, LowerOptionalValue(block, operation, value));
        }

        private IrTerm? LowerOptionalValue(
            IrBlockId block, OperationId operation, IOperation? value)
        {
            return value == null ? null : LowerValue(block, operation, value);
        }

        private void Havoc(IrBlockId block, OperationId operation, IrHavocKind kind, params IrVarId[] variables)
        {
            _builder.Havoc(block, operation, kind, variables);
        }

        private IrVariableTerm CreateHavocTemporary(
            IrBlockId block, OperationId operation, string purpose, IrTypeId type)
        {
            var target = CreateTemporary(purpose, type);
            Havoc(block, operation, IrHavocKind.Variables, target);
            return _factory.Variable(target);
        }

        private IrVarId CreateTemporary(string purpose, IrTypeId type)
        {
            return _factory.CreateVariable(
                "temporary:" + purpose + ":" + (_nextTemporary++).ToString(CultureInfo.InvariantCulture), type);
        }

        private OperationId CreateOperation(
            BasicBlock block, int ordinal, OperationKind kind)
        {
            return _factory.CreateOperation("cfg:" + block.Ordinal.ToString(CultureInfo.InvariantCulture) +
                ":" + ordinal.ToString(CultureInfo.InvariantCulture) + ":" + kind);
        }

        private void Observe(OperationId operation, FrontendSubsetClassification classification)
        {
            if (!classification.IsExact)
            {
                Abstain(operation, classification.Abstention);
            }
        }

        private void Abstain(OperationId operation, FrontendAbstention reason)
        {
            if (reason == FrontendAbstention.None)
            {
                return;
            }

            if (_seenAbstentions.Add((operation.Value, reason)))
            {
                _abstentions.Add(new FrontendProgramAbstention(operation, reason));
            }
        }

        private static bool IsStaticallyBound(IMethodSymbol method)
        {
            return method.IsStatic ||
            !method.IsVirtual &&
            !method.IsAbstract &&
            !method.IsOverride;
        }

        private static bool IsClosureInvocation(IMethodSymbol method)
        {
            return method.MethodKind == MethodKind.LocalFunction ||
                method.ContainingType.TypeKind == TypeKind.Delegate;
        }

        private static bool IsExceptional(ControlFlowBranchSemantics? semantics)
        {
            return semantics is
                ControlFlowBranchSemantics.Throw or ControlFlowBranchSemantics.Rethrow or
                ControlFlowBranchSemantics.ProgramTermination or
                ControlFlowBranchSemantics.StructuredExceptionHandling or
                ControlFlowBranchSemantics.Error;
        }

        private static bool HasMandatoryFinally(ControlFlowBranch? branch)
        {
            return branch != null && !branch.FinallyRegions.IsDefaultOrEmpty;
        }

        private static bool IsInsideCatchHandler(BasicBlock block)
        {
            for (var region = block.EnclosingRegion;
                 region != null;
                 region = region.EnclosingRegion)
            {
                if (region.Kind is
                    ControlFlowRegionKind.Catch or
                    ControlFlowRegionKind.Filter or
                    ControlFlowRegionKind.FilterAndHandler)
                {
                    return true;
                }
            }
            return false;
        }

        private (
            BasicBlock[] Selected,
            BasicBlock? OmittedHandler)
            SelectBlocks()
        {
            var reachable = new HashSet<BasicBlock>();
            var pending = new Stack<BasicBlock>();
            pending.Push(_entry);
            while (pending.Count != 0)
            {
                var block = pending.Pop();
                if (!reachable.Add(block) || IsExceptional(block.FallThroughSuccessor?.Semantics) ||
                    IsExceptional(block.ConditionalSuccessor?.Semantics))
                {
                    continue;
                }

                if (block.FallThroughSuccessor?.Destination is { } fallThrough)
                {
                    pending.Push(fallThrough);
                }

                if (block.ConditionalSuccessor?.Destination is { } conditional)
                {
                    pending.Push(conditional);
                }
            }
            var blocks = _graph.Blocks;
            var ordinalOrder = true;
            for (var index = 0; index < blocks.Length; index++)
            {
                if (blocks[index].Ordinal != index)
                {
                    ordinalOrder = false;
                    break;
                }
            }

            IEnumerable<BasicBlock> orderedBlocks = ordinalOrder
                ? blocks
                : blocks.OrderBy(static block => block.Ordinal);
            var selected = new List<BasicBlock> { _entry };
            BasicBlock? omittedHandler = null;
            foreach (var block in orderedBlocks)
            {
                if (block != _entry && reachable.Contains(block))
                {
                    selected.Add(block);
                }

                if (omittedHandler == null &&
                    block.IsReachable &&
                    !reachable.Contains(block) &&
                    IsInsideCatchHandler(block))
                {
                    omittedHandler = block;
                }
            }

            return ([.. selected], omittedHandler);
        }

        private sealed class LocationLowering(
            IrLocation? location, FrontendAbstention abstention)
        {
            internal IrLocation? Location { get; } = location;
            internal FrontendAbstention Abstention { get; } = abstention;

            internal static LocationLowering FromLocation(IrLocation location)
            {
                return new(location, FrontendAbstention.None);
            }

            internal static LocationLowering Abstain(FrontendAbstention abstention)
            {
                return new(null, abstention);
            }
        }
    }
}

internal sealed class SelectedProgramLoweringResult(
    FrontendProgramLoweringResult lowering, ImmutableDictionary<IrCallInstruction, IInvocationOperation> calls)
{
    internal FrontendProgramLoweringResult Lowering { get; } = lowering;
    internal ImmutableDictionary<IrCallInstruction, IInvocationOperation> Calls { get; } = calls;
}
