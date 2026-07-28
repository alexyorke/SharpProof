namespace SharpProof.Contracts;

internal sealed class ContractExpressionBinder {
    private readonly IrFactory _factory;
    private readonly ContractApiSymbols _api;
    private readonly IMethodSymbol _source;
    private readonly RoslynOperationLowerer _lowerer;
    private readonly Func<ITypeSymbol?, ITypeSymbol?> _specializeType;
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
        _specializeType = specializeType ?? (static type => type);
        _lowerer = new RoslynOperationLowerer(factory) {
            TypeSpecializer = _specializeType
        };
    }

    internal ImmutableArray<FrontendVariableBinding> VariableBindings =>
        [.. _variables.Select(static pair =>
            new FrontendVariableBinding(pair.Key, pair.Value))];

    internal ImmutableArray<IrVarId> ReceiverVariables =>
        [.. _receiverVariables];

    internal IReadOnlyDictionary<IrVarId, IrVarId> PreStateVariables => _preState;

    internal IrVarId? ResultVariable => _result;

    internal ExpressionBindingResult Bind(IOperation operation) => BindCore(operation);

    private ExpressionBindingResult BindCore(IOperation operation) {
        if (operation is IBinaryOperation nullComparison &&
            nullComparison.OperatorMethod == null &&
            !nullComparison.IsLifted &&
            nullComparison.OperatorKind is (
                BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals) &&
            TryGetNullComparisonValue(nullComparison, out var comparedValue)) {
            var value = BindCore(comparedValue);
            if (!value.IsSuccess) return value;
            var operationKind = nullComparison.OperatorKind ==
                BinaryOperatorKind.Equals
                ? IrBinaryOperator.Equal
                : IrBinaryOperator.NotEqual;
            return TryCreate(() => _factory.Binary(
                operationKind,
                value.Term!,
                _factory.Null(value.Term!.Type)));
        }
        if (!ContainsIntrinsic(operation))
            return BindWithFrontend(operation);

        if (operation is IInvocationOperation invocation) {
            if (_api.IsResult(invocation.TargetMethod)) {
                _result ??= _factory.CreateVariable(
                    "source-result",
                    _lowerer.GetTypeId(_source.ReturnType));
                return ExpressionBindingResult.Success(
                    _factory.Variable(_result.Value));
            }
            if (_api.IsOld(invocation.TargetMethod)) {
                var value = BindCore(invocation.Arguments[0].Value);
                if (!value.IsSuccess) return value;
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
                return ExpressionBindingResult.Success(
                    IrSubstitution.Substitute(
                        _factory,
                        value.Term!,
                        substitutions));
            }
        }

        if (operation is IPropertyReferenceOperation property &&
            property.Instance is { Type: IArrayTypeSymbol { Rank: 1 } } instance &&
            property.Property.Name == nameof(Array.Length) &&
            property.Type?.SpecialType == SpecialType.System_Int32) {
            var value = BindCore(instance);
            if (!value.IsSuccess) return value;
            return TryCreate(() => _factory.Length(value.Term!));
        }

        if (operation is IConversionOperation conversion) {
            var operand = BindCore(conversion.Operand);
            if (!operand.IsSuccess) return operand;
            var targetType = _lowerer.GetTypeId(conversion.Type);
            if (SymbolEqualityComparer.Default.Equals(
                    conversion.Operand.Type,
                    conversion.Type))
                return operand;
            if (targetType == operand.Term!.Type &&
                RoslynOperatorSemantics.IsValuePreservingIntegerConversion(
                    conversion.Operand.Type?.SpecialType ?? SpecialType.None,
                    conversion.Type?.SpecialType ?? SpecialType.None))
                return operand;
            return ExpressionBindingResult.Unsupported;
        }
        if (operation is IUnaryOperation unary && unary.OperatorMethod == null) {
            var operand = BindCore(unary.Operand);
            if (!operand.IsSuccess) return operand;
            var mapped = unary.OperatorKind switch {
                UnaryOperatorKind.Not => IrUnaryOperator.Not,
                UnaryOperatorKind.Minus => IrUnaryOperator.Negate,
                _ => (IrUnaryOperator?)null
            };
            if (!mapped.HasValue) return ExpressionBindingResult.Unsupported;
            if (mapped == IrUnaryOperator.Negate &&
                (unary.Type?.SpecialType != SpecialType.System_Int64 ||
                 !unary.IsChecked))
                return ExpressionBindingResult.Unsupported;
            return TryCreate(() =>
                _factory.Unary(mapped.Value, operand.Term!));
        }
        if (operation is IBinaryOperation binary &&
            binary.OperatorMethod == null &&
            !binary.IsLifted) {
            if (!RoslynOperatorSemantics.SupportsBuiltInOperands(
                    binary.OperatorKind,
                    binary.LeftOperand.Type,
                    binary.RightOperand.Type))
                return ExpressionBindingResult.Unsupported;
            var left = BindCore(binary.LeftOperand);
            if (!left.IsSuccess) return left;
            var right = BindCore(binary.RightOperand);
            if (!right.IsSuccess) return right;
            var mapped = RoslynOperatorSemantics.MapBinary(
                binary.OperatorKind,
                binary.Type?.SpecialType ?? SpecialType.None);
            if (!mapped.HasValue) return ExpressionBindingResult.Unsupported;
            if (mapped == IrBinaryOperator.StringConcat ||
                RoslynOperatorSemantics.IsIntegerArithmetic(
                    binary.OperatorKind) &&
                (binary.Type?.SpecialType != SpecialType.System_Int64 ||
                 RoslynOperatorSemantics.RequiresCheckedArithmetic(
                     binary.OperatorKind) &&
                 !binary.IsChecked))
                return ExpressionBindingResult.Unsupported;
            return TryCreate(() =>
                _factory.Binary(mapped.Value, left.Term!, right.Term!));
        }
        if (operation is IConditionalOperation conditional &&
            conditional.WhenFalse != null) {
            var condition = BindCore(conditional.Condition);
            if (!condition.IsSuccess) return condition;
            var whenTrue = BindCore(conditional.WhenTrue);
            if (!whenTrue.IsSuccess) return whenTrue;
            var whenFalse = BindCore(conditional.WhenFalse);
            if (!whenFalse.IsSuccess) return whenFalse;
            return TryCreate(() => _factory.Conditional(
                condition.Term!,
                whenTrue.Term!,
                whenFalse.Term!));
        }
        return ExpressionBindingResult.Unsupported;
    }

    private static bool TryGetNullComparisonValue(
        IBinaryOperation operation,
        out IOperation value) {
        var left = UnwrapImplicitConversions(operation.LeftOperand);
        var right = UnwrapImplicitConversions(operation.RightOperand);
        value = IsNullConstant(right)
            ? left
            : IsNullConstant(left)
                ? right
                : null!;
        return value != null;
    }

    private static IOperation UnwrapImplicitConversions(IOperation operation) {
        while (operation is IConversionOperation {
            IsImplicit: true,
            OperatorMethod: null
        } conversion)
            operation = conversion.Operand;
        return operation;
    }

    private static bool IsNullConstant(IOperation operation) =>
        operation.ConstantValue is { HasValue: true, Value: null };

    private ExpressionBindingResult BindWithFrontend(IOperation operation) {
        var result = _lowerer.Lower(operation);
        if (!result.IsExact)
            return ExpressionBindingResult.Unsupported;
        foreach (var binding in result.Variables)
            _variables[binding.Symbol] = binding.Variable;

        var boundVariables = new HashSet<IrVarId>(
            result.Variables.Select(static binding => binding.Variable));
        foreach (var variable in IrTraversal.CollectVariables(result.Term)) {
            if (boundVariables.Contains(variable)) continue;
            if (_source.IsStatic)
                return ExpressionBindingResult.Unsupported;
            _receiverVariables.Add(variable);
        }
        return ExpressionBindingResult.Success(result.Term);
    }

    private static ExpressionBindingResult TryCreate(Func<IrTerm> create) {
        try {
            return ExpressionBindingResult.Success(create());
        }
        catch (ArgumentException) {
            return ExpressionBindingResult.Unsupported;
        }
    }

    private bool ContainsIntrinsic(IOperation root) =>
        root.DescendantsAndSelf()
            .OfType<IInvocationOperation>()
            .Any(invocation =>
                _api.IsResult(invocation.TargetMethod) ||
                _api.IsOld(invocation.TargetMethod));

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
