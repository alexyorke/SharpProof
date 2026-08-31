namespace SharpProof.Worker;

internal sealed partial class AcyclicBlockPredicateExecutor
{
    private const int DefaultMaximumSymbolicOperations =
        CompilerPreparedBody.MaximumInstructions * 16;
    private readonly int _maximumExpressionDepth;
    private readonly int _maximumSymbolicOperations;

    internal AcyclicBlockPredicateExecutor(
        int maximumExpressionDepth,
        int maximumSymbolicOperations = DefaultMaximumSymbolicOperations)
    {
        _maximumExpressionDepth = ArgumentNullGuard.RequirePositive(
            maximumExpressionDepth, nameof(maximumExpressionDepth));
        _maximumSymbolicOperations = ArgumentNullGuard.RequirePositive(
            maximumSymbolicOperations, nameof(maximumSymbolicOperations));
    }

    internal SymbolicBodyExecution Execute(
        ImmutableArray<CompilerCanonicalVariable> variables,
        IrFactory factory, IrProgram program,
        ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall> specCalls,
        ImmutableDictionary<IrInstructionId, CompilerPreparedSummaryCall> summaryCalls,
        ImmutableDictionary<IrVarId, IrTerm> initialEnvironment,
        ImmutableDictionary<IrVarId, IrVarId> parameterBindings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(program);
        cancellationToken.ThrowIfCancellationRequested();
        return new Run(variables, factory, program, specCalls, summaryCalls, initialEnvironment,
            parameterBindings, _maximumExpressionDepth, _maximumSymbolicOperations,
            cancellationToken).Execute();
    }

    private sealed partial class Run(
        ImmutableArray<CompilerCanonicalVariable> variables,
        IrFactory factory, IrProgram program,
        ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall> specCalls,
        ImmutableDictionary<IrInstructionId, CompilerPreparedSummaryCall> summaryCalls,
        ImmutableDictionary<IrVarId, IrTerm> initialEnvironment,
        ImmutableDictionary<IrVarId, IrVarId> parameterBindings,
        int maximumExpressionDepth, int remainingOperations,
        CancellationToken cancellationToken)
    {
        private readonly Dictionary<IrBlockId, List<FlowState>> _incoming = [];
        private readonly ImmutableArray<SymbolicReturn>.Builder _returns = ImmutableArray.CreateBuilder<SymbolicReturn>();
        private readonly ImmutableDictionary<IrVarId, SpecResultProjection>.Builder _projections =
            ImmutableDictionary.CreateBuilder<IrVarId, SpecResultProjection>();
        private readonly ImmutableArray<GuardedBodySpecAssumption>.Builder _assumptions =
            ImmutableArray.CreateBuilder<GuardedBodySpecAssumption>();
        private readonly ImmutableArray<GuardedBodySummaryAssumption>.Builder _summaryAssumptions =
            ImmutableArray.CreateBuilder<GuardedBodySummaryAssumption>();
        private WorkerClaimReason _reason = WorkerClaimReason.None;

        internal SymbolicBodyExecution Execute()
        {
            cancellationToken.ThrowIfCancellationRequested();
            var order = CreateOrder();
            if (order.IsDefault)
            {
                return Failed();
            }

            foreach (var blockId in order)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var state = Merge(blockId);
                if (state == null)
                {
                    if (_reason != WorkerClaimReason.None)
                    {
                        return Failed();
                    }

                    continue;
                }
                if (!ExecuteBlock(program.GetBlock(blockId), state.Value))
                {
                    return Failed();
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return _returns.Count == 0 ? SymbolicBodyExecution.Failed(WorkerClaimReason.UnsupportedBody) :
                new SymbolicBodyExecution(WorkerClaimReason.None, _returns.ToImmutable(),
                    _projections.ToImmutable(), _assumptions.ToImmutable(),
                    _summaryAssumptions.ToImmutable());
        }

        private bool ExecuteBlock(IrBasicBlock block, FlowState state)
        {
            var environment = state.Environment;
            var predicate = state.Predicate;
            OperationId? expectedMemoryHavoc = null;
            for (var index = 0; index < block.Instructions.Length; index++)
            {
                if (!Spend())
                {
                    return false;
                }

                var instruction = block.Instructions[index];
                if (expectedMemoryHavoc is { } operation)
                {
                    expectedMemoryHavoc = null;
                    if (instruction is IrHavocInstruction
                        {
                            HavocKind: IrHavocKind.Memory,
                            Variables.IsEmpty: true
                        } havoc &&
                        havoc.Operation == operation)
                    {
                        continue;
                    }

                    return false;
                }
                switch (instruction)
                {
                    case IrAssignInstruction assign:
                        var assigned = Substitute(assign.Value, environment);
                        if (assigned == null)
                        {
                            return false;
                        }

                        var constrainedPredicate = ConstrainNormalExecution(
                            predicate,
                            assigned);
                        if (constrainedPredicate == null)
                        {
                            return false;
                        }

                        predicate = constrainedPredicate;
                        environment = environment.SetItem(assign.Target, assigned);
                        break;
                    case IrCallInstruction call:
                        SpecApplication? application = null;
                        if (specCalls.TryGetValue(call.Id, out var preparedSpec))
                        {
                            application = ApplySpec(
                                call,
                                preparedSpec,
                                environment,
                                predicate);
                        }
                        else if (summaryCalls.TryGetValue(
                                     call.Id,
                                     out var preparedSummary))
                        {
                            application = ApplySummary(
                                call,
                                preparedSummary,
                                environment,
                                predicate);
                        }

                        if (application == null)
                        {
                            return false;
                        }

                        environment = environment.SetItem(
                            call.Target!.Value,
                            application.Value.Result);
                        predicate = application.Value.Predicate;
                        expectedMemoryHavoc = application.Value.ConsumesMemoryHavoc
                            ? call.Operation
                            : null;
                        break;
                    case IrBranchInstruction branch:
                        return index == block.Instructions.Length - 1 &&
                            TransferBranch(block.Id, branch, predicate, environment);
                    case IrGotoInstruction go:
                        AddIncoming(go.Target, block.Id.Value << 1, predicate, environment);
                        return index == block.Instructions.Length - 1;
                    case IrReturnInstruction returned:
                        if (index != block.Instructions.Length - 1 || returned.Value == null)
                        {
                            return false;
                        }

                        var returnTerm = Substitute(returned.Value, environment);
                        if (returnTerm == null)
                        {
                            return false;
                        }

                        var currentStates = CreateCurrentStates(environment);
                        if (currentStates == null)
                        {
                            return false;
                        }

                        _returns.Add(new SymbolicReturn(predicate, returnTerm, currentStates));
                        return true;
                    default:
                        return false;
                }
            }
            return false;
        }

        private IrTerm? ConstrainNormalExecution(IrTerm predicate, IrTerm evaluated)
        {
            if (!IrSemanticTerms.RequiresDefinednessWitness(evaluated))
            {
                return predicate;
            }

            if (!Spend(2))
            {
                return null;
            }

            var constrained = IrSemanticTerms.ConstrainSuccessfulEvaluation(
                factory,
                predicate,
                evaluated);
            return Supported(constrained) ? constrained : null;
        }

        private bool TransferBranch(
            IrBlockId predecessor, IrBranchInstruction branch, IrTerm predicate,
            ImmutableDictionary<IrVarId, IrTerm> environment)
        {
            var condition = Substitute(branch.Condition, environment);
            if (condition == null || condition.Type != factory.BooleanType)
            {
                return false;
            }

            var constrainedPredicate = ConstrainNormalExecution(
                predicate,
                condition);
            if (constrainedPredicate == null)
            {
                return false;
            }

            predicate = constrainedPredicate;

            var order = predecessor.Value << 1;
            if (condition is IrBooleanTerm literal)
            {
                AddIncoming(literal.Value ? branch.WhenTrue : branch.WhenFalse,
                    order + (literal.Value ? 0 : 1), predicate, environment);
                return true;
            }
            if (!Spend(2))
            {
                return false;
            }

            var whenTrue = factory.Binary(
                IrBinaryOperator.AndAlso, predicate, condition);
            var whenFalse = factory.Binary(IrBinaryOperator.AndAlso, predicate,
                factory.Unary(IrUnaryOperator.Not, condition));
            if (!Supported(whenTrue) || !Supported(whenFalse))
            {
                return false;
            }

            AddIncoming(branch.WhenTrue, order, whenTrue, environment);
            AddIncoming(branch.WhenFalse, order + 1, whenFalse, environment);
            return true;
        }

        private FlowState? Merge(IrBlockId block)
        {
            if (block == program.Entry)
            {
                return new FlowState(0, factory.Boolean(true), initialEnvironment);
            }

            if (!_incoming.TryGetValue(block, out var values) || values.Count == 0)
            {
                return null;
            }

            values.Sort(static (left, right) => left.Order.CompareTo(right.Order));
            if (!Spend(values.Count))
            {
                return null;
            }

            var predicate = IrSemanticTerms.Disjoin(
                factory, values.Select(static value => value.Predicate).ToArray());
            if (!Supported(predicate))
            {
                return null;
            }

            var environment = ImmutableDictionary.CreateBuilder<IrVarId, IrTerm>();
            foreach (var variable in values[0].Environment.Keys.OrderBy(static value => value.Value))
            {
                if (!Spend(values.Count))
                {
                    return null;
                }

                if (values.Any(value => !value.Environment.ContainsKey(variable)))
                {
                    continue;
                }

                var first = values[0].Environment[variable];
                IrTerm merged = first;
                if (values.Any(value => value.Environment[variable].Id != first.Id))
                {
                    merged = values[^1].Environment[variable];
                    for (var index = values.Count - 2; index >= 0; index--)
                    {
                        merged = factory.Conditional(values[index].Predicate,
                            values[index].Environment[variable], merged);
                    }
                }
                if (!Supported(merged))
                {
                    return null;
                }

                environment.Add(variable, merged);
            }
            return new FlowState(0, predicate, environment.ToImmutable());
        }

        private SpecApplication? ApplySpec(
            IrCallInstruction call, CompilerPreparedSpecCall prepared,
            IReadOnlyDictionary<IrVarId, IrTerm> environment,
            IrTerm guard)
        {
            if (!call.Target.HasValue ||
                !ApiSpecTable.Default.TryGetByWitnessIdentifier(prepared.WitnessIdentifier, out var template) ||
                template.Target.DocumentationCommentId != prepared.CallIdentity ||
                !template.Result.HasValue ||
                prepared.ConsumesMemoryHavoc != (template.Facets.Effects.Effects != SpecEffect.None) ||
                !IsResultType(template.Target.ResultType, factory.GetVariableInfo(call.Target.Value).Type) ||
                call.Arguments.Length != template.Parameters.Length ||
                template.Receiver.HasValue != (call.Receiver != null))
            {
                return null;
            }

            var substitutions = new Dictionary<SpecVarId, IrTerm>();
            if (template.Receiver.HasValue)
            {
                var receiver = Substitute(call.Receiver!, environment);
                if (receiver == null)
                {
                    return null;
                }

                if (ConstrainNormalExecution(guard, receiver) is not { } receiverGuard)
                {
                    return null;
                }

                guard = receiverGuard;
                substitutions.Add(template.Receiver.Value, receiver);
            }
            for (var index = 0; index < call.Arguments.Length; index++)
            {
                var argument = Substitute(call.Arguments[index], environment);
                if (argument == null)
                {
                    return null;
                }

                if (ConstrainNormalExecution(guard, argument) is not { } argumentGuard)
                {
                    return null;
                }

                guard = argumentGuard;
                substitutions.Add(template.Parameters[index], argument);
            }
            var resultVariable = factory.CreateVariable(
                "spec-call-result:" +
                call.Id.Value.ToString(CultureInfo.InvariantCulture),
                factory.GetVariableInfo(call.Target.Value).Type);
            var result = factory.Variable(resultVariable);
            substitutions.Add(template.Result.Value, result);
            if (!SpecResultDomainProjection.TryCreate(
                    factory, template, resultVariable, out var projection,
                    out var facetPredicates))
            {
                return null;
            }

            if (projection != default &&
                _projections.TryGetValue(resultVariable, out var existing) &&
                existing != projection)
            {
                return null;
            }

            var instantiated = ApiSpecInstantiator.InstantiatePostconditions(template, factory, substitutions);
            if (instantiated.Status != SpecInstantiationStatus.Succeeded)
            {
                return null;
            }

            var projectionMap = projection == default
                ? ImmutableDictionary<IrVarId, SpecResultProjection>.Empty
                : ImmutableDictionary<IrVarId, SpecResultProjection>.Empty.Add(
                    resultVariable,
                    projection);
            var predicates = instantiated.Postconditions
                .Select(predicate => SpecResultDomainProjection.Rewrite(factory, predicate, projectionMap))
                .Concat(facetPredicates)
                .ToArray();
            if (predicates.Length == 0 || predicates.Any(predicate => !Supported(predicate)))
            {
                return null;
            }

            if (projection != default)
            {
                _projections[resultVariable] = projection;
            }

            _assumptions.AddRange(predicates.Select(predicate => new GuardedBodySpecAssumption(
                template.Id, template.Target.WitnessIdentifier, guard, predicate)));
            return new SpecApplication(result, guard, prepared.ConsumesMemoryHavoc);
        }

        private SpecApplication? ApplySummary(
            IrCallInstruction call,
            CompilerPreparedSummaryCall prepared,
            ImmutableDictionary<IrVarId, IrTerm> environment,
            IrTerm guard)
        {
            if (!call.Target.HasValue ||
                prepared.Instruction != call.Id ||
                !Enum.IsDefined(prepared.Origin) ||
                factory.GetVariableInfo(call.Target.Value).Type !=
                factory.GetVariableInfo(prepared.Result).Type ||
                !WorkerProtocolJson.IsSha256(prepared.EvidenceSha256))
            {
                return null;
            }

            if (call.Receiver != null)
            {
                var receiver = Substitute(call.Receiver, environment);
                if (receiver == null ||
                    ConstrainNormalExecution(
                        guard,
                        receiver) is not { } receiverGuard)
                {
                    return null;
                }

                guard = receiverGuard;
            }

            foreach (var argumentTerm in call.Arguments)
            {
                var argument = Substitute(argumentTerm, environment);
                if (argument == null ||
                    ConstrainNormalExecution(
                        guard,
                        argument) is not { } argumentGuard)
                {
                    return null;
                }

                guard = argumentGuard;
            }

            var freeVariables = new HashSet<IrVarId>(
                prepared.ExistentialVariables)
            {
                prepared.Result
            };
            if (freeVariables.Count !=
                    prepared.ExistentialVariables.Length + 1 ||
                freeVariables.Overlaps(environment.Keys) ||
                environment.Values.Any(value =>
                    freeVariables.Overlaps(
                        IrTermAnalysis.CollectVariables(value))) ||
                Substitute(
                    prepared.NormalRelation,
                    environment,
                    freeVariables) is not { } relation ||
                relation.Type != factory.BooleanType)
            {
                return null;
            }

            _summaryAssumptions.Add(new GuardedBodySummaryAssumption(
                prepared.CallIdentity,
                prepared.Origin,
                prepared.EvidenceSha256,
                prepared.EvidenceIdentity,
                prepared.DependencyEvidence,
                guard,
                relation));
            return new SpecApplication(
                factory.Variable(prepared.Result),
                guard,
                ConsumesMemoryHavoc: false);
        }

        private ImmutableDictionary<IrVarId, IrTerm>? CreateCurrentStates(
            ImmutableDictionary<IrVarId, IrTerm> environment)
        {
            if (!Spend(variables.Length + parameterBindings.Count))
            {
                return null;
            }

            var states = variables
                .Where(static variable => variable.Role == CompilerVariableRole.Parameter)
                .ToImmutableDictionary(static variable => variable.Variable,
                    variable => (IrTerm)factory.Variable(variable.Variable)).ToBuilder();
            foreach (var binding in parameterBindings)
            {
                if (environment.TryGetValue(binding.Key, out var value))
                {
                    states[binding.Value] = value;
                }
            }

            return states.ToImmutable();
        }

        private ImmutableArray<IrBlockId> CreateOrder()
        {
            var active = new HashSet<IrBlockId>();
            var complete = new HashSet<IrBlockId>();
            var pending = new Stack<(IrBlockId Block, bool Exit)>();
            var result = new List<IrBlockId>();
            pending.Push((program.Entry, false));
            while (pending.Count != 0)
            {
                if (!Spend())
                {
                    return default;
                }

                var frame = pending.Pop();
                if (frame.Exit)
                {
                    active.Remove(frame.Block);
                    if (complete.Add(frame.Block))
                    {
                        result.Add(frame.Block);
                    }

                    continue;
                }
                if (complete.Contains(frame.Block))
                {
                    continue;
                }

                if (!active.Add(frame.Block))
                {
                    return default;
                }

                pending.Push((frame.Block, true));
                switch (program.GetBlock(frame.Block).Terminator)
                {
                    case IrBranchInstruction branch:
                        pending.Push((branch.WhenFalse, false));
                        pending.Push((branch.WhenTrue, false));
                        break;
                    case IrGotoInstruction go:
                        pending.Push((go.Target, false));
                        break;
                    case IrReturnInstruction:
                        break;
                    default:
                        return default;
                }
            }
            result.Reverse();
            return [.. result];
        }

        private void AddIncoming(
            IrBlockId block, int order, IrTerm predicate,
            ImmutableDictionary<IrVarId, IrTerm> environment)
        {
            if (predicate is IrBooleanTerm { Value: false })
            {
                return;
            }

            if (!_incoming.TryGetValue(block, out var values))
            {
                _incoming.Add(block, values = []);
            }

            values.Add(new FlowState(order, predicate, environment));
        }

        private bool IsResultType(IrTypeKind? specType, IrTypeId resultType)
        {
            if (specType is not (
                IrTypeKind.Boolean or
                IrTypeKind.Integer or
                IrTypeKind.String or
                IrTypeKind.Sequence))
            {
                return false;
            }

            return factory.GetTypeInfo(resultType).Kind == specType.Value;
        }

        private IrTerm? Substitute(
            IrTerm term,
            IReadOnlyDictionary<IrVarId, IrTerm> environment,
            HashSet<IrVarId>? freeVariables = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IrTraversal.CollectVariables(term).All(variable =>
                    environment.ContainsKey(variable) ||
                    freeVariables?.Contains(variable) == true))
            {
                return null;
            }

            try
            {
                var result = IrSubstitution.Substitute(factory, term, environment);
                cancellationToken.ThrowIfCancellationRequested();
                return Supported(result) ? result : null;
            }
            catch (ArgumentException) { return null; }
        }

        private bool Supported(IrTerm term)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return IrTermAnalysis.GetDepth(term) <= maximumExpressionDepth;
        }

        private bool Spend(int amount = 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (amount <= remainingOperations)
            {
                remainingOperations -= amount;
                return true;
            }
            _reason = WorkerClaimReason.ResourceLimit;
            return false;
        }

        private SymbolicBodyExecution Failed()
        {
            return SymbolicBodyExecution.Failed(
            _reason == WorkerClaimReason.None ? WorkerClaimReason.UnsupportedBody : _reason);
        }

    }
}

internal sealed partial record SymbolicBodyExecution
{
    internal bool IsSuccess => Reason == WorkerClaimReason.None;
    internal static SymbolicBodyExecution Failed(WorkerClaimReason reason)
    {
        return new(
            reason,
            [],
            ImmutableDictionary<IrVarId, SpecResultProjection>.Empty,
            [],
            []);
    }
}
