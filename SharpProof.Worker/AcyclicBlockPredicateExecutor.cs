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
        return new Run(
            new RunInputs(
                variables, factory, program, specCalls, summaryCalls,
                initialEnvironment, parameterBindings, cancellationToken),
            _maximumExpressionDepth,
            _maximumSymbolicOperations).Execute();
    }

    private readonly record struct RunInputs(
        ImmutableArray<CompilerCanonicalVariable> Variables,
        IrFactory Factory,
        IrProgram Program,
        ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall> SpecCalls,
        ImmutableDictionary<IrInstructionId, CompilerPreparedSummaryCall> SummaryCalls,
        ImmutableDictionary<IrVarId, IrTerm> InitialEnvironment,
        ImmutableDictionary<IrVarId, IrVarId> ParameterBindings,
        CancellationToken CancellationToken);

    private sealed partial class Run(
        RunInputs inputs,
        int maximumExpressionDepth,
        int remainingOperations)
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
        private IrLoopCut? _cut;
        private IrBlockId _current;
        private Dictionary<IrVarId, (long Minimum, long Maximum)?>? _ranges;
        // Conditions under which no reachable operation throws: each is
        // "path condition implies this operation completes normally".
        private readonly List<IrTerm> _normalCompletions = [];
        private bool _throwsUnmodeled;

        internal SymbolicBodyExecution Execute()
        {
            inputs.CancellationToken.ThrowIfCancellationRequested();
            var order = CreateOrder();
            if (order.IsDefault)
            {
                return Failed();
            }

            foreach (var blockId in order)
            {
                inputs.CancellationToken.ThrowIfCancellationRequested();
                _current = blockId;
                var state = Merge(blockId);
                if (state == null)
                {
                    if (_reason != WorkerClaimReason.None)
                    {
                        return Failed();
                    }

                    continue;
                }
                if (_cut!.Loops.TryGetValue(blockId, out var loop))
                {
                    state = CutLoop(blockId, loop, state.Value);
                    if (state == null)
                    {
                        return Failed();
                    }
                }
                if (!ExecuteBlock(inputs.Program.GetBlock(blockId), state.Value))
                {
                    return Failed();
                }
            }
            inputs.CancellationToken.ThrowIfCancellationRequested();
            // No returns means no path completes normally (every path throws
            // or loops); the verifier reports such proofs as vacuous.
            return new SymbolicBodyExecution(WorkerClaimReason.None, _returns.ToImmutable(),
                    _projections.ToImmutable(), _assumptions.ToImmutable(),
                    _summaryAssumptions.ToImmutable())
            {
                NoThrow = _throwsUnmodeled
                    ? null
                    : IrSemanticTerms.Conjoin(inputs.Factory, _normalCompletions)
            };
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
                    case IrAssumeInstruction { Condition: IrBooleanTerm { Value: false } }:
                        // The path does not complete normally (an uncaught
                        // throw); it contributes no return.
                        RequireNormalCompletion(predicate, inputs.Factory.Boolean(false));
                        return true;
                    case IrAssumeInstruction assume:
                        var assumed = Substitute(assume.Condition, environment);
                        if (assumed == null ||
                            assumed.Type != inputs.Factory.BooleanType ||
                            ConstrainNormalExecution(predicate, assumed) is not { } guarded ||
                            !Spend())
                        {
                            return false;
                        }

                        predicate = inputs.Factory.Binary(IrBinaryOperator.AndAlso, guarded, assumed);
                        if (!Supported(predicate))
                        {
                            return false;
                        }

                        break;
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
                        if (inputs.SpecCalls.TryGetValue(call.Id, out var preparedSpec))
                        {
                            application = ApplySpec(
                                call,
                                preparedSpec,
                                environment,
                                predicate);
                        }
                        else if (inputs.SummaryCalls.TryGetValue(
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
                        if (index != block.Instructions.Length - 1 ||
                            returned.Value == null && inputs.Variables.Any(static variable =>
                                variable.Role == CompilerVariableRole.Result))
                        {
                            return false;
                        }

                        // A void method's return carries no value; any
                        // placeholder works because no result variable reads it.
                        var returnTerm = returned.Value == null
                            ? inputs.Factory.Boolean(true)
                            : Substitute(returned.Value, environment);
                        if (returnTerm == null)
                        {
                            return false;
                        }

                        if (IrSemanticTerms.RequiresDefinednessWitness(returnTerm))
                        {
                            RequireNormalCompletion(
                                predicate,
                                inputs.Factory.Binary(IrBinaryOperator.Equal, returnTerm, returnTerm));
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
                inputs.Factory,
                predicate,
                evaluated);
            RequireNormalCompletion(
                predicate,
                inputs.Factory.Binary(IrBinaryOperator.Equal, evaluated, evaluated));
            return Supported(constrained) ? constrained : null;
        }

        private void RequireNormalCompletion(IrTerm predicate, IrTerm completes)
        {
            _normalCompletions.Add(IrSemanticTerms.Guard(inputs.Factory, predicate, completes));
        }

        private bool TransferBranch(
            IrBlockId predecessor, IrBranchInstruction branch, IrTerm predicate,
            ImmutableDictionary<IrVarId, IrTerm> environment)
        {
            var condition = Substitute(branch.Condition, environment);
            if (condition == null || condition.Type != inputs.Factory.BooleanType)
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

            var whenTrue = inputs.Factory.Binary(
                IrBinaryOperator.AndAlso, predicate, condition);
            var whenFalse = inputs.Factory.Binary(IrBinaryOperator.AndAlso, predicate,
                inputs.Factory.Unary(IrUnaryOperator.Not, condition));
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
            if (block == inputs.Program.Entry)
            {
                return new FlowState(0, inputs.Factory.Boolean(true), inputs.InitialEnvironment);
            }

            if (!_incoming.Remove(block, out var values) || values.Count == 0)
            {
                return null;
            }

            values.Sort(static (left, right) => left.Order.CompareTo(right.Order));
            if (!Spend(values.Count))
            {
                return null;
            }

            var predicate = IrSemanticTerms.Disjoin(
                inputs.Factory, values.Select(static value => value.Predicate).ToArray());
            if (!Supported(predicate))
            {
                _reason = WorkerClaimReason.UnsupportedBody;
                return null;
            }

            var environment = ImmutableDictionary.CreateBuilder<IrVarId, IrTerm>();
            var variables = values
                .SelectMany(static value => value.Environment.Keys)
                .Distinct()
                .OrderBy(static value => value.Value);
            foreach (var variable in variables)
            {
                if (!Spend(values.Count))
                {
                    return null;
                }

                // A variable some predecessors never assigned is never read
                // on those paths (C# definite assignment), so it takes a
                // fresh value there instead of being dropped.
                var incoming = new IrTerm[values.Count];
                IrTerm? undefined = null;
                for (var index = 0; index < values.Count; index++)
                {
                    incoming[index] = values[index].Environment.TryGetValue(variable, out var current)
                        ? current
                        : undefined ??= inputs.Factory.Variable(inputs.Factory.CreateVariable(
                            "merge-undefined:" +
                            block.Value.ToString(CultureInfo.InvariantCulture) + ":" +
                            variable.Value.ToString(CultureInfo.InvariantCulture),
                            inputs.Factory.GetVariableInfo(variable).Type));
                }

                var merged = incoming[^1];
                for (var index = values.Count - 2; index >= 0; index--)
                {
                    if (incoming[index].Id != merged.Id)
                    {
                        merged = inputs.Factory.Conditional(
                            values[index].Predicate, incoming[index], merged);
                    }
                }
                if (!Supported(merged))
                {
                    _reason = WorkerClaimReason.UnsupportedBody;
                    return null;
                }

                environment.Add(variable, merged);
            }
            return new FlowState(0, predicate, environment.ToImmutable());
        }

        // Loop cutting: every variable the loop may assign becomes a fresh
        // unknown at its header, so the one symbolic pass over the body
        // covers every iteration. A variable whose every assignment is a
        // C# integer narrowing keeps that range as an invariant.
        private FlowState? CutLoop(
            IrBlockId header,
            ImmutableArray<IrBlockId> loop,
            FlowState state)
        {
            var assigned = new HashSet<IrVarId>();
            foreach (var block in loop)
            {
                foreach (var instruction in inputs.Program.GetBlock(block).Instructions)
                {
                    if (!Spend())
                    {
                        return null;
                    }

                    switch (instruction)
                    {
                        case IrAssignInstruction assign:
                            assigned.Add(assign.Target);
                            break;
                        case IrCallInstruction { Target: { } target }:
                            assigned.Add(target);
                            break;
                        case IrHavocInstruction havoc:
                            assigned.UnionWith(havoc.Variables);
                            break;
                    }
                }
            }

            var environment = state.Environment.ToBuilder();
            var predicate = state.Predicate;
            foreach (var variable in assigned.OrderBy(static variable => variable.Value))
            {
                if (!Spend(3))
                {
                    return null;
                }

                var fresh = inputs.Factory.Variable(inputs.Factory.CreateVariable(
                    "loop-havoc:" +
                    header.Value.ToString(CultureInfo.InvariantCulture) + ":" +
                    variable.Value.ToString(CultureInfo.InvariantCulture),
                    inputs.Factory.GetVariableInfo(variable).Type));
                environment[variable] = fresh;
                if (IntegerRange(variable) is { } range)
                {
                    predicate = inputs.Factory.Binary(
                        IrBinaryOperator.AndAlso,
                        predicate,
                        inputs.Factory.Binary(
                            IrBinaryOperator.AndAlso,
                            inputs.Factory.Binary(
                                IrBinaryOperator.GreaterThanOrEqual,
                                fresh,
                                inputs.Factory.Integer(range.Minimum)),
                            inputs.Factory.Binary(
                                IrBinaryOperator.LessThanOrEqual,
                                fresh,
                                inputs.Factory.Integer(range.Maximum))));
                }
            }

            if (!Supported(predicate))
            {
                _reason = WorkerClaimReason.UnsupportedBody;
                return null;
            }

            return new FlowState(0, predicate, environment.ToImmutable());
        }

        private (long Minimum, long Maximum)? IntegerRange(IrVarId variable)
        {
            if (_ranges == null)
            {
                _ranges = [];
                foreach (var initial in inputs.InitialEnvironment)
                {
                    Widen(initial.Key, initial.Value is IrVariableTerm parameter &&
                        inputs.Variables.FirstOrDefault(candidate =>
                            candidate.Variable == parameter.Variable)?.SourceIntegerInterval
                            is { } interval
                        ? (interval.Minimum, interval.Maximum)
                        : null);
                }
                foreach (var block in inputs.Program.Blocks)
                {
                    foreach (var instruction in block.Instructions)
                    {
                        switch (instruction)
                        {
                            case IrAssignInstruction assign:
                                Widen(assign.Target, assign.Value switch
                                {
                                    IrIntegerTerm constant => (constant.Value, constant.Value),
                                    IrUnaryTerm unary when IrIntegerNarrowing.TryGet(
                                        unary.Operator, out var narrowing) =>
                                        (narrowing.Minimum, narrowing.Maximum),
                                    _ => null
                                });
                                break;
                            case IrCallInstruction { Target: { } target }:
                                Widen(target, null);
                                break;
                            case IrHavocInstruction havoc:
                                foreach (var havocked in havoc.Variables)
                                {
                                    Widen(havocked, null);
                                }
                                break;
                        }
                    }
                }
            }

            return _ranges.TryGetValue(variable, out var range) ? range : null;

            void Widen(IrVarId target, (long Minimum, long Maximum)? value)
            {
                _ranges[target] = !_ranges.TryGetValue(target, out var existing)
                    ? value
                    : existing is { } left && value is { } right
                        ? (Math.Min(left.Minimum, right.Minimum), Math.Max(left.Maximum, right.Maximum))
                        : null;
            }
        }

        private SpecApplication? ApplySpec(
            IrCallInstruction call, CompilerPreparedSpecCall prepared,
            IReadOnlyDictionary<IrVarId, IrTerm> environment,
            IrTerm guard)
        {
            if (!call.Target.HasValue ||
                !ApiSpecTable.Default.TryGetByWitnessIdentifier(
                    prepared.WitnessIdentifier,
                    out var template))
            {
                return null;
            }

            var throws = template.Facets.Throws;
            if (
                template.Target.DocumentationCommentId != prepared.CallIdentity ||
                !template.Result.HasValue ||
                throws.Behavior != SpecThrowBehavior.DoesNotThrow &&
                !(throws.Behavior == SpecThrowBehavior.MayThrow &&
                  throws.NormalCompletion != null) ||
                prepared.ConsumesMemoryHavoc != (template.Facets.Effects.Effects != SpecEffect.None))
            {
                return null;
            }

            var targetType = inputs.Factory.GetVariableInfo(call.Target.Value).Type;
            if (!IsResultType(template.Target.ResultType, targetType) ||
                call.Arguments.Length != template.Parameters.Length ||
                template.Receiver.HasValue != (call.Receiver != null))
            {
                return null;
            }

            var substitutions = new Dictionary<SpecVarId, IrTerm>();
            if (template.Receiver.HasValue)
            {
                if (AdmitOperand(call.Receiver!, environment, ref guard)
                    is not { } receiver)
                {
                    return null;
                }

                substitutions.Add(template.Receiver.Value, receiver);
            }
            for (var index = 0; index < call.Arguments.Length; index++)
            {
                if (AdmitOperand(call.Arguments[index], environment, ref guard)
                    is not { } argument)
                {
                    return null;
                }

                substitutions.Add(template.Parameters[index], argument);
            }
            var resultVariable = inputs.Factory.CreateVariable(
                "spec-call-result:" +
                call.Id.Value.ToString(CultureInfo.InvariantCulture),
                targetType);
            var result = inputs.Factory.Variable(resultVariable);
            substitutions.Add(template.Result.Value, result);
            if (!SpecResultDomainProjection.TryCreate(
                    inputs.Factory, template, resultVariable, out var projection,
                     out var facetPredicates))
            {
                return null;
            }

            var hasProjection = projection != default;
            if (hasProjection &&
                _projections.TryGetValue(resultVariable, out var existing) &&
                existing != projection)
            {
                return null;
            }

            var instantiated = ApiSpecInstantiator.InstantiatePostconditions(template, inputs.Factory, substitutions);
            if (instantiated.Status != SpecInstantiationStatus.Succeeded ||
                (template.Facets.Throws.NormalCompletion == null) !=
                (instantiated.NormalCompletionCondition == null))
            {
                return null;
            }

            var normalCompletionGuard = guard;
            if (instantiated.NormalCompletionCondition is { } normalCompletion)
            {
                if (!Supported(normalCompletion) || !Spend())
                {
                    return null;
                }

                RequireNormalCompletion(guard, normalCompletion);
                normalCompletionGuard = inputs.Factory.Binary(
                    IrBinaryOperator.AndAlso,
                    guard,
                    normalCompletion);
                if (!Supported(normalCompletionGuard))
                {
                    return null;
                }
            }

            var projectionMap = !hasProjection
                ? ImmutableDictionary<IrVarId, SpecResultProjection>.Empty
                : ImmutableDictionary<IrVarId, SpecResultProjection>.Empty.Add(
                    resultVariable,
                    projection);
            var predicates = instantiated.Postconditions
                .Select(predicate => SpecResultDomainProjection.Rewrite(inputs.Factory, predicate, projectionMap))
                .Concat(facetPredicates)
                .ToArray();
            if (predicates.Length == 0 || predicates.Any(predicate => !Supported(predicate)))
            {
                return null;
            }

            if (hasProjection)
            {
                _projections[resultVariable] = projection;
            }

            _assumptions.AddRange(predicates.Select(predicate => new GuardedBodySpecAssumption(
                template.Id,
                template.Target.WitnessIdentifier,
                normalCompletionGuard,
                predicate)));
            return new SpecApplication(
                result,
                normalCompletionGuard,
                prepared.ConsumesMemoryHavoc);
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
                inputs.Factory.GetVariableInfo(call.Target.Value).Type !=
                inputs.Factory.GetVariableInfo(prepared.Result).Type ||
                !WorkerProtocolJson.IsSha256(prepared.EvidenceSha256))
            {
                return null;
            }

            if (call.Receiver != null)
            {
                if (AdmitOperand(call.Receiver, environment, ref guard) == null)
                {
                    return null;
                }
            }

            foreach (var argumentTerm in call.Arguments)
            {
                if (AdmitOperand(argumentTerm, environment, ref guard) == null)
                {
                    return null;
                }
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
                relation.Type != inputs.Factory.BooleanType)
            {
                return null;
            }

            // A summary describes normal completion only; when the callee
            // may throw, when it does is not modeled.
            _throwsUnmodeled |= prepared.MayThrow;
            _summaryAssumptions.Add(new GuardedBodySummaryAssumption(
                prepared.CallIdentity,
                prepared.Origin,
                prepared.EvidenceSha256,
                prepared.EvidenceIdentity,
                prepared.DependencyEvidence,
                guard,
                relation));
            return new SpecApplication(
                inputs.Factory.Variable(prepared.Result),
                guard,
                ConsumesMemoryHavoc: false);
        }

        private ImmutableDictionary<IrVarId, IrTerm>? CreateCurrentStates(
            ImmutableDictionary<IrVarId, IrTerm> environment)
        {
            if (!Spend(inputs.Variables.Length + inputs.ParameterBindings.Count))
            {
                return null;
            }

            var states = ImmutableDictionary.CreateBuilder<IrVarId, IrTerm>();
            foreach (var variable in inputs.Variables.Where(
                         static variable =>
                             variable.Role == CompilerVariableRole.Parameter))
            {
                states.Add(
                    variable.Variable,
                    inputs.Factory.Variable(variable.Variable));
            }
            foreach (var binding in inputs.ParameterBindings)
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
            _cut = IrBlockOrder.TryCutLoops(
                inputs.Program, Spend, out var failure);
            var result = _cut?.Order ?? default;
            if (result.IsDefault)
            {
                _reason = failure switch
                {
                    IrAcyclicOrderFailure.ResourceLimit =>
                        WorkerClaimReason.ResourceLimit,
                    _ => WorkerClaimReason.UnsupportedBody
                };
            }
            return result;
        }

        private void AddIncoming(
            IrBlockId block, int order, IrTerm predicate,
            ImmutableDictionary<IrVarId, IrTerm> environment)
        {
            // A cut back edge is `assume false`: the loop header already
            // stands for every iteration.
            if (predicate is IrBooleanTerm { Value: false } ||
                _cut!.BackEdges.Contains((_current, block)))
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

            return inputs.Factory.GetTypeInfo(resultType).Kind == specType.Value;
        }

        private IrTerm? Substitute(
            IrTerm term,
            IReadOnlyDictionary<IrVarId, IrTerm> environment,
            HashSet<IrVarId>? freeVariables = null)
        {
            inputs.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!IrSubstitution.TrySubstitute(
                        inputs.Factory,
                        term,
                        environment,
                        freeVariables,
                        out var result))
                {
                    return null;
                }

                inputs.CancellationToken.ThrowIfCancellationRequested();
                return Supported(result) ? result : null;
            }
            catch (ArgumentException) { return null; }
        }

        private IrTerm? AdmitOperand(
            IrTerm operand,
            IReadOnlyDictionary<IrVarId, IrTerm> environment,
            ref IrTerm guard)
        {
            var substituted = Substitute(operand, environment);
            if (substituted == null ||
                ConstrainNormalExecution(guard, substituted)
                    is not { } nextGuard)
            {
                return null;
            }

            guard = nextGuard;
            return substituted;
        }

        private bool Supported(IrTerm term)
        {
            inputs.CancellationToken.ThrowIfCancellationRequested();
            return IrTermAnalysis.GetDepth(term) <= maximumExpressionDepth;
        }

        private bool Spend(int amount = 1)
        {
            inputs.CancellationToken.ThrowIfCancellationRequested();
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

    // Holds exactly when no reachable operation throws; null when a callee's
    // throwing is not modeled.
    internal IrTerm? NoThrow { get; init; }
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
