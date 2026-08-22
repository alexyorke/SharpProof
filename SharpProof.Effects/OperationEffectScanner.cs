using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal sealed partial class OperationEffectScanner
{
    private readonly ManagedFlowResult? _abstractFlow;
    private readonly bool _allowDirectWitnesses;
    private readonly EffectCallSiteResolver _callResolver;
    private readonly ConversionEffectClassifier _conversionEffects;
    private readonly CoalesceAssignmentFlowCaptures _coalesceCaptures = new();
    private readonly OperationCompletionEvaluator _completionEvaluator;
    private readonly CreationFlowCaptures _creationCaptures = new();
    private readonly SyntaxNode? _directSyntax;
    private readonly ImmutableArray<EffectDirectWitness>.Builder _directWitnesses =
        ImmutableArray.CreateBuilder<EffectDirectWitness>();
    private readonly INamedTypeSymbol? _exceptionType;
    private readonly ExceptionHandlerReachability _handlerReachability;
    private readonly Dictionary<int, IArrayTypeSymbol> _freshArrayTypes = new();
    private readonly ConversionOwnershipClassifier _conversionOwnership;
    private readonly IMethodSymbol _method;
    private readonly INamedTypeSymbol? _monitorType;
    private readonly IOperation _root;
    private readonly EffectAnalysisSession _session;
    private readonly OperationNullnessEvaluator _nullnessEvaluator;
    private readonly bool _useAbstractReachability;
    private IOperation? _directOperation;
    private int _scanDepth;
    private int _nestingDepth;

    /// <summary>
    /// Matches the expression-depth ceiling the verifier enforces
    /// (<c>WorkerBudgets.MaximumExpressionDepth</c>) and the base-type walk in
    /// <see cref="EffectMethodNodeBuilder"/>.
    /// </summary>
    private const int MaximumOperationNestingDepth = 256;

    internal OperationEffectScanner(
        EffectAnalysisSession session, IMethodSymbol method, List<EffectCallSite> calls,
        IOperation root, ManagedFlowResult? abstractFlow, bool allowDirectWitnesses)
    {
        _session = session;
        _method = method;
        _root = ArgumentNullGuard.NotNull(root, nameof(root));
        _abstractFlow = abstractFlow;
        _callResolver =
            new EffectCallSiteResolver(
                session,
                method,
                calls,
                abstractFlow);
        _conversionEffects = new ConversionEffectClassifier(session, abstractFlow);
        _conversionOwnership = new ConversionOwnershipClassifier(
            _method,
            _coalesceCaptures,
            _creationCaptures);
        _allowDirectWitnesses = allowDirectWitnesses;
        _directSyntax = GetDirectSyntax(root.Syntax);
        _exceptionType = session.Compilation.GetTypeByMetadataName(FrameworkTypeMetadataNames.Exception);
        _handlerReachability = new ExceptionHandlerReachability(
            session.Compilation,
            abstractFlow);
        _monitorType = session.Compilation.GetTypeByMetadataName(FrameworkTypeMetadataNames.Monitor);
        _nullnessEvaluator = new OperationNullnessEvaluator(
            session,
            _root,
            abstractFlow,
            _monitorType);
        _completionEvaluator = new OperationCompletionEvaluator(
            session,
            _nullnessEvaluator.IsProvenNull,
            _nullnessEvaluator.IsProvenNonNull,
            _nullnessEvaluator.IsImplicitLockEnterWithNullValue);
        // ManagedAbstractFlow currently follows regular CFG edges. Its facts
        // remain useful in a try body, but absence of a fact cannot prove an
        // operation unreachable after a normally completing handler. The
        // enclosing Roslyn CFG still supplies the outer IsReachable gate.
        _useAbstractReachability = !root.DescendantsAndSelf().Any(
            static operation => operation is ITryOperation);
        foreach (var creation in root.DescendantsAndSelf()
                     .OfType<IArrayCreationOperation>())
        {
            if (creation.Type is IArrayTypeSymbol type)
            {
                _freshArrayTypes[creation.Syntax.SpanStart] = type;
            }
        }
        _conversionOwnership.BuildLocalRegions(root, IsReachable);
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
                         !ConversionOwnershipClassifier.IsInsideNestedCallable(operation, root)))
        {
            if (!IsReachable(operation))
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

    internal EffectSummary ScanUsingDisposalEffects(IOperation root)
    {
        return new UsingDisposalEffectResolver(
            _session.Compilation,
            _method,
            _callResolver,
            _abstractFlow).Scan(root, _conversionOwnership.ClassifyRegion);
    }

    private EffectSummary Scan(IOperation operation, EffectAccess access)
    {
        // Every recursive path through the scanner funnels here, so this is the
        // one place that has to keep deeply nested expressions from exhausting
        // the stack. StackOverflowException is uncatchable and would take the
        // compiler host down instead of producing an abstention.
        if (_nestingDepth >= MaximumOperationNestingDepth)
        {
            return EffectSummaryOperations.Unsupported();
        }

        _nestingDepth++;
        try
        {
            return ScanCore(operation, access);
        }
        finally
        {
            _nestingDepth--;
        }
    }

    private EffectSummary ScanCore(IOperation operation, EffectAccess access)
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
            IFlowCaptureOperation capture => ScanFlowCapture(capture),
            IParameterReferenceOperation parameter =>
                parameter.Parameter.RefKind is RefKind.Ref or RefKind.Out ||
                PrimaryConstructorParameterOwnership.IsReceiverBacked(
                    parameter.Parameter,
                    _method)
                    ? EffectSummaryOperations.Read(
                        _conversionOwnership.ClassifyParameter(parameter.Parameter))
                    : EffectSummary.Empty,
            IFieldReferenceOperation field => ScanField(field, access),
            IPropertyReferenceOperation property => ScanProperty(property, access),
            IArrayElementReferenceOperation element => ScanArrayElement(element, access),
            ICoalesceAssignmentOperation assignment =>
                ScanCoalesceAssignment(assignment),
            ISimpleAssignmentOperation assignment =>
                ScanSimpleAssignment(assignment),
            ICompoundAssignmentOperation assignment => ScanCompoundAssignment(assignment),
            IIncrementOrDecrementOperation increment => EffectSummaryOperations.Join(
                Scan(increment.Target, EffectAccess.Read),
                ScanWriteTarget(increment.Target, increment.Target, valueIsStoredDirectly: false),
                _conversionEffects.CheckedOverflow(increment.IsChecked, increment),
                ResolveOperatorEffects(increment.OperatorMethod, [increment.Target], increment)),
            IInvocationOperation invocation => ScanInvocation(invocation),
            IObjectCreationOperation creation => ScanObjectCreation(creation),
            IArrayCreationOperation array => ScanArrayCreation(array),
            IOperation allocation when allocation is
                IDelegateCreationOperation or IAnonymousObjectCreationOperation =>
                EffectSummaryOperations.Join(
                    ScanChildren(allocation),
                    EffectSummaryOperations.Allocate(EffectAllocationKind.Managed)),
            IThrowOperation thrown when IsSourceThrow(thrown) => EffectSummaryOperations.Join(
                ScanChildren(thrown),
                EffectSummaryOperations.Throw(
                    ResolveThrownException(thrown))),
            IInterpolatedStringOperation interpolation =>
                ScanInterpolatedString(interpolation),
            IThrowOperation => EffectSummary.Empty,
            IBinaryOperation binary => ScanBinary(binary),
            IUnaryOperation unary => ScanUnary(unary),
            IConversionOperation conversion => ScanConversion(conversion),
            IConditionalAccessOperation conditional =>
                ScanConditionalAccess(conditional),
            ILockOperation @lock => ScanLock(@lock),
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

        var instance = field.Instance == null
            ? EffectStep.Empty
            : ScanStep(field.Instance);
        if (!instance.CompletesNormally)
        {
            return instance.Summary;
        }

        var receiverCheck = field.Instance == null
            ? EffectStep.Empty
            : new EffectStep(
                PotentialNullReceiver(field.Instance, field),
                !_nullnessEvaluator.IsProvenNull(field.Instance, field));
        var evaluation = instance.Then(receiverCheck);
        if (!evaluation.CompletesNormally)
        {
            return evaluation.Summary;
        }

        var region = field.Field.IsStatic
            ? EffectRegionSet.Create(EffectRegionId.Static())
            : _conversionOwnership.ClassifyRegion(field.Instance);
        var accessSummary = access == EffectAccess.Write
            ? EffectSummaryOperations.Write(region) : EffectSummaryOperations.Read(region);
        return EffectSummaryOperations.Join(
            evaluation.Summary,
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
        if (PrimaryConstructorParameterOwnership
            .IsPositionalRecordProperty(property.Property))
        {
            var region = _conversionOwnership.ClassifyRegion(
                property.Instance,
                aliasSource: true);
            return EffectSummaryOperations.Join(
                ScanInstance(property.Instance),
                PotentialNullReceiver(property.Instance, property),
                access == EffectAccess.Read
                    ? EffectSummaryOperations.Read(region)
                    : EffectSummaryOperations.Write(region));
        }

        if (access == EffectAccess.Read &&
            IsIntrinsicArrayCardinalityProperty(property))
        {
            return EffectSummaryOperations.Join(
                ScanInstance(property.Instance),
                PotentialNullReceiver(property.Instance, property),
                EffectSummaryOperations.Read(
                    _conversionOwnership.ClassifyRegion(property.Instance, aliasSource: true)));
        }

        var accessor = access == EffectAccess.Read
            ? property.Property.GetMethod
            : property.Property.SetMethod;
        if (accessor == null)
        {
            return access == EffectAccess.Write
                ? EffectSummaryOperations.Unsupported()
                : EffectSummaryDomain.Instance.Join(
                    ScanSequence(
                        (property.Instance == null
                            ? property.Arguments.Select(static argument => argument.Value)
                            : new[] { property.Instance }.Concat(
                                property.Arguments.Select(static argument => argument.Value))))
                        .Summary,
                    EffectSummaryOperations.Unsupported());
        }

        var arguments = ClassifyArguments(property.Arguments, accessor.Parameters.Length);
        var actualArguments = EffectCallSiteResolver.AlignActualArguments(
            property.Arguments,
            accessor.Parameters.Length);
        if (assignedValue != null)
        {
            arguments = arguments.SetItem(
                accessor.Parameters.Length - 1,
                _conversionOwnership.ClassifyRegion(assignedValue));
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
            PropertyDispatchFacts.IsUncertain(property, accessor),
            property);
    }

    private EffectSummary ScanArrayElement(
        IArrayElementReferenceOperation element,
        EffectAccess access,
        IOperation? assignedValue = null)
    {
        var array = ScanStep(element.ArrayReference);
        if (!array.CompletesNormally)
        {
            return array.Summary;
        }

        var indices = ScanSequence(element.Indices);
        var evaluation = array.Then(indices);
        if (!evaluation.CompletesNormally)
        {
            return evaluation.Summary;
        }

        var region = _conversionOwnership.ClassifyRegion(element.ArrayReference);
        var accessSummary = access == EffectAccess.Write
            ? EffectSummaryOperations.Write(region) : EffectSummaryOperations.Read(region);
        var exceptions = EffectSummary.Empty;
        if (_nullnessEvaluator.IsProvenNull(element.ArrayReference, element))
        {
            return EffectSummaryDomain.Instance.Join(
                evaluation.Summary,
                Throw(FrameworkTypeMetadataNames.NullReferenceException));
        }

        if (!_nullnessEvaluator.IsProvenNonNull(element.ArrayReference, element))
        {
            exceptions = EffectSummaryOperations.Join(
                exceptions,
                Throw(FrameworkTypeMetadataNames.NullReferenceException));
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
            evaluation.Summary,
            accessSummary,
            exceptions);
    }

    private EffectSummary ScanFlowCapture(IFlowCaptureOperation capture)
    {
        _coalesceCaptures.Record(capture);
        _creationCaptures.Record(capture);
        return Scan(capture.Value);
    }

    private bool ArrayStoreIsDefinitelyCompatible(
        IArrayElementReferenceOperation element,
        IArrayTypeSymbol arrayType,
        IOperation? assignedValue)
    {
        if (arrayType.ElementType.IsSealed ||
            assignedValue != null &&
            (assignedValue.ConstantValue is { HasValue: true, Value: null } ||
             _abstractFlow?.TryEvaluate(element, assignedValue, out var value) == true &&
             value.IsDefinitelyNull))
        {
            return true;
        }

        var regions = _conversionOwnership.ClassifyRegion(
            element.ArrayReference,
            aliasSource: true);
        if (assignedValue == null ||
            regions.Regions.Length != 1 ||
            regions.Regions[0] is not { Kind: EffectRegionKind.Fresh } fresh ||
            !_freshArrayTypes.TryGetValue(fresh.Ordinal, out var runtimeType) ||
            assignedValue.Type == null)
        {
            return false;
        }

        return _session.Compilation.ClassifyCommonConversion(
            assignedValue.Type,
            runtimeType.ElementType).IsImplicit;
    }

    private EffectSummary IntegralDivisionExceptions(
        BinaryOperatorKind operatorKind, ITypeSymbol? type, IOperation left, IOperation right, IOperation origin)
    {
        if (_conversionEffects.SkipsLiftedOperator(origin) ||
            operatorKind is not (BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder) ||
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
        if (UsingDisposalEffectResolver
            .IsSynthesizedSynchronousDispose(invocation))
        {
            return EffectSummary.Empty;
        }

        if (invocation.IsImplicit &&
            invocation.Syntax.AncestorsAndSelf().Any(static syntax => syntax is LockStatementSyntax))
        {
            return ScanArgumentValues(invocation.Arguments);
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
        return ScanCallStep(
            method,
            instance,
            arguments,
            argumentRegions,
            actualArguments,
            dispatchUncertain,
            origin,
            receiver).Summary;
    }

    private EffectStep ScanCallStep(
        IMethodSymbol method,
        IOperation? instance,
        ImmutableArray<IArgumentOperation> arguments,
        ImmutableArray<EffectRegionSet> argumentRegions,
        ImmutableArray<IOperation?> actualArguments,
        bool dispatchUncertain,
        IOperation origin,
        EffectRegionSet? receiver = null)
    {
        var result = EffectStep.Empty;
        if (instance != null)
        {
            result = result.Then(ScanStep(instance));
            if (!result.CompletesNormally)
            {
                return result;
            }
        }

        if (instance != null && method.ReducedFrom == null)
        {
            var receiverCheck = new EffectStep(
                PotentialNullReceiver(instance, origin),
                !_nullnessEvaluator.IsProvenNull(instance, origin));
            result = result.Then(receiverCheck);
            if (!result.CompletesNormally)
            {
                return result;
            }
        }

        foreach (var argument in arguments)
        {
            result = result.Then(ScanStep(argument.Value));
            if (!result.CompletesNormally)
            {
                return result;
            }
        }

        var call = _callResolver.Resolve(
            method,
            receiver ?? _conversionOwnership.ClassifyRegion(instance),
            argumentRegions,
            actualArguments,
            dispatchUncertain,
            origin,
            instance,
            arguments);
        return result.Then(new EffectStep(
            call,
            _completionEvaluator.CanCompleteInvocation(method, instance, origin)));
    }

    private EffectSummary ScanInstance(IOperation? instance)
    {
        return instance == null ? EffectSummary.Empty : Scan(instance);
    }

    private EffectSummary ScanArgumentValues(
        IEnumerable<IArgumentOperation> arguments)
    {
        return ScanSequence(arguments.Select(static argument => argument.Value))
            .Summary;
    }

    private EffectSummary ScanObjectCreation(IObjectCreationOperation creation)
    {
        var receiver = EffectRegionSet.Create(EffectRegionId.Fresh(creation.Syntax.SpanStart));
        var arguments = ScanSequence(
            creation.Arguments.Select(static argument => argument.Value));
        var allocation = creation.Type?.IsValueType == true
            ? EffectSummary.Empty
            : EffectSummaryOperations.Allocate(EffectAllocationKind.Managed);
        if (!arguments.CompletesNormally)
        {
            return EffectSummaryDomain.Instance.Join(
                arguments.Summary,
                allocation);
        }

        var construction = _callResolver.ResolveConstruction(
            creation,
            receiver,
            ClassifyArguments(
                creation.Arguments,
                creation.Constructor?.Parameters.Length ?? 0));
        var constructor = new EffectStep(
            EffectSummaryDomain.Instance.Join(allocation, construction),
            _completionEvaluator.CanCompleteConstruction(creation));
        var result = arguments.Then(constructor);
        if (creation.Initializer != null && result.CompletesNormally)
        {
            result = result.Then(ScanStep(creation.Initializer));
        }

        return result.Summary;
    }

    private EffectSummary ScanArrayCreation(IArrayCreationOperation array)
    {
        var dimensions = ScanSequence(array.DimensionSizes);
        var allocation = EffectSummaryOperations.Join(
            EffectSummaryOperations.Allocate(EffectAllocationKind.Managed),
            ArrayCreationExceptions(array));
        if (!dimensions.CompletesNormally)
        {
            return dimensions.Summary;
        }

        var result = dimensions.Then(new EffectStep(allocation, true));
        if (array.Initializer != null && result.CompletesNormally)
        {
            result = result.Then(ScanStep(array.Initializer));
        }
        return result.Summary;
    }

    private EffectSummary ScanConditionalAccess(
        IConditionalAccessOperation conditional)
    {
        var receiver = ScanStep(conditional.Operation);
        if (!receiver.CompletesNormally)
        {
            return receiver.Summary;
        }

        if (_nullnessEvaluator.IsProvenNull(conditional.Operation, conditional))
        {
            return receiver.Summary;
        }

        if (conditional.WhenNotNull is not { } whenNotNull)
        {
            return receiver.Summary;
        }

        var whenNotNullStep = ScanStep(whenNotNull);
        if (_nullnessEvaluator.IsProvenNonNull(conditional.Operation, conditional))
        {
            return receiver.Then(whenNotNullStep).Summary;
        }

        // A nullable receiver gives two paths: the null path completes without
        // evaluating WhenNotNull, while the non-null path evaluates it.
        return EffectSummaryDomain.Instance.Join(
            receiver.Summary,
            whenNotNullStep.Summary);
    }

    private EffectSummary ScanLock(ILockOperation @lock)
    {
        var receiver = ScanStep(@lock.LockedValue);
        if (!receiver.CompletesNormally)
        {
            return receiver.Summary;
        }

        var entry = new EffectStep(
            EffectSummaryOperations.Join(
                PotentialNullLock(@lock.LockedValue, @lock),
                EffectSummaryOperations.Capability(
                    EffectCapabilityKind.Synchronization)),
            !_nullnessEvaluator.IsProvenNull(@lock.LockedValue, @lock));
        var result = receiver.Then(entry);
        if (@lock.Body != null && result.CompletesNormally)
        {
            result = result.Then(ScanStep(@lock.Body));
        }
        return result.Summary;
    }

    private EffectSummary ScanBinary(IBinaryOperation binary)
    {
        var operands = ScanStep(binary.LeftOperand);
        if (!operands.CompletesNormally)
        {
            return operands.Summary;
        }

        operands = operands.Then(ScanStep(binary.RightOperand));
        if (!operands.CompletesNormally)
        {
            return operands.Summary;
        }

        var operation = EffectSummaryOperations.Join(
            StringConcatenationEffectResolver.Resolve(
                binary,
                _session.Compilation,
                _callResolver,
                _abstractFlow,
                _conversionOwnership.ClassifyRegion),
            IntegralDivisionExceptions(binary.OperatorKind, binary.Type,
                binary.LeftOperand, binary.RightOperand, binary),
            _conversionEffects.CheckedOverflow(binary.IsChecked, binary),
            ResolveOperatorEffects(
                binary.OperatorMethod,
                [binary.LeftOperand, binary.RightOperand],
                binary));
        return operands.Then(new EffectStep(operation, true)).Summary;
    }

    private EffectSummary ScanInterpolatedString(
        IInterpolatedStringOperation interpolation)
    {
        if (interpolation.ConstantValue.HasValue)
        {
            return EffectSummary.Empty;
        }

        var summary = EffectSummaryOperations.Allocate(
            EffectAllocationKind.Managed);
        foreach (var part in interpolation.Parts)
        {
            if (part is not IInterpolationOperation value)
            {
                continue;
            }
            if (value.Alignment != null || value.FormatString != null)
            {
                summary = EffectSummaryOperations.Join(
                    summary,
                    ScanChildren(value),
                    EffectSummaryOperations.Unsupported());
                continue;
            }

            summary = EffectSummaryOperations.Join(
                summary,
                Scan(value.Expression),
                StringConcatenationEffectResolver.ResolveFormattedValue(
                    value.Expression,
                    value,
                    _session.Compilation,
                    _callResolver,
                    _abstractFlow,
                    _conversionOwnership.ClassifyRegion));
        }
        return summary;
    }

    private EffectSummary ScanUnary(IUnaryOperation unary)
    {
        return EffectSummaryOperations.Join(
            Scan(unary.Operand),
            _conversionEffects.CheckedOverflow(unary.IsChecked, unary),
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
            _conversionEffects.Classify(operation, conversion),
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
            [.. operands.Select(operand => _conversionOwnership.ClassifyRegion(operand))],
            operands,
            origin);
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
        return ScanSequence(operations).Summary;
    }

    internal EffectStep ScanSequence(IEnumerable<IOperation> operations)
    {
        var result = EffectStep.Empty;
        foreach (var operation in operations)
        {
            result = result.Then(ScanStep(operation));
            if (!result.CompletesNormally)
            {
                break;
            }
        }
        return result;
    }

    private EffectStep ScanStep(IOperation operation)
    {
        return new(Scan(operation), _completionEvaluator.CanCompleteNormally(operation));
    }

    private EffectSummary PotentialNullReceiver(IOperation? instance, IOperation access)
    {
        if (_nullnessEvaluator.IsProvenNonNull(instance, access))
        {
            return EffectSummary.Empty;
        }

        return Throw(FrameworkTypeMetadataNames.NullReferenceException);
    }

    private EffectSummary PotentialNullLock(IOperation value, IOperation origin)
    {
        return _nullnessEvaluator.IsProvenNonNull(value, origin)
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
        return EffectExceptionFlow.ResolveThrownException(
            thrown,
            _session,
            _abstractFlow);
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

    internal bool IsReachable(IOperation operation)
    {
        if (ManagedAbstractFlow.IsCompileTimeUnreachable(
                _session.Compilation,
                operation))
        {
            return false;
        }

        var handler = operation.Syntax.AncestorsAndSelf()
            .FirstOrDefault(static syntax =>
                syntax is CatchClauseSyntax or FinallyClauseSyntax);
        if (handler is FinallyClauseSyntax)
        {
            return true;
        }
        if (handler is CatchClauseSyntax @catch)
        {
            return _handlerReachability.IsReachable(
                @catch,
                @catch.Filter?.Span.Contains(operation.Syntax.Span) == true);
        }

        return _abstractFlow == null ||
        !_useAbstractReachability ||
        _abstractFlow.IsReachable(operation);
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

            result[ordinal] = result[ordinal].Union(
                _conversionOwnership.ClassifyRegion(argument.Value));
        }
        return [.. result];
    }

    private static bool IsDispatchUncertain(IInvocationOperation invocation)
    {
        return invocation.IsVirtual && IsOpenDispatchTarget(invocation.TargetMethod);
    }

    private static bool IsOpenDispatchTarget(IMethodSymbol method)
    {
        return method.ContainingType?.IsSealed != true && !method.IsSealed;
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
