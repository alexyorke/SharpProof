namespace SharpProof.Summaries;

public sealed class IrRelationalSummaryBuildLimits
{
    public IrRelationalSummaryBuildLimits(
        int maximumBlocks = 64,
        int maximumInstructions = 4096,
        int maximumExpressionDepth = 256,
        int maximumSymbolicOperations = 65536)
    {
        MaximumBlocks = ArgumentNullGuard.RequirePositive(
            maximumBlocks,
            nameof(maximumBlocks));
        MaximumInstructions = ArgumentNullGuard.RequirePositive(
            maximumInstructions,
            nameof(maximumInstructions));
        MaximumExpressionDepth = ArgumentNullGuard.RequirePositive(
            maximumExpressionDepth,
            nameof(maximumExpressionDepth));
        MaximumSymbolicOperations = ArgumentNullGuard.RequirePositive(
            maximumSymbolicOperations,
            nameof(maximumSymbolicOperations));
    }

    public static IrRelationalSummaryBuildLimits Default { get; } = new();

    public int MaximumBlocks { get; }

    public int MaximumInstructions { get; }

    public int MaximumExpressionDepth { get; }

    public int MaximumSymbolicOperations { get; }

}

public static class IrRelationalSummaryBuilder
{
    public static IrRelationalSummaryBuildResult Build(
        IrProgram program,
        IrSummarySignature signature,
        IReadOnlyDictionary<IrVarId, IrTerm> initialEnvironment,
        IReadOnlyDictionary<IrInstructionId, IrRelationalSummary>? calls = null,
        IrRelationalSummaryBuildLimits? limits = null,
        bool mayThrow = false)
    {
        if (program == null)
        {
            throw new ArgumentNullException(nameof(program));
        }

        if (signature == null)
        {
            throw new ArgumentNullException(nameof(signature));
        }

        if (initialEnvironment == null)
        {
            throw new ArgumentNullException(nameof(initialEnvironment));
        }

        limits ??= IrRelationalSummaryBuildLimits.Default;
        calls ??= ImmutableDictionary<IrInstructionId, IrRelationalSummary>.Empty;
        if (!ValidateSignature(program.Factory, signature) ||
            !ValidateEnvironment(
                program.Factory,
                signature,
                initialEnvironment) ||
            calls.Values.Any(summary =>
                summary == null ||
                !ReferenceEquals(summary.Factory, program.Factory)))
        {
            return Failed(IrSummaryAbstentionReason.InvalidSignature);
        }

        var instructionCount = program.Blocks.Sum(
            static block => (long)block.Instructions.Length);
        if (program.Blocks.Length > limits.MaximumBlocks ||
            instructionCount > limits.MaximumInstructions)
        {
            return Failed(IrSummaryAbstentionReason.ResourceLimit);
        }

        return new Run(
            program,
            signature,
            initialEnvironment.ToImmutableDictionary(),
            calls,
            limits,
            mayThrow).Execute();
    }

    private static bool ValidateSignature(
        IrFactory factory,
        IrSummarySignature signature)
    {
        try
        {
            var member = factory.GetMemberInfo(signature.Member);

            if (member.IsStatic == signature.Receiver.HasValue ||
                member.ParameterTypes.Length != signature.Parameters.Length ||
                factory.GetVariableInfo(signature.Result).Type != member.ReturnType)
            {
                return false;
            }

            if (signature.Receiver.HasValue &&
                factory.GetVariableInfo(signature.Receiver.Value).Type !=
                member.DeclaringType)
            {
                return false;
            }

            for (var index = 0; index < signature.Parameters.Length; index++)
            {
                if (factory.GetVariableInfo(signature.Parameters[index]).Type !=
                    member.ParameterTypes[index])
                {
                    return false;
                }
            }

            var variables = new HashSet<IrVarId>();
            if (signature.Receiver is { } receiver &&
                !variables.Add(receiver))
            {
                return false;
            }
            foreach (var parameter in signature.Parameters)
            {
                if (!variables.Add(parameter))
                {
                    return false;
                }
            }
            return variables.Add(signature.Result);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool ValidateEnvironment(
        IrFactory factory,
        IrSummarySignature signature,
        IReadOnlyDictionary<IrVarId, IrTerm> environment)
    {
        var inputs = new HashSet<IrVarId>(signature.Parameters.Concat(
            signature.Receiver.HasValue
                ? [signature.Receiver.Value]
                : []));
        try
        {
            foreach (var item in environment)
            {
                if (item.Value == null ||
                    factory.GetVariableInfo(item.Key).Type != item.Value.Type ||
                    !ReferenceEquals(factory.GetTerm(item.Value.Id), item.Value) ||
                    !IrTermAnalysis.CollectVariables(item.Value).All(inputs.Contains))
                {
                    return false;
                }
            }
        }
        catch (ArgumentException)
        {
            return false;
        }

        return true;
    }

    private static IrRelationalSummaryBuildResult Failed(
        IrSummaryAbstentionReason reason)
    {
        return new IrRelationalSummaryBuildResult(null, reason);
    }

    private sealed class Run
    {
        private readonly IrProgram _program;
        private readonly IrSummarySignature _signature;
        private readonly ImmutableDictionary<IrVarId, IrTerm> _initialEnvironment;
        private readonly IReadOnlyDictionary<IrInstructionId, IrRelationalSummary> _calls;
        private readonly IrRelationalSummaryBuildLimits _limits;
        private readonly Dictionary<IrBlockId, List<FlowState>> _incoming = [];
        private readonly List<IrTerm> _completions = [];
        private readonly List<IrTerm> _relations = [];
        private readonly List<IrVarId> _existentials = [];
        private readonly HashSet<IrMemberId> _dependencies = [];
        private readonly Dictionary<(
            IrSummaryOrigin Origin,
            string EvidenceCallIdentity,
            string EvidenceIdentity,
            string EvidenceSha256), IrSummaryProvenance> _dependencyProvenance = [];
        private readonly HashSet<IrId> _visitedTerms = [];
        private readonly Dictionary<IrId, int> _termDepths = [];
        private int _remainingOperations;
        private bool _mayThrow;
        private IrSummaryAbstentionReason _reason;

        internal Run(
            IrProgram program,
            IrSummarySignature signature,
            ImmutableDictionary<IrVarId, IrTerm> initialEnvironment,
            IReadOnlyDictionary<IrInstructionId, IrRelationalSummary> calls,
            IrRelationalSummaryBuildLimits limits,
            bool mayThrow)
        {
            _program = program;
            _signature = signature;
            _initialEnvironment = initialEnvironment;
            _calls = calls;
            _limits = limits;
            _remainingOperations = limits.MaximumSymbolicOperations;
            _mayThrow = mayThrow;
        }

        private IrFactory Factory => _program.Factory;

        internal IrRelationalSummaryBuildResult Execute()
        {
            foreach (var term in _initialEnvironment.Values)
            {
                if (!Supported(term))
                {
                    return Failure();
                }
            }

            var order = CreateOrder();
            if (order.IsDefault)
            {
                return Failure();
            }

            foreach (var blockId in order)
            {
                var state = Merge(blockId);
                if (state == null)
                {
                    if (_reason != IrSummaryAbstentionReason.None)
                    {
                        return Failure();
                    }

                    continue;
                }

                if (!ExecuteBlock(_program.GetBlock(blockId), state.Value))
                {
                    return Failure();
                }
            }

            if (_relations.Count == 0)
            {
                _reason = IrSummaryAbstentionReason.UnsupportedBody;
                return Failure();
            }

            var normalCompletion = IrSemanticTerms.Disjoin(
                Factory,
                _completions);
            var normalRelation = IrSemanticTerms.Disjoin(
                Factory,
                _relations);
            if (!Supported(normalCompletion) || !Supported(normalRelation))
            {
                return Failure();
            }

            var summary = new IrRelationalSummary(
                Factory,
                _signature,
                [.. _existentials],
                normalCompletion,
                normalRelation,
                [.. _dependencies.OrderBy(
                    static member => member.Value)],
                [.. _dependencyProvenance
                    .OrderBy(static item => item.Key.Origin)
                    .ThenBy(static item => item.Key.EvidenceCallIdentity,
                        StringComparer.Ordinal)
                    .ThenBy(static item => item.Key.EvidenceIdentity,
                        StringComparer.Ordinal)
                    .ThenBy(static item => item.Key.EvidenceSha256,
                        StringComparer.Ordinal)
                    .Select(static item => item.Value)],
                _mayThrow ? IrSummaryEffect.MayThrow : IrSummaryEffect.None);
            return new IrRelationalSummaryBuildResult(
                summary,
                IrSummaryAbstentionReason.None);
        }

        private bool ExecuteBlock(IrBasicBlock block, FlowState state)
        {
            var environment = state.Environment;
            var predicate = state.Predicate;
            for (var index = 0; index < block.Instructions.Length; index++)
            {
                if (!Spend())
                {
                    return false;
                }

                var instruction = block.Instructions[index];
                switch (instruction)
                {
                    case IrAssignInstruction assign:
                        {
                            var assigned = Substitute(assign.Value, environment);
                            if (assigned == null ||
                                ConstrainNormalExecution(
                                    predicate,
                                    assigned) is not { } constrained)
                            {
                                return false;
                            }

                            predicate = constrained;
                            environment = environment.SetItem(
                                assign.Target,
                                assigned);
                            break;
                        }
                    case IrAssumeInstruction assume:
                        {
                            var condition = Substitute(
                                assume.Condition,
                                environment);
                            if (condition == null ||
                                condition.Type != Factory.BooleanType)
                            {
                                return false;
                            }

                            _mayThrow |=
                                IrSemanticTerms.RequiresDefinednessWitness(condition);
                            predicate = Factory.Binary(
                                IrBinaryOperator.AndAlso,
                                predicate,
                                condition);
                            if (!Supported(predicate))
                            {
                                return false;
                            }

                            break;
                        }
                    case IrCallInstruction call:
                        {
                            if (!_calls.TryGetValue(call.Id, out var dependency) ||
                                dependency.Signature.Member != call.Member ||
                                !call.Target.HasValue ||
                                ApplyCall(
                                    call,
                                    dependency,
                                    environment,
                                    predicate) is not { } application)
                            {
                                if (_reason == IrSummaryAbstentionReason.None)
                                {
                                    _reason = _calls.ContainsKey(call.Id)
                                        ? IrSummaryAbstentionReason.InvalidSignature
                                        : IrSummaryAbstentionReason.MissingDependency;
                                }
                                return false;
                            }

                            predicate = application.Predicate;
                            environment = environment.SetItem(
                                call.Target.Value,
                                Factory.Variable(application.Result));
                            break;
                        }
                    case IrBranchInstruction branch:
                        return index == block.Instructions.Length - 1 &&
                            TransferBranch(
                                block.Id,
                                branch,
                                predicate,
                                environment);
                    case IrGotoInstruction go:
                        AddIncoming(
                            go.Target,
                            block.Id.Value << 1,
                            predicate,
                            environment);
                        return index == block.Instructions.Length - 1;
                    case IrReturnInstruction returned:
                        return index == block.Instructions.Length - 1 &&
                            AddReturn(returned, predicate, environment);
                    default:
                        _reason =
                            IrSummaryAbstentionReason.UnsupportedInstruction;
                        return false;
                }
            }

            _reason = IrSummaryAbstentionReason.UnsupportedBody;
            return false;
        }

        private CallApplication? ApplyCall(
            IrCallInstruction call,
            IrRelationalSummary dependency,
            ImmutableDictionary<IrVarId, IrTerm> environment,
            IrTerm predicate)
        {
            IrTerm? receiver = null;
            if (call.Receiver != null)
            {
                receiver = Substitute(call.Receiver, environment);
                if (receiver == null ||
                    ConstrainNormalExecution(
                        predicate,
                        receiver) is not { } constrained)
                {
                    return null;
                }

                predicate = constrained;
                if (ConstrainNonNullReceiver(
                        predicate,
                        receiver) is not { } nonNullReceiver)
                {
                    return null;
                }

                predicate = nonNullReceiver;
            }

            var arguments = new IrTerm[call.Arguments.Length];
            for (var index = 0; index < call.Arguments.Length; index++)
            {
                var argument = Substitute(
                    call.Arguments[index],
                    environment);
                if (argument == null ||
                    ConstrainNormalExecution(
                        predicate,
                        argument) is not { } constrained)
                {
                    return null;
                }

                arguments[index] = argument;
                predicate = constrained;
            }

            IrSummaryInstantiation instantiated;
            try
            {
                if (!Supported(dependency.NormalCompletion) ||
                    !Supported(dependency.NormalRelation))
                {
                    return null;
                }
                instantiated = IrRelationalSummaryInstantiator.Instantiate(
                    dependency,
                    receiver,
                    arguments,
                    call.Id.Value);
            }
            catch (ArgumentException)
            {
                return null;
            }

            predicate = IrSemanticTerms.Conjoin(
                Factory,
                [
                    predicate,
                    instantiated.NormalCompletion,
                    instantiated.NormalRelation
                ]);
            if (!Supported(predicate))
            {
                return null;
            }

            _existentials.AddRange(instantiated.FreshVariables);
            _dependencies.Add(dependency.Signature.Member);
            AddDependencyProvenance(dependency.Signature.Provenance);
            foreach (var provenance in dependency.DependencyProvenance)
            {
                AddDependencyProvenance(provenance);
            }
            _mayThrow |= dependency.Effects == IrSummaryEffect.MayThrow;
            return new CallApplication(instantiated.Result, predicate);
        }

        private IrTerm? ConstrainNonNullReceiver(
            IrTerm predicate,
            IrTerm receiver)
        {
            if (!IrOperatorCatalog.IsNullable(
                    Factory.GetTypeInfo(receiver.Type).Kind))
            {
                return predicate;
            }

            if (!Spend(2))
            {
                return null;
            }

            var nonNull = Factory.Binary(
                IrBinaryOperator.NotEqual,
                receiver,
                Factory.Null(receiver.Type));
            _mayThrow |= nonNull is not IrBooleanTerm { Value: true };
            var result = Factory.Binary(
                IrBinaryOperator.AndAlso,
                predicate,
                nonNull);
            return Supported(result) ? result : null;
        }

        private void AddDependencyProvenance(IrSummaryProvenance provenance)
        {
            var key = (
                provenance.Origin,
                provenance.EvidenceCallIdentity,
                provenance.EvidenceIdentity,
                provenance.EvidenceSha256);
            _dependencyProvenance[key] = provenance;
        }

        private bool AddReturn(
            IrReturnInstruction returned,
            IrTerm predicate,
            ImmutableDictionary<IrVarId, IrTerm> environment)
        {
            if (returned.Value == null)
            {
                _reason = IrSummaryAbstentionReason.InvalidSignature;
                return false;
            }

            var value = Substitute(returned.Value, environment);
            if (value == null ||
                value.Type != Factory.GetVariableInfo(
                    _signature.Result).Type ||
                ConstrainNormalExecution(
                    predicate,
                    value) is not { } completion)
            {
                return false;
            }

            var relation = Factory.Binary(
                IrBinaryOperator.AndAlso,
                predicate,
                Factory.Binary(
                    IrBinaryOperator.Equal,
                    Factory.Variable(_signature.Result),
                    value));
            if (!Supported(completion) || !Supported(relation))
            {
                return false;
            }

            _completions.Add(completion);
            _relations.Add(relation);
            return true;
        }

        private IrTerm? ConstrainNormalExecution(
            IrTerm predicate,
            IrTerm evaluated)
        {
            if (IrSemanticTerms.RequiresDefinednessWitness(evaluated))
            {
                _mayThrow = true;
                if (!Spend(2))
                {
                    return null;
                }
            }

            var result = IrSemanticTerms.ConstrainSuccessfulEvaluation(
                Factory,
                predicate,
                evaluated);
            return Supported(result) ? result : null;
        }

        private bool TransferBranch(
            IrBlockId predecessor,
            IrBranchInstruction branch,
            IrTerm predicate,
            ImmutableDictionary<IrVarId, IrTerm> environment)
        {
            var condition = Substitute(branch.Condition, environment);
            if (condition == null ||
                condition.Type != Factory.BooleanType)
            {
                return false;
            }

            _mayThrow |=
                IrSemanticTerms.RequiresDefinednessWitness(condition);
            var order = predecessor.Value << 1;
            if (condition is IrBooleanTerm literal)
            {
                AddIncoming(
                    literal.Value ? branch.WhenTrue : branch.WhenFalse,
                    order + (literal.Value ? 0 : 1),
                    predicate,
                    environment);
                return true;
            }

            if (!Spend(2))
            {
                return false;
            }

            var whenTrue = Factory.Binary(
                IrBinaryOperator.AndAlso,
                predicate,
                condition);
            var whenFalse = Factory.Binary(
                IrBinaryOperator.AndAlso,
                predicate,
                Factory.Unary(IrUnaryOperator.Not, condition));
            if (!Supported(whenTrue) || !Supported(whenFalse))
            {
                return false;
            }

            AddIncoming(
                branch.WhenTrue,
                order,
                whenTrue,
                environment);
            AddIncoming(
                branch.WhenFalse,
                order + 1,
                whenFalse,
                environment);
            return true;
        }

        private FlowState? Merge(IrBlockId block)
        {
            if (block == _program.Entry)
            {
                return new FlowState(
                    0,
                    Factory.Boolean(true),
                    _initialEnvironment);
            }

            if (!_incoming.TryGetValue(block, out var values) ||
                values.Count == 0)
            {
                return null;
            }

            values.Sort(static (left, right) =>
                left.Order.CompareTo(right.Order));
            if (!Spend(values.Count))
            {
                return null;
            }

            var predicate = IrSemanticTerms.Disjoin(
                Factory,
                values.Select(static value => value.Predicate).ToArray());
            if (!Supported(predicate))
            {
                return null;
            }

            var environment =
                ImmutableDictionary.CreateBuilder<IrVarId, IrTerm>();
            foreach (var variable in values[0].Environment.Keys.OrderBy(
                         static value => value.Value))
            {
                if (!Spend(values.Count))
                {
                    return null;
                }

                var first = values[0].Environment[variable];
                var merged = first;
                var hasMissing = false;
                var hasDifferentValue = false;
                for (var index = 0; index < values.Count; index++)
                {
                    if (!values[index].Environment.TryGetValue(
                            variable,
                            out var value))
                    {
                        hasMissing = true;
                        break;
                    }

                    hasDifferentValue |= value.Id != first.Id;
                }

                if (hasMissing)
                {
                    continue;
                }

                if (hasDifferentValue)
                {
                    merged = values[values.Count - 1].Environment[variable];
                    for (var index = values.Count - 2; index >= 0; index--)
                    {
                        merged = Factory.Conditional(
                            values[index].Predicate,
                            values[index].Environment[variable],
                            merged);
                    }
                }

                if (!Supported(merged))
                {
                    return null;
                }

                environment.Add(variable, merged);
            }

            return new FlowState(
                0,
                predicate,
                environment.ToImmutable());
        }

        private ImmutableArray<IrBlockId> CreateOrder()
        {
            var result = IrBlockOrder.TryCreateAcyclicOrder(
                _program, Spend, out var failure);
            if (result.IsDefault)
            {
                _reason = failure switch
                {
                    IrAcyclicOrderFailure.ResourceLimit =>
                        IrSummaryAbstentionReason.ResourceLimit,
                    IrAcyclicOrderFailure.CyclicControlFlow =>
                        IrSummaryAbstentionReason.CyclicControlFlow,
                    IrAcyclicOrderFailure.UnsupportedInstruction =>
                        IrSummaryAbstentionReason.UnsupportedInstruction,
                    _ => IrSummaryAbstentionReason.UnsupportedBody
                };
            }
            return result;
        }

        private IrTerm? Substitute(
            IrTerm term,
            IReadOnlyDictionary<IrVarId, IrTerm> environment)
        {
            if (!Supported(term))
            {
                return null;
            }
            if (!IrTermAnalysis.CollectVariables(term).All(
                    environment.ContainsKey))
            {
                _reason = IrSummaryAbstentionReason.UnsupportedBody;
                return null;
            }

            try
            {
                var result = IrSubstitution.Substitute(
                    Factory,
                    term,
                    environment);
                return Supported(result) ? result : null;
            }
            catch (ArgumentException)
            {
                _reason = IrSummaryAbstentionReason.InvalidSignature;
                return null;
            }
        }

        private bool Supported(IrTerm term)
        {
            if (!Charge(term))
            {
                return false;
            }
            if (IrTermAnalysis.GetDepth(term, _termDepths) <=
                _limits.MaximumExpressionDepth)
            {
                return true;
            }

            _reason = IrSummaryAbstentionReason.ExpressionDepth;
            return false;
        }

        private bool Charge(IrTerm root)
        {
            var pending = new Stack<IrTerm>();
            pending.Push(root);
            while (pending.Count != 0)
            {
                var term = pending.Pop();
                if (!_visitedTerms.Add(term.Id))
                {
                    continue;
                }
                if (!Spend())
                {
                    return false;
                }
                foreach (var child in IrTraversal.GetChildren(term).Reverse())
                {
                    pending.Push(child);
                }
            }
            return true;
        }

        private void AddIncoming(
            IrBlockId block,
            int order,
            IrTerm predicate,
            ImmutableDictionary<IrVarId, IrTerm> environment)
        {
            if (predicate is IrBooleanTerm { Value: false })
            {
                return;
            }

            if (!_incoming.TryGetValue(block, out var values))
            {
                values = [];
                _incoming.Add(block, values);
            }

            values.Add(new FlowState(order, predicate, environment));
        }

        private bool Spend(int amount = 1)
        {
            if (amount >= 0 && amount <= _remainingOperations)
            {
                _remainingOperations -= amount;
                return true;
            }

            _reason = IrSummaryAbstentionReason.ResourceLimit;
            return false;
        }

        private IrRelationalSummaryBuildResult Failure()
        {
            return Failed(
                _reason == IrSummaryAbstentionReason.None
                    ? IrSummaryAbstentionReason.UnsupportedBody
                    : _reason);
        }

        private readonly struct FlowState
        {
            internal FlowState(
                int order,
                IrTerm predicate,
                ImmutableDictionary<IrVarId, IrTerm> environment)
            {
                Order = order;
                Predicate = predicate;
                Environment = environment;
            }

            internal int Order { get; }

            internal IrTerm Predicate { get; }

            internal ImmutableDictionary<IrVarId, IrTerm> Environment
            {
                get;
            }
        }

        private readonly struct CallApplication
        {
            internal CallApplication(IrVarId result, IrTerm predicate)
            {
                Result = result;
                Predicate = predicate;
            }

            internal IrVarId Result { get; }

            internal IrTerm Predicate { get; }
        }
    }
}
