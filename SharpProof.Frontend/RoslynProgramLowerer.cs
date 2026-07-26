namespace SharpProof.Frontend;

public sealed class RoslynProgramLowerer(
    IrFactory factory,
    Func<IMethodSymbol, bool>? isKnownPure = null) {
    private readonly IrFactory _factory =
        factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly Func<IMethodSymbol, bool> _isKnownPure =
        isKnownPure ?? (static _ => false);

    public FrontendProgramLoweringResult Lower(ControlFlowGraph graph) {
        if (graph == null) throw new ArgumentNullException(nameof(graph));
        return new LoweringSession(
            _factory,
            graph,
            _isKnownPure).Lower();
    }

    private sealed class LoweringSession {
        private readonly IrFactory _factory;
        private readonly ControlFlowGraph _graph;
        private readonly IrProgramBuilder _builder;
        private readonly RoslynOperationLowerer _expressions;
        private readonly Func<IMethodSymbol, bool> _isKnownPure;
        private readonly Dictionary<BasicBlock, IrBlockId> _blocks = [];
        private readonly List<FrontendProgramAbstention> _abstentions = [];
        private readonly HashSet<(int Operation, FrontendAbstention Reason)>
            _seenAbstentions = [];
        private int _nextTemporary;

        internal LoweringSession(
            IrFactory factory,
            ControlFlowGraph graph,
            Func<IMethodSymbol, bool> isKnownPure) {
            _factory = factory;
            _graph = graph;
            _builder = new IrProgramBuilder(factory);
            _expressions = new RoslynOperationLowerer(
                factory,
                isKnownPure);
            _isKnownPure = isKnownPure;
        }

        internal FrontendProgramLoweringResult Lower() {
            foreach (var block in _graph.Blocks.OrderBy(static block => block.Ordinal)) {
                _blocks.Add(
                    block,
                    _builder.CreateBlock(
                        "cfg:" +
                        block.Ordinal.ToString(CultureInfo.InvariantCulture) +
                        ":" +
                        block.Kind));
            }
            _builder.SetEntry(_blocks[_graph.Blocks[0]]);
            foreach (var block in _graph.Blocks.OrderBy(static block => block.Ordinal))
                LowerBlock(block);

            var firstReason = _abstentions.Count == 0
                ? FrontendAbstention.None
                : _abstentions[0].Reason;
            return new FrontendProgramLoweringResult(
                _builder.Build(),
                firstReason == FrontendAbstention.None
                    ? FrontendSubsetClassification.Exact
                    : FrontendSubsetClassification.Abstain(firstReason),
                _expressions.CreateVariableBindings(),
                _expressions.CreateCaptureBindings(),
                [.. _abstentions]);
        }

        private void LowerBlock(BasicBlock source) {
            var block = _blocks[source];
            var terminated = false;
            var operationOrdinal = 0;
            foreach (var operation in source.Operations) {
                var identity = CreateOperation(
                    source,
                    operationOrdinal++,
                    operation.Kind);
                if (LowerStatement(block, identity, operation)) {
                    terminated = true;
                    break;
                }
            }
            if (!terminated)
                LowerTerminator(
                    source,
                    block,
                    CreateOperation(
                        source,
                        operationOrdinal,
                        OperationKind.None));
        }

        private bool LowerStatement(
            IrBlockId block,
            OperationId operation,
            IOperation statement) {
            switch (statement) {
                case IVariableDeclarationGroupOperation group:
                    foreach (var declaration in group.Declarations)
                        LowerDeclaration(block, operation, declaration);
                    return false;
                case IVariableDeclarationOperation declaration:
                    LowerDeclaration(block, operation, declaration);
                    return false;
                case IVariableDeclaratorOperation declarator:
                    LowerDeclarator(block, operation, declarator);
                    return false;
                case IExpressionStatementOperation expression:
                    return LowerStatement(
                        block,
                        operation,
                        expression.Operation);
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
                    _builder.Return(
                        block,
                        operation,
                        returned.ReturnedValue == null
                            ? null
                            : LowerValue(
                                block,
                                operation,
                                returned.ReturnedValue));
                    return true;
                case IEmptyOperation:
                    return false;
                case IIncrementOrDecrementOperation mutation:
                    Abstain(operation, FrontendAbstention.UnsupportedMutation);
                    HavocTarget(block, operation, mutation.Target);
                    return false;
                case ICompoundAssignmentOperation mutation:
                    Abstain(operation, FrontendAbstention.UnsupportedMutation);
                    HavocTarget(block, operation, mutation.Target);
                    return false;
                default:
                    Abstain(operation, FrontendAbstention.UnsupportedStatement);
                    HavocKnownState(block, operation);
                    return false;
            }
        }

        private void LowerDeclaration(
            IrBlockId block,
            OperationId operation,
            IVariableDeclarationOperation declaration) {
            foreach (var declarator in declaration.Declarators)
                LowerDeclarator(block, operation, declarator);
        }

        private void LowerDeclarator(
            IrBlockId block,
            OperationId operation,
            IVariableDeclaratorOperation declarator) {
            var target = _expressions.GetVariable(
                declarator.Symbol,
                declarator.Symbol.Type);
            if (declarator.Initializer == null) {
                _builder.Havoc(
                    block,
                    operation,
                    IrHavocKind.Variables,
                    target.Variable);
                return;
            }
            var value = LowerValue(
                block,
                operation,
                declarator.Initializer.Value);
            AssignOrHavoc(block, operation, target.Variable, value);
        }

        private void LowerCapture(
            IrBlockId block,
            OperationId operation,
            IFlowCaptureOperation capture) {
            var target = _expressions.GetCapture(
                capture.Id,
                capture.Value.Type);
            var value = LowerValue(block, operation, capture.Value);
            AssignOrHavoc(block, operation, target.Variable, value);
        }

        private void LowerAssignment(
            IrBlockId block,
            OperationId operation,
            ISimpleAssignmentOperation assignment) {
            var value = LowerValue(block, operation, assignment.Value);
            switch (assignment.Target) {
                case ILocalReferenceOperation local:
                    AssignOrHavoc(
                        block,
                        operation,
                        _expressions.GetVariable(local.Local, local.Type).Variable,
                        value);
                    return;
                case IParameterReferenceOperation parameter:
                    AssignOrHavoc(
                        block,
                        operation,
                        _expressions.GetVariable(
                            parameter.Parameter,
                            parameter.Type).Variable,
                        value);
                    return;
                case IFlowCaptureReferenceOperation capture:
                    AssignOrHavoc(
                        block,
                        operation,
                        _expressions.GetCapture(capture.Id, capture.Type).Variable,
                        value);
                    return;
            }

            var location = LowerLocation(
                block,
                operation,
                assignment.Target);
            if (location.Location == null) {
                Abstain(operation, location.Abstention);
                _builder.Havoc(
                    block,
                    operation,
                    IrHavocKind.Memory);
                return;
            }
            if (location.Location.Type != value.Type) {
                Abstain(operation, FrontendAbstention.UnsupportedType);
                _builder.Havoc(
                    block,
                    operation,
                    IrHavocKind.Memory);
                return;
            }
            _builder.Store(block, operation, location.Location, value);
        }

        private IrTerm LowerValue(
            IrBlockId block,
            OperationId operation,
            IOperation value) {
            switch (value) {
                case IInvocationOperation invocation:
                    return LowerInvocation(
                        block,
                        operation,
                        invocation,
                        wantsResult: true)!;
                case IFieldReferenceOperation:
                case IPropertyReferenceOperation property
                    when !IsIntrinsicLength(property):
                case IArrayElementReferenceOperation:
                    var location = LowerLocation(block, operation, value);
                    if (location.Location != null) {
                        var target = CreateTemporary(
                            "load",
                            location.Location.Type);
                        _builder.Load(
                            block,
                            operation,
                            target,
                            location.Location);
                        return _factory.Variable(target);
                    }
                    Abstain(operation, location.Abstention);
                    break;
            }

            var lowered = _expressions.Lower(value);
            Observe(operation, lowered.Classification);
            return lowered.Term;
        }

        private IrVariableTerm? LowerInvocation(
            IrBlockId block,
            OperationId operation,
            IInvocationOperation invocation,
            bool wantsResult) {
            var receiver = invocation.Instance == null
                ? null
                : LowerValue(block, operation, invocation.Instance);
            var arguments = invocation.Arguments
                .Select(argument => LowerValue(
                    block,
                    operation,
                    argument.Value))
                .ToArray();
            var resultType = _expressions.GetTypeId(invocation.Type);
            var declaringType = receiver?.Type ??
                _expressions.GetTypeId(
                    invocation.TargetMethod.ContainingType);
            var member = _factory.GetOrCreateMember(
                CompilerIdentityBridge.InternSymbol(
                    _factory,
                    invocation.TargetMethod),
                declaringType,
                "call:" +
                CompilerIdentityBridge.CreateSymbolDisplay(
                    invocation.TargetMethod),
                resultType,
                receiver == null,
                [.. arguments.Select(static argument => argument.Type)]);
            var isDirect = IsDirectInvocation(invocation);
            if (!isDirect)
                Abstain(
                    operation,
                    FrontendAbstention.UnsupportedInvocationShape);

            IrVarId? target = null;
            if (wantsResult && !invocation.TargetMethod.ReturnsVoid)
                target = CreateTemporary("call", resultType);
            _builder.Call(
                block,
                operation,
                target,
                member,
                receiver,
                arguments);

            var mutated = invocation.Arguments
                .Where(static argument =>
                    argument.Parameter?.RefKind is
                        RefKind.Ref or RefKind.Out)
                .Select(argument => GetReferencedVariable(argument.Value))
                .Where(static variable => variable.HasValue)
                .Select(static variable => variable!.Value)
                .Distinct()
                .OrderBy(static variable => variable.Value)
                .ToArray();
            if (mutated.Length != 0 ||
                !isDirect ||
                !IsStaticallyBound(invocation.TargetMethod) ||
                !_isKnownPure(invocation.TargetMethod))
                _builder.Havoc(
                    block,
                    operation,
                    mutated.Length == 0
                        ? IrHavocKind.Memory
                        : IrHavocKind.VariablesAndMemory,
                    mutated);

            if (target.HasValue) return _factory.Variable(target.Value);
            if (wantsResult) {
                Abstain(operation, FrontendAbstention.UnsupportedType);
                var missing = CreateTemporary("void-call", _factory.ObjectType);
                _builder.Havoc(
                    block,
                    operation,
                    IrHavocKind.Variables,
                    missing);
                return _factory.Variable(missing);
            }
            return null;
        }

        private IrVarId? GetReferencedVariable(IOperation operation) {
            while (operation is IConversionOperation conversion)
                operation = conversion.Operand;
            return operation switch {
                ILocalReferenceOperation local =>
                    _expressions.GetVariable(
                        local.Local,
                        local.Type).Variable,
                IParameterReferenceOperation parameter =>
                    _expressions.GetVariable(
                        parameter.Parameter,
                        parameter.Type).Variable,
                IFlowCaptureReferenceOperation capture =>
                    _expressions.GetCapture(
                        capture.Id,
                        capture.Type).Variable,
                _ => null
            };
        }

        private LocationLowering LowerLocation(
            IrBlockId block,
            OperationId operation,
            IOperation target) {
            try {
                switch (target) {
                    case IFieldReferenceOperation field:
                        var fieldReceiver = field.Instance == null
                            ? null
                            : LowerValue(block, operation, field.Instance);
                        var fieldMember = _factory.GetOrCreateMember(
                            CompilerIdentityBridge.InternSymbol(
                                _factory,
                                field.Field),
                            fieldReceiver?.Type ??
                            _expressions.GetTypeId(field.Field.ContainingType),
                            "field:" +
                            CompilerIdentityBridge.CreateSymbolDisplay(
                                field.Field),
                            _expressions.GetTypeId(field.Type),
                            fieldReceiver == null);
                        return LocationLowering.FromLocation(
                            _builder.MemberLocation(
                                fieldMember,
                                fieldReceiver));
                    case IPropertyReferenceOperation property:
                        var propertyReceiver = property.Instance == null
                            ? null
                            : LowerValue(block, operation, property.Instance);
                        var propertyArguments = property.Arguments
                            .Select(argument => LowerValue(
                                block,
                                operation,
                                argument.Value))
                            .ToArray();
                        var propertyMember = _factory.GetOrCreateMember(
                            CompilerIdentityBridge.InternSymbol(
                                _factory,
                                property.Property),
                            propertyReceiver?.Type ??
                            _expressions.GetTypeId(
                                property.Property.ContainingType),
                            "property:" +
                            CompilerIdentityBridge.CreateSymbolDisplay(
                                property.Property),
                            _expressions.GetTypeId(property.Type),
                            propertyReceiver == null,
                            [.. propertyArguments.Select(
                                static argument => argument.Type)]);
                        return LocationLowering.FromLocation(
                            _builder.MemberLocation(
                                propertyMember,
                                propertyReceiver,
                                propertyArguments));
                    case IArrayElementReferenceOperation element
                        when element.Indices.Length == 1:
                        return LocationLowering.FromLocation(
                            _builder.SequenceLocation(
                                LowerValue(
                                    block,
                                    operation,
                                    element.ArrayReference),
                                LowerValue(
                                    block,
                                    operation,
                                    element.Indices[0])));
                    case IArrayElementReferenceOperation:
                        return LocationLowering.Abstain(
                            FrontendAbstention.UnsupportedMemberAccess);
                    default:
                        return LocationLowering.Abstain(
                            FrontendAbstention.UnsupportedMutation);
                }
            }
            catch (ArgumentException) {
                return LocationLowering.Abstain(
                    FrontendAbstention.UnsupportedType);
            }
        }

        private void LowerTerminator(
            BasicBlock source,
            IrBlockId block,
            OperationId operation) {
            if (source.Kind == BasicBlockKind.Exit) {
                _builder.Return(block, operation);
                return;
            }

            var fallThrough = source.FallThroughSuccessor;
            var conditional = source.ConditionalSuccessor;
            if (fallThrough?.Semantics == ControlFlowBranchSemantics.Return) {
                _builder.Return(
                    block,
                    operation,
                    source.BranchValue == null
                        ? null
                        : LowerValue(
                            block,
                            operation,
                            source.BranchValue));
                return;
            }
            if (IsExceptional(fallThrough?.Semantics) ||
                IsExceptional(conditional?.Semantics)) {
                Abstain(
                    operation,
                    FrontendAbstention.UnsupportedControlFlow);
                _builder.Havoc(
                    block,
                    operation,
                    IrHavocKind.Memory);
                _builder.Return(block, operation);
                return;
            }

            if (source.ConditionKind != ControlFlowConditionKind.None &&
                source.BranchValue != null &&
                conditional?.Destination != null &&
                fallThrough?.Destination != null) {
                var condition = LowerValue(
                    block,
                    operation,
                    source.BranchValue);
                if (condition.Type != _factory.BooleanType) {
                    Abstain(
                        operation,
                        FrontendAbstention.UnsupportedType);
                    var unknown = CreateTemporary(
                        "condition",
                        _factory.BooleanType);
                    _builder.Havoc(
                        block,
                        operation,
                        IrHavocKind.Variables,
                        unknown);
                    condition = _factory.Variable(unknown);
                }
                var conditionalTarget = _blocks[conditional.Destination];
                var fallThroughTarget = _blocks[fallThrough.Destination];
                _builder.Branch(
                    block,
                    operation,
                    condition,
                    source.ConditionKind == ControlFlowConditionKind.WhenTrue
                        ? conditionalTarget
                        : fallThroughTarget,
                    source.ConditionKind == ControlFlowConditionKind.WhenTrue
                        ? fallThroughTarget
                        : conditionalTarget);
                return;
            }

            var destination =
                fallThrough?.Destination ??
                conditional?.Destination;
            if (destination != null) {
                if (fallThrough?.Semantics is not (
                    null or ControlFlowBranchSemantics.Regular) ||
                    conditional?.Semantics is not (
                        null or ControlFlowBranchSemantics.Regular))
                    Abstain(
                        operation,
                        FrontendAbstention.UnsupportedControlFlow);
                _builder.Goto(block, operation, _blocks[destination]);
                return;
            }

            Abstain(
                operation,
                FrontendAbstention.UnsupportedControlFlow);
            _builder.Return(block, operation);
        }

        private void AssignOrHavoc(
            IrBlockId block,
            OperationId operation,
            IrVarId target,
            IrTerm value) {
            if (_factory.GetVariableInfo(target).Type == value.Type) {
                _builder.Assign(block, operation, target, value);
                return;
            }
            Abstain(operation, FrontendAbstention.UnsupportedType);
            _builder.Havoc(
                block,
                operation,
                IrHavocKind.Variables,
                target);
        }

        private void HavocTarget(
            IrBlockId block,
            OperationId operation,
            IOperation target) {
            var variable = GetReferencedVariable(target);
            if (variable.HasValue) {
                _builder.Havoc(
                    block,
                    operation,
                    IrHavocKind.Variables,
                    variable.Value);
                return;
            }
            _builder.Havoc(
                block,
                operation,
                IrHavocKind.Memory);
        }

        private void HavocKnownState(
            IrBlockId block,
            OperationId operation) {
            var variables = _expressions.CreateVariableBindings()
                .Select(static binding => binding.Variable)
                .Concat(_expressions.CreateCaptureBindings())
                .Distinct()
                .OrderBy(static variable => variable.Value)
                .ToArray();
            _builder.Havoc(
                block,
                operation,
                variables.Length == 0
                    ? IrHavocKind.Memory
                    : IrHavocKind.VariablesAndMemory,
                variables);
        }

        private IrVarId CreateTemporary(string purpose, IrTypeId type) =>
            _factory.CreateVariable(
                "temporary:" +
                purpose +
                ":" +
                (_nextTemporary++).ToString(CultureInfo.InvariantCulture),
                type);

        private OperationId CreateOperation(
            BasicBlock block,
            int ordinal,
            OperationKind kind) =>
            _factory.CreateOperation(
                "cfg:" +
                block.Ordinal.ToString(CultureInfo.InvariantCulture) +
                ":" +
                ordinal.ToString(CultureInfo.InvariantCulture) +
                ":" +
                kind);

        private void Observe(
            OperationId operation,
            FrontendSubsetClassification classification) {
            if (!classification.IsExact)
                Abstain(operation, classification.Abstention);
        }

        private void Abstain(
            OperationId operation,
            FrontendAbstention reason) {
            if (reason == FrontendAbstention.None) return;
            if (_seenAbstentions.Add((operation.Value, reason)))
                _abstentions.Add(
                    new FrontendProgramAbstention(operation, reason));
        }

        private static bool IsIntrinsicLength(
            IPropertyReferenceOperation property) =>
            property.Instance != null &&
            property.Property.Name is "Length" or "LongLength" &&
            property.Arguments.IsDefaultOrEmpty &&
            (property.Instance.Type?.SpecialType ==
                SpecialType.System_String ||
             property.Instance.Type is IArrayTypeSymbol);

        private static bool IsDirectInvocation(
            IInvocationOperation invocation) {
            if (invocation.TargetMethod.ReducedFrom != null) return false;
            if (invocation.Arguments.Length !=
                invocation.TargetMethod.Parameters.Length)
                return false;
            for (var index = 0; index < invocation.Arguments.Length; index++) {
                var argument = invocation.Arguments[index];
                if (argument.ArgumentKind != ArgumentKind.Explicit ||
                    argument.Parameter?.Ordinal != index)
                    return false;
            }
            return true;
        }

        private static bool IsStaticallyBound(IMethodSymbol method) =>
            method.IsStatic ||
            !method.IsVirtual &&
            !method.IsAbstract &&
            !method.IsOverride;

        private static bool IsExceptional(
            ControlFlowBranchSemantics? semantics) =>
            semantics is
                ControlFlowBranchSemantics.Throw or
                ControlFlowBranchSemantics.Rethrow or
                ControlFlowBranchSemantics.ProgramTermination or
                ControlFlowBranchSemantics.StructuredExceptionHandling or
                ControlFlowBranchSemantics.Error;

        private sealed class LocationLowering {
            private LocationLowering(
                IrLocation? location,
                FrontendAbstention abstention) {
                Location = location;
                Abstention = abstention;
            }

            internal IrLocation? Location { get; }
            internal FrontendAbstention Abstention { get; }

            internal static LocationLowering FromLocation(
                IrLocation location) =>
                new(location, FrontendAbstention.None);

            internal static LocationLowering Abstain(
                FrontendAbstention abstention) =>
                new(null, abstention);
        }
    }
}
