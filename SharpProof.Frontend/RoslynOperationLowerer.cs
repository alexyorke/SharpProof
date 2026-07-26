namespace SharpProof.Frontend;

public sealed class RoslynOperationLowerer {
    private readonly IrFactory _factory;
    private readonly Func<IMethodSymbol, bool> _isKnownPure;
    private readonly Dictionary<ISymbol, IrVarId> _variables =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ITypeSymbol, IrVarId> _instances =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<CaptureId, IrVarId> _captures = [];
    private readonly List<IrVarId> _captureOrder = [];
    private readonly LoweringVisitor _visitor;
    private IrVarId? _missingInstance;

    public RoslynOperationLowerer(
        IrFactory factory,
        Func<IMethodSymbol, bool>? isKnownPure = null) {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _isKnownPure = isKnownPure ?? (static _ => false);
        _visitor = new LoweringVisitor(this);
    }

    internal Func<ITypeSymbol?, ITypeSymbol?> TypeSpecializer { get; set; } = static type => type;
    public FrontendLoweringResult Lower(IOperation operation) {
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        var lowered = _visitor.Visit(operation, default);
        return new FrontendLoweringResult(
            lowered.Term, lowered.Classification, CreateVariableBindings());
    }

    internal ImmutableArray<FrontendVariableBinding> CreateVariableBindings() =>
        [.. _variables
            .Select(static pair => new FrontendVariableBinding(pair.Key, pair.Value))
            .OrderBy(static binding => binding.Variable.Value)];

    internal ImmutableArray<IrVarId> CreateCaptureBindings() => [.. _captureOrder];

    private LoweredExpression LowerCore(IOperation operation) =>
        _visitor.Visit(operation, default);

    internal IrTypeId GetTypeId(ITypeSymbol? type) {
        type = TypeSpecializer(type);
        if (type == null) return _factory.ObjectType;
        if (type.TypeKind == TypeKind.Error)
            return _factory.GetOrCreateReferenceType(
                CompilerIdentityBridge.InternType(_factory, type),
                "error:" + CompilerIdentityBridge.CreateTypeDisplay(type));
        if (type is IArrayTypeSymbol array) {
            var element = GetTypeId(array.ElementType);
            return _factory.GetOrCreateSequenceType(
                CompilerIdentityBridge.InternType(_factory, array), element,
                CompilerIdentityBridge.CreateTypeDisplay(array));
        }
        return type.SpecialType switch {
            SpecialType.System_Boolean => _factory.BooleanType,
            SpecialType.System_SByte or
            SpecialType.System_Byte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Char or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 => _factory.IntegerType,
            SpecialType.System_String => _factory.StringType,
            SpecialType.System_Object => _factory.ObjectType,
            _ => _factory.GetOrCreateReferenceType(
                CompilerIdentityBridge.InternType(_factory, type),
                CompilerIdentityBridge.CreateTypeDisplay(type))
        };
    }

    internal IrVariableTerm GetVariable(ISymbol symbol, ITypeSymbol? type) {
        if (!_variables.TryGetValue(symbol, out var variable)) {
            variable = _factory.CreateVariable(
                symbol.Kind + ":" + symbol.MetadataName,
                GetTypeId(type));
            _variables.Add(symbol, variable);
        }
        return _factory.Variable(variable);
    }

    internal IrVariableTerm GetCapture(CaptureId id, ITypeSymbol? type) {
        if (!_captures.TryGetValue(id, out var variable)) {
            variable = _factory.CreateVariable(
                "capture:" +
                _captureOrder.Count.ToString(CultureInfo.InvariantCulture),
                GetTypeId(type));
            _captures.Add(id, variable);
            _captureOrder.Add(variable);
        }
        return _factory.Variable(variable);
    }

    internal IrVarId? GetReferencedVariable(
        IOperation operation, bool unwrapConversions = true) {
        if (unwrapConversions)
            while (operation is IConversionOperation conversion)
                operation = conversion.Operand;
        return operation switch {
            ILocalReferenceOperation local =>
                GetVariable(local.Local, local.Type).Variable,
            IParameterReferenceOperation parameter =>
                GetVariable(parameter.Parameter, parameter.Type).Variable,
            IFlowCaptureReferenceOperation capture =>
                GetCapture(capture.Id, capture.Type).Variable,
            _ => null
        };
    }

    internal IrMemberId GetMember(
        ISymbol symbol, IrTerm? receiver, string purpose, ITypeSymbol? resultType,
        params IrTerm[] arguments) =>
        _factory.GetOrCreateMember(
            CompilerIdentityBridge.InternSymbol(_factory, symbol),
            receiver?.Type ?? GetTypeId(symbol.ContainingType),
            purpose + CompilerIdentityBridge.CreateSymbolDisplay(symbol),
            GetTypeId(resultType),
            receiver == null,
            [.. arguments.Select(static argument => argument.Type)]);

    internal static bool IsIntrinsicLength(IPropertyReferenceOperation property) =>
        property.Instance != null &&
        property.Property.Name is "Length" or "LongLength" &&
        property.Arguments.IsDefaultOrEmpty &&
        (property.Instance.Type?.SpecialType == SpecialType.System_String ||
         property.Instance.Type is IArrayTypeSymbol);

    private IrVariableTerm GetInstance(IInstanceReferenceOperation operation) {
        var type = operation.Type;
        if (type == null) return GetSyntheticInstance(operation);
        if (!_instances.TryGetValue(type, out var variable)) {
            variable = _factory.CreateVariable(
                "instance:" + type.MetadataName,
                GetTypeId(type));
            _instances.Add(type, variable);
        }
        return _factory.Variable(variable);
    }

    private IrVariableTerm GetSyntheticInstance(IOperation operation) {
        var symbol = operation.SemanticModel?.GetEnclosingSymbol(operation.Syntax.SpanStart);
        if (symbol != null) return GetVariable(symbol, operation.Type);
        var type = operation.Type;
        if (type == null) {
            _missingInstance ??= _factory.CreateVariable(
                "instance:<unknown>",
                _factory.ObjectType);
            return _factory.Variable(_missingInstance.Value);
        }
        if (!_instances.TryGetValue(type, out var variable)) {
            variable = _factory.CreateVariable("instance:<unknown>", GetTypeId(type));
            _instances.Add(type, variable);
        }
        return _factory.Variable(variable);
    }

    private LoweredExpression Opaque(
        IOperation operation, FrontendAbstention abstention,
        ISymbol? symbol = null, IOperation? receiver = null,
        IEnumerable<IOperation>? arguments = null) {
        var loweredReceiver = receiver == null ? null : LowerCore(receiver);
        var loweredArguments = (arguments ?? operation.ChildOperations)
            .Where(child => !ReferenceEquals(child, receiver))
            .Select(LowerCore)
            .ToArray();
        var receiverTerm = loweredReceiver?.Term;
        var argumentTerms = loweredArguments.Select(static value => value.Term).ToArray();
        var resultType = GetTypeId(operation.Type);
        var declaringType = receiverTerm?.Type ??
            (symbol?.ContainingType == null
                ? _factory.ObjectType
                : GetTypeId(symbol.ContainingType));
        var isPure = IsDemonstrablyPure(operation);
        var identity = CompilerIdentityBridge.InternOperation(
            _factory, operation, symbol, isPure);
        var displayName =
            "opaque:" + operation.Kind + ":" +
            CompilerIdentityBridge.CreateSymbolDisplay(symbol) +
            ":result=" +
            CompilerIdentityBridge.CreateTypeDisplay(operation.Type);
        var parameterTypes = argumentTerms.Select(static value => value.Type).ToArray();
        var member = _factory.GetOrCreateMember(
            identity, declaringType, displayName, resultType,
            receiverTerm == null, parameterTypes);
        var term = isPure
            ? _factory.PureOpaque(member, receiverTerm, argumentTerms)
            : _factory.ImpureOpaque(
                _factory.CreateOperation(
                    operation.Kind + "@" +
                    operation.Syntax.SpanStart.ToString(CultureInfo.InvariantCulture)),
                member, receiverTerm, argumentTerms);
        return new LoweredExpression(
            term, FrontendSubsetClassification.Abstain(abstention));
    }

    private bool IsDemonstrablyPure(IOperation operation) {
        if (operation.ConstantValue.HasValue) return true;
        return operation switch {
            ILiteralOperation => true,
            ILocalReferenceOperation => true,
            IParameterReferenceOperation => true,
            IInstanceReferenceOperation => true,
            IDefaultValueOperation => true,
            ITypeOfOperation => true,
            ISizeOfOperation => true,
            IConversionOperation conversion =>
                conversion.OperatorMethod == null &&
                IsDemonstrablyPure(conversion.Operand),
            IUnaryOperation unary =>
                unary.OperatorMethod == null &&
                IsDemonstrablyPure(unary.Operand),
            IBinaryOperation binary =>
                binary.OperatorMethod == null &&
                IsDemonstrablyPure(binary.LeftOperand) &&
                IsDemonstrablyPure(binary.RightOperand),
            IConditionalOperation conditional =>
                IsDemonstrablyPure(conditional.Condition) &&
                IsDemonstrablyPure(conditional.WhenTrue) &&
                conditional.WhenFalse != null &&
                IsDemonstrablyPure(conditional.WhenFalse),
            IIsNullOperation isNull => IsDemonstrablyPure(isNull.Operand),
            IInvocationOperation invocation =>
                _isKnownPure(invocation.TargetMethod) &&
                (invocation.Instance == null ||
                 IsDemonstrablyPure(invocation.Instance)) &&
                invocation.Arguments.All(argument =>
                    IsDemonstrablyPure(argument.Value)),
            _ => false
        };
    }

    private LoweredExpression LowerConstant(IOperation operation) {
        var value = operation.ConstantValue.Value;
        var type = GetTypeId(operation.Type);
        if (operation.Type is { IsValueType: true, SpecialType: SpecialType.None })
            return Opaque(operation, FrontendAbstention.UnsupportedType);
        if (value == null) {
            var info = _factory.GetTypeInfo(type);
            if (info.Kind is IrTypeKind.String or IrTypeKind.Reference or IrTypeKind.Sequence)
                return LoweredExpression.Exact(_factory.Null(type));
            return Opaque(operation, FrontendAbstention.UnsupportedType);
        }
        return value switch {
            bool boolean when type == _factory.BooleanType =>
                LoweredExpression.Exact(_factory.Boolean(boolean)),
            string text when type == _factory.StringType =>
                LoweredExpression.Exact(_factory.String(text)),
            _ when type == _factory.IntegerType && value is sbyte or byte or short or ushort or int or uint or long or char =>
                LoweredExpression.Exact(_factory.Integer(Convert.ToInt64(value, CultureInfo.InvariantCulture))),
            _ => Opaque(operation, FrontendAbstention.UnsupportedType)
        };
    }

    private sealed class LoweringVisitor(RoslynOperationLowerer owner)
        : OperationVisitor<LoweringContext, LoweredExpression> {
        private readonly RoslynOperationLowerer _owner = owner;

        public override LoweredExpression Visit(
            IOperation? operation, LoweringContext argument) {
            if (operation == null)
                return _owner.CreateMissingOperation();
            if (operation.ConstantValue.HasValue)
                return _owner.LowerConstant(operation);
            return base.Visit(operation, argument)!;
        }

        public override LoweredExpression DefaultVisit(
            IOperation operation, LoweringContext argument) =>
            _owner.Opaque(
                operation,
                operation.Type?.TypeKind == TypeKind.Error
                    ? FrontendAbstention.ErrorOperation
                    : FrontendAbstention.UnsupportedOperationKind);

        public override LoweredExpression VisitInvalid(
            IInvalidOperation operation, LoweringContext argument) =>
            _owner.Opaque(operation, FrontendAbstention.InvalidOperation);

        public override LoweredExpression VisitLiteral(
            ILiteralOperation operation, LoweringContext argument) =>
            _owner.Opaque(operation, FrontendAbstention.UnsupportedType);

        public override LoweredExpression VisitLocalReference(
            ILocalReferenceOperation operation, LoweringContext argument) =>
            LoweredExpression.Exact(
                _owner.GetVariable(operation.Local, operation.Type));

        public override LoweredExpression VisitParameterReference(
            IParameterReferenceOperation operation, LoweringContext argument) =>
            LoweredExpression.Exact(
                _owner.GetVariable(operation.Parameter, operation.Type));

        public override LoweredExpression VisitFlowCapture(
            IFlowCaptureOperation operation, LoweringContext argument) {
            _owner.GetCapture(operation.Id, operation.Value.Type);
            return _owner.LowerCore(operation.Value);
        }

        public override LoweredExpression VisitFlowCaptureReference(
            IFlowCaptureReferenceOperation operation, LoweringContext argument) =>
            LoweredExpression.Exact(
                _owner.GetCapture(operation.Id, operation.Type));

        public override LoweredExpression VisitInstanceReference(
            IInstanceReferenceOperation operation, LoweringContext argument) =>
            LoweredExpression.Exact(_owner.GetInstance(operation));

        public override LoweredExpression VisitDefaultValue(
            IDefaultValueOperation operation, LoweringContext argument) {
            var type = _owner.GetTypeId(operation.Type);
            var info = _owner._factory.GetTypeInfo(type);
            return info.Kind switch {
                IrTypeKind.Boolean =>
                    LoweredExpression.Exact(_owner._factory.Boolean(false)),
                IrTypeKind.Integer =>
                    LoweredExpression.Exact(_owner._factory.Integer(0)),
                IrTypeKind.String or IrTypeKind.Reference or IrTypeKind.Sequence =>
                    LoweredExpression.Exact(_owner._factory.Null(type)),
                _ => _owner.Opaque(operation, FrontendAbstention.UnsupportedType)
            };
        }

        public override LoweredExpression VisitUnaryOperator(
            IUnaryOperation operation, LoweringContext argument) {
            if (operation.OperatorMethod != null)
                return OpaqueOperand(
                    operation, operation.Operand,
                    FrontendAbstention.UserDefinedOperator,
                    operation.OperatorMethod);
            if (operation.IsLifted)
                return OpaqueOperand(
                    operation, operation.Operand, FrontendAbstention.LiftedOperator);
            var operand = _owner.LowerCore(operation.Operand);
            if (!operand.Classification.IsExact)
                return OpaqueOperand(
                    operation, operation.Operand,
                    operand.Classification.Abstention);
            if (operation.OperatorKind == UnaryOperatorKind.Not &&
                operand.Term.Type == _owner._factory.BooleanType)
                return LoweredExpression.Exact(
                    _owner._factory.Unary(IrUnaryOperator.Not, operand.Term));
            if (operation.OperatorKind == UnaryOperatorKind.Minus &&
                operand.Term.Type == _owner._factory.IntegerType) {
                if (operation.Type?.SpecialType != SpecialType.System_Int64)
                    return OpaqueOperand(
                        operation, operation.Operand,
                        FrontendAbstention.UnsupportedType);
                if (!operation.IsChecked)
                    return OpaqueOperand(
                        operation, operation.Operand,
                        FrontendAbstention.UncheckedOverflowSemantics);
                return LoweredExpression.Exact(
                    _owner._factory.Unary(IrUnaryOperator.Negate, operand.Term));
            }
            if (operation.OperatorKind == UnaryOperatorKind.Plus)
                return operand;
            return OpaqueOperand(
                operation, operation.Operand,
                FrontendAbstention.UnsupportedOperationKind);
        }

        public override LoweredExpression VisitBinaryOperator(
            IBinaryOperation operation, LoweringContext argument) {
            if (operation.OperatorMethod != null)
                return OpaqueBinary(
                    operation, FrontendAbstention.UserDefinedOperator,
                    operation.OperatorMethod);
            if (operation.IsLifted)
                return OpaqueBinary(operation, FrontendAbstention.LiftedOperator);

            var left = _owner.LowerCore(operation.LeftOperand);
            if (operation.OperatorKind == BinaryOperatorKind.ConditionalAnd &&
                left.Term is IrBooleanTerm { Value: false })
                return LoweredExpression.Exact(left.Term);
            if (operation.OperatorKind == BinaryOperatorKind.ConditionalOr &&
                left.Term is IrBooleanTerm { Value: true })
                return LoweredExpression.Exact(left.Term);

            var right = _owner.LowerCore(operation.RightOperand);
            if (!left.Classification.IsExact || !right.Classification.IsExact)
                return OpaqueBinary(operation, FirstAbstention(left, right));

            var mapped = MapBinary(operation);
            if (!mapped.HasValue)
                return OpaqueBinary(
                    operation, FrontendAbstention.UnsupportedOperationKind);
            if (mapped.Value != IrBinaryOperator.StringConcat &&
                IsIntegerArithmetic(operation.OperatorKind) &&
                operation.Type?.SpecialType != SpecialType.System_Int64)
                return OpaqueBinary(operation, FrontendAbstention.UnsupportedType);
            if (mapped.Value != IrBinaryOperator.StringConcat &&
                RequiresCheckedArithmetic(operation.OperatorKind) &&
                !operation.IsChecked)
                return OpaqueBinary(
                    operation, FrontendAbstention.UncheckedOverflowSemantics);
            try {
                return LoweredExpression.Exact(
                    _owner._factory.Binary(mapped.Value, left.Term, right.Term));
            }
            catch (ArgumentException) {
                return OpaqueBinary(operation, FrontendAbstention.UnsupportedType);
            }
        }

        public override LoweredExpression VisitConditional(
            IConditionalOperation operation, LoweringContext argument) {
            if (operation.WhenFalse == null)
                return _owner.Opaque(
                    operation,
                    FrontendAbstention.UnsupportedOperationKind);
            var condition = _owner.LowerCore(operation.Condition);
            if (condition.Term is IrBooleanTerm constant)
                return _owner.LowerCore(
                    constant.Value ? operation.WhenTrue : operation.WhenFalse);
            var whenTrue = _owner.LowerCore(operation.WhenTrue);
            var whenFalse = _owner.LowerCore(operation.WhenFalse);
            if (!condition.Classification.IsExact ||
                !whenTrue.Classification.IsExact ||
                !whenFalse.Classification.IsExact)
                return OpaqueConditional(
                    operation, FirstAbstention(condition, whenTrue, whenFalse));
            try {
                return LoweredExpression.Exact(
                    _owner._factory.Conditional(
                        condition.Term,
                        whenTrue.Term,
                        whenFalse.Term));
            }
            catch (ArgumentException) {
                return OpaqueConditional(
                    operation, FrontendAbstention.UnsupportedType);
            }
        }

        public override LoweredExpression VisitConversion(
            IConversionOperation operation, LoweringContext argument) {
            if (operation.OperatorMethod != null)
                return OpaqueOperand(
                    operation, operation.Operand,
                    FrontendAbstention.UserDefinedOperator,
                    operation.OperatorMethod);
            var operand = _owner.LowerCore(operation.Operand);
            if (!operand.Classification.IsExact)
                return OpaqueOperand(
                    operation, operation.Operand,
                    operand.Classification.Abstention);
            var target = _owner.GetTypeId(operation.Type);
            if (SymbolEqualityComparer.Default.Equals(
                    operation.Operand.Type,
                    operation.Type))
                return operand;
            if (target == operand.Term.Type &&
                IsValuePreservingIntegerConversion(
                    operation.Operand.Type,
                    operation.Type))
                return operand;
            if (operation.Operand.ConstantValue.HasValue)
                return _owner.LowerConstant(operation);
            if (!operation.IsTryCast &&
                operation.Conversion.IsReference &&
                operation.Type?.SpecialType == SpecialType.System_String &&
                _owner._factory.GetTypeInfo(operand.Term.Type).Kind ==
                    IrTypeKind.Reference)
                return LoweredExpression.Exact(
                    _owner._factory.Cast(target, operand.Term));
            return OpaqueOperand(
                operation, operation.Operand,
                FrontendAbstention.ConversionMayChangeValue);
        }

        public override LoweredExpression VisitIsNull(
            IIsNullOperation operation, LoweringContext argument) {
            var operand = _owner.LowerCore(operation.Operand);
            if (!operand.Classification.IsExact)
                return OpaqueOperand(
                    operation, operation.Operand,
                    operand.Classification.Abstention);
            var type = _owner._factory.GetTypeInfo(operand.Term.Type);
            if (type.Kind is not (
                IrTypeKind.String or IrTypeKind.Reference or IrTypeKind.Sequence))
                return OpaqueOperand(
                    operation, operation.Operand,
                    FrontendAbstention.UnsupportedType);
            return LoweredExpression.Exact(
                _owner._factory.Binary(
                    IrBinaryOperator.Equal,
                    operand.Term,
                    _owner._factory.Null(operand.Term.Type)));
        }

        public override LoweredExpression VisitPropertyReference(
            IPropertyReferenceOperation operation, LoweringContext argument) {
            if (IsIntrinsicLength(operation)) {
                var instance = _owner.LowerCore(operation.Instance!);
                if (!instance.Classification.IsExact)
                    return _owner.Opaque(
                        operation,
                        instance.Classification.Abstention,
                        operation.Property,
                        operation.Instance,
                        []);
                return LoweredExpression.Exact(
                    _owner._factory.Length(instance.Term));
            }
            return _owner.Opaque(
                operation, FrontendAbstention.UnsupportedMemberAccess,
                operation.Property, operation.Instance,
                operation.Arguments.Select(static value => value.Value));
        }

        public override LoweredExpression VisitArrayElementReference(
            IArrayElementReferenceOperation operation, LoweringContext argument) {
            if (operation.Indices.Length != 1)
                return OpaqueElement(
                    operation, FrontendAbstention.UnsupportedMemberAccess);
            var array = _owner.LowerCore(operation.ArrayReference);
            var index = _owner.LowerCore(operation.Indices[0]);
            if (!array.Classification.IsExact || !index.Classification.IsExact)
                return OpaqueElement(operation, FirstAbstention(array, index));
            try {
                return LoweredExpression.Exact(
                    _owner._factory.SequenceAccess(array.Term, index.Term));
            }
            catch (ArgumentException) {
                return OpaqueElement(
                    operation, FrontendAbstention.UnsupportedType);
            }
        }

        public override LoweredExpression VisitInvocation(
            IInvocationOperation operation, LoweringContext argument) =>
            _owner.Opaque(
                operation, FrontendAbstention.UnsupportedInvocationShape,
                operation.TargetMethod, operation.Instance,
                operation.Arguments.Select(static value => value.Value));

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
                BinaryOperatorKind.LessThanOrEqual => IrBinaryOperator.LessThanOrEqual,
                BinaryOperatorKind.GreaterThan => IrBinaryOperator.GreaterThan,
                BinaryOperatorKind.GreaterThanOrEqual => IrBinaryOperator.GreaterThanOrEqual,
                _ => null
            };

        private static bool RequiresCheckedArithmetic(BinaryOperatorKind kind) =>
            kind is BinaryOperatorKind.Add or
                BinaryOperatorKind.Subtract or
                BinaryOperatorKind.Multiply;

        private static bool IsIntegerArithmetic(BinaryOperatorKind kind) =>
            kind is BinaryOperatorKind.Add or
                BinaryOperatorKind.Subtract or
                BinaryOperatorKind.Multiply or
                BinaryOperatorKind.Divide or
                BinaryOperatorKind.Remainder;

        private static bool IsValuePreservingIntegerConversion(
            ITypeSymbol? source, ITypeSymbol? target) {
            var sourceRange = GetIntegerRange(source?.SpecialType ?? SpecialType.None);
            var targetRange = GetIntegerRange(target?.SpecialType ?? SpecialType.None);
            return sourceRange.HasValue &&
                   targetRange.HasValue &&
                   sourceRange.Value.Minimum >= targetRange.Value.Minimum &&
                   sourceRange.Value.Maximum <= targetRange.Value.Maximum;
        }

        private static IntegerRange? GetIntegerRange(SpecialType type) =>
            type switch {
                SpecialType.System_SByte => new(sbyte.MinValue, sbyte.MaxValue),
                SpecialType.System_Byte => new(byte.MinValue, byte.MaxValue),
                SpecialType.System_Int16 => new(short.MinValue, short.MaxValue),
                SpecialType.System_UInt16 => new(ushort.MinValue, ushort.MaxValue),
                SpecialType.System_Char => new(char.MinValue, char.MaxValue),
                SpecialType.System_Int32 => new(int.MinValue, int.MaxValue),
                SpecialType.System_UInt32 => new(uint.MinValue, uint.MaxValue),
                SpecialType.System_Int64 => new(long.MinValue, long.MaxValue),
                _ => null
            };

        private static FrontendAbstention FirstAbstention(
            params LoweredExpression[] expressions) =>
            expressions
                .Select(static expression => expression.Classification.Abstention)
                .First(static abstention => abstention != FrontendAbstention.None);

        private LoweredExpression OpaqueOperand(
            IOperation operation,
            IOperation operand,
            FrontendAbstention abstention,
            ISymbol? symbol = null) =>
            _owner.Opaque(
                operation, abstention, symbol, arguments: [operand]);

        private LoweredExpression OpaqueConditional(
            IConditionalOperation operation,
            FrontendAbstention abstention) =>
            _owner.Opaque(
                operation, abstention,
                arguments:
                [operation.Condition, operation.WhenTrue, operation.WhenFalse!]);

        private LoweredExpression OpaqueElement(
            IArrayElementReferenceOperation operation,
            FrontendAbstention abstention) =>
            _owner.Opaque(
                operation, abstention, receiver: operation.ArrayReference,
                arguments: operation.Indices);

        private LoweredExpression OpaqueBinary(
            IBinaryOperation operation, FrontendAbstention abstention,
            IMethodSymbol? symbol = null) =>
            _owner.Opaque(operation, abstention, symbol,
                arguments: [operation.LeftOperand, operation.RightOperand]);
    }

    private LoweredExpression CreateMissingOperation() {
        var member = _factory.GetOrCreateMember(
            _factory.CreateIdentity(), _factory.ObjectType,
            "opaque:<missing-operation>", _factory.ObjectType, true);
        var term = _factory.ImpureOpaque(
            _factory.CreateOperation("missing-operation"), member, null);
        return new LoweredExpression(
            term, FrontendSubsetClassification.Abstain(
                FrontendAbstention.InvalidOperation));
    }

    private readonly struct LoweringContext;

    private readonly struct IntegerRange(long minimum, long maximum) {
        internal long Minimum { get; } = minimum;
        internal long Maximum { get; } = maximum;
    }

    private sealed class LoweredExpression(
        IrTerm term,
        FrontendSubsetClassification classification) {
        internal IrTerm Term { get; } = term;
        internal FrontendSubsetClassification Classification { get; } =
            classification;

        internal static LoweredExpression Exact(IrTerm term) =>
            new(term, FrontendSubsetClassification.Exact);
    }
}
