namespace SharpProof.Contracts;

internal sealed class ContractExpressionBinder
{
    private readonly IrFactory _factory;
    private readonly ContractApiSymbols _api;
    private readonly IMethodSymbol _source;
    private readonly RoslynOperationLowerer _lowerer;
    private readonly ImmutableHashSet<IrVarId>.Builder _boundVariables = ImmutableHashSet.CreateBuilder<IrVarId>();
    private readonly ImmutableHashSet<IrVarId>.Builder _receiverVariables = ImmutableHashSet.CreateBuilder<IrVarId>();
    private readonly ImmutableDictionary<IrVarId, IrVarId>.Builder _preState = ImmutableDictionary.CreateBuilder<IrVarId, IrVarId>();
    private readonly ImmutableArray<KeyValuePair<IrVarId, IrVarId>>.Builder _preStateOrder = ImmutableArray.CreateBuilder<KeyValuePair<IrVarId, IrVarId>>();
    private readonly ImmutableHashSet<IrVarId>.Builder _preStateValues = ImmutableHashSet.CreateBuilder<IrVarId>();
    private readonly ImmutableArray<IrVarId>.Builder _receiverOrder = ImmutableArray.CreateBuilder<IrVarId>();
    private ImmutableArray<FrontendVariableBinding> _variableBindings;
    private IrVarId? _result;

    internal ContractExpressionBinder(
        IrFactory factory,
        ContractApiSymbols api,
        IMethodSymbol source,
        Func<ITypeSymbol?, ITypeSymbol?>? specializeType = null)
    {
        _factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        _api = ArgumentNullGuard.NotNull(api, nameof(api));
        _source = ArgumentNullGuard.NotNull(source, nameof(source));
        _lowerer = RoslynOperationLowerer.CreateConfigured(
            factory, specializeType ?? (static type => type), BindIntrinsic);
    }

    internal ImmutableArray<FrontendVariableBinding> VariableBindings
    {
        get => _variableBindings.IsDefault ? [] : _variableBindings;
    }

    internal ImmutableArray<IrVarId> ReceiverVariables =>
        _receiverOrder.ToImmutable();

    internal ImmutableDictionary<IrVarId, IrVarId> PreStateVariables => _preState.ToImmutable();
    internal ImmutableArray<KeyValuePair<IrVarId, IrVarId>> PreStateVariablePairs => _preStateOrder.ToImmutable();

    internal IrVarId? ResultVariable => _result;

    private (bool Handled, IrTerm? Term) BindIntrinsic(IOperation operation)
    {
        if (operation is not IInvocationOperation invocation)
        {
            return default;
        }

        if (_api.IsResult(invocation.TargetMethod))
        {
            if (!_lowerer.IsSupportedValueDomain(invocation.Type))
            {
                return (true, null);
            }

            _result ??= _factory.CreateVariable(
                "source-result",
                _lowerer.GetTypeId(invocation.Type));
            return (true, _factory.Variable(_result.Value));
        }
        if (!_api.IsOld(invocation.TargetMethod))
        {
            return default;
        }

        return ContractInvocationShape.ApplySingleArgument(
            invocation, this,
            static (call, binder) => binder is null ? throw new System.ArgumentNullException(nameof(binder)) : call is null ? throw new System.ArgumentNullException(nameof(call)) : (binder.BindOldArgument(call.Arguments[0].Value)),
            (true, (IrTerm?)null));
    }

    private (bool Handled, IrTerm? Term) BindOldArgument(IOperation argument)
    {
        argument = RepositoryParameterGuard.NotNull(argument, nameof(argument));
        var value = Bind(argument);
        if (!value.IsSuccess)
        {
            return (true, null);
        }

        var substitutions = ImmutableDictionary.CreateBuilder<IrVarId, IrTerm>();
        foreach (var variable in value.Variables)
        {
            if (!_preState.TryGetValue(variable, out var preState))
            {
                var info = _factory.GetVariableInfo(variable);
                preState = _factory.CreateVariable(
                    "source-pre:" +
                    variable.Value.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    info.Type);
                _preState.Add(variable, preState);
                _preStateOrder.Add(new(variable, preState));
                _preStateValues.Add(preState);
            }
            substitutions[variable] = _factory.Variable(preState);
        }
        return (true, IrSubstitution.Substitute(
            _factory,
            value.Term ?? throw new InvalidOperationException(
                "A successful old() binding must carry a term."),
            substitutions.ToImmutable()));
    }

    internal ExpressionBindingResult Bind(IOperation operation)
    {
        var result = _lowerer.Lower(ArgumentNullGuard.NotNull(operation, nameof(operation)));
        if (!result.IsExact)
        {
            return ExpressionBindingResult.Unsupported;
        }

        _variableBindings = result.Variables;
        foreach (var binding in result.Variables)
        {
            _boundVariables.Add(binding.Variable);
        }

        var variables = IrTraversal.CollectVariables(result.Term);
        foreach (var variable in variables)
        {
            if (_boundVariables.Contains(variable) ||
                variable == _result ||
                _preStateValues.Contains(variable))
            {
                continue;
            }

            if (_source.IsStatic)
            {
                return ExpressionBindingResult.Unsupported;
            }

            if (_receiverVariables.Add(variable))
            {
                _receiverOrder.Add(variable);
            }
        }
        return ExpressionBindingResult.Success(result.Term, variables);
    }

}

internal readonly struct ExpressionBindingResult(
    IrTerm? term,
    ContractBindingFailure failure,
    ImmutableHashSet<IrVarId> variables)
{
    internal IrTerm? Term { get; } = term;
    internal ContractBindingFailure Failure { get; } = failure;
    internal ImmutableHashSet<IrVarId> Variables { get; } = (variables ?? throw new System.ArgumentNullException(nameof(variables)));
    internal bool IsSuccess => Failure == ContractBindingFailure.None;

    internal static ExpressionBindingResult Success(
        IrTerm term,
        ImmutableHashSet<IrVarId> variables)
    {
        term = RepositoryParameterGuard.NotNull(term, nameof(term));
        variables = RepositoryParameterGuard.NotNull(variables, nameof(variables));
        return new(term, ContractBindingFailure.None, variables);
    }

    internal static ExpressionBindingResult Fail(
        ContractBindingFailure failure)
    {
        return new(null, failure, ImmutableHashSet<IrVarId>.Empty);
    }

    internal static ExpressionBindingResult Unsupported
    {
        get;
    } =
        Fail(ContractBindingFailure.UnsupportedExpression);
}
