namespace SharpProof.Contracts;

internal sealed class ContractExpressionBinder {
    private readonly IrFactory _factory;
    private readonly ContractApiSymbols _api;
    private readonly IMethodSymbol _source;
    private readonly ContractTypeMapper _types;
    private readonly RoslynOperationLowerer _lowerer;
    private readonly Dictionary<ISymbol, IrVarId> _variables =
        new(SymbolEqualityComparer.Default);
    private readonly HashSet<IrVarId> _receiverVariables = [];
    private readonly Dictionary<IrVarId, IrVarId> _preState = [];
    private IrVarId? _result;

    internal ContractExpressionBinder(
        IrFactory factory,
        ContractApiSymbols api,
        IMethodSymbol source) {
        _factory = factory;
        _api = api;
        _source = source;
        _types = new ContractTypeMapper(factory);
        _lowerer = new RoslynOperationLowerer(factory);
    }

    internal ImmutableArray<FrontendVariableBinding> VariableBindings =>
        [.. _variables.Select(static pair =>
            new FrontendVariableBinding(pair.Key, pair.Value))];

    internal ImmutableArray<IrVarId> ReceiverVariables =>
        [.. _receiverVariables];

    internal IReadOnlyDictionary<IrVarId, IrVarId> PreStateVariables =>
        _preState;

    internal IrVarId? ResultVariable => _result;

    internal ExpressionBindingResult Bind(
        IOperation operation,
        BoundContractKind clauseKind) =>
        BindCore(operation, clauseKind, insideOld: false);

    private ExpressionBindingResult BindCore(
        IOperation operation,
        BoundContractKind clauseKind,
        bool insideOld) {
        if (!ContainsIntrinsic(operation))
            return BindWithFrontend(operation);

        if (operation is IInvocationOperation invocation) {
            if (_api.IsResult(invocation.TargetMethod)) {
                if (clauseKind != BoundContractKind.Ensures)
                    return ExpressionBindingResult.Fail(
                        ContractBindingFailure.ResultOutsideEnsures);
                if (insideOld)
                    return ExpressionBindingResult.Fail(
                        ContractBindingFailure.UnsupportedExpression);
                if (invocation.Arguments.Length != 0 ||
                    _source.ReturnsVoid ||
                    _source.MethodKind == MethodKind.Constructor ||
                    invocation.Type == null ||
                    !SymbolEqualityComparer.IncludeNullability.Equals(
                        invocation.Type,
                        _source.ReturnType))
                    return ExpressionBindingResult.Fail(
                        ContractBindingFailure.InvalidIntrinsicSignature);
                _result ??= _factory.CreateVariable(
                    "source-result",
                    _types.GetTypeId(_source.ReturnType));
                return ExpressionBindingResult.Success(
                    _factory.Variable(_result.Value));
            }
            if (_api.IsOld(invocation.TargetMethod)) {
                if (clauseKind != BoundContractKind.Ensures)
                    return ExpressionBindingResult.Fail(
                        ContractBindingFailure.OldOutsideEnsures);
                if (insideOld)
                    return ExpressionBindingResult.Fail(
                        ContractBindingFailure.NestedOld);
                if (invocation.Arguments.Length != 1)
                    return ExpressionBindingResult.Fail(
                        ContractBindingFailure.InvalidIntrinsicSignature);
                var value = BindCore(
                    invocation.Arguments[0].Value,
                    clauseKind,
                    insideOld: true);
                if (!value.IsSuccess) return value;
                var substitutions = new Dictionary<IrVarId, IrTerm>();
                foreach (var variable in CollectVariables(value.Term!)) {
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

        if (operation is IBinaryOperation nullComparison &&
            nullComparison.OperatorMethod == null &&
            !nullComparison.IsLifted &&
            nullComparison.OperatorKind is (
                BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals) &&
            TryGetResultNullOperands(nullComparison, out var resultInvocation)) {
            var value = BindCore(resultInvocation, clauseKind, insideOld);
            if (!value.IsSuccess) return value;
            try {
                var operationKind = nullComparison.OperatorKind ==
                    BinaryOperatorKind.Equals
                    ? IrBinaryOperator.Equal
                    : IrBinaryOperator.NotEqual;
                return ExpressionBindingResult.Success(
                    _factory.Binary(
                        operationKind, value.Term!,
                        _factory.Null(value.Term!.Type)));
            }
            catch (ArgumentException) {
                return ExpressionBindingResult.Fail(
                    ContractBindingFailure.UnsupportedExpression);
            }
        }

        if (operation is IPropertyReferenceOperation property &&
            property.Instance is { Type: IArrayTypeSymbol { Rank: 1 } } instance &&
            property.Property.Name == nameof(Array.Length) &&
            property.Type?.SpecialType == SpecialType.System_Int32) {
            var value = BindCore(instance, clauseKind, insideOld);
            if (!value.IsSuccess) return value;
            try {
                return ExpressionBindingResult.Success(
                    _factory.Length(value.Term!));
            }
            catch (ArgumentException) {
                return ExpressionBindingResult.Fail(
                    ContractBindingFailure.UnsupportedExpression);
            }
        }

        if (operation is IConversionOperation conversion) {
            var operand = BindCore(conversion.Operand, clauseKind, insideOld);
            if (!operand.IsSuccess) return operand;
            var targetType = _types.GetTypeId(conversion.Type);
            if (SymbolEqualityComparer.Default.Equals(
                    conversion.Operand.Type,
                    conversion.Type))
                return operand;
            if (targetType == operand.Term!.Type &&
                IsValuePreservingIntegerConversion(
                    conversion.Operand.Type,
                    conversion.Type))
                return operand;
            return ExpressionBindingResult.Fail(
                ContractBindingFailure.UnsupportedExpression);
        }
        if (operation is IUnaryOperation unary && unary.OperatorMethod == null) {
            var operand = BindCore(unary.Operand, clauseKind, insideOld);
            if (!operand.IsSuccess) return operand;
            var mapped = unary.OperatorKind switch {
                UnaryOperatorKind.Not => IrUnaryOperator.Not,
                UnaryOperatorKind.Minus => IrUnaryOperator.Negate,
                _ => (IrUnaryOperator?)null
            };
            if (!mapped.HasValue) return ExpressionBindingResult.Fail(
                ContractBindingFailure.UnsupportedExpression);
            if (mapped == IrUnaryOperator.Negate &&
                (unary.Type?.SpecialType != SpecialType.System_Int64 ||
                 !unary.IsChecked))
                return ExpressionBindingResult.Fail(
                    ContractBindingFailure.UnsupportedExpression);
            try {
                return ExpressionBindingResult.Success(
                    _factory.Unary(mapped.Value, operand.Term!));
            }
            catch (ArgumentException) {
                return ExpressionBindingResult.Fail(
                    ContractBindingFailure.UnsupportedExpression);
            }
        }
        if (operation is IBinaryOperation binary &&
            binary.OperatorMethod == null &&
            !binary.IsLifted) {
            var left = BindCore(binary.LeftOperand, clauseKind, insideOld);
            if (!left.IsSuccess) return left;
            var right = BindCore(binary.RightOperand, clauseKind, insideOld);
            if (!right.IsSuccess) return right;
            var mapped = MapBinary(binary);
            if (!mapped.HasValue) return ExpressionBindingResult.Fail(
                ContractBindingFailure.UnsupportedExpression);
            if (mapped == IrBinaryOperator.StringConcat ||
                IsIntegerArithmetic(binary.OperatorKind) &&
                (binary.Type?.SpecialType != SpecialType.System_Int64 ||
                 RequiresCheckedArithmetic(binary.OperatorKind) &&
                 !binary.IsChecked))
                return ExpressionBindingResult.Fail(
                    ContractBindingFailure.UnsupportedExpression);
            try {
                return ExpressionBindingResult.Success(
                    _factory.Binary(mapped.Value, left.Term!, right.Term!));
            }
            catch (ArgumentException) {
                return ExpressionBindingResult.Fail(
                    ContractBindingFailure.UnsupportedExpression);
            }
        }
        if (operation is IConditionalOperation conditional &&
            conditional.WhenFalse != null) {
            var condition = BindCore(
                conditional.Condition,
                clauseKind,
                insideOld);
            if (!condition.IsSuccess) return condition;
            var whenTrue = BindCore(
                conditional.WhenTrue,
                clauseKind,
                insideOld);
            if (!whenTrue.IsSuccess) return whenTrue;
            var whenFalse = BindCore(
                conditional.WhenFalse,
                clauseKind,
                insideOld);
            if (!whenFalse.IsSuccess) return whenFalse;
            try {
                return ExpressionBindingResult.Success(
                    _factory.Conditional(
                        condition.Term!,
                        whenTrue.Term!,
                        whenFalse.Term!));
            }
            catch (ArgumentException) {
                return ExpressionBindingResult.Fail(
                    ContractBindingFailure.UnsupportedExpression);
            }
        }
        return ExpressionBindingResult.Fail(
            ContractBindingFailure.UnsupportedExpression);
    }

    private bool TryGetResultNullOperands(
        IBinaryOperation operation,
        out IInvocationOperation result) {
        var left = UnwrapImplicitConversions(operation.LeftOperand);
        var right = UnwrapImplicitConversions(operation.RightOperand);
        var match =
            left is IInvocationOperation leftInvocation &&
            _api.IsResult(leftInvocation.TargetMethod) &&
            IsNullConstant(right)
                ? leftInvocation
                : right is IInvocationOperation rightInvocation &&
                  _api.IsResult(rightInvocation.TargetMethod) &&
                  IsNullConstant(left)
                    ? rightInvocation
                    : null;
        result = match!;
        return match != null;
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
            return ExpressionBindingResult.Fail(
                ContractBindingFailure.UnsupportedExpression);
        foreach (var binding in result.Variables)
            _variables[binding.Symbol] = binding.Variable;

        var boundVariables = new HashSet<IrVarId>(
            result.Variables.Select(static binding => binding.Variable));
        foreach (var variable in CollectVariables(result.Term)) {
            if (boundVariables.Contains(variable)) continue;
            if (_source.IsStatic)
                return ExpressionBindingResult.Fail(
                    ContractBindingFailure.UnsupportedExpression);
            _receiverVariables.Add(variable);
        }
        return ExpressionBindingResult.Success(result.Term);
    }

    private bool ContainsIntrinsic(IOperation root) =>
        root.DescendantsAndSelf()
            .OfType<IInvocationOperation>()
            .Any(invocation =>
                _api.IsResult(invocation.TargetMethod) ||
                _api.IsOld(invocation.TargetMethod));

    private static IrBinaryOperator? MapBinary(IBinaryOperation operation) =>
        operation.OperatorKind switch {
            BinaryOperatorKind.Add
                when operation.Type?.SpecialType == SpecialType.System_String =>
                IrBinaryOperator.StringConcat,
            BinaryOperatorKind.Add => IrBinaryOperator.Add,
            BinaryOperatorKind.Subtract => IrBinaryOperator.Subtract,
            BinaryOperatorKind.Multiply => IrBinaryOperator.Multiply,
            BinaryOperatorKind.Divide => IrBinaryOperator.Divide,
            BinaryOperatorKind.Remainder => IrBinaryOperator.Remainder,
            BinaryOperatorKind.ConditionalAnd => IrBinaryOperator.AndAlso,
            BinaryOperatorKind.ConditionalOr => IrBinaryOperator.OrElse,
            BinaryOperatorKind.Equals => IrBinaryOperator.Equal,
            BinaryOperatorKind.NotEquals => IrBinaryOperator.NotEqual,
            BinaryOperatorKind.LessThan => IrBinaryOperator.LessThan,
            BinaryOperatorKind.LessThanOrEqual =>
                IrBinaryOperator.LessThanOrEqual,
            BinaryOperatorKind.GreaterThan => IrBinaryOperator.GreaterThan,
            BinaryOperatorKind.GreaterThanOrEqual =>
                IrBinaryOperator.GreaterThanOrEqual,
            _ => null
        };

    private static bool IsIntegerArithmetic(BinaryOperatorKind kind) =>
        kind is BinaryOperatorKind.Add or
            BinaryOperatorKind.Subtract or
            BinaryOperatorKind.Multiply or
            BinaryOperatorKind.Divide or
            BinaryOperatorKind.Remainder;

    private static bool RequiresCheckedArithmetic(BinaryOperatorKind kind) =>
        kind is BinaryOperatorKind.Add or
            BinaryOperatorKind.Subtract or
            BinaryOperatorKind.Multiply;

    private static bool IsValuePreservingIntegerConversion(
        ITypeSymbol? source,
        ITypeSymbol? target) {
        var sourceRange = GetIntegerRange(
            source?.SpecialType ?? SpecialType.None);
        var targetRange = GetIntegerRange(
            target?.SpecialType ?? SpecialType.None);
        return sourceRange.HasValue &&
               targetRange.HasValue &&
               sourceRange.Value.Minimum >= targetRange.Value.Minimum &&
               sourceRange.Value.Maximum <= targetRange.Value.Maximum;
    }

    private static IntegerRange? GetIntegerRange(SpecialType type) =>
        type switch {
            SpecialType.System_SByte =>
                new(sbyte.MinValue, sbyte.MaxValue),
            SpecialType.System_Byte =>
                new(byte.MinValue, byte.MaxValue),
            SpecialType.System_Int16 =>
                new(short.MinValue, short.MaxValue),
            SpecialType.System_UInt16 =>
                new(ushort.MinValue, ushort.MaxValue),
            SpecialType.System_Char =>
                new(char.MinValue, char.MaxValue),
            SpecialType.System_Int32 =>
                new(int.MinValue, int.MaxValue),
            SpecialType.System_UInt32 =>
                new(uint.MinValue, uint.MaxValue),
            SpecialType.System_Int64 =>
                new(long.MinValue, long.MaxValue),
            _ => null
        };

    private static ImmutableHashSet<IrVarId> CollectVariables(IrTerm root) {
        var result = ImmutableHashSet.CreateBuilder<IrVarId>();
        var pending = new Stack<IrTerm>();
        var visited = new HashSet<IrId>();
        pending.Push(root);
        while (pending.Count != 0) {
            var term = pending.Pop();
            if (!visited.Add(term.Id)) continue;
            switch (term) {
                case IrVariableTerm variable:
                    result.Add(variable.Variable);
                    break;
                case IrOpaqueTerm opaque:
                    if (opaque.Receiver != null) pending.Push(opaque.Receiver);
                    foreach (var argument in opaque.Arguments)
                        pending.Push(argument);
                    break;
                case IrUnaryTerm unary:
                    pending.Push(unary.Operand);
                    break;
                case IrBinaryTerm binary:
                    pending.Push(binary.Left);
                    pending.Push(binary.Right);
                    break;
                case IrConditionalTerm conditional:
                    pending.Push(conditional.Condition);
                    pending.Push(conditional.WhenTrue);
                    pending.Push(conditional.WhenFalse);
                    break;
                case IrCastTerm cast:
                    pending.Push(cast.Operand);
                    break;
                case IrLengthTerm length:
                    pending.Push(length.Value);
                    break;
                case IrSequenceAccessTerm access:
                    pending.Push(access.Sequence);
                    pending.Push(access.Index);
                    break;
            }
        }
        return result.ToImmutable();
    }

    private readonly struct IntegerRange(long minimum, long maximum) {
        internal long Minimum { get; } = minimum;
        internal long Maximum { get; } = maximum;
    }
}

internal readonly struct ExpressionBindingResult {
    private ExpressionBindingResult(
        IrTerm? term,
        ContractBindingFailure failure) {
        Term = term;
        Failure = failure;
    }

    internal IrTerm? Term { get; }
    internal ContractBindingFailure Failure { get; }
    internal bool IsSuccess => Failure == ContractBindingFailure.None;

    internal static ExpressionBindingResult Success(IrTerm term) =>
        new(term, ContractBindingFailure.None);

    internal static ExpressionBindingResult Fail(
        ContractBindingFailure failure) =>
        new(null, failure);
}
