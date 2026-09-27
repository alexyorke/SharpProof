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
        ArgumentNullGuard.NotNull(program, nameof(program));
        ArgumentNullGuard.NotNull(signature, nameof(signature));
        ArgumentNullGuard.NotNull(initialEnvironment, nameof(initialEnvironment));

        limits ??= IrRelationalSummaryBuildLimits.Default;
        calls ??= ImmutableDictionary<IrInstructionId, IrRelationalSummary>.Empty;
        var inputs = ValidateSignature(program.Factory, signature);
        if (inputs == null ||
            !ValidateEnvironment(
                program.Factory,
                inputs,
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

    private static HashSet<IrVarId>? ValidateSignature(
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
                return null;
            }

            if (signature.Receiver.HasValue &&
                factory.GetVariableInfo(signature.Receiver.Value).Type !=
                member.DeclaringType)
            {
                return null;
            }

            for (var index = 0; index < signature.Parameters.Length; index++)
            {
                if (factory.GetVariableInfo(signature.Parameters[index]).Type !=
                    member.ParameterTypes[index])
                {
                    return null;
                }
            }

            var variables = new HashSet<IrVarId>();
            if (signature.Receiver is { } receiver &&
                !variables.Add(receiver))
            {
                return null;
            }
            foreach (var parameter in signature.Parameters)
            {
                if (!variables.Add(parameter))
                {
                    return null;
                }
            }
            return variables.Contains(signature.Result) ? null : variables;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool ValidateEnvironment(
        IrFactory factory,
        HashSet<IrVarId> inputs,
        IReadOnlyDictionary<IrVarId, IrTerm> environment)
    {
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

    private sealed class Run(
        IrProgram program,
        IrSummarySignature signature,
        ImmutableDictionary<IrVarId, IrTerm> initialEnvironment,
        IReadOnlyDictionary<IrInstructionId, IrRelationalSummary> calls,
        IrRelationalSummaryBuildLimits limits,
        bool mayThrow)
        : IrForwardExecutor(
            program,
            initialEnvironment,
            limits.MaximumExpressionDepth,
            limits.MaximumSymbolicOperations,
            CancellationToken.None)
    {
        private readonly List<IrTerm> _completions = [];
        private readonly List<IrTerm> _relations = [];
        private readonly List<IrVarId> _existentials = [];
        private readonly HashSet<IrMemberId> _dependencies = [];
        private readonly Dictionary<(
            IrSummaryOrigin Origin,
            string EvidenceCallIdentity,
            string EvidenceIdentity,
            string EvidenceSha256), IrSummaryProvenance> _dependencyProvenance = [];
        private bool _mayThrow = mayThrow;
        private IrSummaryAbstentionReason _reason;

        internal IrRelationalSummaryBuildResult Execute()
        {
            if (!Run())
            {
                return Abstain();
            }

            if (_relations.Count == 0)
            {
                _reason = IrSummaryAbstentionReason.UnsupportedBody;
                return Abstain();
            }

            var normalCompletion = IrSemanticTerms.Disjoin(
                Factory,
                _completions);
            var normalRelation = IrSemanticTerms.Disjoin(
                Factory,
                _relations);
            if (!Supported(normalCompletion) || !Supported(normalRelation))
            {
                return Abstain();
            }

            var summary = new IrRelationalSummary(
                Factory,
                signature,
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

        protected override void OnThrowSite(IrTerm predicate, IrTerm completes)
        {
            _mayThrow = true;
        }

        // Loop-header and merge unknowns are existential in the relation.
        protected override void OnFreshVariable(IrVarId variable)
        {
            _existentials.Add(variable);
        }

        protected override bool ExecuteCall(
            IrCallInstruction call,
            ref IrTerm predicate,
            ref ImmutableDictionary<IrVarId, IrTerm> environment)
        {
            if (!calls.TryGetValue(call.Id, out var dependency) ||
                dependency.Signature.Member != call.Member ||
                !call.Target.HasValue ||
                ApplyCall(
                    call,
                    dependency,
                    environment,
                    predicate) is not { } application)
            {
                if (_reason == IrSummaryAbstentionReason.None &&
                    Failure == IrForwardFailure.None)
                {
                    _reason = calls.ContainsKey(call.Id)
                        ? IrSummaryAbstentionReason.InvalidSignature
                        : IrSummaryAbstentionReason.MissingDependency;
                }
                return false;
            }

            predicate = application.Predicate;
            environment = environment.SetItem(
                call.Target.Value,
                Factory.Variable(application.Result));
            return true;
        }

        protected override bool ExecuteReturn(
            IrReturnInstruction returned,
            IrTerm predicate,
            ImmutableDictionary<IrVarId, IrTerm> environment)
        {
            return AddReturn(returned, predicate, environment);
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
                    signature.Result).Type ||
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
                    Factory.Variable(signature.Result),
                    value));
            if (!Supported(completion) || !Supported(relation))
            {
                return false;
            }

            _completions.Add(completion);
            _relations.Add(relation);
            return true;
        }

        private IrRelationalSummaryBuildResult Abstain()
        {
            return Failed(_reason != IrSummaryAbstentionReason.None
                ? _reason
                : Failure switch
                {
                    IrForwardFailure.ResourceLimit => IrSummaryAbstentionReason.ResourceLimit,
                    IrForwardFailure.ExpressionDepth => IrSummaryAbstentionReason.ExpressionDepth,
                    IrForwardFailure.CyclicControlFlow => IrSummaryAbstentionReason.CyclicControlFlow,
                    IrForwardFailure.UnsupportedInstruction => IrSummaryAbstentionReason.UnsupportedInstruction,
                    _ => IrSummaryAbstentionReason.UnsupportedBody
                });
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
