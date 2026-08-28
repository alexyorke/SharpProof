namespace SharpProof.Frontend;

public sealed class RoslynOperationLowerer
{
    private readonly IrFactory _factory;
    private readonly Func<IMethodSymbol, bool> _isKnownPure;
    private readonly bool _allowCompilerConstants;
    private readonly Dictionary<ISymbol, IrVarId> _variables =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ITypeSymbol, IrVarId> _instances =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<CaptureId, IrVarId> _captures = [];
    private readonly HashSet<CaptureId> _boundCaptures = [];
    private readonly List<IrVarId> _captureOrder = [];
    private readonly LoweringVisitor _visitor;
    private IrVarId? _missingInstance;

    public RoslynOperationLowerer(
        IrFactory factory,
        Func<IMethodSymbol, bool>? isKnownPure = null)
        : this(factory, isKnownPure, allowCompilerConstants: false)
    {
    }

    private RoslynOperationLowerer(
        IrFactory factory,
        Func<IMethodSymbol, bool>? isKnownPure,
        bool allowCompilerConstants)
    {
        _factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        _isKnownPure = isKnownPure ?? (static _ => false);
        _allowCompilerConstants = allowCompilerConstants;
        _visitor = new LoweringVisitor(this);
    }

    internal static RoslynOperationLowerer CreateForConcreteReplay(
        IrFactory factory,
        Func<IMethodSymbol, bool>? isKnownPure = null)
    {
        return new RoslynOperationLowerer(
            factory,
            isKnownPure,
            allowCompilerConstants: true);
    }

    internal Func<ITypeSymbol?, ITypeSymbol?> TypeSpecializer { get; set; } = static type => type;
    internal Func<IOperation, (bool Handled, IrTerm? Term)> CustomLowering
    {
        get;
        set;
    } = static _ => default;

    public FrontendLoweringResult Lower(IOperation operation)
    {
        operation = ArgumentNullGuard.NotNull(operation, nameof(operation));

        var lowered = _visitor.Visit(operation, default);
        return new FrontendLoweringResult(
            lowered.Term, lowered.Classification, CreateVariableBindings());
    }

    internal ImmutableArray<FrontendVariableBinding> CreateVariableBindings()
    {
        return [.. _variables
            .Select(static pair => new FrontendVariableBinding(pair.Key, pair.Value))
            .OrderBy(static binding => binding.Variable.Value)];
    }

    internal ImmutableArray<IrVarId> CreateCaptureBindings()
    {
        return [.. _captureOrder];
    }

    private LoweredExpression LowerCore(IOperation operation)
    {
        return _visitor.Visit(operation, default);
    }

    internal IrTypeId GetTypeId(ITypeSymbol? type)
    {
        type = TypeSpecializer(type);
        if (type == null)
        {
            return _factory.ObjectType;
        }

        if (type.TypeKind == TypeKind.Error)
        {
            return _factory.GetOrCreateReferenceType(
                CompilerIdentityBridge.InternType(_factory, type),
                "error:" + CompilerIdentityBridge.CreateTypeDisplay(type));
        }

        if (type is IArrayTypeSymbol array)
        {
            var element = GetTypeId(array.ElementType);
            return _factory.GetOrCreateSequenceType(
                CompilerIdentityBridge.InternType(_factory, array), element,
                CompilerIdentityBridge.CreateTypeDisplay(array));
        }
        if (CSharpScalarSemantics.IsSupportedInteger(type.SpecialType))
        {
            return _factory.IntegerType;
        }

        return CSharpScalarSemantics.TryGetBuiltInType(
                _factory, type.SpecialType) ??
            _factory.GetOrCreateReferenceType(
                CompilerIdentityBridge.InternType(_factory, type),
                CompilerIdentityBridge.CreateTypeDisplay(type));
    }

    private bool IsSupportedValueDomain(ITypeSymbol? type)
    {
        return CompilerIdentityBridge.IsSupportedValueDomain(
            TypeSpecializer(type));
    }

    internal IrVariableTerm GetVariable(ISymbol symbol, ITypeSymbol? type)
    {
        if (!_variables.TryGetValue(symbol, out var variable))
        {
            variable = _factory.CreateVariable(
                symbol.Kind + ":" + symbol.MetadataName,
                GetTypeId(type));
            _variables.Add(symbol, variable);
        }
        return _factory.Variable(variable);
    }

    internal IrVariableTerm GetCapture(CaptureId id, ITypeSymbol? type)
    {
        if (!_captures.TryGetValue(id, out var variable))
        {
            variable = _factory.CreateVariable(
                "capture:" +
                _captureOrder.Count.ToString(CultureInfo.InvariantCulture),
                GetTypeId(type));
            _captures.Add(id, variable);
            _captureOrder.Add(variable);
        }
        return _factory.Variable(variable);
    }

    internal void BindCapture(CaptureId id)
    {
        _boundCaptures.Add(id);
    }

    internal bool IsCaptureBound(CaptureId id)
    {
        return _boundCaptures.Contains(id);
    }

    internal IrVarId? GetReferencedVariable(
        IOperation operation, bool unwrapConversions = true)
    {
        if (unwrapConversions)
        {
            while (operation is IConversionOperation conversion)
            {
                operation = conversion.Operand;
            }
        }

        return operation switch
        {
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
        params IrTerm[] arguments)
    {
        return _factory.GetOrCreateMember(
            CompilerIdentityBridge.InternSymbol(_factory, symbol),
            receiver?.Type ?? GetTypeId(symbol.ContainingType),
            purpose + CompilerIdentityBridge.CreateSymbolDisplay(symbol),
            GetTypeId(resultType),
            receiver == null,
            [.. arguments.Select(static argument => argument.Type)]);
    }

    internal static bool IsIntrinsicLength(IPropertyReferenceOperation property)
    {
        return CompilerIdentityBridge.IsIntrinsicSequenceLength(property);
    }

    private static bool TryGetNullComparisonValue(
        IBinaryOperation operation,
        out IOperation value)
    {
        if (operation.OperatorKind is not (
                BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
        {
            value = null!;
            return false;
        }
        var left = UnwrapImplicitConversions(operation.LeftOperand);
        var right = UnwrapImplicitConversions(operation.RightOperand);
        value = IsNullConstant(right)
            ? left
            : IsNullConstant(left) ? right : null!;
        return value != null;
    }

    private static IOperation UnwrapImplicitConversions(IOperation operation)
    {
        while (operation is IConversionOperation
            {
                IsImplicit: true,
                OperatorMethod: null
            } conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private static IOperation UnwrapImplicitReferenceConversions(
        IOperation operation)
    {
        while (operation is IConversionOperation
            {
                IsImplicit: true,
                OperatorMethod: null
            } conversion && conversion.Conversion.IsReference)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    private static bool IsNullConstant(IOperation operation)
    {
        return operation.ConstantValue is { HasValue: true, Value: null };
    }

    private IrVariableTerm GetInstance(IInstanceReferenceOperation operation)
    {
        var type = operation.Type;
        if (type == null)
        {
            return GetSyntheticInstance(operation);
        }

        if (!_instances.TryGetValue(type, out var variable))
        {
            variable = _factory.CreateVariable(
                "instance:" + type.MetadataName,
                GetTypeId(type));
            _instances.Add(type, variable);
        }
        return _factory.Variable(variable);
    }

    private IrVariableTerm GetSyntheticInstance(IOperation operation)
    {
        var symbol = operation.SemanticModel?.GetEnclosingSymbol(operation.Syntax.SpanStart);
        if (symbol != null)
        {
            return GetVariable(symbol, operation.Type);
        }

        var type = operation.Type;
        if (type == null)
        {
            _missingInstance ??= _factory.CreateVariable(
                "instance:<unknown>",
                _factory.ObjectType);
            return _factory.Variable(_missingInstance.Value);
        }
        if (!_instances.TryGetValue(type, out var variable))
        {
            variable = _factory.CreateVariable("instance:<unknown>", GetTypeId(type));
            _instances.Add(type, variable);
        }
        return _factory.Variable(variable);
    }

    private LoweredExpression Opaque(
        IOperation operation, FrontendAbstention abstention,
        ISymbol? symbol = null, IOperation? receiver = null,
        IEnumerable<IOperation>? arguments = null)
    {
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

    private bool IsDemonstrablyPure(IOperation operation)
    {
        if (operation.ConstantValue.HasValue)
        {
            return IsRepresentableConstant(operation);
        }

        return operation switch
        {
            ILiteralOperation or
                ILocalReferenceOperation or
                IParameterReferenceOperation or
                IInstanceReferenceOperation or
                IDefaultValueOperation or
                ITypeOfOperation or
                ISizeOfOperation => true,
            IConversionOperation conversion =>
                conversion.OperatorMethod == null &&
                !IsBoxingConversion(conversion) &&
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

    private static bool IsBoxingConversion(IConversionOperation conversion)
    {
        return conversion.Operand.Type?.IsValueType == true &&
            conversion.Type?.IsValueType != true;
    }

    private bool IsRepresentableConstant(IOperation operation)
    {
        var value = operation.ConstantValue.Value;
        var type = GetTypeId(operation.Type);
        return value == null ||
            value is bool && type == _factory.BooleanType ||
            value is string text && type == _factory.StringType &&
                Utf16WellFormedness.IsWellFormed(text) ||
            type == _factory.IntegerType && value is
                sbyte or byte or short or ushort or int or uint or long or char;
    }

    private LoweredExpression LowerConstant(IOperation operation)
    {
        // An absent constant is not a null constant. Without this guard the
        // null-valued branch below would turn "Roslyn folded nothing here" into
        // an exact null term.
        if (!operation.ConstantValue.HasValue)
        {
            return Opaque(operation, FrontendAbstention.UnsupportedType);
        }

        var value = operation.ConstantValue.Value;
        var type = GetTypeId(operation.Type);
        if (operation.Type is { IsValueType: true, SpecialType: SpecialType.None })
        {
            return Opaque(operation, FrontendAbstention.UnsupportedType);
        }

        if (value == null)
        {
            var info = _factory.GetTypeInfo(type);
            if (info.Kind is IrTypeKind.String or IrTypeKind.Reference or IrTypeKind.Sequence)
            {
                return LoweredExpression.Exact(_factory.Null(type));
            }

            return Opaque(operation, FrontendAbstention.UnsupportedType);
        }
        return value switch
        {
            bool boolean when type == _factory.BooleanType =>
                LoweredExpression.Exact(_factory.Boolean(boolean)),
            string text when type == _factory.StringType &&
                Utf16WellFormedness.IsWellFormed(text) =>
                LoweredExpression.Exact(_factory.String(text)),
            _ when type == _factory.IntegerType && value is sbyte or byte or short or ushort or int or uint or long or char =>
                LoweredExpression.Exact(_factory.Integer(Convert.ToInt64(value, CultureInfo.InvariantCulture))),
            _ => Opaque(operation, FrontendAbstention.UnsupportedType)
        };
    }

    private sealed class LoweringVisitor(RoslynOperationLowerer owner)
        : OperationVisitor<LoweringContext, LoweredExpression>
    {
        private readonly RoslynOperationLowerer _owner = owner;

        public override LoweredExpression Visit(
            IOperation? operation, LoweringContext argument)
        {
            if (operation == null)
            {
                return _owner.CreateMissingOperation();
            }

            var custom = _owner.CustomLowering(operation);
            if (custom.Handled)
            {
                return custom.Term == null
                    ? _owner.Opaque(
                        operation,
                        FrontendAbstention.UnsupportedOperationKind)
                    : LoweredExpression.Exact(custom.Term);
            }

            if (_owner._allowCompilerConstants &&
                operation.ConstantValue.HasValue)
            {
                return _owner.LowerConstant(operation);
            }

            return base.Visit(operation, argument)!;
        }

        public override LoweredExpression DefaultVisit(
            IOperation operation, LoweringContext argument)
        {
            var abstention = operation.Type?.TypeKind == TypeKind.Error
                ? FrontendAbstention.ErrorOperation
                : operation.ConstantValue.HasValue &&
                    operation.Type is
                    {
                        IsValueType: true,
                        SpecialType: SpecialType.None
                    }
                    ? FrontendAbstention.UnsupportedType
                    : FrontendAbstention.UnsupportedOperationKind;
            return _owner.Opaque(operation, abstention, arguments: []);
        }

        public override LoweredExpression VisitInvalid(
            IInvalidOperation operation, LoweringContext argument)
        {
            return _owner.Opaque(operation, FrontendAbstention.InvalidOperation);
        }

        public override LoweredExpression VisitLiteral(
            ILiteralOperation operation, LoweringContext argument)
        {
            return _owner.LowerConstant(operation);
        }

        public override LoweredExpression VisitFieldReference(
            IFieldReferenceOperation operation, LoweringContext argument)
        {
            return CompilerConstantAdmission.IsCatalogIntegerBoundary(operation)
                ? _owner.LowerConstant(operation)
                : DefaultVisit(operation, argument);
        }

        public override LoweredExpression VisitLocalReference(
            ILocalReferenceOperation operation, LoweringContext argument)
        {
            return _owner.IsSupportedValueDomain(operation.Type)
                ? LoweredExpression.Exact(
                    _owner.GetVariable(operation.Local, operation.Type))
                : _owner.Opaque(
                    operation,
                    FrontendAbstention.UnsupportedType);
        }

        public override LoweredExpression VisitParameterReference(
            IParameterReferenceOperation operation, LoweringContext argument)
        {
            return _owner.IsSupportedValueDomain(operation.Type)
                ? LoweredExpression.Exact(
                    _owner.GetVariable(operation.Parameter, operation.Type))
                : _owner.Opaque(
                    operation,
                    FrontendAbstention.UnsupportedType);
        }

        public override LoweredExpression VisitFlowCapture(
            IFlowCaptureOperation operation, LoweringContext argument)
        {
            return _owner.Opaque(
                operation,
                FrontendAbstention.UnsupportedControlFlow,
                arguments: [operation.Value]);
        }

        public override LoweredExpression VisitFlowCaptureReference(
            IFlowCaptureReferenceOperation operation, LoweringContext argument)
        {
            return _owner.IsSupportedValueDomain(operation.Type) &&
                _owner.IsCaptureBound(operation.Id)
                ? LoweredExpression.Exact(
                    _owner.GetCapture(operation.Id, operation.Type))
                : _owner.Opaque(
                    operation,
                    _owner.IsSupportedValueDomain(operation.Type)
                        ? FrontendAbstention.UnsupportedControlFlow
                        : FrontendAbstention.UnsupportedType);
        }

        public override LoweredExpression VisitInstanceReference(
            IInstanceReferenceOperation operation, LoweringContext argument)
        {
            return _owner.IsSupportedValueDomain(operation.Type)
                ? LoweredExpression.Exact(_owner.GetInstance(operation))
                : _owner.Opaque(operation, FrontendAbstention.UnsupportedType);
        }

        public override LoweredExpression VisitDefaultValue(
            IDefaultValueOperation operation, LoweringContext argument)
        {
            var type = _owner.TypeSpecializer(operation.Type);
            var specialType = type?.SpecialType ?? SpecialType.None;
            if (specialType == SpecialType.System_Boolean)
            {
                return LoweredExpression.Exact(
                    _owner._factory.Boolean(false));
            }

            if (CSharpScalarSemantics.IsSupportedInteger(specialType))
            {
                return LoweredExpression.Exact(
                    _owner._factory.Integer(0));
            }

            if (type?.IsReferenceType != true)
            {
                return _owner.Opaque(
                    operation,
                    FrontendAbstention.UnsupportedType);
            }

            var typeId = _owner.GetTypeId(type);
            return LoweredExpression.Exact(
                _owner._factory.Null(typeId));
        }

        public override LoweredExpression VisitUnaryOperator(
            IUnaryOperation operation, LoweringContext argument)
        {
            if (operation.OperatorMethod != null)
            {
                return OpaqueOperand(operation, operation.Operand,
                    FrontendAbstention.UserDefinedOperator, operation.OperatorMethod);
            }

            if (operation.IsLifted)
            {
                return OpaqueOperand(operation, operation.Operand, FrontendAbstention.LiftedOperator);
            }

            var operand = _owner.LowerCore(operation.Operand);
            if (!operand.Classification.IsExact)
            {
                return OpaqueOperand(operation, operation.Operand, operand.Classification.Abstention);
            }

            if (!CSharpScalarSemantics.TryGetUnary(
                    operation.OperatorKind,
                    out var semantics))
            {
                return OpaqueOperand(
                    operation,
                    operation.Operand,
                    FrontendAbstention.UnsupportedOperationKind);
            }

            if (semantics.IsIdentity)
            {
                if (!CSharpScalarSemantics.IsSupportedInteger(
                        operation.Type?.SpecialType ?? SpecialType.None))
                {
                    return OpaqueOperand(
                        operation,
                        operation.Operand,
                        FrontendAbstention.UnsupportedType);
                }

                return operand;
            }

            if (CompilerConstantAdmission.IsLiteralIntegerNegation(operation))
            {
                return _owner.LowerConstant(operation);
            }

            if (semantics.RequiresExactIntegerDomain &&
                !CSharpScalarSemantics.SupportsExactIntegerIrArithmetic(
                    operation.Type?.SpecialType ?? SpecialType.None))
            {
                return OpaqueOperand(
                    operation,
                    operation.Operand,
                    FrontendAbstention.UnsupportedType);
            }

            if (semantics.RequiresCheckedArithmetic &&
                !operation.IsChecked)
            {
                return OpaqueOperand(
                    operation,
                    operation.Operand,
                    FrontendAbstention.UncheckedOverflowSemantics);
            }

            try
            {
                return LoweredExpression.Exact(
                    _owner._factory.Unary(
                        semantics.IrOperator!.Value,
                        operand.Term));
            }
            catch (ArgumentException)
            {
                return OpaqueOperand(
                    operation,
                    operation.Operand,
                    FrontendAbstention.UnsupportedType);
            }
        }

        public override LoweredExpression VisitBinaryOperator(
            IBinaryOperation operation, LoweringContext argument)
        {
            if (operation.OperatorMethod is { } operatorMethod)
            {
                return OpaqueBinary(operation, FrontendAbstention.UserDefinedOperator, operatorMethod);
            }

            if (operation.IsLifted)
            {
                return OpaqueBinary(operation, FrontendAbstention.LiftedOperator);
            }

            if (TryGetNullComparisonValue(operation, out var compared))
            {
                var value = _owner.LowerCore(compared);
                if (!value.Classification.IsExact)
                {
                    return OpaqueBinary(operation, value.Classification.Abstention);
                }

                try
                {
                    var kind = operation.OperatorKind == BinaryOperatorKind.Equals
                        ? IrBinaryOperator.Equal
                        : IrBinaryOperator.NotEqual;
                    return LoweredExpression.Exact(_owner._factory.Binary(
                        kind, value.Term, _owner._factory.Null(value.Term.Type)));
                }
                catch (ArgumentException)
                {
                    return OpaqueBinary(operation, FrontendAbstention.UnsupportedType);
                }
            }
            var leftOperand = operation.LeftOperand;
            var rightOperand = operation.RightOperand;
            if (operation.OperatorKind is
                BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals)
            {
                leftOperand = UnwrapImplicitReferenceConversions(leftOperand);
                rightOperand = UnwrapImplicitReferenceConversions(rightOperand);
                if (ChangesReferenceEqualityToString(leftOperand) ||
                    ChangesReferenceEqualityToString(rightOperand))
                {
                    return OpaqueBinary(
                        operation,
                        FrontendAbstention.UnsupportedType);
                }
            }
            if (!CSharpScalarSemantics.SupportsBuiltInOperands(
                    operation.OperatorKind,
                    leftOperand.Type,
                    rightOperand.Type))
            {
                return OpaqueBinary(operation, FrontendAbstention.UnsupportedType);
            }

            var left = _owner.LowerCore(leftOperand);
            if (operation.OperatorKind == BinaryOperatorKind.ConditionalAnd &&
                left.Term is IrBooleanTerm { Value: false })
            {
                return LoweredExpression.Exact(left.Term);
            }

            if (operation.OperatorKind == BinaryOperatorKind.ConditionalOr &&
                left.Term is IrBooleanTerm { Value: true })
            {
                return LoweredExpression.Exact(left.Term);
            }

            var right = _owner.LowerCore(rightOperand);
            if (!left.Classification.IsExact || !right.Classification.IsExact)
            {
                return OpaqueBinary(operation, FirstAbstention(left, right));
            }

            var mapped = CSharpScalarSemantics.MapBinary(operation.OperatorKind,
                operation.Type?.SpecialType ?? SpecialType.None);
            if (!mapped.HasValue)
            {
                return OpaqueBinary(operation, FrontendAbstention.UnsupportedOperationKind);
            }

            if (mapped.Value != IrBinaryOperator.StringConcat)
            {
                if (CSharpScalarSemantics.IsIntegerArithmetic(
                        operation.OperatorKind) &&
                    !CSharpScalarSemantics.SupportsExactIntegerIrArithmetic(
                        operation.Type?.SpecialType ?? SpecialType.None))
                {
                    return OpaqueBinary(
                        operation,
                        FrontendAbstention.UnsupportedType);
                }

                if (CSharpScalarSemantics.RequiresCheckedArithmetic(
                        operation.OperatorKind) &&
                    !operation.IsChecked &&
                    !IsKnownSafeUncheckedArithmetic(
                        operation.OperatorKind,
                        left.Term,
                        right.Term))
                {
                    return OpaqueBinary(
                        operation,
                        FrontendAbstention.UncheckedOverflowSemantics);
                }
            }

            try
            {
                return LoweredExpression.Exact(_owner._factory.Binary(
                    mapped.Value, left.Term, right.Term));
            }
            catch (ArgumentException)
            {
                return OpaqueBinary(operation, FrontendAbstention.UnsupportedType);
            }
        }

        private bool ChangesReferenceEqualityToString(IOperation operand)
        {
            return operand.Type is ITypeParameterSymbol &&
                _owner.TypeSpecializer(operand.Type)?.SpecialType ==
                    SpecialType.System_String;
        }

        private static bool IsKnownSafeUncheckedArithmetic(
            BinaryOperatorKind kind,
            IrTerm left,
            IrTerm right)
        {
            // The only overflow case for integral division is
            // long.MinValue / -1. An exact non-minimum numerator or exact
            // denominator therefore remains safe even when the surrounding
            // operation is unchecked; divide-by-zero and the exceptional
            // minimum-value case are represented by the IR interpreter.
            return kind == BinaryOperatorKind.Divide &&
                (left is IrIntegerTerm { Value: not long.MinValue } ||
                 right is IrIntegerTerm { Value: not -1L });
        }

        public override LoweredExpression VisitConditional(
            IConditionalOperation operation, LoweringContext argument)
        {
            if (operation.WhenFalse == null)
            {
                return _owner.Opaque(operation, FrontendAbstention.UnsupportedOperationKind);
            }

            var condition = _owner.LowerCore(operation.Condition);
            if (condition.Term is IrBooleanTerm constant)
            {
                return _owner.LowerCore(constant.Value
                    ? operation.WhenTrue : operation.WhenFalse);
            }

            var whenTrue = _owner.LowerCore(operation.WhenTrue);
            var whenFalse = _owner.LowerCore(operation.WhenFalse);
            if (!condition.Classification.IsExact ||
                !whenTrue.Classification.IsExact ||
                !whenFalse.Classification.IsExact)
            {
                return OpaqueConditional(operation,
                    FirstAbstention(condition, whenTrue, whenFalse));
            }

            try
            {
                return LoweredExpression.Exact(_owner._factory.Conditional(
                    condition.Term, whenTrue.Term, whenFalse.Term));
            }
            catch (ArgumentException)
            {
                return OpaqueConditional(operation, FrontendAbstention.UnsupportedType);
            }
        }

        public override LoweredExpression VisitConversion(
            IConversionOperation operation, LoweringContext argument)
        {
            if (operation.OperatorMethod != null)
            {
                return OpaqueOperand(operation, operation.Operand,
                    FrontendAbstention.UserDefinedOperator, operation.OperatorMethod);
            }

            var operand = _owner.LowerCore(operation.Operand);
            if (!operand.Classification.IsExact)
            {
                return OpaqueOperand(operation, operation.Operand,
                    operand.Classification.Abstention);
            }

            var target = _owner.GetTypeId(operation.Type);
            if (SymbolEqualityComparer.Default.Equals(
                    operation.Operand.Type,
                    operation.Type))
            {
                return operand;
            }

            if (target == operand.Term.Type &&
                CSharpScalarSemantics.IsValuePreservingIntegerConversion(operation.Operand.Type?.SpecialType ?? SpecialType.None,
                    operation.Type?.SpecialType ?? SpecialType.None))
            {
                return operand;
            }

            var operandInfo = _owner._factory.GetTypeInfo(operand.Term.Type);
            var targetInfo = _owner._factory.GetTypeInfo(target);
            if (targetInfo.Kind == IrTypeKind.String &&
                operandInfo.Kind == IrTypeKind.Reference &&
                operation.Syntax is not Microsoft.CodeAnalysis.CSharp.Syntax.BinaryExpressionSyntax
                {
                    RawKind: (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.AsExpression
                })
            {
                return LoweredExpression.Exact(
                    _owner._factory.Cast(target, operand.Term));
            }

            // Routing stays keyed on the operand's constant so the abstention
            // reasons keep their existing split. LowerConstant is what guards
            // against the conversion itself carrying no constant, which is the
            // case for boxing.
            if (operation.Operand.ConstantValue.HasValue)
            {
                return _owner.LowerConstant(operation);
            }

            return OpaqueOperand(operation, operation.Operand,
                FrontendAbstention.ConversionMayChangeValue);
        }

        public override LoweredExpression VisitIsNull(
            IIsNullOperation operation, LoweringContext argument)
        {
            var operand = _owner.LowerCore(operation.Operand);
            if (!operand.Classification.IsExact)
            {
                return OpaqueOperand(operation, operation.Operand,
                    operand.Classification.Abstention);
            }

            var type = _owner._factory.GetTypeInfo(operand.Term.Type);
            if (type.Kind is not (
                IrTypeKind.String or IrTypeKind.Reference or IrTypeKind.Sequence))
            {
                return OpaqueOperand(operation, operation.Operand,
                    FrontendAbstention.UnsupportedType);
            }

            return LoweredExpression.Exact(_owner._factory.Binary(
                IrBinaryOperator.Equal, operand.Term,
                _owner._factory.Null(operand.Term.Type)));
        }

        public override LoweredExpression VisitPropertyReference(
            IPropertyReferenceOperation operation, LoweringContext argument)
        {
            if (IsIntrinsicLength(operation))
            {
                var instance = _owner.LowerCore(operation.Instance!);
                if (!instance.Classification.IsExact)
                {
                    return _owner.Opaque(operation, instance.Classification.Abstention,
                        operation.Property, operation.Instance, []);
                }

                return LoweredExpression.Exact(_owner._factory.Length(instance.Term));
            }
            return _owner.Opaque(operation, FrontendAbstention.UnsupportedMemberAccess,
                operation.Property, operation.Instance,
                operation.Arguments.Select(static value => value.Value));
        }

        public override LoweredExpression VisitArrayElementReference(
            IArrayElementReferenceOperation operation, LoweringContext argument)
        {
            if (operation.Indices.Length != 1)
            {
                return OpaqueElement(operation, FrontendAbstention.UnsupportedMemberAccess);
            }

            var array = _owner.LowerCore(operation.ArrayReference);
            var index = _owner.LowerCore(operation.Indices[0]);
            if (!array.Classification.IsExact || !index.Classification.IsExact)
            {
                return OpaqueElement(operation, FirstAbstention(array, index));
            }

            try
            {
                return LoweredExpression.Exact(_owner._factory.SequenceAccess(
                    array.Term, index.Term));
            }
            catch (ArgumentException)
            {
                return OpaqueElement(operation, FrontendAbstention.UnsupportedType);
            }
        }

        public override LoweredExpression VisitInvocation(
            IInvocationOperation operation, LoweringContext argument)
        {
            return _owner.Opaque(operation, FrontendAbstention.UnsupportedInvocationShape,
                operation.TargetMethod, operation.Instance,
                operation.Arguments.Select(static value => value.Value));
        }

        private static FrontendAbstention FirstAbstention(
            params LoweredExpression[] expressions)
        {
            return expressions
                .First(static expression =>
                    expression.Classification.Abstention != FrontendAbstention.None)
                .Classification.Abstention;
        }

        private LoweredExpression OpaqueOperand(
            IOperation operation,
            IOperation operand,
            FrontendAbstention abstention,
            ISymbol? symbol = null)
        {
            return _owner.Opaque(operation, abstention, symbol, arguments: [operand]);
        }

        private LoweredExpression OpaqueConditional(
            IConditionalOperation operation,
            FrontendAbstention abstention)
        {
            return _owner.Opaque(operation, abstention,
                arguments: [operation.Condition, operation.WhenTrue, operation.WhenFalse!]);
        }

        private LoweredExpression OpaqueElement(
            IArrayElementReferenceOperation operation,
            FrontendAbstention abstention)
        {
            return _owner.Opaque(operation, abstention, receiver: operation.ArrayReference,
                arguments: operation.Indices);
        }

        private LoweredExpression OpaqueBinary(
            IBinaryOperation operation, FrontendAbstention abstention,
            IMethodSymbol? symbol = null)
        {
            return _owner.Opaque(operation, abstention, symbol,
                arguments: [operation.LeftOperand, operation.RightOperand]);
        }
    }

    private LoweredExpression CreateMissingOperation()
    {
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

    private sealed class LoweredExpression(
        IrTerm term,
        FrontendSubsetClassification classification)
    {
        internal IrTerm Term { get; } = term;
        internal FrontendSubsetClassification Classification
        {
            get;
        } =
            classification;

        internal static LoweredExpression Exact(IrTerm term)
        {
            return new(term, FrontendSubsetClassification.Exact);
        }
    }
}
