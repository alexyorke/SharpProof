using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal sealed class OperationEffectScanner
{
    private readonly ManagedFlowResult? _abstractFlow;
    private readonly bool _allowDirectWitnesses;
    private readonly EffectCallSiteResolver _callResolver;
    private readonly SyntaxNode? _directSyntax;
    private readonly ImmutableArray<EffectDirectWitness>.Builder _directWitnesses =
        ImmutableArray.CreateBuilder<EffectDirectWitness>();
    private readonly INamedTypeSymbol? _exceptionType;
    private readonly Dictionary<ISymbol, EffectRegionSet> _localRegions =
        new(SymbolEqualityComparer.Default);
    private readonly IMethodSymbol _method;
    private readonly INamedTypeSymbol? _monitorType;
    private readonly EffectAnalysisSession _session;
    private IOperation? _directOperation;
    private int _scanDepth;

    internal OperationEffectScanner(
        EffectAnalysisSession session, IMethodSymbol method, List<EffectCallSite> calls,
        IOperation root, ManagedFlowResult? abstractFlow, bool allowDirectWitnesses)
    {
        _session = session;
        _method = method;
        _abstractFlow = abstractFlow;
        _callResolver =
            new EffectCallSiteResolver(
                session,
                method,
                calls,
                abstractFlow);
        _allowDirectWitnesses = allowDirectWitnesses;
        _directSyntax = GetDirectSyntax(root.Syntax);
        _exceptionType = session.Compilation.GetTypeByMetadataName(FrameworkTypeMetadataNames.Exception);
        _monitorType = session.Compilation.GetTypeByMetadataName(FrameworkTypeMetadataNames.Monitor);
        BuildLocalRegions(root);
    }

    internal ImmutableArray<EffectDirectWitness> DirectWitnesses =>
        _directWitnesses.ToImmutable();

    internal EffectSummary Scan(IOperation operation)
    {
        operation = ArgumentNullGuard.NotNull(operation, nameof(operation));
        if (_scanDepth++ == 0)
        {
            _directOperation = _allowDirectWitnesses
                ? ManagedFlowResult.GetUnavoidableDirectOperation(operation, _directSyntax)
                : null;
        }

        try
        {
            return Scan(operation, EffectAccess.Read);
        }
        finally
        {
            if (--_scanDepth == 0)
            {
                _directOperation = null;
            }
        }
    }

    internal EffectSummary ScanLexicalControlEffects(IOperation root)
    {
        var result = EffectSummary.Empty;
        foreach (var operation in root.DescendantsAndSelf()
                     .Where(operation =>
                         operation is ILockOperation or IThrowOperation &&
                         !IsInsideNestedCallable(operation, root)))
        {
            if (_abstractFlow != null &&
                !_abstractFlow.IsReachable(operation) &&
                !IsInsideExceptionHandler(operation))
            {
                continue;
            }

            if (IsDirectSyntax(operation))
            {
                if (operation is ILockOperation directLock)
                {
                    RecordDirectLock(directLock);
                }
                else if (operation is IThrowOperation)
                {
                    RecordDirect(operation);
                }
            }
            var lexical = operation switch
            {
                ILockOperation @lock => EffectSummaryOperations.Join(
                    PotentialNullLock(@lock.LockedValue, @lock),
                    EffectSummaryOperations.Capability(EffectCapabilityKind.Synchronization)),
                IThrowOperation thrown when IsSourceThrow(thrown) => EffectExceptionFlow.KeepEscaping(
                    EffectSummaryOperations.Throw(
                        ResolveThrownException(thrown)),
                    thrown, _session.Compilation),
                _ => EffectSummary.Empty
            };
            result = EffectSummaryDomain.Instance.Join(result, lexical);
        }
        return result;
    }

    private EffectSummary Scan(IOperation operation, EffectAccess access)
    {
        if (ManagedFlowResult.HasSameIdentity(operation, _directOperation))
        {
            RecordDirect(operation);
        }

        var summary = operation switch
        {
            IAnonymousFunctionOperation or ILocalFunctionOperation or ILiteralOperation or
                ILocalReferenceOperation or IInstanceReferenceOperation or IDefaultValueOperation or
                ITypeOfOperation or INameOfOperation or ISizeOfOperation => EffectSummary.Empty,
            IParameterReferenceOperation parameter => parameter.Parameter.RefKind is RefKind.Ref or RefKind.Out
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
                ScanWriteTarget(increment.Target, increment.Target, valueIsStoredDirectly: false),
                CheckedOverflow(increment.IsChecked, increment),
                ResolveOperatorEffects(increment.OperatorMethod, [increment.Target], increment)),
            IInvocationOperation invocation => ScanInvocation(invocation),
            IObjectCreationOperation creation => ScanObjectCreation(creation),
            IArrayCreationOperation array => EffectSummaryOperations.Join(ScanChildren(array),
                EffectSummaryOperations.Allocate(EffectAllocationKind.Managed), ArrayCreationExceptions(array)),
            IOperation allocation when allocation is
                IDelegateCreationOperation or IAnonymousObjectCreationOperation =>
                EffectSummaryOperations.Join(
                    ScanChildren(allocation),
                    EffectSummaryOperations.Allocate(EffectAllocationKind.Managed)),
            IInterpolatedStringOperation { ConstantValue.HasValue: true } => EffectSummary.Empty,
            IThrowOperation thrown when IsSourceThrow(thrown) => EffectSummaryOperations.Join(
                ScanChildren(thrown),
                EffectSummaryOperations.Throw(
                    ResolveThrownException(thrown))),
            IThrowOperation => EffectSummary.Empty,
            IBinaryOperation binary => ScanBinary(binary),
            IUnaryOperation unary => ScanUnary(unary),
            IConversionOperation conversion => ScanConversion(conversion),
            ILockOperation @lock => EffectSummaryOperations.Join(
                ScanChildren(@lock),
                PotentialNullLock(@lock.LockedValue, @lock),
                EffectSummaryOperations.Capability(EffectCapabilityKind.Synchronization)),
            ILoopOperation loop => EffectSummaryOperations.Join(
                ScanChildren(loop), EffectSummaryOperations.MayDiverge()),
            IInvalidOperation or IDynamicInvocationOperation or
                IDynamicIndexerAccessOperation or IFunctionPointerInvocationOperation =>
                EffectSummaryOperations.Join(
                    ScanChildren(operation),
                    EffectSummaryOperations.Unsupported()),
            _ => ScanDefault(operation)
        };
        return EffectExceptionFlow.KeepEscaping(summary, operation, _session.Compilation);
    }

    private EffectSummary ScanField(IFieldReferenceOperation field, EffectAccess access)
    {
        if (field.Field.IsConst)
        {
            return EffectSummary.Empty;
        }

        var region = field.Field.IsStatic
            ? EffectRegionSet.Create(EffectRegionId.Static())
            : ClassifyRegion(field.Instance);
        var accessSummary = access == EffectAccess.Write
            ? EffectSummaryOperations.Write(region) : EffectSummaryOperations.Read(region);
        return EffectSummaryOperations.Join(
            field.Instance == null ? EffectSummary.Empty : Scan(field.Instance),
            PotentialNullReceiver(field.Instance, field),
            accessSummary,
            field.Field.IsVolatile
                ? EffectSummaryOperations.Capability(EffectCapabilityKind.Synchronization)
                : EffectSummary.Empty,
            field.Field.IsStatic && !field.Field.IsConst
                ? _session.ResolveStaticFieldTypeInitialization(_method, field.Field)
                : EffectSummary.Empty);
    }

    private EffectSummary ScanProperty(
        IPropertyReferenceOperation property, EffectAccess access, IOperation? assignedValue = null)
    {
        if (access == EffectAccess.Read &&
            IsIntrinsicArrayCardinalityProperty(property))
        {
            return EffectSummaryOperations.Join(
                property.Instance == null
                    ? EffectSummary.Empty : Scan(property.Instance),
                PotentialNullReceiver(property.Instance, property),
                EffectSummaryOperations.Read(
                    ClassifyRegion(property.Instance, aliasSource: true)));
        }

        var accessor = access == EffectAccess.Read
            ? property.Property.GetMethod
            : property.Property.SetMethod;
        if (accessor == null)
        {
            return access == EffectAccess.Write
                ? EffectSummaryOperations.Unsupported()
                : EffectSummaryOperations.Join(
                    ScanCallChildren(property.Instance, property.Arguments, property),
                    EffectSummaryOperations.Unsupported());
        }

        var arguments = ClassifyArguments(property.Arguments, accessor.Parameters.Length);
        var actualArguments = EffectCallSiteResolver.AlignActualArguments(
            property.Arguments,
            accessor.Parameters.Length);
        if (assignedValue != null)
        {
            arguments = arguments.SetItem(accessor.Parameters.Length - 1, ClassifyRegion(assignedValue));
            actualArguments = actualArguments.SetItem(
                accessor.Parameters.Length - 1,
                assignedValue);
        }

        return ScanCall(
            accessor,
            property.Instance,
            property.Arguments,
            arguments,
            actualArguments,
            IsDispatchUncertain(accessor),
            property);
    }

    private EffectSummary ScanArrayElement(
        IArrayElementReferenceOperation element,
        EffectAccess access,
        IOperation? assignedValue = null)
    {
        var region = ClassifyRegion(element.ArrayReference);
        var accessSummary = access == EffectAccess.Write
            ? EffectSummaryOperations.Write(region) : EffectSummaryOperations.Read(region);
        var exceptions = EffectSummary.Empty;
        if (!DefiniteOperationFacts.IsDefinitelyNonNull(element.ArrayReference) &&
            _abstractFlow?.ProvesNonNull(element, element.ArrayReference) != true)
        {
            exceptions = EffectSummaryOperations.Join(exceptions, Throw(FrameworkTypeMetadataNames.NullReferenceException));
        }

        if (_abstractFlow?.ProvesArrayAccess(element) != true)
        {
            exceptions = EffectSummaryOperations.Join(exceptions, Throw(FrameworkTypeMetadataNames.IndexOutOfRangeException));
        }

        if (access == EffectAccess.Write &&
            element.ArrayReference.Type is IArrayTypeSymbol arrayType &&
            !arrayType.ElementType.IsValueType &&
            !ArrayStoreIsDefinitelyCompatible(element, arrayType, assignedValue))
        {
            exceptions = EffectSummaryOperations.Join(exceptions, Throw(FrameworkTypeMetadataNames.ArrayTypeMismatchException));
        }

        return EffectSummaryOperations.Join(
            Scan(element.ArrayReference),
            ScanMany(element.Indices),
            accessSummary,
            exceptions);
    }

    private EffectSummary ScanWriteTarget(
        IOperation target,
        IOperation value,
        bool valueIsStoredDirectly = true)
    {
        return target switch
        {
            IFieldReferenceOperation field => ScanField(field, EffectAccess.Write),
            IArrayElementReferenceOperation element =>
                ScanArrayElement(
                    element,
                    EffectAccess.Write,
                    valueIsStoredDirectly ? value : null),
            IPropertyReferenceOperation property =>
                ScanProperty(
                    property,
                    EffectAccess.Write,
                    valueIsStoredDirectly ? value : null),
            IParameterReferenceOperation parameter
                when parameter.Parameter.RefKind is RefKind.Ref or RefKind.Out =>
                EffectSummaryOperations.Write(ClassifyParameter(parameter.Parameter)),
            ILocalReferenceOperation or IParameterReferenceOperation or IDiscardOperation => EffectSummary.Empty,
            _ => EffectSummaryOperations.Join(
                Scan(target),
                EffectSummaryOperations.Unsupported())
        };
    }

    private EffectSummary ScanCompoundAssignment(ICompoundAssignmentOperation assignment)
    {
        var operatorCall = ResolveOperatorEffects(
            assignment.OperatorMethod,
            [assignment.Target, assignment.Value],
            assignment);
        var exceptions = IntegralDivisionExceptions(assignment.OperatorKind, assignment.Type,
            assignment.Target, assignment.Value, assignment);
        return EffectSummaryOperations.Join(
            Scan(assignment.Target, EffectAccess.Read),
            Scan(assignment.Value),
            ScanWriteTarget(assignment.Target, assignment.Value, valueIsStoredDirectly: false),
            operatorCall,
            exceptions,
            CheckedOverflow(assignment.IsChecked, assignment));
    }

    private bool ArrayStoreIsDefinitelyCompatible(
        IArrayElementReferenceOperation element,
        IArrayTypeSymbol arrayType,
        IOperation? assignedValue)
    {
        if (arrayType.ElementType.IsSealed)
        {
            return true;
        }

        return assignedValue != null &&
            (assignedValue.ConstantValue is { HasValue: true, Value: null } ||
             _abstractFlow?.TryEvaluate(element, assignedValue, out var value) == true &&
             value.IsDefinitelyNull);
    }

    private EffectSummary IntegralDivisionExceptions(
        BinaryOperatorKind operatorKind, ITypeSymbol? type, IOperation left, IOperation right, IOperation origin)
    {
        if (operatorKind is not (BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder) ||
            !TryGetIntegralDivisionSemantics(
                type, out var isSigned, out var hasMinimum, out var minimum))
        {
            return EffectSummary.Empty;
        }

        var result = _abstractFlow?.ProvesNonZero(origin, right) == true
            ? EffectSummary.Empty
            : Throw(FrameworkTypeMetadataNames.DivideByZeroException);
        if (isSigned)
        {
            var overflowProvenAbsent = hasMinimum &&
                _abstractFlow?.ProvesNoSignedDivisionOverflow(origin, left, right, minimum) == true;
            if (!overflowProvenAbsent)
            {
                result = EffectSummaryOperations.Join(result, Throw(FrameworkTypeMetadataNames.OverflowException));
            }
        }
        return result;
    }

    private EffectSummary ScanInvocation(IInvocationOperation invocation)
    {
        if (invocation.IsImplicit &&
            invocation.Syntax.AncestorsAndSelf().Any(static syntax => syntax is LockStatementSyntax))
        {
            return ScanMany(invocation.Arguments.Select(static argument => argument.Value));
        }

        if (_session.IsConditionallyElided(invocation))
        {
            return EffectSummary.Empty;
        }

        return ScanCall(
            invocation.TargetMethod,
            invocation.Instance,
            invocation.Arguments,
            ClassifyArguments(invocation.Arguments, invocation.TargetMethod.Parameters.Length),
            EffectCallSiteResolver.AlignActualArguments(
                invocation.Arguments,
                invocation.TargetMethod.Parameters.Length),
            IsDispatchUncertain(invocation),
            invocation);
    }

    private EffectSummary ScanCall(
        IMethodSymbol method,
        IOperation? instance,
        ImmutableArray<IArgumentOperation> arguments,
        ImmutableArray<EffectRegionSet> argumentRegions,
        ImmutableArray<IOperation?> actualArguments,
        bool dispatchUncertain,
        IOperation origin,
        EffectRegionSet? receiver = null)
    {
        return EffectSummaryOperations.Join(
            ScanCallChildren(instance, arguments, origin),
            _callResolver.Resolve(
                method,
                receiver ?? ClassifyRegion(instance),
                argumentRegions,
                actualArguments,
                dispatchUncertain,
                origin,
                instance,
                arguments));
    }

    private EffectSummary ScanCallChildren(
        IOperation? instance, IEnumerable<IArgumentOperation> arguments, IOperation origin)
    {
        return EffectSummaryOperations.Join(
            instance == null ? EffectSummary.Empty : Scan(instance),
            ScanMany(arguments.Select(static argument => argument.Value)),
            PotentialNullReceiver(instance, origin));
    }

    private EffectSummary ScanObjectCreation(IObjectCreationOperation creation)
    {
        var receiver = EffectRegionSet.Create(EffectRegionId.Fresh(creation.Syntax.SpanStart));
        return EffectSummaryOperations.Join(
            ScanMany(creation.Arguments.Select(static argument => argument.Value)),
            creation.Initializer == null
                ? EffectSummary.Empty
                : ScanChildren(creation.Initializer),
            creation.Type?.IsValueType == true ? EffectSummary.Empty : EffectSummaryOperations.Allocate(EffectAllocationKind.Managed),
            _callResolver.ResolveConstruction(
                creation,
                receiver,
                ClassifyArguments(
                    creation.Arguments,
                    creation.Constructor?.Parameters.Length ??
                    0)));
    }

    private EffectSummary ScanBinary(IBinaryOperation binary)
    {
        return EffectSummaryOperations.Join(
            Scan(binary.LeftOperand),
            Scan(binary.RightOperand),
            binary.OperatorKind == BinaryOperatorKind.Add &&
            binary.Type?.SpecialType == SpecialType.System_String &&
            !binary.ConstantValue.HasValue
                ? EffectSummaryOperations.Allocate(EffectAllocationKind.Managed)
                : EffectSummary.Empty,
            IntegralDivisionExceptions(binary.OperatorKind, binary.Type,
                binary.LeftOperand, binary.RightOperand, binary),
            CheckedOverflow(binary.IsChecked, binary),
            ResolveOperatorEffects(
                binary.OperatorMethod,
                [binary.LeftOperand, binary.RightOperand],
                binary));
    }

    private EffectSummary ScanUnary(IUnaryOperation unary)
    {
        return EffectSummaryOperations.Join(
            Scan(unary.Operand),
            CheckedOverflow(unary.IsChecked, unary),
            ResolveOperatorEffects(unary.OperatorMethod, [unary.Operand], unary));
    }

    private EffectSummary ScanConversion(IConversionOperation operation)
    {
        if (!string.Equals(operation.Syntax.Language, LanguageNames.CSharp, StringComparison.Ordinal))
        {
            return EffectSummaryOperations.Join(
                Scan(operation.Operand),
                EffectSummaryOperations.Unsupported());
        }

        var conversion = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetConversion(operation);
        return EffectSummaryOperations.Join(
            Scan(operation.Operand),
            ClassifyConversion(operation, conversion),
            ResolveOperatorEffects(operation.OperatorMethod, [operation.Operand], operation));
    }

    private EffectSummary ResolveOperatorEffects(
        IMethodSymbol? method,
        ImmutableArray<IOperation?> operands,
        IOperation origin)
    {
        return _callResolver.ResolveOperator(
            method,
            EffectRegionSet.Empty,
            [.. operands.Select(operand => ClassifyRegion(operand))],
            operands,
            origin);
    }

    private EffectSummary ClassifyConversion(
        IConversionOperation operation,
        Microsoft.CodeAnalysis.CSharp.Conversion conversion)
    {
        if (!conversion.Exists)
        {
            return EffectSummaryOperations.Unsupported();
        }

        // These categories can overlap. Handle the effectful categories first,
        // then the effect-neutral categories, and fail closed for every
        // remaining Roslyn conversion category.
        if (conversion.IsDynamic)
        {
            return EffectSummaryOperations.Unsupported();
        }

        if (conversion.IsBoxing)
        {
            return EffectSummaryOperations.Allocate(EffectAllocationKind.Managed);
        }

        if (conversion.IsUnboxing)
        {
            return Throw(
                FrameworkTypeMetadataNames.InvalidCastException,
                FrameworkTypeMetadataNames.NullReferenceException);
        }

        if (conversion.IsUserDefined)
        {
            return ClassifyNullableAndCheckedConversion(operation);
        }

        if (conversion.IsReference)
        {
            return conversion.IsExplicit && !operation.IsTryCast
                ? Throw(FrameworkTypeMetadataNames.InvalidCastException)
                : EffectSummary.Empty;
        }

        if (conversion.IsNullable)
        {
            return ClassifyNullableAndCheckedConversion(operation);
        }

        if (conversion.IsNumeric || conversion.IsEnumeration)
        {
            return CheckedOverflow(operation.IsChecked, operation);
        }

        if (conversion.IsInterpolatedString)
        {
            return EffectSummaryOperations.Allocate(EffectAllocationKind.Managed);
        }

        if (conversion.IsAnonymousFunction || conversion.IsMethodGroup)
        {
            return EffectSummaryOperations.Allocate(EffectAllocationKind.Managed);
        }

        if (conversion.IsIdentity ||
            conversion.IsNullLiteral ||
            conversion.IsDefaultLiteral ||
            conversion.IsConstantExpression ||
            conversion.IsThrow ||
            conversion.IsObjectCreation ||
            conversion.IsSwitchExpression ||
            conversion.IsConditionalExpression)
        {
            return EffectSummary.Empty;
        }

        // Collection expressions, interpolated-string handlers, tuple
        // conversions, stackalloc/span/inline-array conversions, pointer and
        // native-integer conversions are not modeled by this effect domain.
        return EffectSummaryOperations.Unsupported();
    }

    private EffectSummary ClassifyNullableAndCheckedConversion(
        IConversionOperation operation)
    {
        var result = CheckedOverflow(operation.IsChecked, operation);
        if (IsNullableType(operation.Operand.Type) && !IsNullableType(operation.Type))
        {
            result = EffectSummaryOperations.Join(result,
                Throw(FrameworkTypeMetadataNames.InvalidOperationException));
        }

        return result;
    }

    private EffectSummary CheckedOverflow(
        bool isChecked, IOperation operation)
    {
        return isChecked &&
        _abstractFlow?.ProvesNoOverflow(operation) != true
            ? Throw(FrameworkTypeMetadataNames.OverflowException)
            : EffectSummary.Empty;
    }

    private EffectSummary Throw(params string[] exceptionMetadataNames)
    {
        return EffectSummaryOperations.Throw(_session.ResolveExceptionSet(exceptionMetadataNames));
    }

    private EffectSummary ScanDefault(IOperation operation)
    {
        var classification = OperationSubsetClassifier.Classify(
            OperationSupportStage.EffectDiscovery,
            operation.Kind);
        var children = ScanChildren(operation);
        return classification.IsExact
            ? children
            : EffectSummaryOperations.Join(children, EffectSummaryOperations.Unsupported());
    }

    private EffectSummary ScanChildren(IOperation operation)
    {
        return ScanMany(operation.ChildOperations);
    }

    private EffectSummary ScanMany(IEnumerable<IOperation> operations)
    {
        return EffectSummaryOperations.JoinFrom(EffectSummary.Empty, operations.Select(Scan));
    }

    private EffectSummary PotentialNullReceiver(IOperation? instance, IOperation access)
    {
        if (instance == null ||
            instance is IInstanceReferenceOperation ||
            instance.Type is { IsValueType: true } ||
            DefiniteOperationFacts.IsDefinitelyNonNull(instance) ||
            _abstractFlow?.ProvesNonNull(access, instance) == true)
        {
            return EffectSummary.Empty;
        }

        return Throw(FrameworkTypeMetadataNames.NullReferenceException);
    }

    private EffectSummary PotentialNullLock(IOperation value, IOperation origin)
    {
        return DefiniteOperationFacts.IsDefinitelyNonNull(value) ||
        _abstractFlow?.ProvesNonNull(origin, value) == true
            ? EffectSummary.Empty
            : Throw(FrameworkTypeMetadataNames.ArgumentNullException);
    }

    private EffectSummary ArrayCreationExceptions(
        IArrayCreationOperation creation)
    {
        return creation.DimensionSizes.All(size =>
            IsDefinitelyNonNegative(size) ||
            _abstractFlow?.ProvesNonNegative(creation, size) == true)
            ? EffectSummary.Empty
            : Throw(FrameworkTypeMetadataNames.OverflowException);
    }

    private EffectThrowSet ResolveThrownException(IThrowOperation thrown)
    {
        if (thrown.Exception == null)
        {
            return EffectExceptionFlow.ResolveRethrow(thrown);
        }

        var exceptions = _session.ResolveThrownException(thrown.Exception);
        if (DefiniteOperationFacts.IsDefinitelyNonNull(thrown.Exception) ||
            _abstractFlow?.ProvesNonNull(thrown, thrown.Exception) == true)
        {
            return exceptions;
        }

        return exceptions.Union(
            _session.ResolveExceptionSet(
                FrameworkTypeMetadataNames.NullReferenceException));
    }

    private static bool IsDefinitelyNonNegative(IOperation operation)
    {
        return operation.ConstantValue is { HasValue: true } constant &&
        ManagedAbstractValue.FromConstant(constant.Value, operation.Type)
            .TryGetInteger(out var interval) &&
        interval.LowerBound >= 0;
    }

    private void RecordDirect(IOperation operation)
    {
        switch (operation)
        {
            case IObjectCreationOperation creation:
                _ = RecordAllocation(creation);
                break;
            case IArrayCreationOperation array:
                _ = RecordArrayAllocation(array);
                break;
            case IThrowOperation { Exception: { } exception } thrown when
                DefiniteOperationFacts.UnwrapHarmlessValue(exception) is IObjectCreationOperation
                {
                    Type: INamedTypeSymbol exceptionType
                } creation &&
                HasNonThrowingConstructorSpec(creation):
                _ = RecordAllocation(creation);
                var exact = IsFrameworkException(exceptionType);
                AddWitness(
                    EffectContractKind.Throws,
                    EffectDirectEventKind.ExplicitThrow,
                    Symbol(exceptionType) + (exact ? ";exact-type=true" : ";exact-type=false"),
                    thrown, exact ? exceptionType : null);
                break;
            case ISimpleAssignmentOperation assignment:
                _ = RecordField(assignment.Target, isWrite: true,
                    DefiniteOperationFacts.IsHarmlessValue(assignment.Value));
                break;
            case IFieldReferenceOperation field:
                _ = RecordField(field, isWrite: false, safeValue: true);
                break;
            case IInvocationOperation invocation when IsMonitorCall(invocation):
                AddSynchronization(
                    EffectDirectEventKind.MonitorCall,
                    Symbol(invocation.TargetMethod),
                    invocation);
                break;
        }
    }

    private void RecordDirectLock(ILockOperation @lock)
    {
        if (@lock.Body is not IBlockOperation { Operations.Length: 0 } ||
            !RecordDirectLockReceiver(@lock.LockedValue))
        {
            return;
        }

        AddSynchronization(
            EffectDirectEventKind.EmptyLock,
            FrameworkTypeMetadataNames.Monitor,
            @lock);
    }

    private bool RecordDirectLockReceiver(IOperation value)
    {
        var receiver = DefiniteOperationFacts.UnwrapHarmlessValue(value);
        return receiver switch
        {
            IObjectCreationOperation creation =>
                RecordAllocation(creation) &&
                HasNonThrowingConstructorSpec(creation),
            IArrayCreationOperation array => RecordArrayAllocation(array),
            IInstanceReferenceOperation or
                IConditionalAccessInstanceOperation or
                ITypeOfOperation => true,
            _ => receiver.ConstantValue is { HasValue: true, Value: not null }
        };
    }

    private bool IsDirectSyntax(IOperation operation)
    {
        return _allowDirectWitnesses &&
        _directWitnesses.Count == 0 &&
        _directSyntax != null &&
        operation.Syntax.SyntaxTree == _directSyntax.SyntaxTree &&
        operation.Syntax.Span == _directSyntax.Span;
    }

    private bool RecordAllocation(IObjectCreationOperation creation)
    {
        if (creation.Type is not INamedTypeSymbol type ||
            !type.IsReferenceType ||
            EffectMethodNodeBuilder
                .HasPotentialConstructionInitialization(
                    type,
                    _session.ApiSpecs) ||
            creation.Initializer != null ||
            !creation.Arguments.All(argument => DefiniteOperationFacts.IsHarmlessValue(argument.Value)))
        {
            return false;
        }

        AddWitness(
            EffectContractKind.Allocates,
            EffectDirectEventKind.ManagedObjectAllocation,
            Symbol(creation.Constructor ?? (ISymbol?)creation.Type), creation);
        return true;
    }

    private bool RecordArrayAllocation(IArrayCreationOperation array)
    {
        if (!DefiniteOperationFacts.IsDirectArrayCreationComplete(array))
        {
            return false;
        }

        AddWitness(
            EffectContractKind.Allocates,
            EffectDirectEventKind.ManagedArrayAllocation,
            Symbol(array.Type),
            array);
        return true;
    }

    private bool RecordField(IOperation target, bool isWrite, bool safeValue)
    {
        if (!safeValue ||
            target is not IFieldReferenceOperation
            {
                Field: { IsConst: false, IsStatic: false },
                Instance: IInstanceReferenceOperation
            } field)
        {
            return false;
        }

        AddWitness(
            isWrite ? EffectContractKind.WritesReceiverState : EffectContractKind.ReadsReceiverState,
            isWrite
                ? EffectDirectEventKind.ReceiverFieldWrite
                : EffectDirectEventKind.ReceiverFieldRead,
            Symbol(field.Field),
            field);
        if (field.Field.IsVolatile)
        {
            AddSynchronization(
                EffectDirectEventKind.VolatileFieldAccess,
                Symbol(field.Field),
                field);
        }

        return true;
    }

    private bool IsMonitorCall(IInvocationOperation invocation)
    {
        return !invocation.IsImplicit &&
        invocation.Instance == null &&
        !invocation.Arguments.IsDefaultOrEmpty &&
        invocation.Arguments.All(argument => DefiniteOperationFacts.IsHarmlessValue(argument.Value)) &&
        DefiniteOperationFacts.IsDefinitelyNonNull(invocation.Arguments[0].Value) &&
        invocation.TargetMethod.Name is "Enter" or "Exit" or "Pulse" or "PulseAll" or "TryEnter" or "Wait" &&
        _monitorType != null &&
        SymbolEqualityComparer.Default.Equals(
            invocation.TargetMethod.ContainingType.OriginalDefinition, _monitorType.OriginalDefinition);
    }

    private bool IsFrameworkException(INamedTypeSymbol type)
    {
        return _exceptionType != null &&
        SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _exceptionType.ContainingAssembly) &&
        EffectTypeFacts.IsDerivedFrom(type, _exceptionType);
    }

    private bool HasNonThrowingConstructorSpec(IObjectCreationOperation creation)
    {
        return creation.Constructor != null &&
               _session.ApiSpecs.TryGet(creation.Constructor, out var spec) &&
               spec.Template.Facets.Throws.Behavior ==
               SpecThrowBehavior.DoesNotThrow &&
               spec.Template.Facets.Termination?.Behavior ==
               SpecTerminationBehavior.Terminates;
    }

    private void AddWitness(
        EffectContractKind effects,
        EffectDirectEventKind eventKind,
        string detail,
        IOperation operation,
        INamedTypeSymbol? exceptionType = null,
        EffectContractCapabilityKind capabilities = EffectContractCapabilityKind.None)
    {
        _directWitnesses.Add(new EffectDirectWitness(
            effects, capabilities, exceptionType, eventKind, detail, operation));
    }

    private void AddSynchronization(
        EffectDirectEventKind eventKind,
        string detail,
        IOperation operation)
    {
        AddWitness(
            EffectContractKind.Synchronizes,
            eventKind,
            detail,
            operation,
            capabilities: EffectContractCapabilityKind.Synchronization);
    }

    private static SyntaxNode? GetDirectSyntax(SyntaxNode declaration)
    {
        return declaration switch
        {
            BaseMethodDeclarationSyntax method =>
                (SyntaxNode?)method.ExpressionBody?.Expression ?? SingleStatement(method.Body),
            AccessorDeclarationSyntax accessor =>
                (SyntaxNode?)accessor.ExpressionBody?.Expression ?? SingleStatement(accessor.Body),
            LocalFunctionStatementSyntax local =>
                (SyntaxNode?)local.ExpressionBody?.Expression ?? SingleStatement(local.Body),
            BlockSyntax block => SingleStatement(block),
            _ => null
        };
    }

    private static StatementSyntax? SingleStatement(BlockSyntax? body)
    {
        return body is { Statements.Count: 1 } ? body.Statements[0] : null;
    }

    private static string Symbol(ISymbol? symbol)
    {
        return symbol == null
            ? "<unknown>"
            : DocumentationCommentId.CreateDeclarationId(symbol) ?? symbol.Kind + ":" + symbol.MetadataName;
    }

    private EffectRegionSet ClassifyRegion(
        IOperation? operation, bool aliasSource = false)
    {
        while (operation is IConversionOperation conversion &&
               conversion.OperatorMethod == null)
        {
            operation = conversion.Operand;
        }

        return operation switch
        {
            null => aliasSource ? EffectRegionSet.Unknown : EffectRegionSet.Empty,
            ILiteralOperation or IDefaultValueOperation when aliasSource => EffectRegionSet.Empty,
            IInstanceReferenceOperation => EffectRegionSet.Create(EffectRegionId.Receiver),
            IParameterReferenceOperation parameter => ClassifyParameter(parameter.Parameter),
            ILocalReferenceOperation local => ClassifyLocal(local.Local),
            IFieldReferenceOperation { Field.IsStatic: true } => EffectRegionSet.Create(EffectRegionId.Static()),
            IFieldReferenceOperation => EffectRegionSet.Unknown,
            IArrayElementReferenceOperation => EffectRegionSet.Unknown,
            IOperation creation when creation is
                IObjectCreationOperation or IArrayCreationOperation =>
                EffectRegionSet.Create(EffectRegionId.Fresh(creation.Syntax.SpanStart)),
            IConditionalOperation conditional when aliasSource =>
                ClassifyRegion(conditional.WhenTrue, true).Union(
                    ClassifyRegion(conditional.WhenFalse, true)),
            ICoalesceOperation coalesce when aliasSource =>
                ClassifyRegion(coalesce.Value, true).Union(
                    ClassifyRegion(coalesce.WhenNull, true)),
            IParenthesizedOperation parenthesized when aliasSource => ClassifyRegion(parenthesized.Operand, true),
            _ => EffectRegionSet.Unknown
        };
    }

    private EffectRegionSet ClassifyParameter(IParameterSymbol parameter)
    {
        if (SymbolEqualityComparer.Default.Equals(
                parameter.ContainingSymbol?.OriginalDefinition,
                _method.OriginalDefinition))
        {
            return EffectRegionSet.Create(EffectRegionId.Parameter(parameter.Ordinal));
        }

        return EffectRegionSet.Create(EffectRegionId.Captured(parameter.Ordinal));
    }

    private EffectRegionSet ClassifyLocal(ILocalSymbol local)
    {
        if (SymbolEqualityComparer.Default.Equals(
                local.ContainingSymbol?.OriginalDefinition,
                _method.OriginalDefinition))
        {
            return _localRegions.TryGetValue(local, out var regions)
                ? regions
                : EffectRegionSet.Unknown;
        }

        var ordinal = local.DeclaringSyntaxReferences.FirstOrDefault()?.Span.Start ?? 0;
        return EffectRegionSet.Create(EffectRegionId.Captured(ordinal));
    }

    private void BuildLocalRegions(IOperation root)
    {
        var relevant = root.DescendantsAndSelf()
            .Where(operation => !IsInsideNestedCallable(operation, root))
            .ToImmutableArray();
        foreach (var declarator in relevant.OfType<IVariableDeclaratorOperation>())
        {
            if (!_localRegions.ContainsKey(declarator.Symbol))
            {
                _localRegions.Add(declarator.Symbol, EffectRegionSet.Empty);
            }
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var operation in relevant)
            {
                (ILocalSymbol? Target, IOperation? Value) source = operation switch
                {
                    IVariableDeclaratorOperation declarator =>
                        (declarator.Symbol, declarator.Initializer?.Value),
                    ISimpleAssignmentOperation { Target: ILocalReferenceOperation local } assignment =>
                        (local.Local, assignment.Value),
                    ICoalesceAssignmentOperation { Target: ILocalReferenceOperation local } assignment =>
                        (local.Local, assignment.Value),
                    _ => default
                };
                if (source.Value == null || source.Target == null)
                {
                    continue;
                }

                var discovered = ClassifyRegion(source.Value, aliasSource: true);
                var previous = _localRegions.TryGetValue(source.Target, out var existing)
                    ? existing
                    : EffectRegionSet.Empty;
                var joined = previous.Union(discovered);
                if (joined == previous)
                {
                    continue;
                }

                _localRegions[source.Target] = joined;
                changed = true;
            }
        }
    }

    private static bool IsInsideNestedCallable(IOperation operation, IOperation root)
    {
        for (var parent = operation.Parent; parent != null && !ReferenceEquals(parent, root); parent = parent.Parent)
        {
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return true;
            }
        }

        return false;
    }

    internal bool IsReachable(IOperation operation)
    {
        return _abstractFlow == null ||
        _abstractFlow.IsReachable(operation) ||
        IsInsideExceptionHandler(operation);
    }

    private static bool IsInsideExceptionHandler(IOperation operation)
    {
        return operation.Syntax.AncestorsAndSelf().Any(static syntax =>
            syntax is CatchClauseSyntax or CatchFilterClauseSyntax or FinallyClauseSyntax);
    }

    private static bool IsSourceThrow(IThrowOperation operation)
    {
        return operation.Syntax is ThrowStatementSyntax or ThrowExpressionSyntax;
    }

    private ImmutableArray<EffectRegionSet> ClassifyArguments(
        IEnumerable<IArgumentOperation> arguments, int parameterCount)
    {
        var result = new EffectRegionSet[parameterCount];
        foreach (var argument in arguments)
        {
            var ordinal = argument.Parameter?.Ordinal ?? -1;
            if (ordinal < 0 || ordinal >= result.Length)
            {
                return [.. Enumerable.Repeat(EffectRegionSet.Unknown, parameterCount)];
            }

            result[ordinal] = result[ordinal].Union(ClassifyRegion(argument.Value));
        }
        return [.. result];
    }

    private static bool IsDispatchUncertain(IInvocationOperation invocation)
    {
        return invocation.IsVirtual && IsOpenDispatchTarget(invocation.TargetMethod);
    }

    private static bool IsDispatchUncertain(IMethodSymbol accessor)
    {
        return !accessor.IsStatic &&
        (accessor.IsVirtual ||
         accessor.IsAbstract ||
         accessor.IsOverride ||
         accessor.ContainingType?.TypeKind == TypeKind.Interface) &&
        IsOpenDispatchTarget(accessor);
    }

    private static bool IsOpenDispatchTarget(IMethodSymbol method)
    {
        return method.ContainingType?.IsSealed != true && !method.IsSealed;
    }

    private static bool IsNullableType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
        };
    }

    private static bool IsIntrinsicArrayCardinalityProperty(
        IPropertyReferenceOperation property)
    {
        return property.Instance?.Type is IArrayTypeSymbol &&
        CompilerIdentityBridge.IsIntrinsicSequenceLength(property);
    }

    private static bool TryGetIntegralDivisionSemantics(
        ITypeSymbol? type, out bool isSigned, out bool hasMinimum, out long minimum)
    {
        var specialType = type is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            TypeArguments.Length: 1
        } nullable
            ? nullable.TypeArguments[0].SpecialType
            : type?.SpecialType ?? SpecialType.None;
        if (CSharpScalarSemantics.TryGetInteger(specialType, out var semantics))
        {
            isSigned = semantics.IsSigned;
            hasMinimum = isSigned;
            minimum = semantics.Minimum;
            return true;
        }

        isSigned = specialType == SpecialType.System_IntPtr;
        hasMinimum = false;
        minimum = 0;
        return specialType is
            SpecialType.System_UInt64 or SpecialType.System_IntPtr or SpecialType.System_UIntPtr;
    }

    private enum EffectAccess
    {
        Read,
        Write
    }
}
