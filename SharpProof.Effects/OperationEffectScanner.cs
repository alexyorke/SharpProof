namespace SharpProof.Effects;

internal sealed class EffectCallSite {
    internal EffectCallSite(
        IMethodSymbol target,
        EffectRegionSet receiver,
        ImmutableArray<EffectRegionSet> arguments) {
        Target = target;
        Receiver = receiver;
        Arguments = arguments;
    }

    internal IMethodSymbol Target { get; }
    internal EffectRegionSet Receiver { get; }
    internal ImmutableArray<EffectRegionSet> Arguments { get; }
}

internal sealed class OperationEffectScanner {
    private readonly List<EffectCallSite> _calls;
    private readonly Dictionary<ISymbol, EffectRegionSet> _localRegions =
        new(SymbolEqualityComparer.Default);
    private readonly IMethodSymbol _method;
    private readonly EffectAnalysisSession _session;

    internal OperationEffectScanner(
        EffectAnalysisSession session,
        IMethodSymbol method,
        List<EffectCallSite> calls,
        IOperation root) {
        _session = session;
        _method = method;
        _calls = calls;
        BuildLocalRegions(root);
    }

    internal EffectSummary Scan(IOperation operation) =>
        Scan(operation, EffectAccess.Read);

    internal EffectSummary ScanLexicalControlEffects(IOperation root) {
        var result = EffectSummary.Empty;
        foreach (var operation in root.DescendantsAndSelf()
                     .Where(operation =>
                         !IsInsideNestedCallable(operation, root))) {
            if (operation is not ILockOperation @lock) continue;
            result = EffectSummaryDomain.Instance.Join(
                result,
                EffectSummaryOperations.Join(
                    PotentialNullLock(@lock.LockedValue),
                    EffectSummaryOperations.Capability(
                        EffectCapabilityKind.Synchronization)));
        }
        return result;
    }

    internal EffectSummary ScanExceptionalBranch(
        ControlFlowBranch? branch,
        IOperation? branchValue) =>
        branch?.Semantics switch {
            ControlFlowBranchSemantics.Throw =>
                EffectSummaryOperations.Throw(
                    _session.ResolveThrownException(branchValue)),
            ControlFlowBranchSemantics.Rethrow =>
                EffectSummaryOperations.Throw(EffectThrowSet.Unknown),
            _ => EffectSummary.Empty
        };

    private EffectSummary Scan(IOperation operation, EffectAccess access) {
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        switch (operation) {
            case IAnonymousFunctionOperation:
            case ILocalFunctionOperation:
                return EffectSummary.Empty;
            case ILiteralOperation:
            case ILocalReferenceOperation:
            case IInstanceReferenceOperation:
            case IDefaultValueOperation:
            case ITypeOfOperation:
            case ISizeOfOperation:
                return EffectSummary.Empty;
            case IParameterReferenceOperation parameter:
                return parameter.Parameter.RefKind is RefKind.Ref or RefKind.Out
                    ? EffectSummaryOperations.Read(ClassifyParameter(parameter.Parameter))
                    : EffectSummary.Empty;
            case IFieldReferenceOperation field:
                return ScanField(field, access);
            case IPropertyReferenceOperation property:
                return ScanProperty(property, access);
            case IArrayElementReferenceOperation element:
                return ScanArrayElement(element, access);
            case ISimpleAssignmentOperation assignment:
                return EffectSummaryOperations.Join(
                    Scan(assignment.Value),
                    ScanWriteTarget(assignment.Target, assignment.Value));
            case ICompoundAssignmentOperation assignment:
                return ScanCompoundAssignment(assignment);
            case IIncrementOrDecrementOperation increment:
                return EffectSummaryOperations.Join(
                    Scan(increment.Target, EffectAccess.Read),
                    Scan(increment.Target, EffectAccess.Write),
                    increment.IsChecked
                        ? EffectSummaryOperations.Throw(
                            _session.ResolveExceptionSet(
                                FrameworkTypeMetadataNames.OverflowException))
                        : EffectSummary.Empty,
                    ScanOperatorCall(
                        increment.OperatorMethod,
                        EffectRegionSet.Empty,
                        [ClassifyRegion(increment.Target)]));
            case IInvocationOperation invocation:
                return ScanInvocation(invocation);
            case IObjectCreationOperation creation:
                return ScanObjectCreation(creation);
            case IArrayCreationOperation array:
                return EffectSummaryOperations.Join(
                    ScanChildren(array),
                    EffectSummaryOperations.Allocate(EffectAllocationKind.Managed),
                    ArrayCreationExceptions(array));
            case IDelegateCreationOperation delegateCreation:
                return EffectSummaryOperations.Join(
                    ScanChildren(delegateCreation),
                    EffectSummaryOperations.Allocate(EffectAllocationKind.Managed));
            case IAnonymousObjectCreationOperation anonymousObject:
                return EffectSummaryOperations.Join(
                    ScanChildren(anonymousObject),
                    EffectSummaryOperations.Allocate(EffectAllocationKind.Managed));
            case IThrowOperation throwOperation:
                return EffectSummaryOperations.Join(
                    ScanChildren(throwOperation),
                    EffectSummaryOperations.Throw(
                        _session.ResolveThrownException(throwOperation.Exception)));
            case IBinaryOperation binary:
                return ScanBinary(binary);
            case IUnaryOperation unary:
                return ScanUnary(unary);
            case IConversionOperation conversion:
                return ScanConversion(conversion);
            case ILockOperation @lock:
                return EffectSummaryOperations.Join(
                    ScanChildren(@lock),
                    PotentialNullLock(@lock.LockedValue),
                    EffectSummaryOperations.Capability(
                        EffectCapabilityKind.Synchronization));
            case ILoopOperation loop:
                return EffectSummaryOperations.Join(
                    ScanChildren(loop),
                    EffectSummaryOperations.MayDiverge());
            case IInvalidOperation:
            case IDynamicInvocationOperation:
            case IDynamicIndexerAccessOperation:
            case IFunctionPointerInvocationOperation:
                return EffectSummaryOperations.Join(
                    ScanChildren(operation),
                    EffectSummaryOperations.Unsupported());
            default:
                return ScanDefault(operation);
        }
    }

    private EffectSummary ScanField(
        IFieldReferenceOperation field,
        EffectAccess access) {
        var region = field.Field.IsStatic
            ? EffectRegionSet.Create(EffectRegionId.Static())
            : ClassifyRegion(field.Instance);
        var accessSummary = access == EffectAccess.Write
            ? EffectSummaryOperations.Write(region)
            : EffectSummaryOperations.Read(region);
        return EffectSummaryOperations.Join(
            field.Instance == null ? EffectSummary.Empty : Scan(field.Instance),
            PotentialNullReceiver(field.Instance),
            accessSummary,
            field.Field.IsStatic && !field.Field.IsConst
                ? _session.ResolveStaticFieldTypeInitialization(
                    _method,
                    field.Field)
                : EffectSummary.Empty);
    }

    private EffectSummary ScanProperty(
        IPropertyReferenceOperation property,
        EffectAccess access) {
        if (access == EffectAccess.Write)
            return EffectSummaryOperations.Unsupported();
        var receiver = ClassifyRegion(property.Instance);
        var arguments = property.Arguments
            .ToImmutableArray();
        var childEffects = EffectSummaryOperations.Join(
            property.Instance == null ? EffectSummary.Empty : Scan(property.Instance),
            ScanMany(property.Arguments.Select(static argument => argument.Value)),
            PotentialNullReceiver(property.Instance));
        var getter = property.Property.GetMethod;
        return getter == null
            ? EffectSummaryOperations.Join(childEffects, EffectSummaryOperations.Unsupported())
            : EffectSummaryOperations.Join(
                childEffects,
                _session.ResolveCall(
                    getter,
                    receiver,
                    ClassifyArguments(arguments, getter.Parameters.Length),
                    IsDispatchUncertain(getter),
                    _calls));
    }

    private EffectSummary ScanArrayElement(
        IArrayElementReferenceOperation element,
        EffectAccess access) {
        var region = ClassifyRegion(element.ArrayReference);
        var accessSummary = access == EffectAccess.Write
            ? EffectSummaryOperations.Write(region)
            : EffectSummaryOperations.Read(region);
        var exceptions = access == EffectAccess.Write &&
                         element.ArrayReference.Type is IArrayTypeSymbol arrayType &&
                         !arrayType.ElementType.IsValueType
            ? _session.ResolveExceptionSet(
                FrameworkTypeMetadataNames.NullReferenceException,
                FrameworkTypeMetadataNames.IndexOutOfRangeException,
                FrameworkTypeMetadataNames.ArrayTypeMismatchException)
            : _session.ResolveExceptionSet(
                FrameworkTypeMetadataNames.NullReferenceException,
                FrameworkTypeMetadataNames.IndexOutOfRangeException);
        return EffectSummaryOperations.Join(
            Scan(element.ArrayReference),
            ScanMany(element.Indices),
            accessSummary,
            EffectSummaryOperations.Throw(exceptions));
    }

    private EffectSummary ScanWriteTarget(IOperation target, IOperation value) {
        switch (target) {
            case IFieldReferenceOperation field:
                return ScanField(field, EffectAccess.Write);
            case IArrayElementReferenceOperation element:
                return ScanArrayElement(element, EffectAccess.Write);
            case IPropertyReferenceOperation property:
                return ScanPropertySetter(property, value);
            case IParameterReferenceOperation parameter
                when parameter.Parameter.RefKind is RefKind.Ref or RefKind.Out:
                return EffectSummaryOperations.Write(
                    ClassifyParameter(parameter.Parameter));
            case ILocalReferenceOperation:
            case IParameterReferenceOperation:
            case IDiscardOperation:
                return EffectSummary.Empty;
            default:
                return EffectSummaryOperations.Join(
                    Scan(target),
                    EffectSummaryOperations.Unsupported());
        }
    }

    private EffectSummary ScanPropertySetter(
        IPropertyReferenceOperation property,
        IOperation value) {
        var setter = property.Property.SetMethod;
        if (setter == null) return EffectSummaryOperations.Unsupported();
        var receiver = ClassifyRegion(property.Instance);
        var arguments = ClassifyArguments(
            property.Arguments,
            setter.Parameters.Length);
        arguments = arguments.SetItem(
            setter.Parameters.Length - 1,
            ClassifyRegion(value));
        return EffectSummaryOperations.Join(
            property.Instance == null ? EffectSummary.Empty : Scan(property.Instance),
            ScanMany(property.Arguments.Select(static argument => argument.Value)),
            PotentialNullReceiver(property.Instance),
            _session.ResolveCall(
                setter,
                receiver,
                arguments,
                IsDispatchUncertain(setter),
                _calls));
    }

    private EffectSummary ScanCompoundAssignment(ICompoundAssignmentOperation assignment) {
        var operatorCall = ScanOperatorCall(
            assignment.OperatorMethod,
            EffectRegionSet.Empty,
            [ClassifyRegion(assignment.Target), ClassifyRegion(assignment.Value)]);
        var exceptions = IntegralDivisionExceptions(
            assignment.OperatorKind,
            assignment.Type);
        if (assignment.IsChecked)
            exceptions = EffectSummaryOperations.Join(
                exceptions,
                EffectSummaryOperations.Throw(
                    _session.ResolveExceptionSet(
                        FrameworkTypeMetadataNames.OverflowException)));
        return EffectSummaryOperations.Join(
            Scan(assignment.Target, EffectAccess.Read),
            Scan(assignment.Value),
            ScanWriteTarget(assignment.Target, assignment.Value),
            operatorCall,
            exceptions);
    }

    private EffectSummary IntegralDivisionExceptions(
        BinaryOperatorKind operatorKind,
        ITypeSymbol? type) {
        if (operatorKind is not (
                BinaryOperatorKind.Divide or
                BinaryOperatorKind.Remainder) ||
            !IsIntegral(type))
            return EffectSummary.Empty;
        return IsSignedIntegral(type)
            ? EffectSummaryOperations.Throw(
                _session.ResolveExceptionSet(
                    FrameworkTypeMetadataNames.DivideByZeroException,
                    FrameworkTypeMetadataNames.OverflowException))
            : EffectSummaryOperations.Throw(
                _session.ResolveExceptionSet(
                    FrameworkTypeMetadataNames.DivideByZeroException));
    }

    private EffectSummary ScanInvocation(IInvocationOperation invocation) {
        if (invocation.IsImplicit &&
            invocation.Syntax.AncestorsAndSelf().Any(static syntax =>
                syntax is
                    Microsoft.CodeAnalysis.CSharp.Syntax.LockStatementSyntax))
            return ScanMany(
                invocation.Arguments.Select(static argument => argument.Value));
        if (_session.IsConditionallyElided(invocation))
            return EffectSummary.Empty;
        var receiver = ClassifyRegion(invocation.Instance);
        var arguments = ClassifyArguments(
            invocation.Arguments,
            invocation.TargetMethod.Parameters.Length);
        return EffectSummaryOperations.Join(
            invocation.Instance == null ? EffectSummary.Empty : Scan(invocation.Instance),
            ScanMany(invocation.Arguments.Select(static argument => argument.Value)),
            PotentialNullReceiver(invocation.Instance),
            _session.ResolveCall(
                invocation.TargetMethod,
                receiver,
                arguments,
                IsDispatchUncertain(invocation),
                _calls));
    }

    private EffectSummary ScanObjectCreation(IObjectCreationOperation creation) {
        var receiver = EffectRegionSet.Create(
            EffectRegionId.Fresh(creation.Syntax.SpanStart));
        var constructor = creation.Constructor;
        return EffectSummaryOperations.Join(
            ScanMany(creation.Arguments.Select(static argument => argument.Value)),
            creation.Initializer == null
                ? EffectSummary.Empty
                : ScanChildren(creation.Initializer),
            EffectSummaryOperations.Allocate(EffectAllocationKind.Managed),
            constructor == null
                ? EffectSummaryOperations.Unsupported()
                : _session.ResolveCall(
                    constructor,
                    receiver,
                    ClassifyArguments(
                        creation.Arguments,
                        constructor.Parameters.Length),
                    false,
                    _calls));
    }

    private EffectSummary ScanBinary(IBinaryOperation binary) {
        var exceptions =
            IntegralDivisionExceptions(binary.OperatorKind, binary.Type);
        if (binary.IsChecked)
            exceptions = EffectSummaryOperations.Join(
                exceptions,
                EffectSummaryOperations.Throw(
                    _session.ResolveExceptionSet(FrameworkTypeMetadataNames.OverflowException)));
        return EffectSummaryOperations.Join(
            Scan(binary.LeftOperand),
            Scan(binary.RightOperand),
            binary.OperatorKind == BinaryOperatorKind.Add &&
            binary.Type?.SpecialType == SpecialType.System_String &&
            !binary.ConstantValue.HasValue
                ? EffectSummaryOperations.Allocate(EffectAllocationKind.Managed)
                : EffectSummary.Empty,
            exceptions,
            ScanOperatorCall(
                binary.OperatorMethod,
                EffectRegionSet.Empty,
                [ClassifyRegion(binary.LeftOperand), ClassifyRegion(binary.RightOperand)]));
    }

    private EffectSummary ScanUnary(IUnaryOperation unary) =>
        EffectSummaryOperations.Join(
            Scan(unary.Operand),
            unary.IsChecked
                ? EffectSummaryOperations.Throw(
                    _session.ResolveExceptionSet(
                        FrameworkTypeMetadataNames.OverflowException))
                : EffectSummary.Empty,
            ScanOperatorCall(
                unary.OperatorMethod,
                EffectRegionSet.Empty,
                [ClassifyRegion(unary.Operand)]));

    private EffectSummary ScanConversion(IConversionOperation operation) {
        if (!string.Equals(
                operation.Syntax.Language,
                LanguageNames.CSharp,
                StringComparison.Ordinal))
            return EffectSummaryOperations.Join(
                Scan(operation.Operand),
                EffectSummaryOperations.Unsupported());

        var conversion =
            Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetConversion(operation);
        return EffectSummaryOperations.Join(
            Scan(operation.Operand),
            ClassifyConversion(operation, conversion),
            ScanOperatorCall(
                operation.OperatorMethod,
                EffectRegionSet.Empty,
                [ClassifyRegion(operation.Operand)]));
    }

    private EffectSummary ClassifyConversion(
        IConversionOperation operation,
        Microsoft.CodeAnalysis.CSharp.Conversion conversion) {
        if (!conversion.Exists)
            return EffectSummaryOperations.Unsupported();

        // These categories can overlap. Handle the effectful categories first,
        // then the effect-neutral categories, and fail closed for every
        // remaining Roslyn conversion category.
        if (conversion.IsDynamic)
            return EffectSummaryOperations.Unsupported();
        if (conversion.IsBoxing)
            return EffectSummaryOperations.Allocate(EffectAllocationKind.Managed);
        if (conversion.IsUnboxing)
            return EffectSummaryOperations.Throw(
                _session.ResolveExceptionSet(
                    FrameworkTypeMetadataNames.InvalidCastException,
                    FrameworkTypeMetadataNames.NullReferenceException));
        if (conversion.IsUserDefined)
            return ClassifyNullableAndCheckedConversion(operation);
        if (conversion.IsReference)
            return conversion.IsExplicit && !operation.IsTryCast
                ? EffectSummaryOperations.Throw(
                    _session.ResolveExceptionSet(
                        FrameworkTypeMetadataNames.InvalidCastException))
                : EffectSummary.Empty;
        if (conversion.IsNullable)
            return ClassifyNullableAndCheckedConversion(operation);
        if (conversion.IsNumeric || conversion.IsEnumeration)
            return CheckedConversionException(operation);
        if (conversion.IsInterpolatedString)
            return EffectSummaryOperations.Allocate(EffectAllocationKind.Managed);
        if (conversion.IsAnonymousFunction || conversion.IsMethodGroup)
            return EffectSummaryOperations.Allocate(EffectAllocationKind.Managed);
        if (conversion.IsIdentity ||
            conversion.IsNullLiteral ||
            conversion.IsDefaultLiteral ||
            conversion.IsConstantExpression ||
            conversion.IsThrow ||
            conversion.IsObjectCreation ||
            conversion.IsSwitchExpression ||
            conversion.IsConditionalExpression)
            return EffectSummary.Empty;

        // Collection expressions, interpolated-string handlers, tuple
        // conversions, stackalloc/span/inline-array conversions, pointer and
        // native-integer conversions are not modeled by this effect domain.
        return EffectSummaryOperations.Unsupported();
    }

    private EffectSummary ClassifyNullableAndCheckedConversion(
        IConversionOperation operation) {
        var result = CheckedConversionException(operation);
        if (IsNullableType(operation.Operand.Type) &&
            !IsNullableType(operation.Type))
            result = EffectSummaryOperations.Join(
                result,
                EffectSummaryOperations.Throw(
                    _session.ResolveExceptionSet(
                        FrameworkTypeMetadataNames.InvalidOperationException)));
        return result;
    }

    private EffectSummary CheckedConversionException(
        IConversionOperation operation) =>
        operation.IsChecked
            ? EffectSummaryOperations.Throw(
                _session.ResolveExceptionSet(
                    FrameworkTypeMetadataNames.OverflowException))
            : EffectSummary.Empty;

    private EffectSummary ScanOperatorCall(
        IMethodSymbol? method,
        EffectRegionSet receiver,
        ImmutableArray<EffectRegionSet> arguments) =>
        method == null
            ? EffectSummary.Empty
            : _session.ResolveCall(
                method,
                receiver,
                arguments,
                false,
                _calls);

    private EffectSummary ScanDefault(IOperation operation) {
        var classification = OperationSubsetClassifier.Classify(operation.Kind);
        var children = ScanChildren(operation);
        return classification.IsExact || IsEffectNeutralContainer(operation)
            ? children
            : EffectSummaryOperations.Join(children, EffectSummaryOperations.Unsupported());
    }

    private EffectSummary ScanChildren(IOperation operation) =>
        ScanMany(operation.ChildOperations);

    private EffectSummary ScanMany(IEnumerable<IOperation> operations) {
        var result = EffectSummary.Empty;
        foreach (var operation in operations)
            result = EffectSummaryDomain.Instance.Join(result, Scan(operation));
        return result;
    }

    private EffectSummary PotentialNullReceiver(IOperation? instance) {
        if (instance == null ||
            instance is IInstanceReferenceOperation ||
            instance.Type is { IsValueType: true })
            return EffectSummary.Empty;
        return EffectSummaryOperations.Throw(
            _session.ResolveExceptionSet(
                FrameworkTypeMetadataNames.NullReferenceException));
    }

    private EffectSummary PotentialNullLock(IOperation value) =>
        IsDefinitelyNonNull(value)
            ? EffectSummary.Empty
            : EffectSummaryOperations.Throw(
                _session.ResolveExceptionSet(
                    FrameworkTypeMetadataNames.ArgumentNullException));

    private static bool IsDefinitelyNonNull(IOperation operation) {
        while (operation is IConversionOperation conversion &&
               conversion.OperatorMethod == null)
            operation = conversion.Operand;
        return operation switch {
            IInstanceReferenceOperation => true,
            IObjectCreationOperation => true,
            IArrayCreationOperation => true,
            ITypeOfOperation => true,
            ILiteralOperation {
                ConstantValue: { HasValue: true, Value: not null }
            } => true,
            _ => false
        };
    }

    private EffectSummary ArrayCreationExceptions(
        IArrayCreationOperation creation) =>
        creation.DimensionSizes.All(IsDefinitelyNonNegative)
            ? EffectSummary.Empty
            : EffectSummaryOperations.Throw(
                _session.ResolveExceptionSet(
                    FrameworkTypeMetadataNames.OverflowException));

    private static bool IsDefinitelyNonNegative(IOperation operation) {
        if (!operation.ConstantValue.HasValue ||
            operation.ConstantValue.Value == null)
            return false;
        try {
            return Convert.ToInt64(
                    operation.ConstantValue.Value,
                    System.Globalization.CultureInfo.InvariantCulture) >= 0;
        }
        catch (Exception exception) when (
            exception is InvalidCastException or
            FormatException or
            OverflowException) {
            return false;
        }
    }

    private EffectRegionSet ClassifyRegion(IOperation? operation) {
        while (operation is IConversionOperation conversion &&
               conversion.OperatorMethod == null)
            operation = conversion.Operand;
        return operation switch {
            null => EffectRegionSet.Empty,
            IInstanceReferenceOperation => EffectRegionSet.Create(EffectRegionId.Receiver),
            IParameterReferenceOperation parameter =>
                ClassifyParameter(parameter.Parameter),
            ILocalReferenceOperation local =>
                ClassifyLocal(local.Local),
            IFieldReferenceOperation { Field.IsStatic: true } =>
                EffectRegionSet.Create(EffectRegionId.Static()),
            IFieldReferenceOperation field => ClassifyRegion(field.Instance),
            IArrayElementReferenceOperation element => ClassifyRegion(element.ArrayReference),
            IObjectCreationOperation creation =>
                EffectRegionSet.Create(EffectRegionId.Fresh(creation.Syntax.SpanStart)),
            IArrayCreationOperation creation =>
                EffectRegionSet.Create(EffectRegionId.Fresh(creation.Syntax.SpanStart)),
            _ => EffectRegionSet.Unknown
        };
    }

    private EffectRegionSet ClassifyParameter(IParameterSymbol parameter) {
        if (SymbolEqualityComparer.Default.Equals(
                parameter.ContainingSymbol?.OriginalDefinition,
                _method.OriginalDefinition))
            return EffectRegionSet.Create(EffectRegionId.Parameter(parameter.Ordinal));
        return EffectRegionSet.Create(EffectRegionId.Captured(parameter.Ordinal));
    }

    private EffectRegionSet ClassifyLocal(ILocalSymbol local) {
        if (SymbolEqualityComparer.Default.Equals(
                local.ContainingSymbol?.OriginalDefinition,
                _method.OriginalDefinition))
            return _localRegions.TryGetValue(local, out var regions)
                ? regions
                : EffectRegionSet.Unknown;
        var ordinal = local.DeclaringSyntaxReferences.FirstOrDefault()?.Span.Start ?? 0;
        return EffectRegionSet.Create(EffectRegionId.Captured(ordinal));
    }

    private void BuildLocalRegions(IOperation root) {
        var relevant = root.DescendantsAndSelf()
            .Where(operation => !IsInsideNestedCallable(operation, root))
            .ToImmutableArray();
        foreach (var declarator in relevant.OfType<IVariableDeclaratorOperation>())
            if (!_localRegions.ContainsKey(declarator.Symbol))
                _localRegions.Add(
                    declarator.Symbol,
                    EffectRegionSet.Empty);

        var changed = true;
        while (changed) {
            changed = false;
            foreach (var operation in relevant) {
                ILocalSymbol? target;
                IOperation? value;
                switch (operation) {
                    case IVariableDeclaratorOperation declarator:
                        target = declarator.Symbol;
                        value = declarator.Initializer?.Value;
                        break;
                    case ISimpleAssignmentOperation {
                        Target: ILocalReferenceOperation local
                    } assignment:
                        target = local.Local;
                        value = assignment.Value;
                        break;
                    case ICoalesceAssignmentOperation {
                        Target: ILocalReferenceOperation local
                    } assignment:
                        target = local.Local;
                        value = assignment.Value;
                        break;
                    default:
                        continue;
                }
                if (value == null) continue;
                var discovered = ClassifyAliasSource(value);
                var previous = _localRegions.TryGetValue(target, out var existing)
                    ? existing
                    : EffectRegionSet.Empty;
                var joined = previous.Union(discovered);
                if (joined == previous) continue;
                _localRegions[target] = joined;
                changed = true;
            }
        }
    }

    private EffectRegionSet ClassifyAliasSource(IOperation operation) {
        while (operation is IConversionOperation conversion &&
               conversion.OperatorMethod == null)
            operation = conversion.Operand;
        return operation switch {
            ILiteralOperation or
            IDefaultValueOperation =>
                EffectRegionSet.Empty,
            IInstanceReferenceOperation =>
                EffectRegionSet.Create(EffectRegionId.Receiver),
            IParameterReferenceOperation parameter =>
                ClassifyParameter(parameter.Parameter),
            ILocalReferenceOperation local =>
                ClassifyLocal(local.Local),
            IFieldReferenceOperation { Field.IsStatic: true } =>
                EffectRegionSet.Create(EffectRegionId.Static()),
            IFieldReferenceOperation field =>
                ClassifyAliasSourceOrUnknown(field.Instance),
            IArrayElementReferenceOperation element =>
                ClassifyAliasSource(element.ArrayReference),
            IObjectCreationOperation creation =>
                EffectRegionSet.Create(
                    EffectRegionId.Fresh(creation.Syntax.SpanStart)),
            IArrayCreationOperation creation =>
                EffectRegionSet.Create(
                    EffectRegionId.Fresh(creation.Syntax.SpanStart)),
            IConditionalOperation conditional =>
                ClassifyAliasSource(conditional.WhenTrue).Union(
                    conditional.WhenFalse == null
                        ? EffectRegionSet.Unknown
                        : ClassifyAliasSource(conditional.WhenFalse)),
            ICoalesceOperation coalesce =>
                ClassifyAliasSource(coalesce.Value).Union(
                    ClassifyAliasSource(coalesce.WhenNull)),
            IParenthesizedOperation parenthesized =>
                ClassifyAliasSource(parenthesized.Operand),
            _ => EffectRegionSet.Unknown
        };
    }

    private EffectRegionSet ClassifyAliasSourceOrUnknown(
        IOperation? operation) =>
        operation == null
            ? EffectRegionSet.Unknown
            : ClassifyAliasSource(operation);

    private static bool IsInsideNestedCallable(
        IOperation operation,
        IOperation root) {
        for (var parent = operation.Parent;
             parent != null && !ReferenceEquals(parent, root);
             parent = parent.Parent)
            if (parent is IAnonymousFunctionOperation or
                ILocalFunctionOperation)
                return true;
        return false;
    }

    private EffectRegionSet[] CreateUnknownArguments(int count) {
        var result = new EffectRegionSet[count];
        for (var index = 0; index < result.Length; index++)
            result[index] = EffectRegionSet.Unknown;
        return result;
    }

    private ImmutableArray<EffectRegionSet> ClassifyArguments(
        IEnumerable<IArgumentOperation> arguments,
        int parameterCount) {
        var result = new EffectRegionSet[parameterCount];
        foreach (var argument in arguments) {
            var ordinal = argument.Parameter?.Ordinal ?? -1;
            if (ordinal < 0 || ordinal >= result.Length)
                return [.. CreateUnknownArguments(parameterCount)];
            result[ordinal] = result[ordinal].Union(
                ClassifyRegion(argument.Value));
        }
        return [.. result];
    }

    private static bool IsDispatchUncertain(IInvocationOperation invocation) =>
        invocation.IsVirtual &&
        IsOpenDispatchTarget(invocation.TargetMethod);

    private static bool IsDispatchUncertain(IMethodSymbol accessor) =>
        !accessor.IsStatic &&
        (accessor.IsVirtual ||
         accessor.IsAbstract ||
         accessor.IsOverride ||
         accessor.ContainingType?.TypeKind == TypeKind.Interface) &&
        IsOpenDispatchTarget(accessor);

    private static bool IsOpenDispatchTarget(IMethodSymbol method) =>
        method.ContainingType?.IsSealed != true &&
        !method.IsSealed;

    private static bool IsNullableType(ITypeSymbol? type) =>
        type is INamedTypeSymbol {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
        };

    private static bool IsIntegral(ITypeSymbol? type) => type?.SpecialType is
        SpecialType.System_SByte or
        SpecialType.System_Byte or
        SpecialType.System_Int16 or
        SpecialType.System_UInt16 or
        SpecialType.System_Int32 or
        SpecialType.System_UInt32 or
        SpecialType.System_Int64 or
        SpecialType.System_UInt64 or
        SpecialType.System_Char;

    private static bool IsSignedIntegral(ITypeSymbol? type) =>
        type?.SpecialType is
            SpecialType.System_SByte or
            SpecialType.System_Int16 or
            SpecialType.System_Int32 or
            SpecialType.System_Int64;

    private static bool IsEffectNeutralContainer(IOperation operation) => operation is
        IBlockOperation or
        IExpressionStatementOperation or
        IReturnOperation or
        IConditionalOperation or
        IConditionalAccessOperation or
        IConditionalAccessInstanceOperation or
        IVariableDeclarationGroupOperation or
        IVariableDeclarationOperation or
        IVariableDeclaratorOperation or
        IVariableInitializerOperation or
        IObjectOrCollectionInitializerOperation or
        IMemberInitializerOperation or
        IArrayInitializerOperation or
        IArgumentOperation or
        IParenthesizedOperation or
        IFlowCaptureOperation or
        IFlowCaptureReferenceOperation or
        IBranchOperation or
        IEmptyOperation or
        IMethodBodyOperation or
        IConstructorBodyOperation;

    private enum EffectAccess {
        Read,
        Write
    }
}
