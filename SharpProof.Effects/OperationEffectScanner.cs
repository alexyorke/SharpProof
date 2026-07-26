namespace SharpProof.Effects;

internal sealed class EffectCallSite(
    IMethodSymbol target,
    EffectRegionSet receiver,
    ImmutableArray<EffectRegionSet> arguments) {
    internal IMethodSymbol Target { get; } = target;
    internal EffectRegionSet Receiver { get; } = receiver;
    internal ImmutableArray<EffectRegionSet> Arguments { get; } = arguments;
}

internal sealed class OperationEffectScanner {
    private readonly List<EffectCallSite> _calls;
    private readonly Dictionary<ISymbol, EffectRegionSet> _localRegions =
        new(SymbolEqualityComparer.Default);
    private readonly IMethodSymbol _method;
    private readonly EffectAnalysisSession _session;

    internal OperationEffectScanner(
        EffectAnalysisSession session, IMethodSymbol method,
        List<EffectCallSite> calls, IOperation root) {
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
        ControlFlowBranch? branch, IOperation? branchValue) =>
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
        return operation switch {
            IAnonymousFunctionOperation or
                ILocalFunctionOperation or
                ILiteralOperation or
                ILocalReferenceOperation or
                IInstanceReferenceOperation or
                IDefaultValueOperation or
                ITypeOfOperation or
                ISizeOfOperation => EffectSummary.Empty,
            IParameterReferenceOperation parameter =>
                parameter.Parameter.RefKind is RefKind.Ref or RefKind.Out
                    ? EffectSummaryOperations.Read(ClassifyParameter(parameter.Parameter))
                    : EffectSummary.Empty,
            IFieldReferenceOperation field => ScanField(field, access),
            IPropertyReferenceOperation property => ScanProperty(property, access),
            IArrayElementReferenceOperation element => ScanArrayElement(element, access),
            ISimpleAssignmentOperation assignment => EffectSummaryOperations.Join(
                Scan(assignment.Value),
                ScanWriteTarget(assignment.Target, assignment.Value)),
            ICompoundAssignmentOperation assignment => ScanCompoundAssignment(assignment),
            IIncrementOrDecrementOperation increment => EffectSummaryOperations.Join(
                Scan(increment.Target, EffectAccess.Read),
                Scan(increment.Target, EffectAccess.Write),
                CheckedOverflow(increment.IsChecked),
                ScanOperatorCall(
                    increment.OperatorMethod,
                    EffectRegionSet.Empty,
                    [ClassifyRegion(increment.Target)])),
            IInvocationOperation invocation => ScanInvocation(invocation),
            IObjectCreationOperation creation => ScanObjectCreation(creation),
            IArrayCreationOperation array => EffectSummaryOperations.Join(
                ScanChildren(array),
                EffectSummaryOperations.Allocate(EffectAllocationKind.Managed),
                ArrayCreationExceptions(array)),
            IDelegateCreationOperation delegateCreation => EffectSummaryOperations.Join(
                ScanChildren(delegateCreation),
                EffectSummaryOperations.Allocate(EffectAllocationKind.Managed)),
            IAnonymousObjectCreationOperation anonymousObject => EffectSummaryOperations.Join(
                ScanChildren(anonymousObject),
                EffectSummaryOperations.Allocate(EffectAllocationKind.Managed)),
            IThrowOperation thrown => EffectSummaryOperations.Join(
                ScanChildren(thrown),
                EffectSummaryOperations.Throw(
                    _session.ResolveThrownException(thrown.Exception))),
            IBinaryOperation binary => ScanBinary(binary),
            IUnaryOperation unary => ScanUnary(unary),
            IConversionOperation conversion => ScanConversion(conversion),
            ILockOperation @lock => EffectSummaryOperations.Join(
                ScanChildren(@lock),
                PotentialNullLock(@lock.LockedValue),
                EffectSummaryOperations.Capability(
                    EffectCapabilityKind.Synchronization)),
            ILoopOperation loop => EffectSummaryOperations.Join(
                ScanChildren(loop),
                EffectSummaryOperations.MayDiverge()),
            IInvalidOperation or
                IDynamicInvocationOperation or
                IDynamicIndexerAccessOperation or
                IFunctionPointerInvocationOperation => EffectSummaryOperations.Join(
                    ScanChildren(operation),
                    EffectSummaryOperations.Unsupported()),
            _ => ScanDefault(operation)
        };
    }

    private EffectSummary ScanField(
        IFieldReferenceOperation field, EffectAccess access) {
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
        IPropertyReferenceOperation property, EffectAccess access) {
        if (access == EffectAccess.Write)
            return EffectSummaryOperations.Unsupported();
        var getter = property.Property.GetMethod;
        return getter == null
            ? EffectSummaryOperations.Join(
                ScanCallChildren(property.Instance, property.Arguments),
                EffectSummaryOperations.Unsupported())
            : ScanCall(
                getter,
                property.Instance,
                property.Arguments,
                ClassifyArguments(property.Arguments, getter.Parameters.Length),
                IsDispatchUncertain(getter));
    }

    private EffectSummary ScanArrayElement(
        IArrayElementReferenceOperation element, EffectAccess access) {
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

    private EffectSummary ScanWriteTarget(IOperation target, IOperation value) =>
        target switch {
            IFieldReferenceOperation field => ScanField(field, EffectAccess.Write),
            IArrayElementReferenceOperation element =>
                ScanArrayElement(element, EffectAccess.Write),
            IPropertyReferenceOperation property => ScanPropertySetter(property, value),
            IParameterReferenceOperation parameter
                when parameter.Parameter.RefKind is RefKind.Ref or RefKind.Out =>
                EffectSummaryOperations.Write(ClassifyParameter(parameter.Parameter)),
            ILocalReferenceOperation or
                IParameterReferenceOperation or
                IDiscardOperation => EffectSummary.Empty,
            _ => EffectSummaryOperations.Join(
                Scan(target),
                EffectSummaryOperations.Unsupported())
        };

    private EffectSummary ScanPropertySetter(
        IPropertyReferenceOperation property, IOperation value) {
        var setter = property.Property.SetMethod;
        if (setter == null) return EffectSummaryOperations.Unsupported();
        var receiver = ClassifyRegion(property.Instance);
        var arguments = ClassifyArguments(
            property.Arguments,
            setter.Parameters.Length);
        arguments = arguments.SetItem(
            setter.Parameters.Length - 1,
            ClassifyRegion(value));
        return ScanCall(
            setter,
            property.Instance,
            property.Arguments,
            arguments,
            IsDispatchUncertain(setter),
            receiver);
    }

    private EffectSummary ScanCompoundAssignment(ICompoundAssignmentOperation assignment) {
        var operatorCall = ScanOperatorCall(
            assignment.OperatorMethod,
            EffectRegionSet.Empty,
            [ClassifyRegion(assignment.Target), ClassifyRegion(assignment.Value)]);
        var exceptions = IntegralDivisionExceptions(
            assignment.OperatorKind,
            assignment.Type);
        return EffectSummaryOperations.Join(
            Scan(assignment.Target, EffectAccess.Read),
            Scan(assignment.Value),
            ScanWriteTarget(assignment.Target, assignment.Value),
            operatorCall,
            exceptions,
            CheckedOverflow(assignment.IsChecked));
    }

    private EffectSummary IntegralDivisionExceptions(
        BinaryOperatorKind operatorKind, ITypeSymbol? type) {
        if (operatorKind is not (
                BinaryOperatorKind.Divide or
                BinaryOperatorKind.Remainder) ||
            !IsIntegral(type))
            return EffectSummary.Empty;
        return IsSignedIntegral(type)
            ? Throw(
                FrameworkTypeMetadataNames.DivideByZeroException,
                FrameworkTypeMetadataNames.OverflowException)
            : Throw(FrameworkTypeMetadataNames.DivideByZeroException);
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
        return ScanCall(
            invocation.TargetMethod,
            invocation.Instance,
            invocation.Arguments,
            ClassifyArguments(
                invocation.Arguments,
                invocation.TargetMethod.Parameters.Length),
            IsDispatchUncertain(invocation));
    }

    private EffectSummary ScanCall(
        IMethodSymbol method, IOperation? instance,
        IEnumerable<IArgumentOperation> arguments,
        ImmutableArray<EffectRegionSet> argumentRegions,
        bool dispatchUncertain, EffectRegionSet? receiver = null) =>
        EffectSummaryOperations.Join(
            ScanCallChildren(instance, arguments),
            _session.ResolveCall(
                method,
                receiver ?? ClassifyRegion(instance),
                argumentRegions,
                dispatchUncertain,
                _calls));

    private EffectSummary ScanCallChildren(
        IOperation? instance, IEnumerable<IArgumentOperation> arguments) =>
        EffectSummaryOperations.Join(
            instance == null ? EffectSummary.Empty : Scan(instance),
            ScanMany(arguments.Select(static argument => argument.Value)),
            PotentialNullReceiver(instance));

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

    private EffectSummary ScanBinary(IBinaryOperation binary) =>
        EffectSummaryOperations.Join(
            Scan(binary.LeftOperand),
            Scan(binary.RightOperand),
            binary.OperatorKind == BinaryOperatorKind.Add &&
            binary.Type?.SpecialType == SpecialType.System_String &&
            !binary.ConstantValue.HasValue
                ? EffectSummaryOperations.Allocate(EffectAllocationKind.Managed)
                : EffectSummary.Empty,
            IntegralDivisionExceptions(binary.OperatorKind, binary.Type),
            CheckedOverflow(binary.IsChecked),
            ScanOperatorCall(
                binary.OperatorMethod,
                EffectRegionSet.Empty,
                [ClassifyRegion(binary.LeftOperand), ClassifyRegion(binary.RightOperand)]));

    private EffectSummary ScanUnary(IUnaryOperation unary) =>
        EffectSummaryOperations.Join(
            Scan(unary.Operand),
            CheckedOverflow(unary.IsChecked),
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
            return Throw(
                FrameworkTypeMetadataNames.InvalidCastException,
                FrameworkTypeMetadataNames.NullReferenceException);
        if (conversion.IsUserDefined)
            return ClassifyNullableAndCheckedConversion(operation);
        if (conversion.IsReference)
            return conversion.IsExplicit && !operation.IsTryCast
                ? Throw(FrameworkTypeMetadataNames.InvalidCastException)
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
                Throw(FrameworkTypeMetadataNames.InvalidOperationException));
        return result;
    }

    private EffectSummary CheckedConversionException(
        IConversionOperation operation) =>
        CheckedOverflow(operation.IsChecked);

    private EffectSummary CheckedOverflow(bool isChecked) =>
        isChecked
            ? Throw(FrameworkTypeMetadataNames.OverflowException)
            : EffectSummary.Empty;

    private EffectSummary Throw(params string[] exceptionMetadataNames) =>
        EffectSummaryOperations.Throw(
            _session.ResolveExceptionSet(exceptionMetadataNames));

    private EffectSummary ScanOperatorCall(
        IMethodSymbol? method, EffectRegionSet receiver,
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

    private EffectSummary ScanMany(IEnumerable<IOperation> operations) =>
        EffectSummaryOperations.JoinFrom(
            EffectSummary.Empty,
            operations.Select(Scan));

    private EffectSummary PotentialNullReceiver(IOperation? instance) {
        if (instance == null ||
            instance is IInstanceReferenceOperation ||
            instance.Type is { IsValueType: true } ||
            IsDefinitelyNonNull(instance))
            return EffectSummary.Empty;
        return Throw(FrameworkTypeMetadataNames.NullReferenceException);
    }

    private EffectSummary PotentialNullLock(IOperation value) =>
        IsDefinitelyNonNull(value)
            ? EffectSummary.Empty
            : Throw(FrameworkTypeMetadataNames.ArgumentNullException);

    private static bool IsDefinitelyNonNull(IOperation operation) {
        while (operation is IConversionOperation conversion &&
               conversion.OperatorMethod == null &&
               !conversion.IsTryCast)
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
            : Throw(FrameworkTypeMetadataNames.OverflowException);

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

    private EffectRegionSet ClassifyRegion(
        IOperation? operation, bool aliasSource = false) {
        while (operation is IConversionOperation conversion &&
               conversion.OperatorMethod == null)
            operation = conversion.Operand;
        return operation switch {
            null => aliasSource ? EffectRegionSet.Unknown : EffectRegionSet.Empty,
            ILiteralOperation or IDefaultValueOperation when aliasSource =>
                EffectRegionSet.Empty,
            IInstanceReferenceOperation => EffectRegionSet.Create(EffectRegionId.Receiver),
            IParameterReferenceOperation parameter =>
                ClassifyParameter(parameter.Parameter),
            ILocalReferenceOperation local =>
                ClassifyLocal(local.Local),
            IFieldReferenceOperation { Field.IsStatic: true } =>
                EffectRegionSet.Create(EffectRegionId.Static()),
            IFieldReferenceOperation field =>
                ClassifyRegion(field.Instance, aliasSource),
            IArrayElementReferenceOperation element =>
                ClassifyRegion(element.ArrayReference, aliasSource),
            IObjectCreationOperation creation =>
                EffectRegionSet.Create(EffectRegionId.Fresh(creation.Syntax.SpanStart)),
            IArrayCreationOperation creation =>
                EffectRegionSet.Create(EffectRegionId.Fresh(creation.Syntax.SpanStart)),
            IConditionalOperation conditional when aliasSource =>
                ClassifyRegion(conditional.WhenTrue, true).Union(
                    ClassifyRegion(conditional.WhenFalse, true)),
            ICoalesceOperation coalesce when aliasSource =>
                ClassifyRegion(coalesce.Value, true).Union(
                    ClassifyRegion(coalesce.WhenNull, true)),
            IParenthesizedOperation parenthesized when aliasSource =>
                ClassifyRegion(parenthesized.Operand, true),
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

    private EffectRegionSet ClassifyAliasSource(IOperation operation) =>
        ClassifyRegion(operation, aliasSource: true);

    private static bool IsInsideNestedCallable(
        IOperation operation, IOperation root) {
        for (var parent = operation.Parent;
             parent != null && !ReferenceEquals(parent, root);
             parent = parent.Parent)
            if (parent is IAnonymousFunctionOperation or
                ILocalFunctionOperation)
                return true;
        return false;
    }

    private static EffectRegionSet[] CreateUnknownArguments(int count) {
        var result = new EffectRegionSet[count];
        for (var index = 0; index < result.Length; index++)
            result[index] = EffectRegionSet.Unknown;
        return result;
    }

    private ImmutableArray<EffectRegionSet> ClassifyArguments(
        IEnumerable<IArgumentOperation> arguments, int parameterCount) {
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
