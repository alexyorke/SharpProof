namespace SharpProof.Worker;

internal sealed class AcyclicBlockPredicateExecutor
{
    private const int DefaultMaximumSymbolicOperations =
        CompilerPreparedBody.MaximumInstructions * 16;
    private readonly int _maximumExpressionDepth;
    private readonly int _maximumSymbolicOperations;

    internal AcyclicBlockPredicateExecutor(
        int maximumExpressionDepth,
        int maximumSymbolicOperations = DefaultMaximumSymbolicOperations)
    {
        _maximumExpressionDepth = maximumExpressionDepth > 0
            ? maximumExpressionDepth
            : throw new ArgumentOutOfRangeException(nameof(maximumExpressionDepth));
        _maximumSymbolicOperations = maximumSymbolicOperations > 0
            ? maximumSymbolicOperations
            : throw new ArgumentOutOfRangeException(nameof(maximumSymbolicOperations));
    }

    internal SymbolicBodyExecution Execute(
        ImmutableArray<CompilerCanonicalVariable> variables,
        IrFactory factory, IrProgram program,
        ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall> specCalls,
        ImmutableDictionary<IrVarId, IrTerm> initialEnvironment,
        ImmutableDictionary<IrVarId, IrVarId> parameterBindings)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(program);
        return new Run(variables, factory, program, specCalls, initialEnvironment,
            parameterBindings, _maximumExpressionDepth, _maximumSymbolicOperations).Execute();
    }

    private sealed class Run(
        ImmutableArray<CompilerCanonicalVariable> variables,
        IrFactory factory, IrProgram program,
        ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall> specCalls,
        ImmutableDictionary<IrVarId, IrTerm> initialEnvironment,
        ImmutableDictionary<IrVarId, IrVarId> parameterBindings,
        int maximumExpressionDepth, int remainingOperations)
    {
        private readonly Dictionary<IrBlockId, List<FlowState>> _incoming = [];
        private readonly ImmutableArray<SymbolicReturn>.Builder _returns = ImmutableArray.CreateBuilder<SymbolicReturn>();
        private readonly ImmutableDictionary<IrVarId, SpecResultProjection>.Builder _projections =
            ImmutableDictionary.CreateBuilder<IrVarId, SpecResultProjection>();
        private readonly ImmutableArray<GuardedBodySpecAssumption>.Builder _assumptions =
            ImmutableArray.CreateBuilder<GuardedBodySpecAssumption>();
        private WorkerClaimReason _reason = WorkerClaimReason.None;

        internal SymbolicBodyExecution Execute()
        {
            var order = CreateOrder();
            if (order.IsDefault)
            {
                return Failed();
            }

            foreach (var blockId in order)
            {
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
            return _returns.Count == 0 ? SymbolicBodyExecution.Failed(WorkerClaimReason.UnsupportedBody) :
                new SymbolicBodyExecution(WorkerClaimReason.None, _returns.ToImmutable(),
                    _projections.ToImmutable(), _assumptions.ToImmutable());
        }

        private bool ExecuteBlock(IrBasicBlock block, FlowState state)
        {
            var environment = state.Environment;
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

                        environment = environment.SetItem(assign.Target, assigned);
                        break;
                    case IrCallInstruction call:
                        if (!specCalls.TryGetValue(call.Id, out var prepared) ||
                            ApplySpec(call, prepared, environment, state.Predicate) is not { } application)
                        {
                            return false;
                        }

                        environment = environment.SetItem(
                            call.Target!.Value, application.Result);
                        expectedMemoryHavoc = application.ConsumesMemoryHavoc ? call.Operation : null;
                        break;
                    case IrBranchInstruction branch:
                        return index == block.Instructions.Length - 1 &&
                            TransferBranch(block.Id, branch, state.Predicate, environment);
                    case IrGotoInstruction go:
                        AddIncoming(go.Target, block.Id.Value << 1, state.Predicate, environment);
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

                        _returns.Add(new SymbolicReturn(state.Predicate, returnTerm, currentStates));
                        return true;
                    default:
                        return false;
                }
            }
            return false;
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

            var predicate = SymbolicTermOperations.Disjoin(
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

                substitutions.Add(template.Receiver.Value, receiver);
            }
            for (var index = 0; index < call.Arguments.Length; index++)
            {
                var argument = Substitute(call.Arguments[index], environment);
                if (argument == null)
                {
                    return null;
                }

                substitutions.Add(template.Parameters[index], argument);
            }
            var result = factory.Variable(call.Target.Value);
            substitutions.Add(template.Result.Value, result);
            if (!SpecResultDomainProjection.TryCreate(
                    factory, template, call.Target.Value, out var projection,
                    out var facetPredicates))
            {
                return null;
            }

            if (projection != default &&
                _projections.TryGetValue(call.Target.Value, out var existing) &&
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
                : ImmutableDictionary<IrVarId, SpecResultProjection>.Empty.Add(call.Target.Value, projection);
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
                _projections[call.Target.Value] = projection;
            }

            _assumptions.AddRange(predicates.Select(predicate => new GuardedBodySpecAssumption(
                template.Id, template.Target.WitnessIdentifier, guard, predicate)));
            return new SpecApplication(result, prepared.ConsumesMemoryHavoc);
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

        private bool IsResultType(SpecValueType? specType, IrTypeId resultType)
        {
            return specType switch
            {
                SpecValueType.Boolean => resultType == factory.BooleanType,
                SpecValueType.Integer => resultType == factory.IntegerType,
                SpecValueType.String => resultType == factory.StringType,
                SpecValueType.Sequence => factory.GetTypeInfo(resultType).Kind == IrTypeKind.Sequence,
                _ => false
            };
        }

        private IrTerm? Substitute(
            IrTerm term,
            IReadOnlyDictionary<IrVarId, IrTerm> environment)
        {
            if (!IrTraversal.CollectVariables(term).All(environment.ContainsKey))
            {
                return null;
            }

            try
            {
                var result = IrSubstitution.Substitute(factory, term, environment);
                return Supported(result) ? result : null;
            }
            catch (ArgumentException) { return null; }
        }

        private bool Supported(IrTerm term)
        {
            return SymbolicTermOperations.GetDepth(term) <= maximumExpressionDepth;
        }

        private bool Spend(int amount = 1)
        {
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

        private readonly record struct FlowState(
            int Order, IrTerm Predicate,
            ImmutableDictionary<IrVarId, IrTerm> Environment);
        private readonly record struct SpecApplication(IrTerm Result, bool ConsumesMemoryHavoc);
    }
}

internal sealed record SymbolicBodyExecution(
    WorkerClaimReason Reason, ImmutableArray<SymbolicReturn> Returns,
    ImmutableDictionary<IrVarId, SpecResultProjection> SpecResultProjections,
    ImmutableArray<GuardedBodySpecAssumption> SpecAssumptions)
{
    internal bool IsSuccess => Reason == WorkerClaimReason.None;
    internal static SymbolicBodyExecution Failed(WorkerClaimReason reason)
    {
        return new(reason, [], ImmutableDictionary<IrVarId, SpecResultProjection>.Empty, []);
    }
}

internal readonly record struct SymbolicReturn(
    IrTerm Predicate, IrTerm? ReturnTerm,
    ImmutableDictionary<IrVarId, IrTerm> CurrentStates);

internal readonly record struct GuardedBodySpecAssumption(
    SpecId Spec, string WitnessIdentifier, IrTerm Guard, IrTerm Predicate);

internal static class SymbolicTermOperations
{
    internal static IrTerm Guard(IrFactory factory, IrTerm condition, IrTerm consequence)
    {
        return factory.Binary(IrBinaryOperator.OrElse,
            factory.Unary(IrUnaryOperator.Not, condition), consequence);
    }

    internal static IrTerm Conjoin(IrFactory factory, IReadOnlyList<IrTerm> terms)
    {
        return Combine(factory, terms, IrBinaryOperator.AndAlso, identity: true);
    }

    internal static IrTerm Disjoin(IrFactory factory, IReadOnlyList<IrTerm> terms)
    {
        return Combine(factory, terms, IrBinaryOperator.OrElse, identity: false);
    }

    private static IrTerm Combine(
        IrFactory factory, IReadOnlyList<IrTerm> terms,
        IrBinaryOperator @operator, bool identity)
    {
        if (terms.Count == 0)
        {
            return factory.Boolean(identity);
        }

        return Visit(0, terms.Count);

        IrTerm Visit(int start, int count)
        {
            if (count == 1)
            {
                return terms[start];
            }

            var leftCount = count / 2;
            return factory.Binary(@operator, Visit(start, leftCount),
                Visit(start + leftCount, count - leftCount));
        }
    }

    internal static int GetDepth(IrTerm root)
    {
        var memo = new Dictionary<IrId, int>();
        return Visit(root);

        int Visit(IrTerm term)
        {
            if (memo.TryGetValue(term.Id, out var existing))
            {
                return existing;
            }

            var children = IrTraversal.GetChildren(term);
            var depth = children.Length == 0 ? 1 : 1 + children.Max(Visit);
            memo.Add(term.Id, depth);
            return depth;
        }
    }
}
