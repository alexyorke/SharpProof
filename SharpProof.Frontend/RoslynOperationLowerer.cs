namespace SharpProof.Frontend;

public sealed class RoslynOperationLowerer
{
    private const int MaximumLoweringDepth = 256;
    private readonly IrFactory _factory;
    private readonly Func<IMethodSymbol, bool> _isKnownPure;
    private readonly bool _allowCompilerConstants;
    private readonly Dictionary<ISymbol, IrVarId> _variables =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ITypeSymbol, IrVarId> _instances =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<CaptureId, IrVarId> _captures = [];
    private readonly List<IrVarId> _captureOrder = [];
    private readonly LoweringVisitor _visitor;
    private Dictionary<IOperation, LoweredExpression>? _currentLoweringResults;
    private Dictionary<IOperation, Dictionary<int, bool>>?
        _currentPurityResults;
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

        var previousResults = _currentLoweringResults;
        var previousPurityResults = _currentPurityResults;
        _currentLoweringResults = new(
            ReferenceComparer<IOperation>.Instance);
        _currentPurityResults = new(
            ReferenceComparer<IOperation>.Instance);
        try
        {
            var lowered = LowerCore(operation);
            return new FrontendLoweringResult(
                lowered.Term, lowered.Classification, CreateVariableBindings());
        }
        finally
        {
            _currentLoweringResults = previousResults;
            _currentPurityResults = previousPurityResults;
        }
    }

    internal ImmutableArray<FrontendVariableBinding> CreateVariableBindings()
    {
        return [.. _variables
            .Select(static pair => new FrontendVariableBinding(pair.Key, pair.Value))
            .Concat(_instances.Select(static pair =>
                new FrontendVariableBinding(pair.Key, pair.Value)))
            .OrderBy(static binding => binding.Variable.Value)];
    }

    internal ImmutableArray<IrVarId> CreateCaptureBindings()
    {
        return [.. _captureOrder];
    }

    private LoweredExpression LowerCore(IOperation operation)
    {
        var results = _currentLoweringResults;
        if (results == null)
        {
            return _visitor.Visit(operation, default);
        }

        if (results.TryGetValue(operation, out var existing))
        {
            return existing;
        }

        var lowered = _visitor.Visit(operation, default);
        results.Add(operation, lowered);
        return lowered;
    }

    internal IrTypeId GetTypeId(
        ITypeSymbol? type, bool typeAlreadySpecialized = false)
    {
        if (!typeAlreadySpecialized)
        {
            type = TypeSpecializer(type);
        }
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
            var element = GetTypeId(array.ElementType, typeAlreadySpecialized);
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

    internal bool IsSupportedValueDomain(ITypeSymbol? type)
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
        ISymbol symbol, ref IrTerm? receiver, string purpose, ITypeSymbol? resultType,
        params IrTerm[] arguments)
    {
        return GetMember(
            symbol,
            ref receiver,
            purpose,
            GetTypeId(resultType),
            arguments);
    }

    internal IrMemberId GetMember(
        ISymbol symbol, ref IrTerm? receiver, string purpose, IrTypeId resultType,
        params IrTerm[] arguments)
    {
        var declaringType = GetTypeId(symbol.ContainingType);
        if (receiver != null && receiver.Type != declaringType)
        {
            receiver = _factory.Cast(declaringType, receiver);
        }

        return _factory.GetOrCreateMember(
            CompilerIdentityBridge.InternSymbol(_factory, symbol),
            declaringType,
            purpose + CompilerIdentityBridge.CreateSymbolDisplay(symbol),
            resultType,
            receiver == null,
            [.. arguments.Select(static argument => argument.Type)]);
    }

    private static bool TryGetNullComparisonValue(
        IBinaryOperation operation,
        IOperation left,
        IOperation right,
        out IOperation value)
    {
        if (operation.OperatorKind is not (
                BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
        {
            value = null!;
            return false;
        }
        value = IsNullConstant(right)
            ? left
            : IsNullConstant(left) ? right : null!;
        return value != null;
    }

    private static (IOperation Any, IOperation ReferenceOnly)
        UnwrapComparisonOperand(IOperation operation)
    {
        var any = operation;
        var referenceOnly = operation;
        var canUnwrapReference = true;
        while (any is IConversionOperation
            {
                IsImplicit: true,
                OperatorMethod: null
            } conversion)
        {
            any = conversion.Operand;
            if (canUnwrapReference && conversion.Conversion.IsReference)
            {
                referenceOnly = any;
            }
            else
            {
                canUnwrapReference = false;
            }
        }

        return (any, referenceOnly);
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

        _missingInstance ??= _factory.CreateVariable(
            "instance:<unknown>",
            _factory.ObjectType);
        return _factory.Variable(_missingInstance.Value);
    }

    private LoweredExpression Opaque(
        IOperation operation, FrontendAbstention abstention,
        ISymbol? symbol = null, IOperation? receiver = null,
        IEnumerable<IOperation>? arguments = null)
    {
        var depthLimited = abstention == FrontendAbstention.ExpressionDepthLimit;
        var loweredReceiver = depthLimited || receiver == null
            ? null
            : LowerCore(receiver);
        IrTerm[] loweredArguments = depthLimited
            ? []
            : (arguments ?? operation.ChildOperations)
                .Where(child => !ReferenceEquals(child, receiver))
                .Select((child, index) =>
                {
                    var argument = child as IArgumentOperation;
                    return (
                        Ordinal: argument?.Parameter?.Ordinal ?? int.MaxValue,
                        Index: index,
                        Term: LowerCore(argument?.Value ?? child).Term);
                })
                .OrderBy(static value => value.Ordinal)
                .ThenBy(static value => value.Index)
                .Select(static value => value.Term)
                .ToArray();
        var receiverTerm = loweredReceiver?.Term;
        var argumentTerms = loweredArguments;
        var resultType = GetTypeId(operation.Type);
        var declaringType = receiverTerm?.Type ??
            (symbol?.ContainingType == null
                ? _factory.ObjectType
                : GetTypeId(symbol.ContainingType));
        var isPure = !depthLimited && IsDemonstrablyPure(operation, 0);
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

    private bool IsDemonstrablyPure(IOperation operation, int depth)
    {
        if (depth >= MaximumLoweringDepth)
        {
            return false;
        }

        if (_currentPurityResults is { } purityResults)
        {
            if (!purityResults.TryGetValue(operation, out var depths))
            {
                depths = [];
                purityResults.Add(operation, depths);
            }
            if (depths.TryGetValue(depth, out var cached))
            {
                return cached;
            }

            var result = IsDemonstrablyPureCore(operation, depth);
            depths.Add(depth, result);
            return result;
        }

        return IsDemonstrablyPureCore(operation, depth);
    }

    private bool IsDemonstrablyPureCore(IOperation operation, int depth)
    {

        var childDepth = depth + 1;
        if (operation.ConstantValue.HasValue)
        {
            return true;
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
                IsDemonstrablyPure(conversion.Operand, childDepth),
            IUnaryOperation unary =>
                unary.OperatorMethod == null &&
                IsDemonstrablyPure(unary.Operand, childDepth),
            IBinaryOperation binary =>
                binary.OperatorMethod == null &&
                IsDemonstrablyPure(binary.LeftOperand, childDepth) &&
                IsDemonstrablyPure(binary.RightOperand, childDepth),
            IConditionalOperation conditional =>
                IsDemonstrablyPure(conditional.Condition, childDepth) &&
                IsDemonstrablyPure(conditional.WhenTrue, childDepth) &&
                conditional.WhenFalse != null &&
                IsDemonstrablyPure(conditional.WhenFalse, childDepth),
            IIsNullOperation isNull =>
                IsDemonstrablyPure(isNull.Operand, childDepth),
            IInvocationOperation invocation =>
                _isKnownPure(invocation.TargetMethod) &&
                (invocation.Instance == null ||
                 IsDemonstrablyPure(invocation.Instance, childDepth)) &&
                invocation.Arguments.All(argument =>
                    IsDemonstrablyPure(argument.Value, childDepth)),
            _ => false
        };
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

        var sourceType = operation.Type ??
            operation.SemanticModel?.GetTypeInfo(operation.Syntax).ConvertedType;
        if (sourceType?.TypeKind == TypeKind.Error ||
            !IsSupportedValueDomain(sourceType))
        {
            return Opaque(operation, FrontendAbstention.UnsupportedType);
        }

        var value = operation.ConstantValue.Value;
        var type = GetTypeId(sourceType);
        if (sourceType is { IsValueType: true, SpecialType: SpecialType.None })
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
        private int _depth;

        public override LoweredExpression Visit(
            IOperation? operation, LoweringContext argument)
        {
            if (operation == null)
            {
                return _owner.CreateMissingOperation();
            }

            if (_depth >= MaximumLoweringDepth)
            {
                return _owner.Opaque(
                    operation,
                    FrontendAbstention.ExpressionDepthLimit,
                    arguments: []);
            }

            _depth++;
            try
            {
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
            finally
            {
                _depth--;
            }
        }

        public override LoweredExpression DefaultVisit(
            IOperation operation, LoweringContext argument)
        {
            var abstention = operation.Type?.TypeKind == TypeKind.Error
                ? FrontendAbstention.ErrorOperation
                : GetUnsupportedValueAbstention(operation);
            return _owner.Opaque(operation, abstention, arguments: []);
        }

        private static FrontendAbstention GetUnsupportedValueAbstention(
            IOperation operation)
        {
            return operation.ConstantValue.HasValue &&
                operation.Type is
                {
                    IsValueType: true,
                    SpecialType: SpecialType.None
                }
                ? FrontendAbstention.UnsupportedType
                : FrontendAbstention.UnsupportedOperationKind;
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
            if (CompilerConstantAdmission.IsCatalogIntegerBoundary(operation))
            {
                return _owner.LowerConstant(operation);
            }

            var abstention = GetUnsupportedValueAbstention(operation);
            return _owner.Opaque(operation, abstention, operation.Field);
        }

        public override LoweredExpression VisitLocalReference(
            ILocalReferenceOperation operation, LoweringContext argument)
        {
            if (operation.Local.RefKind != RefKind.None)
            {
                return _owner.Opaque(
                    operation,
                    FrontendAbstention.UnsupportedMutation);
            }

            return LowerSupportedReference(
                operation,
                () => _owner.GetVariable(operation.Local, operation.Type));
        }

        public override LoweredExpression VisitParameterReference(
            IParameterReferenceOperation operation, LoweringContext argument)
        {
            return LowerSupportedReference(
                operation,
                () => _owner.GetVariable(operation.Parameter, operation.Type));
        }

        public override LoweredExpression VisitFlowCapture(
            IFlowCaptureOperation operation, LoweringContext argument)
        {
            _owner.GetCapture(operation.Id, operation.Value.Type);
            return _owner.LowerCore(operation.Value);
        }

        public override LoweredExpression VisitFlowCaptureReference(
            IFlowCaptureReferenceOperation operation, LoweringContext argument)
        {
            return LowerSupportedReference(
                operation,
                () => _owner.GetCapture(operation.Id, operation.Type));
        }

        public override LoweredExpression VisitInstanceReference(
            IInstanceReferenceOperation operation, LoweringContext argument)
        {
            return LowerSupportedReference(
                operation,
                () => _owner.GetInstance(operation));
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

            var typeId = _owner.GetTypeId(type, typeAlreadySpecialized: true);
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

            if (CompilerConstantAdmission.IsLiteralIntegerNegation(operation))
            {
                return _owner.LowerConstant(operation);
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

            var leftOperand = operation.LeftOperand;
            var rightOperand = operation.RightOperand;
            ITypeSymbol? referenceComparisonType = null;
            if (operation.OperatorKind is
                 BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals)
            {
                var leftOperands = UnwrapComparisonOperand(leftOperand);
                var rightOperands = UnwrapComparisonOperand(rightOperand);
                if (GetNullComparison(
                        operation,
                        leftOperands.Any,
                        rightOperands.Any) is { } nullComparison)
                {
                    return nullComparison;
                }
                if (leftOperand.Type?.IsReferenceType == true &&
                    SymbolEqualityComparer.Default.Equals(
                        leftOperand.Type,
                        rightOperand.Type))
                {
                    referenceComparisonType = leftOperand.Type;
                }
                leftOperand = leftOperands.ReferenceOnly;
                rightOperand = rightOperands.ReferenceOnly;
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
                        operation.OperatorKind) && !operation.IsChecked)
                {
                    return OpaqueBinary(
                        operation,
                        FrontendAbstention.UncheckedOverflowSemantics);
                }
            }

            try
            {
                var leftTerm = left.Term;
                var rightTerm = right.Term;
                if (referenceComparisonType != null)
                {
                    var comparisonType =
                        _owner.GetTypeId(referenceComparisonType);
                    leftTerm = _owner._factory.Cast(
                        comparisonType,
                        leftTerm);
                    rightTerm = _owner._factory.Cast(
                        comparisonType,
                        rightTerm);
                }
                return LoweredExpression.Exact(_owner._factory.Binary(
                    mapped.Value, leftTerm, rightTerm));
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

            // Classification must use the constructed type. Roslyn exposes
            // the open type on generic operation nodes while this lowerer may
            // already have specialized it (for example T -> string).
            var specializedOperandType =
                _owner.TypeSpecializer(operation.Operand.Type);
            var specializedTargetType =
                _owner.TypeSpecializer(operation.Type);
            if (!_owner.IsSupportedValueDomain(specializedTargetType))
            {
                // Nullable targets are outside the IR value domain, but a
                // conversion from a non-constant supported operand still has
                // the normal value-changing conversion uncertainty. Preserve
                // UnsupportedType for constant forms, which are handled by
                // the closed-domain edge cases.
                if (!operation.Operand.ConstantValue.HasValue &&
                    specializedTargetType?.OriginalDefinition.SpecialType ==
                    SpecialType.System_Nullable_T)
                {
                    return OpaqueOperand(operation, operation.Operand,
                        FrontendAbstention.ConversionMayChangeValue);
                }
                return OpaqueOperand(
                    operation,
                    operation.Operand,
                    FrontendAbstention.UnsupportedType);
            }

            // A conversion can be the first operation that gives an untyped
            // null literal a supported domain. Lower the folded conversion,
            // not its deliberately typeless operand.
            if (operation.ConstantValue.HasValue)
            {
                return _owner.LowerConstant(operation);
            }

            var operand = _owner.LowerCore(operation.Operand);
            if (!operand.Classification.IsExact)
            {
                return OpaqueOperand(operation, operation.Operand,
                    operand.Classification.Abstention);
            }

            var target = _owner.GetTypeId(operation.Type);
            if (SymbolEqualityComparer.Default.Equals(
                    specializedOperandType,
                    specializedTargetType))
            {
                return operand;
            }

            if (target == operand.Term.Type &&
                CSharpScalarSemantics.IsValuePreservingIntegerConversion(specializedOperandType?.SpecialType ?? SpecialType.None,
                    specializedTargetType?.SpecialType ?? SpecialType.None))
            {
                return operand;
            }

            // Routing stays keyed on the operand's constant so the abstention
            // reasons keep their existing split. LowerConstant is what guards
            // against the conversion itself carrying no constant, which is the
            // case for boxing.
            if (!operation.IsTryCast &&
                operation.Conversion.IsReference &&
                operation.Type?.SpecialType == SpecialType.System_String &&
                _owner._factory.GetTypeInfo(operand.Term.Type).Kind == IrTypeKind.Reference)
            {
                return LoweredExpression.Exact(_owner._factory.Cast(target, operand.Term));
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
            if (CompilerIdentityBridge.IsIntrinsicSequenceLength(operation))
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
                operation.Arguments);
        }

        private LoweredExpression? GetNullComparison(
            IBinaryOperation operation,
            IOperation left,
            IOperation right)
        {
            if (!TryGetNullComparisonValue(operation, left, right, out var compared))
            {
                return null;
            }

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

        private static FrontendAbstention FirstAbstention(
            params LoweredExpression[] expressions)
        {
            return expressions
                .First(static expression =>
                    expression.Classification.Abstention != FrontendAbstention.None)
                .Classification.Abstention;
        }

        private LoweredExpression LowerSupportedReference(
            IOperation operation, Func<IrTerm> exact)
        {
            return _owner.IsSupportedValueDomain(operation.Type)
                ? LoweredExpression.Exact(exact())
                : _owner.Opaque(operation, FrontendAbstention.UnsupportedType);
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
