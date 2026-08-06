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
        ImmutableDictionary<IrVarId, IrTerm> initialEnvironment,
        ImmutableDictionary<IrVarId, IrVarId> parameterBindings)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(program);
        return new Run(variables, factory, program, specCalls, initialEnvironment,
            parameterBindings, _maximumExpressionDepth, _maximumSymbolicOperations).Execute();
    }

    private sealed partial class Run(
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
                        if (!specCalls.TryGetValue(call.Id, out var prepared) ||
                            ApplySpec(call, prepared, environment, predicate) is not { } application)
                        {
                            return false;
                        }

                        environment = environment.SetItem(
                            call.Target!.Value, application.Result);
                        predicate = application.Predicate;
                        expectedMemoryHavoc = application.ConsumesMemoryHavoc ? call.Operation : null;
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
            if (!SymbolicTermOperations.RequiresDefinednessWitness(evaluated))
            {
                return predicate;
            }

            if (!Spend(2))
            {
                return null;
            }

            var constrained = SymbolicTermOperations.ConstrainSuccessfulEvaluation(
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
            return new SpecApplication(result, guard, prepared.ConsumesMemoryHavoc);
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

    }
}

internal sealed partial record SymbolicBodyExecution
{
    internal bool IsSuccess => Reason == WorkerClaimReason.None;
    internal static SymbolicBodyExecution Failed(WorkerClaimReason reason)
    {
        return new(reason, [], ImmutableDictionary<IrVarId, SpecResultProjection>.Empty, []);
    }
}

internal static class SymbolicTermOperations
{
    internal static bool RequiresDefinednessWitness(IrTerm? term)
    {
        return term is not (
            null or
            IrBooleanTerm or
            IrIntegerTerm or
            IrStringTerm or
            IrNullTerm or
            IrVariableTerm);
    }

    internal static IrTerm ConstrainSuccessfulEvaluation(
        IrFactory factory,
        IrTerm predicate,
        IrTerm? evaluated)
    {
        if (!RequiresDefinednessWitness(evaluated))
        {
            return predicate;
        }

        var successfulEvaluation = factory.Binary(
            IrBinaryOperator.Equal,
            evaluated!,
            evaluated!);
        return factory.Binary(
            IrBinaryOperator.AndAlso,
            predicate,
            successfulEvaluation);
    }

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

    /// <summary>
    /// Measures term depth with an explicit stack. This is the function that
    /// enforces the expression-depth budget, so it must not itself recurse to
    /// the full depth of the term it is about to reject.
    /// </summary>
    internal static int GetDepth(IrTerm root)
    {
        var memo = new Dictionary<IrId, int>();
        var pending = new Stack<(IrTerm Term, bool ChildrenReady)>();
        pending.Push((root, false));
        while (pending.Count != 0)
        {
            var (term, childrenReady) = pending.Pop();
            if (memo.ContainsKey(term.Id))
            {
                continue;
            }

            var children = IrTraversal.GetChildren(term);
            if (!childrenReady && children.Length != 0)
            {
                // Re-queue below the children so every child is memoised by the
                // time this term is popped again.
                pending.Push((term, true));
                foreach (var child in children)
                {
                    if (!memo.ContainsKey(child.Id))
                    {
                        pending.Push((child, false));
                    }
                }

                continue;
            }

            var depth = 1;
            foreach (var child in children)
            {
                depth = Math.Max(depth, 1 + memo[child.Id]);
            }

            memo.Add(term.Id, depth);
        }

        return memo[root.Id];
    }
}
