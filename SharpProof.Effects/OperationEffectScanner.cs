using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal sealed partial class OperationEffectScanner
{
    private readonly ManagedFlowResult? _abstractFlow;
    private readonly bool _allowDirectWitnesses;
    private readonly EffectCallSiteResolver _callResolver;
    private readonly ConversionEffectClassifier _conversionEffects;
    private readonly CoalesceAssignmentFlowCaptures _coalesceCaptures = new();
    private readonly ConditionalTruthOperatorFlowCaptures _conditionalTruthCaptures = new();
    private readonly OperationCompletionEvaluator _completionEvaluator;
    private readonly CreationFlowCaptures _creationCaptures = new();
    private readonly SyntaxNode? _directSyntax;
    private readonly ImmutableArray<EffectDirectWitness>.Builder _directWitnesses =
        ImmutableArray.CreateBuilder<EffectDirectWitness>();
    private readonly INamedTypeSymbol? _contractType;
    private readonly INamedTypeSymbol? _exceptionType;
    private readonly ExceptionHandlerReachability _handlerReachability;
    private readonly Dictionary<
        (SyntaxTree Tree, int SpanStart),
        IArrayTypeSymbol> _freshArrayTypes = new();
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
        IOperation root, ManagedFlowResult? abstractFlow, bool allowDirectWitnesses,
        CancellationToken cancellationToken = default)
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
            session.Compilation,
            _coalesceCaptures,
            _conditionalTruthCaptures,
            _creationCaptures);
        _allowDirectWitnesses = allowDirectWitnesses;
        _directSyntax = GetDirectSyntax(root.Syntax);
        _contractType = session.Compilation.GetTypeByMetadataName(
            ContractApiCatalog.Contract);
        _exceptionType = session.Compilation.GetTypeByMetadataName(FrameworkTypeMetadataNames.Exception);
        _monitorType = session.Compilation.GetTypeByMetadataName(FrameworkTypeMetadataNames.Monitor);
        _nullnessEvaluator = new OperationNullnessEvaluator(
            session,
            _root,
            abstractFlow,
            _monitorType);
        _completionEvaluator = new OperationCompletionEvaluator(
            session,
            method,
            _nullnessEvaluator.IsProvenNull,
            _nullnessEvaluator.IsProvenNonNull,
            _nullnessEvaluator.GetNullState,
            _nullnessEvaluator.GetNullStatePreferNull,
            _nullnessEvaluator.IsImplicitLockEnterWithNullValue,
            abstractFlow,
            cancellationToken);
        _handlerReachability = new ExceptionHandlerReachability(
            session.Compilation,
            _method,
            abstractFlow,
            _completionEvaluator.CanCompleteNormally,
            _completionEvaluator.CanMethodCompleteNormally,
            _completionEvaluator.CanCompleteCompoundValue,
            _completionEvaluator.CanCompleteIncrementValue,
            _completionEvaluator.CanCompleteWithClone,
            _conversionEffects,
            _completionEvaluator.GetReachableImplicitListPatternMembers,
            session.ApiSpecs,
            session.KnownSymbols,
            IsKnownNonThrowing);
        var operations = root.DescendantsAndSelf().ToImmutableArray();
        // ManagedAbstractFlow currently follows regular CFG edges. Its facts
        // remain useful in a try body, but absence of a fact cannot prove an
        // operation unreachable after a normally completing handler. The
        // enclosing Roslyn CFG still supplies the outer IsReachable gate.
        _useAbstractReachability = !operations.Any(
            static operation => operation is ITryOperation);
        foreach (var creation in operations
                     .OfType<IArrayCreationOperation>())
        {
            if (creation.Type is IArrayTypeSymbol type)
            {
                _freshArrayTypes[
                    (creation.Syntax.SyntaxTree, creation.Syntax.SpanStart)] =
                    type;
            }
        }
        _conversionOwnership.BuildLocalRegions(root, IsReachable, operations);
    }

    internal static OperationEffectScanner CreateReachabilityProbe(
        Compilation compilation,
        IMethodSymbol method,
        IOperation root,
        ManagedFlowResult? abstractFlow)
    {
        return new OperationEffectScanner(
            new EffectAnalysisSession(compilation),
            method,
            new List<EffectCallSite>(),
            root,
            abstractFlow,
            allowDirectWitnesses: false);
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
                else if (operation is IThrowOperation thrown &&
                         CanReachThrow(thrown))
                {
                    RecordDirect(operation);
                }
            }
            var lexical = operation switch
            {
                ILockOperation @lock
                    when _completionEvaluator.CanCompleteNormally(
                        @lock.LockedValue) => EffectSummaryOperations.Join(
                            PotentialNullLock(@lock.LockedValue, @lock),
                            EffectSummaryOperations.Capability(
                                EffectCapabilityKind.Synchronization)),
                IThrowOperation thrown when IsSourceThrow(thrown) &&
                    CanReachThrow(thrown) => EffectExceptionFlow.KeepEscaping(
                    IsUnmodeledExternalExceptionConstruction(thrown.Exception)
                        ? EffectSummaryOperations.ExceptionConstructionThrow(
                            EffectSummary.Empty,
                            ResolveThrownException(thrown))
                        : EffectSummaryOperations.Throw(
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
            _abstractFlow).Scan(
                root,
                _conversionOwnership.ClassifyRegion,
                _completionEvaluator.CanCompleteNormally,
                _completionEvaluator.CanMethodCompleteNormally,
                _handlerReachability.CanMethodThrow,
                _handlerReachability.CanExitAbruptly);
    }

    private EffectSummary Scan(
        IOperation operation,
        EffectAccess access,
        EffectStep? evaluatedLocation = null)
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
            return ScanCore(operation, access, evaluatedLocation);
        }
        finally
        {
            _nestingDepth--;
        }
    }

    private EffectSummary ScanCore(
        IOperation operation,
        EffectAccess access,
        EffectStep? evaluatedLocation = null)
    {
        if (ManagedFlowResult.HasSameIdentity(operation, _directOperation))
        {
            RecordDirect(operation);
        }

        return EffectExceptionFlow.KeepEscaping(
            ScanCoreOperation(operation, access, evaluatedLocation),
            operation,
            _session.Compilation);
    }

    private EffectSummary ScanCoreOperation(
        IOperation operation,
        EffectAccess access,
        EffectStep? evaluatedLocation = null)
    {
        return operation switch
        {
            IAnonymousFunctionOperation or ILocalFunctionOperation or ILiteralOperation or
                IInstanceReferenceOperation or IDefaultValueOperation or
                ITypeOfOperation or INameOfOperation or ISizeOfOperation => EffectSummary.Empty,
            ILocalReferenceOperation local
                when local.Local.RefKind != RefKind.None =>
                EffectSummaryOperations.Read(
                    _conversionOwnership.ClassifyRefLocalStorage(
                        local.Local)),
            ILocalReferenceOperation => EffectSummary.Empty,
            IFlowCaptureOperation capture => ScanFlowCapture(capture),
            IFlowCaptureReferenceOperation => EffectSummary.Empty,
            IParameterReferenceOperation parameter =>
                parameter.Parameter.RefKind is RefKind.Ref or RefKind.Out ||
                PrimaryConstructorParameterOwnership.IsReceiverBacked(
                    parameter.Parameter,
                    _method)
                    ? EffectSummaryOperations.Read(
                        _conversionOwnership.ClassifyParameter(parameter.Parameter))
                    : EffectSummary.Empty,
            IFieldReferenceOperation field => ScanField(
                field,
                access,
                evaluatedLocation),
            IPropertyReferenceOperation property => ScanProperty(
                property,
                access,
                evaluatedLocation: evaluatedLocation),
            IArrayElementReferenceOperation element => ScanArrayElement(
                element,
                access,
                evaluatedLocation: evaluatedLocation),
            ICoalesceAssignmentOperation assignment =>
                ScanCoalesceAssignment(assignment),
            IDeconstructionAssignmentOperation deconstruction =>
                ScanDeconstruction(deconstruction),
            IEventAssignmentOperation eventAssignment =>
                ScanEventAssignment(eventAssignment),
            IAwaitOperation awaitOperation => ScanAwait(awaitOperation),
            ISimpleAssignmentOperation assignment =>
                ScanSimpleAssignment(assignment),
            ICompoundAssignmentOperation assignment => ScanCompoundAssignment(assignment),
            IIncrementOrDecrementOperation increment =>
                ScanIncrementOrDecrement(increment),
            IMethodReferenceOperation methodReference =>
                ScanMethodReference(methodReference),
            IInvocationOperation invocation => ScanInvocation(invocation),
            IObjectCreationOperation creation => ScanObjectCreation(creation),
            IArrayCreationOperation array => ScanArrayCreation(array),
            _ => ScanCoreOperationTail(operation)
        };
    }

    private EffectSummary ScanField(
        IFieldReferenceOperation field,
        EffectAccess access,
        EffectStep? evaluatedLocation = null)
    {
        if (field.Field.IsConst)
        {
            return EffectSummary.Empty;
        }

        var instance = evaluatedLocation ?? (field.Instance == null
            ? EffectStep.Empty
            : ScanStep(field.Instance));
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
        IPropertyReferenceOperation property,
        EffectAccess access,
        IOperation? assignedValue = null,
        EffectRegionSet? assignedValueRegion = null,
        EffectStep? evaluatedLocation = null)
    {
        if (PrimaryConstructorParameterOwnership
            .IsPositionalRecordProperty(property.Property))
        {
            return ScanIntrinsicProperty(property, access, evaluatedLocation);
        }

        if (access == EffectAccess.Read &&
            IsIntrinsicArrayCardinalityProperty(property))
        {
            return ScanIntrinsicProperty(property, access, evaluatedLocation);
        }

        var accessor = access == EffectAccess.Read
            ? property.Property.GetMethod
            : property.Property.SetMethod;
        if (accessor == null)
        {
            var location = evaluatedLocation?.Summary ??
                ScanSequence(
                    (property.Instance == null
                        ? property.Arguments.Select(static argument => argument.Value)
                        : new[] { property.Instance }.Concat(
                            property.Arguments.Select(static argument => argument.Value))))
                    .Summary;
            return access == EffectAccess.Write
                ? EffectSummaryOperations.Unsupported()
                : EffectSummaryDomain.Instance.Join(
                    location,
                    EffectSummaryOperations.Unsupported());
        }

        var argumentProjection = ProjectArguments(
            property.Arguments,
            accessor.Parameters.Length);
        var arguments = argumentProjection.Regions;
        var actualArguments = argumentProjection.ActualArguments;
        var storedValueRegion = assignedValueRegion;
        if (storedValueRegion == null && assignedValue != null)
        {
            storedValueRegion =
                _conversionOwnership.ClassifyCallArgumentRegion(
                    assignedValue);
        }
        if (storedValueRegion is { } region)
        {
            arguments = arguments.SetItem(
                accessor.Parameters.Length - 1,
                region);
        }
        if (assignedValue != null)
        {
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
            property,
            evaluatedLocation: evaluatedLocation,
            hasParamArray: argumentProjection.HasParamArray);
    }

    private EffectSummary ScanIntrinsicProperty(
        IPropertyReferenceOperation property,
        EffectAccess access,
        EffectStep? evaluatedLocation = null)
    {
        var instance = evaluatedLocation ?? (property.Instance == null
            ? EffectStep.Empty
            : ScanStep(property.Instance));
        if (!instance.CompletesNormally)
        {
            return instance.Summary;
        }

        var receiverCheck = property.Instance == null
            ? EffectStep.Empty
            : new EffectStep(
                PotentialNullReceiver(property.Instance, property),
                !_nullnessEvaluator.IsProvenNull(
                    property.Instance,
                    property));
        var evaluation = instance.Then(receiverCheck);
        if (!evaluation.CompletesNormally)
        {
            return evaluation.Summary;
        }

        var region = _conversionOwnership.ClassifyRegion(
            property.Instance,
            aliasSource: true);
        return EffectSummaryDomain.Instance.Join(
            evaluation.Summary,
            access == EffectAccess.Read
                ? EffectSummaryOperations.Read(region)
                : EffectSummaryOperations.Write(region));
    }

    private EffectSummary ScanMethodReference(
        IMethodReferenceOperation methodReference)
    {
        var instance = methodReference.Instance == null
            ? EffectStep.Empty
            : ScanStep(methodReference.Instance);
        if (!instance.CompletesNormally ||
            methodReference.Method.IsStatic ||
            methodReference.Instance == null ||
            MethodGroupConversionFacts
                .UsesDelegateConstructorNullCheck(methodReference))
        {
            return instance.Summary;
        }
        return EffectSummaryOperations.Join(
            instance.Summary,
            PotentialNullReceiver(
                methodReference.Instance,
                methodReference));
    }

    private EffectSummary ScanArrayElement(
        IArrayElementReferenceOperation element,
        EffectAccess access,
        IOperation? assignedValue = null,
        EffectStep? evaluatedLocation = null)
    {
        var evaluation = evaluatedLocation ??
            ScanStep(element.ArrayReference).Then(
                ScanSequence(element.Indices));
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
        _conditionalTruthCaptures.Record(capture);
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
            !_freshArrayTypes.TryGetValue(
                (element.Syntax.SyntaxTree, fresh.Ordinal),
                out var runtimeType) ||
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
        if (invocation.TargetMethod.Name == "<Clone>$" &&
            invocation.Syntax.ToString().IndexOf("with", StringComparison.Ordinal) >= 0 &&
            OperationCompletionEvaluator.GetRecordCopyConstructor(
                invocation.TargetMethod) is { } copyConstructor &&
            invocation.Instance is { } original)
        {
            return ScanRecordCopyConstruction(
                original,
                copyConstructor,
                invocation,
                _completionEvaluator.CanCompleteNormally(invocation)).Summary;
        }
        if (UsingDisposalEffectResolver
            .IsSynthesizedSynchronousDispose(invocation))
        {
            return EffectSummary.Empty;
        }

        if (IsSynthesizedLockMonitorCall(invocation))
        {
            return ScanArgumentValues(invocation.Arguments);
        }

        if (_session.IsConditionallyElided(invocation))
        {
            return EffectSummary.Empty;
        }

        var argumentProjection = ProjectArguments(
            invocation.Arguments,
            invocation.TargetMethod.Parameters.Length);
        return ScanCall(
            invocation.TargetMethod,
            invocation.Instance,
            invocation.Arguments,
            argumentProjection.Regions,
            argumentProjection.ActualArguments,
            IsDispatchUncertain(invocation),
            invocation,
            hasParamArray: argumentProjection.HasParamArray);
    }

    private EffectSummary ScanCall(
        IMethodSymbol method,
        IOperation? instance,
        ImmutableArray<IArgumentOperation> arguments,
        ImmutableArray<EffectRegionSet> argumentRegions,
        ImmutableArray<IOperation?> actualArguments,
        bool dispatchUncertain,
        IOperation origin,
        EffectRegionSet? receiver = null,
        EffectStep? evaluatedLocation = null,
        bool? hasParamArray = null)
    {
        return ScanCallStep(
            method,
            instance,
            arguments,
            argumentRegions,
            actualArguments,
            dispatchUncertain,
            origin,
            receiver,
            evaluatedLocation,
            hasParamArray).Summary;
    }

    private EffectStep ScanCallStep(
        IMethodSymbol method,
        IOperation? instance,
        ImmutableArray<IArgumentOperation> arguments,
        ImmutableArray<EffectRegionSet> argumentRegions,
        ImmutableArray<IOperation?> actualArguments,
        bool dispatchUncertain,
        IOperation origin,
        EffectRegionSet? receiver = null,
        EffectStep? evaluatedLocation = null,
        bool? hasParamArray = null)
    {
        var result = evaluatedLocation ?? EffectStep.Empty;
        if (evaluatedLocation == null && instance != null)
        {
            result = result.Then(ScanStep(instance));
            if (!result.CompletesNormally)
            {
                return result;
            }
        }

        if (evaluatedLocation == null)
        {
            foreach (var argument in arguments)
            {
                result = result.Then(ScanStep(argument.Value));
                if (!result.CompletesNormally)
                {
                    return result;
                }
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

        var receiverRegion = receiver ??
            (method.ReducedFrom == null
                ? _conversionOwnership.ClassifyRegion(instance)
                : _conversionOwnership.ClassifyCallArgumentRegion(
                    instance));
        var writeReceiver = UsesDefensiveReceiverCopy(method, instance)
            ? EffectRegionSet.Empty
            : receiverRegion;
        var call = _callResolver.Resolve(
            method,
            receiverRegion,
            writeReceiver,
            argumentRegions,
            actualArguments,
            dispatchUncertain,
            origin,
            instance,
            arguments,
            hasParamArray);
        return result.Then(new EffectStep(
            call,
            _completionEvaluator.CanCompleteInvocation(method, instance, origin)));
    }

    private bool UsesDefensiveReceiverCopy(
        IMethodSymbol method,
        IOperation? instance)
    {
        if (instance == null || method.IsStatic || method.IsReadOnly ||
            method.ContainingType?.IsRefLikeType == true ||
            method.ContainingType?.IsValueType != true)
        {
            return false;
        }
        instance = DefiniteOperationFacts.UnwrapHarmlessValue(instance);
        return instance switch
        {
            IInstanceReferenceOperation => _method.IsReadOnly,
            IParameterReferenceOperation parameter =>
                parameter.Parameter.RefKind is RefKind.In or
                    RefKind.RefReadOnlyParameter,
            ILocalReferenceOperation local =>
                local.Local.RefKind is RefKind.RefReadOnly or
                    RefKind.RefReadOnlyParameter,
            IFieldReferenceOperation field => field.Field.IsReadOnly,
            IPropertyReferenceOperation property =>
                property.Property.ReturnsByRefReadonly,
            IInvocationOperation invocation =>
                invocation.TargetMethod.ReturnsByRefReadonly,
            _ => false
        };
    }

    private EffectSummary ScanArgumentValues(
        IEnumerable<IArgumentOperation> arguments)
    {
        // An out argument is a destination, not an input value.  Reading its
        // operation invents a state read before the callee initializes it.
        return ScanSequence(arguments
                .Where(static argument => argument.Parameter?.RefKind != RefKind.Out)
                .Select(static argument => argument.Value))
            .Summary;
    }

    private EffectSummary ScanObjectCreation(IObjectCreationOperation creation)
    {
        if (creation.IsImplicit)
        {
            return EffectSummary.Empty;
        }
        var receiver = EffectRegionSet.Create(EffectRegionId.Fresh(creation.Syntax.SpanStart));
        var arguments = ScanSequence(
            creation.Arguments.Select(static argument => argument.Value));
        var allocation = creation.Type?.IsValueType == true
            ? EffectSummary.Empty
            : EffectSummaryOperations.Allocate(EffectAllocationKind.Managed);
        if (!arguments.CompletesNormally)
        {
            return arguments.Summary;
        }

        var argumentProjection = ProjectArguments(
            creation.Arguments,
            creation.Constructor?.Parameters.Length ?? 0);
        var construction = IsUnmodeledExternalExceptionConstruction(creation) &&
            creation.Syntax.AncestorsAndSelf().Any(static syntax =>
                syntax is ThrowExpressionSyntax or ThrowStatementSyntax)
            ? EffectSummary.Empty
            : _callResolver.ResolveConstruction(
                creation,
                receiver,
                argumentProjection.Regions,
                argumentProjection.ActualArguments,
                argumentProjection.HasParamArray);
        var constructor = new EffectStep(
            EffectSummaryDomain.Instance.Join(allocation, construction),
            _completionEvaluator.CanCompleteConstructorCall(creation));
        var result = arguments.Then(constructor);
        if (creation.Initializer != null && result.CompletesNormally)
        {
            result = result.Then(ScanStep(creation.Initializer));
        }

        return result.Summary;
    }

    private EffectSummary ScanManagedAllocation(IOperation allocation)
    {
        var children = ScanSequence(allocation.ChildOperations);
        return children.CompletesNormally
            ? children.Then(new EffectStep(
                EffectSummaryOperations.Allocate(
                    EffectAllocationKind.Managed),
                true)).Summary
            : children.Summary;
    }

    private EffectSummary ScanDelegateCreation(
        IDelegateCreationOperation delegateCreation)
    {
        var children = ScanSequence(delegateCreation.ChildOperations);
        if (!children.CompletesNormally)
        {
            return children.Summary;
        }

        var allocation = EffectSummaryOperations.Allocate(
            EffectAllocationKind.Managed);
        var methodReference = MethodGroupConversionFacts
            .GetDelegateConstructorCheckedTarget(delegateCreation);
        if (methodReference?.Instance is not { } instance)
        {
            return EffectSummaryOperations.Join(
                children.Summary,
                allocation);
        }

        var instanceNullState = _nullnessEvaluator.GetNullState(
            instance,
            methodReference);
        if (instanceNullState == OperationNullnessEvaluator.NullState.NonNull)
        {
            return EffectSummaryOperations.Join(
                children.Summary,
                allocation);
        }

        return children.Then(new EffectStep(
            EffectSummaryOperations.Join(
                allocation,
                Throw(FrameworkTypeMetadataNames.ArgumentException)),
            instanceNullState != OperationNullnessEvaluator.NullState.Null)).Summary;
    }

    private EffectSummary ScanThrow(IThrowOperation thrown)
    {
        if (thrown.Exception is { } exception &&
            DefiniteOperationFacts.UnwrapHarmlessValue(exception)
                is IObjectCreationOperation creation &&
            IsExternalExceptionConstruction(creation) &&
            !HasNonThrowingConstructorSpec(creation))
        {
            var arguments = ScanSequence(
                creation.Arguments.Select(static argument => argument.Value));
            if (!arguments.CompletesNormally)
            {
                return arguments.Summary;
            }

            var receiver = EffectRegionSet.Create(
                EffectRegionId.Fresh(creation.Syntax.SpanStart));
            var argumentProjection = ProjectArguments(
                creation.Arguments,
                creation.Constructor?.Parameters.Length ?? 0);
            var construction = _callResolver.ResolveConstruction(
                creation,
                receiver,
                argumentProjection.Regions,
                argumentProjection.ActualArguments,
                argumentProjection.HasParamArray);
            var result = arguments.Then(new EffectStep(
                construction,
                _completionEvaluator.CanCompleteConstructorCall(creation)));
            if (creation.Initializer != null && result.CompletesNormally)
            {
                result = result.Then(ScanStep(creation.Initializer));
            }
            return EffectSummaryOperations.ExceptionConstructionThrow(
                result.Summary,
                result.CompletesNormally
                    ? ResolveThrownException(thrown)
                    : EffectThrowSet.Empty);
        }
        var expression = thrown.Exception == null
            ? EffectStep.Empty
            : ScanStep(thrown.Exception);
        return expression.CompletesNormally
            ? expression.Then(new EffectStep(
                EffectSummaryOperations.Throw(
                    ResolveThrownException(thrown)),
                false)).Summary
            : expression.Summary;
    }

    private bool IsUnmodeledExternalExceptionConstruction(IOperation? operation)
    {
        if (operation == null)
        {
            return false;
        }
        operation = DefiniteOperationFacts.UnwrapHarmlessValue(operation);
        return operation is IObjectCreationOperation creation &&
            IsExternalExceptionConstruction(creation) &&
            !HasNonThrowingConstructorSpec(creation);
    }

    private bool IsExternalExceptionConstruction(
        IObjectCreationOperation creation)
    {
        return
            creation.Type is INamedTypeSymbol type &&
            _exceptionType is { } exceptionType &&
            EffectTypeFacts.IsDerivedFrom(type, exceptionType) &&
            creation.Constructor is
            { DeclaringSyntaxReferences.Length: 0 };
    }

    private EffectSummary ScanArrayCreation(IArrayCreationOperation array)
    {
        var dimensions = EffectStep.Empty;
        var hasDimensionOverflow = false;
        foreach (var size in array.DimensionSizes)
        {
            dimensions = dimensions.Then(ScanStep(size));
            hasDimensionOverflow |=
                !IsDefinitelyNonNegative(size) &&
                _abstractFlow?.ProvesNonNegative(array, size) != true;
            if (!dimensions.CompletesNormally)
            {
                return dimensions.Summary;
            }
        }

        var allocation = EffectSummaryOperations.Join(
            EffectSummaryOperations.Allocate(EffectAllocationKind.Managed),
            hasDimensionOverflow
                ? Throw(FrameworkTypeMetadataNames.OverflowException)
                : EffectSummary.Empty);

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

    private EffectSummary ScanSwitchExpression(
        ISwitchExpressionOperation switchExpression)
    {
        var value = ScanStep(switchExpression.Value);
        if (!value.CompletesNormally)
        {
            return value.Summary;
        }

        var arms = EffectSummary.Empty;
        var inputDefinitelyNonNull = _nullnessEvaluator.IsProvenNonNull(
            switchExpression.Value,
            switchExpression);
        foreach (var arm in SwitchExpressionFacts.GetEvaluatedPatternOnlyArms(
                     switchExpression,
                     _completionEvaluator.CanCompleteNormally,
                     inputDefinitelyNonNull,
                     valueAlreadyComplete: true))
        {
            arms = EffectSummaryDomain.Instance.Join(
                arms,
                Scan(arm.Pattern));
        }
        foreach (var arm in SwitchExpressionFacts.GetReachableArms(
                     switchExpression,
                     _completionEvaluator.CanCompleteNormally,
                     inputDefinitelyNonNull,
                     valueAlreadyComplete: true))
        {
            arms = EffectSummaryDomain.Instance.Join(arms, Scan(arm));
        }

        var unmatched = SwitchExpressionFacts.HasReachableUnmatchedPath(
            switchExpression,
            _completionEvaluator.CanCompleteNormally,
            inputDefinitelyNonNull,
            valueAlreadyComplete: true)
                ? Throw(FrameworkTypeMetadataNames.SwitchExpressionException)
                : EffectSummary.Empty;
        return EffectSummaryOperations.Join(value.Summary, arms, unmatched);
    }

    private EffectSummary ScanListPattern(IListPatternOperation pattern)
    {
        var instance = SwitchExpressionFacts.GetGoverningValue(pattern);
        if (_nullnessEvaluator.IsProvenNull(instance, pattern))
        {
            return EffectSummary.Empty;
        }

        var receiver = _conversionOwnership.ClassifyRegion(
            instance,
            aliasSource: true);
        var reachableMembers = _completionEvaluator
            .GetReachableImplicitListPatternMembers(pattern);
        var memberIndex = 0;
        var result = EffectStep.Empty;

        if (SwitchExpressionFacts.GetCallableListPatternMember(
                pattern.LengthSymbol) is { } lengthMember)
        {
            if (!TryScanReachableListPatternMember(
                    pattern,
                    instance,
                    receiver,
                    reachableMembers,
                    ref memberIndex,
                    lengthMember,
                    ref result) ||
                !result.CompletesNormally)
            {
                return result.Summary;
            }
        }

        foreach (var item in pattern.Patterns)
        {
            var nestedPattern = item is ISlicePatternOperation slice
                ? slice.Pattern
                : item;
            var member = SwitchExpressionFacts.GetCallableListPatternMember(
                pattern,
                item);
            if (member != null &&
                (!TryScanReachableListPatternMember(
                        pattern,
                        instance,
                        receiver,
                        reachableMembers,
                        ref memberIndex,
                        member,
                        ref result) ||
                 !result.CompletesNormally))
            {
                return result.Summary;
            }

            if (nestedPattern == null)
            {
                continue;
            }
            result = result.Then(ScanStep(nestedPattern));
            if (!result.CompletesNormally)
            {
                return result.Summary;
            }
        }
        return result.Summary;
    }

    private bool TryScanReachableListPatternMember(
        IListPatternOperation pattern,
        IOperation? instance,
        EffectRegionSet receiver,
        IReadOnlyList<IMethodSymbol> reachableMembers,
        ref int memberIndex,
        IMethodSymbol method,
        ref EffectStep result)
    {
        if (memberIndex >= reachableMembers.Count ||
            !SymbolEqualityComparer.Default.Equals(
                reachableMembers[memberIndex],
                method))
        {
            return false;
        }
        memberIndex++;

        if (SwitchExpressionFacts
            .IsCompilerIntrinsicListPatternMember(
                _session.Compilation,
                pattern,
                method))
        {
            return true;
        }

        result = result.Then(ScanImplicitPatternCall(
            method,
            receiver,
            pattern,
            instance));
        return true;
    }

    private EffectStep ScanImplicitPatternCall(
        IMethodSymbol method,
        EffectRegionSet receiver,
        IOperation pattern,
        IOperation? instance)
    {
        var argumentRegions = Enumerable.Repeat(
                EffectRegionSet.Empty,
                method.Parameters.Length)
            .ToImmutableArray();
        var actualArguments = Enumerable.Repeat<IOperation?>(
                null,
                method.Parameters.Length)
            .ToImmutableArray();
        var call = _callResolver.Resolve(
            method,
            receiver,
            receiver,
            argumentRegions,
            actualArguments,
            method.IsVirtual || method.IsAbstract,
            pattern,
            instance,
            ImmutableArray<IArgumentOperation>.Empty);
        var completesNormally = method.IsAbstract ||
            method.IsVirtual && !method.IsSealed ||
            _completionEvaluator.CanMethodCompleteNormally(method);
        return new EffectStep(call, completesNormally);
    }

    private EffectSummary ScanDefaultPattern(IOperation pattern)
    {
        if (_nullnessEvaluator.IsProvenNull(
                SwitchExpressionFacts.GetGoverningValue(
                    (IPatternOperation)pattern),
                pattern))
        {
            return EffectSummary.Empty;
        }

        return pattern is IRecursivePatternOperation recursivePattern
            ? ScanRecursivePattern(recursivePattern)
            : ScanChildren(pattern);
    }

    private EffectSummary ScanRecursivePattern(
        IRecursivePatternOperation pattern)
    {
        if (pattern.DeconstructSymbol is not IMethodSymbol deconstruct)
        {
            return ScanChildren(pattern);
        }

        var instance = SwitchExpressionFacts.GetGoverningValue(pattern);
        var receiver = _conversionOwnership.ClassifyRegion(
            instance,
            aliasSource: true);
        var result = ScanImplicitPatternCall(
            deconstruct,
            receiver,
            pattern,
            instance);
        return result.CompletesNormally
            ? result.Then(ScanSequence(pattern.ChildOperations)).Summary
            : result.Summary;
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

    internal bool CanCompleteNormally(IOperation operation)
    {
        return _completionEvaluator.CanCompleteNormally(operation);
    }

    private EffectStep ScanStep(IOperation operation)
    {
        return new(Scan(operation), _completionEvaluator.CanCompleteNormally(operation));
    }

    private EffectSummary PotentialNullReceiver(IOperation? instance, IOperation access)
    {
        return PotentialNullAccess(
            instance, access, FrameworkTypeMetadataNames.NullReferenceException);
    }

    private EffectSummary PotentialNullLock(IOperation value, IOperation origin)
    {
        return PotentialNullAccess(
            value, origin, FrameworkTypeMetadataNames.ArgumentNullException);
    }

    private EffectThrowSet ResolveThrownException(IThrowOperation thrown)
    {
        if (thrown.Exception == null)
        {
            return _handlerReachability.ResolveRethrow(thrown);
        }

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
        return MonitorFacts.IsExplicitMonitorCall(invocation, _monitorType);
    }

    private bool IsSynthesizedLockMonitorCall(IInvocationOperation invocation)
    {
        return invocation.IsImplicit &&
            invocation.Instance == null &&
            invocation.TargetMethod.Name is "Enter" or "Exit" &&
            MonitorFacts.IsMonitorMethod(invocation.TargetMethod, _monitorType) &&
            invocation.Syntax.AncestorsAndSelf().Any(
                static syntax => syntax is LockStatementSyntax);
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
            HasNonThrowingMethodSpec(creation.Constructor);
    }

    private bool HasNonThrowingMethodSpec(IMethodSymbol method)
    {
        return _session.ApiSpecs.IsNonThrowingAndTerminating(method);
    }

    private bool IsKnownNonThrowing(IMethodSymbol method)
    {
        return _contractType != null &&
            SymbolEqualityComparer.Default.Equals(
                method.ContainingType.OriginalDefinition,
                _contractType.OriginalDefinition) &&
            ContractApiCatalog.ContractMethodCandidateNames.Contains(
                method.Name,
                StringComparer.Ordinal) ||
            HasNonThrowingMethodSpec(method);
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
        var body = ExecutableBodySyntax.Get(declaration);
        return body is BlockSyntax block ? SingleStatement(block) : body;
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

    private bool CanReachThrow(IThrowOperation thrown)
    {
        return thrown.Exception == null ||
            _completionEvaluator.CanCompleteNormally(thrown.Exception);
    }

    private readonly record struct ArgumentProjection(
        ImmutableArray<EffectRegionSet> Regions,
        ImmutableArray<IOperation?> ActualArguments,
        bool HasParamArray);

    private ArgumentProjection ProjectArguments(
        ImmutableArray<IArgumentOperation> arguments,
        int parameterCount)
    {
        var regions = new EffectRegionSet[parameterCount];
        var actualArguments = ImmutableArray.CreateBuilder<IOperation?>(
            parameterCount);
        actualArguments.Count = parameterCount;
        var hasInvalidOrdinal = false;
        var hasParamArray = false;
        foreach (var argument in arguments)
        {
            hasParamArray |= argument.ArgumentKind == ArgumentKind.ParamArray;
            var ordinal = argument.Parameter?.Ordinal ?? -1;
            if (ordinal < 0 || ordinal >= regions.Length)
            {
                hasInvalidOrdinal = true;
                continue;
            }

            if (!hasInvalidOrdinal)
            {
                regions[ordinal] = regions[ordinal].Union(
                    _conversionOwnership.ClassifyCallArgumentRegion(
                        argument.Value));
            }
            if (argument.ArgumentKind != ArgumentKind.ParamArray)
            {
                actualArguments[ordinal] = argument.Value;
            }
        }
        return new(
            hasInvalidOrdinal
                ? [.. Enumerable.Repeat(EffectRegionSet.Unknown, parameterCount)]
                : [.. regions],
            actualArguments.MoveToImmutable(),
            hasParamArray);
    }

    internal static bool IsDispatchUncertain(IInvocationOperation invocation)
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
        var specialType = (CompilerIdentityBridge.GetNullableUnderlyingType(type) ??
            type)?.SpecialType ?? SpecialType.None;
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
