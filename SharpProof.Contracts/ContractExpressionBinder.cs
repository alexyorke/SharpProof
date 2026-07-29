namespace SharpProof.Contracts;

internal sealed class ContractExpressionBinder {
    private readonly IrFactory _factory;
    private readonly ContractApiSymbols _api;
    private readonly IMethodSymbol _source;
    private readonly RoslynOperationLowerer _lowerer;
    private readonly Dictionary<ISymbol, IrVarId> _variables =
        new(SymbolEqualityComparer.Default);
    private readonly HashSet<IrVarId> _receiverVariables = [];
    private readonly Dictionary<IrVarId, IrVarId> _preState = [];
    private IrVarId? _result;

    internal ContractExpressionBinder(
        IrFactory factory,
        ContractApiSymbols api,
        IMethodSymbol source,
        Func<ITypeSymbol?, ITypeSymbol?>? specializeType = null) {
        _factory = factory;
        _api = api;
        _source = source;
        _lowerer = new RoslynOperationLowerer(factory) {
            TypeSpecializer = specializeType ?? (static type => type),
            CustomLowering = BindIntrinsic
        };
    }

    internal ImmutableArray<FrontendVariableBinding> VariableBindings =>
        [.. _variables.Select(static pair =>
            new FrontendVariableBinding(pair.Key, pair.Value))];

    internal ImmutableArray<IrVarId> ReceiverVariables =>
        [.. _receiverVariables];

    internal IReadOnlyDictionary<IrVarId, IrVarId> PreStateVariables => _preState;

    internal IrVarId? ResultVariable => _result;

    internal ExpressionBindingResult Bind(IOperation operation) =>
        BindWithFrontend(operation);

    private (bool Handled, IrTerm? Term) BindIntrinsic(IOperation operation) {
        if (operation is not IInvocationOperation invocation)
            return default;
        if (_api.IsResult(invocation.TargetMethod)) {
            _result ??= _factory.CreateVariable(
                "source-result",
                _lowerer.GetTypeId(_source.ReturnType));
            return (true, _factory.Variable(_result.Value));
        }
        if (!_api.IsOld(invocation.TargetMethod))
            return default;
        if (invocation.Arguments.Length != 1)
            return (true, null);
        var value = Bind(invocation.Arguments[0].Value);
        if (!value.IsSuccess)
            return (true, null);
        var substitutions = new Dictionary<IrVarId, IrTerm>();
        foreach (var variable in IrTraversal.CollectVariables(value.Term!)) {
            if (!_preState.TryGetValue(variable, out var preState)) {
                var info = _factory.GetVariableInfo(variable);
                preState = _factory.CreateVariable(
                    "source-pre:" +
                    variable.Value.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    info.Type);
                _preState.Add(variable, preState);
            }
            substitutions[variable] = _factory.Variable(preState);
        }
        return (true, IrSubstitution.Substitute(
            _factory,
            value.Term!,
            substitutions));
    }

    private ExpressionBindingResult BindWithFrontend(IOperation operation) {
        var result = _lowerer.Lower(operation);
        if (!result.IsExact)
            return ExpressionBindingResult.Unsupported;
        foreach (var binding in result.Variables)
            _variables[binding.Symbol] = binding.Variable;

        var boundVariables = new HashSet<IrVarId>(
            result.Variables.Select(static binding => binding.Variable));
        foreach (var variable in IrTraversal.CollectVariables(result.Term)) {
            if (boundVariables.Contains(variable) ||
                variable == _result ||
                _preState.ContainsValue(variable))
                continue;
            if (_source.IsStatic)
                return ExpressionBindingResult.Unsupported;
            _receiverVariables.Add(variable);
        }
        return ExpressionBindingResult.Success(result.Term);
    }

}

internal readonly struct ExpressionBindingResult(
    IrTerm? term,
    ContractBindingFailure failure) {
    internal IrTerm? Term { get; } = term;
    internal ContractBindingFailure Failure { get; } = failure;
    internal bool IsSuccess => Failure == ContractBindingFailure.None;

    internal static ExpressionBindingResult Success(IrTerm term) =>
        new(term, ContractBindingFailure.None);

    internal static ExpressionBindingResult Fail(
        ContractBindingFailure failure) =>
        new(null, failure);

    internal static ExpressionBindingResult Unsupported { get; } =
        Fail(ContractBindingFailure.UnsupportedExpression);
}
