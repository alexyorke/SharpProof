namespace SharpProof.Analyzer;

internal sealed class AnalyzerEffectCallPreconditionPolicy(
    ContractBinder binder,
    ContractClauseInventoryBuilder clauses,
    IrFactory factory,
    ConservativeEffectCallPreconditionPolicy fallback)
    : IEffectCallPreconditionPolicy
{
    private readonly ContractBinder _binder =
        ArgumentNullGuard.NotNull(binder, nameof(binder));
    private readonly ContractClauseInventoryBuilder
        _clauses =
            ArgumentNullGuard.NotNull(clauses, nameof(clauses));
    private readonly IrFactory _factory =
        ArgumentNullGuard.NotNull(factory, nameof(factory));
    private readonly ConservativeEffectCallPreconditionPolicy
        _fallback =
            ArgumentNullGuard.NotNull(fallback, nameof(fallback));

    public EffectCallPreconditionStatus Assess(
        EffectCallPreconditionContext context)
    {
        if (HasInvalidEntryInventory(context.Target))
        {
            return EffectCallPreconditionStatus.NotProven;
        }

        var binding = _binder.BindRequires(
            context.Target);
        if (binding is not
            { IsSuccess: true, Contracts: { } contracts })
        {
            return _fallback.HasPotentialPreconditions(
                    context.Target)
                ? EffectCallPreconditionStatus.NotProven
                : EffectCallPreconditionStatus.None;
        }

        var requires = contracts.Clauses
            .Where(static clause =>
                clause.Kind ==
                BoundContractKind.Requires)
            .ToImmutableArray();
        if (requires.IsEmpty)
        {
            return _fallback
                .HasPotentialDirectOrClosedPreconditions(
                    context.Target)
                ? EffectCallPreconditionStatus.NotProven
                : EffectCallPreconditionStatus.None;
        }

        if (AssessEntry(context.Caller) ==
            EffectCallPreconditionStatus.NotProven)
        {
            return EffectCallPreconditionStatus.NotProven;
        }

        if (context.Flow == null)
        {
            return EffectCallPreconditionStatus.NotProven;
        }

        var variables = new Dictionary<
            IrVarId,
            ManagedAbstractValue>();
        var definitelyStrings =
            new HashSet<IrVarId>();
        foreach (var variable in
                 binding.Contracts.Variables.Where(
                     static variable =>
                         variable.Role is
                             BoundContractVariableRole
                                 .Receiver or
                             BoundContractVariableRole
                                 .Parameter))
        {
            var actual = variable.Role ==
                BoundContractVariableRole.Receiver
                    ? context.Receiver
                    : variable.Ordinal >= 0 &&
                      variable.Ordinal <
                      context.Arguments.Length
                        ? context.Arguments[
                            variable.Ordinal]
                        : null;
            if (actual == null ||
                !TryEvaluateActual(
                    context,
                    variable,
                    actual,
                    out var value))
            {
                return EffectCallPreconditionStatus
                    .NotProven;
            }

            if (variable.Role ==
                    BoundContractVariableRole.Receiver &&
                actual.Type is { IsReferenceType: true } &&
                !value.IsDefinitelyNonNull)
            {
                return EffectCallPreconditionStatus
                    .NotProven;
            }

            variables.Add(variable.Variable, value);
            if (IsDefinitelyString(actual))
            {
                definitelyStrings.Add(
                    variable.Variable);
            }
        }

        return requires.All(clause =>
            ManagedContractFacts.Evaluate(
                    clause.Condition,
                    variables,
                    definitelyStrings,
                    _factory.StringType)
                .TryGetBoolean(out var established) &&
            established)
                ? EffectCallPreconditionStatus.Proven
                : EffectCallPreconditionStatus.NotProven;
    }

    private static bool TryEvaluateActual(
        EffectCallPreconditionContext context,
        BoundContractVariable variable,
        IOperation actual,
        out ManagedAbstractValue value)
    {
        var evaluation = GetArgumentEvaluation(
            context,
            variable,
            actual);
        if (evaluation ==
            CallArgumentEvaluation.Unsupported)
        {
            value = default;
            return false;
        }

        return evaluation ==
                CallArgumentEvaluation.CallEntry
            ? context.Flow!.TryEvaluateAtOrigin(
                context.Origin,
                actual,
                out value)
            : context.Flow!.TryEvaluate(
                        context.Origin,
                        actual,
                        out value);
    }

    private static CallArgumentEvaluation
        GetArgumentEvaluation(
            EffectCallPreconditionContext context,
            BoundContractVariable variable,
            IOperation actual)
    {
        if (variable.Role !=
                BoundContractVariableRole.Parameter)
        {
            return CallArgumentEvaluation.Snapshot;
        }

        if (variable.Ordinal < 0 ||
            variable.Ordinal >=
                context.Target.Parameters.Length)
        {
            return CallArgumentEvaluation.Unsupported;
        }

        var parameter =
            context.Target.Parameters[variable.Ordinal];
        var isReducedReceiver =
            context.Origin is IInvocationOperation
            {
                TargetMethod.ReducedFrom: not null
            } &&
            variable.Ordinal == 0;
        if (isReducedReceiver)
        {
            return CallArgumentAliasPolicy.Classify(
                parameter.RefKind,
                actual,
                argumentSyntax: null,
                isSyntheticReceiver: true);
        }

        var argument = FindArgument(
            context.Origin,
            variable.Ordinal,
            context.Origin is IInvocationOperation
            {
                TargetMethod.ReducedFrom: not null
            });
        return CallArgumentAliasPolicy.Classify(
            parameter.RefKind,
            actual,
            argument?.Syntax,
            isSyntheticReceiver:
                context.Target.IsExtensionMethod &&
                variable.Ordinal == 0 &&
                argument?.Syntax is not ArgumentSyntax);
    }

    private static IArgumentOperation? FindArgument(
        IOperation origin,
        int normalizedOrdinal,
        bool isReducedExtension)
    {
        var ordinal = isReducedExtension
            ? normalizedOrdinal - 1
            : normalizedOrdinal;
        if (ordinal < 0)
        {
            return null;
        }

        var arguments = origin switch
        {
            IInvocationOperation invocation =>
                invocation.Arguments,
            IObjectCreationOperation creation =>
                creation.Arguments,
            _ => []
        };
        return arguments.FirstOrDefault(
            argument =>
                argument.Parameter?.Ordinal ==
                ordinal);
    }

    public EffectCallPreconditionStatus AssessEntry(
        IMethodSymbol method)
    {
        if (HasInvalidEntryInventory(method))
        {
            return EffectCallPreconditionStatus.NotProven;
        }

        var binding = _binder.BindRequires(method);
        if (binding is not
            { IsSuccess: true, Contracts: { } })
        {
            return _fallback.HasPotentialPreconditions(
                    method)
                ? EffectCallPreconditionStatus.NotProven
                : EffectCallPreconditionStatus.None;
        }

        if (method is
            {
                IsAbstract: false,
                IsExtern: false,
                DeclaringSyntaxReferences: { IsEmpty: false }
            } &&
            binding.Contracts.Clauses.Any(
                static clause =>
                    clause.Kind ==
                    BoundContractKind.Requires))
        {
            return EffectCallPreconditionStatus.Proven;
        }

        return _fallback.HasPotentialPreconditions(method)
            ? EffectCallPreconditionStatus.NotProven
            : EffectCallPreconditionStatus.None;
    }

    private bool HasInvalidEntryInventory(
        IMethodSymbol method)
    {
        var inventory = _clauses.Create(method);
        return inventory.HasRejectedContractApiUsage ||
            inventory.Clauses.Any(static clause =>
                clause.Kind ==
                    BoundContractKind.Requires &&
                clause.Placement !=
                    ContractClausePlacement
                        .NestedCallable &&
                !clause.IsValid);
    }

    private static bool IsDefinitelyString(
        IOperation operation)
    {
        return DefiniteOperationFacts
            .IsDefinitelyString(operation);
    }
}
