namespace SharpProof.Worker;

internal sealed partial class SymbolicBodyExecutor
{
    private const int DefaultMaximumSymbolicOperations =
        CompilerPreparedBody.MaximumInstructions * 16;
    private readonly int _maximumExpressionDepth;
    private readonly int _maximumSymbolicOperations;

    internal SymbolicBodyExecutor(
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
        : IrForwardExecutor(
            inputs.Program,
            inputs.InitialEnvironment,
            maximumExpressionDepth,
            remainingOperations,
            inputs.CancellationToken)
    {
        private readonly ImmutableArray<SymbolicReturn>.Builder _returns = ImmutableArray.CreateBuilder<SymbolicReturn>();
        private readonly ImmutableDictionary<IrVarId, SpecResultProjection>.Builder _projections =
            ImmutableDictionary.CreateBuilder<IrVarId, SpecResultProjection>();
        private readonly ImmutableArray<GuardedBodySpecAssumption>.Builder _assumptions =
            ImmutableArray.CreateBuilder<GuardedBodySpecAssumption>();
        private readonly ImmutableArray<GuardedBodySummaryAssumption>.Builder _summaryAssumptions =
            ImmutableArray.CreateBuilder<GuardedBodySummaryAssumption>();
        // Conditions under which no reachable operation throws: each is
        // "path condition implies this operation completes normally".
        private readonly List<IrTerm> _normalCompletions = [];
        private bool _throwsUnmodeled;

        internal SymbolicBodyExecution Execute()
        {
            if (!Run())
            {
                return SymbolicBodyExecution.Failed(
                    Failure == IrForwardFailure.ResourceLimit
                        ? WorkerClaimReason.ResourceLimit
                        : WorkerClaimReason.UnsupportedBody);
            }

            // No returns means no path completes normally (every path throws
            // or loops); the verifier reports such proofs as vacuous.
            return new SymbolicBodyExecution(WorkerClaimReason.None, _returns.ToImmutable(),
                    _projections.ToImmutable(), _assumptions.ToImmutable(),
                    _summaryAssumptions.ToImmutable())
            {
                NoThrow = _throwsUnmodeled
                    ? null
                    : IrSemanticTerms.Conjoin(Factory, _normalCompletions)
            };
        }

        protected override void OnThrowSite(IrTerm predicate, IrTerm completes)
        {
            _normalCompletions.Add(IrSemanticTerms.Guard(Factory, predicate, completes));
        }

        protected override (long Minimum, long Maximum)? InitialRange(IrTerm initial)
        {
            return initial is IrVariableTerm parameter &&
                inputs.Variables.FirstOrDefault(candidate =>
                    candidate.Variable == parameter.Variable)?.SourceIntegerInterval is { } interval
                ? (interval.Minimum, interval.Maximum)
                : null;
        }

        protected override bool ExecuteCall(
            IrCallInstruction call,
            ref IrTerm predicate,
            ref ImmutableDictionary<IrVarId, IrTerm> environment)
        {
            SpecApplication? application = null;
            if (inputs.SpecCalls.TryGetValue(call.Id, out var preparedSpec))
            {
                application = ApplySpec(call, preparedSpec, environment, predicate);
            }
            else if (inputs.SummaryCalls.TryGetValue(call.Id, out var preparedSummary))
            {
                application = ApplySummary(call, preparedSummary, environment, predicate);
            }

            if (application == null)
            {
                return false;
            }

            environment = environment.SetItem(call.Target!.Value, application.Value.Result);
            predicate = application.Value.Predicate;
            if (application.Value.ConsumesMemoryHavoc)
            {
                ExpectMemoryHavoc(call.Operation);
            }

            return true;
        }

        protected override bool ExecuteReturn(
            IrReturnInstruction returned,
            IrTerm predicate,
            ImmutableDictionary<IrVarId, IrTerm> environment)
        {
            if (returned.Value == null && inputs.Variables.Any(static variable =>
                    variable.Role == CompilerVariableRole.Result))
            {
                return false;
            }

            // A void method's return carries no value; any placeholder works
            // because no result variable reads it.
            var returnTerm = returned.Value == null
                ? Factory.Boolean(true)
                : Substitute(returned.Value, environment);
            if (returnTerm == null)
            {
                return false;
            }

            if (IrSemanticTerms.RequiresDefinednessWitness(returnTerm))
            {
                OnThrowSite(predicate, Factory.Binary(IrBinaryOperator.Equal, returnTerm, returnTerm));
            }

            var currentStates = CreateCurrentStates(environment);
            if (currentStates == null)
            {
                return false;
            }

            _returns.Add(new SymbolicReturn(predicate, returnTerm, currentStates));
            return true;
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

            var targetType = Factory.GetVariableInfo(call.Target.Value).Type;
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
            var resultVariable = Factory.CreateVariable(
                "spec-call-result:" +
                call.Id.Value.ToString(CultureInfo.InvariantCulture),
                targetType);
            var result = Factory.Variable(resultVariable);
            substitutions.Add(template.Result.Value, result);
            if (!SpecResultDomainProjection.TryCreate(
                    Factory, template, resultVariable, out var projection,
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

            var instantiated = ApiSpecInstantiator.InstantiatePostconditions(template, Factory, substitutions);
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

                OnThrowSite(guard, normalCompletion);
                normalCompletionGuard = Factory.Binary(
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
                .Select(predicate => SpecResultDomainProjection.Rewrite(Factory, predicate, projectionMap))
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
                Factory.GetVariableInfo(call.Target.Value).Type !=
                Factory.GetVariableInfo(prepared.Result).Type ||
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
                relation.Type != Factory.BooleanType)
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
                Factory.Variable(prepared.Result),
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
                    Factory.Variable(variable.Variable));
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

            return Factory.GetTypeInfo(resultType).Kind == specType.Value;
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
