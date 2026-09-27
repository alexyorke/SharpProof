namespace SharpProof.Contracts;

internal sealed class ContractExpressionBinder
{
    private readonly IrFactory _factory;
    private readonly ContractApiSymbols _api;
    private readonly IMethodSymbol _source;
    private readonly RoslynOperationLowerer _lowerer;
    private readonly HashSet<IrVarId> _boundVariables = [];
    private readonly HashSet<IrVarId> _receiverVariables = [];
    private readonly Dictionary<IrVarId, IrVarId> _preState = [];
    private readonly HashSet<IrVarId> _preStateValues = [];
    private ImmutableArray<FrontendVariableBinding> _variableBindings;
    private IrVarId? _result;

    internal ContractExpressionBinder(
        IrFactory factory,
        ContractApiSymbols api,
        IMethodSymbol source,
        Func<ITypeSymbol?, ITypeSymbol?>? specializeType = null)
    {
        _factory = factory;
        _api = api;
        _source = source;
        _lowerer = new RoslynOperationLowerer(factory)
        {
            TypeSpecializer = specializeType ?? (static type => type),
            CustomLowering = BindIntrinsic
        };
    }

    internal ImmutableArray<FrontendVariableBinding> VariableBindings
    {
        get => _variableBindings.IsDefault ? [] : _variableBindings;
    }

    internal ImmutableArray<IrVarId> ReceiverVariables =>
        [.. _receiverVariables];

    internal IReadOnlyDictionary<IrVarId, IrVarId> PreStateVariables => _preState;

    internal IrVarId? ResultVariable => _result;

    private (bool Handled, IrTerm? Term) BindIntrinsic(IOperation operation)
    {
        if (operation is not IInvocationOperation invocation)
        {
            return default;
        }

        if (_api.IsResult(invocation.TargetMethod))
        {
            if (!_lowerer.IsSupportedValueDomain(_source.ReturnType))
            {
                return (true, null);
            }

            _result ??= _factory.CreateVariable(
                "source-result",
                _lowerer.GetTypeId(_source.ReturnType));
            return (true, _factory.Variable(_result.Value));
        }
        if (!_api.IsOld(invocation.TargetMethod))
        {
            return default;
        }

        if (invocation.Arguments.Length != 1)
        {
            return (true, null);
        }

        var value = Bind(invocation.Arguments[0].Value);
        if (!value.IsSuccess)
        {
            return (true, null);
        }

        var substitutions = new Dictionary<IrVarId, IrTerm>();
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
                _preStateValues.Add(preState);
            }
            substitutions[variable] = _factory.Variable(preState);
        }
        return (true, IrSubstitution.Substitute(
            _factory,
            value.Term!,
            substitutions));
    }

    internal ExpressionBindingResult Bind(IOperation operation)
    {
        var result = _lowerer.Lower(operation);
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

            _receiverVariables.Add(variable);
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
    internal ImmutableHashSet<IrVarId> Variables { get; } = variables;
    internal bool IsSuccess => Failure == ContractBindingFailure.None;

    internal static ExpressionBindingResult Success(
        IrTerm term,
        ImmutableHashSet<IrVarId> variables)
    {
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
