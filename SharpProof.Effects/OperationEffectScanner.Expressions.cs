using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal sealed partial class OperationEffectScanner
{
    private EffectSummary PotentialNullAccess(
        IOperation? value,
        IOperation origin,
        string exceptionType)
    {
        return _nullnessEvaluator.IsProvenNonNull(value, origin)
            ? EffectSummary.Empty
            : Throw(exceptionType);
    }

    private EffectSummary ScanPropertySubpattern(
        IPropertySubpatternOperation propertySubpattern)
    {
        var member = ScanStep(propertySubpattern.Member);
        return member.CompletesNormally
            ? member.Then(ScanStep(propertySubpattern.Pattern)).Summary
            : member.Summary;
    }

    private EffectSummary ScanDeconstruction(
        IDeconstructionAssignmentOperation deconstruction)
    {
        // C# evaluates every target location before any right-hand element,
        // then performs the target writes from left to right.
        var result = ScanDeconstructionTargetEvaluations(
            deconstruction.Target);
        if (!result.CompletesNormally)
        {
            return result.Summary;
        }

        result = result.Then(ScanStep(deconstruction.Value));
        if (!result.CompletesNormally)
        {
            return result.Summary;
        }

        var phasesComplete = _completionEvaluator
            .CanCompleteDeconstructionPhases(deconstruction);
        // Identity tuple deconstruction has no intervening call or conversion.
        // Other phases retain the existing conservative boundary.
        var phases = IsDirectTupleDeconstruction(
                deconstruction.Target,
                deconstruction.Value)
            ? EffectSummary.Empty
            : phasesComplete
                ? EffectSummaryOperations.Unsupported()
                : EffectSummaryOperations.MayDiverge();
        result = result.Then(new EffectStep(phases, phasesComplete));
        return !result.CompletesNormally
            ? result.Summary
            : result.Then(ScanDeconstructionTargetWrites(
                deconstruction.Target,
                deconstruction.Value)).Summary;
    }

    private EffectStep ScanDeconstructionTargetEvaluations(
        IOperation target)
    {
        return ScanDeconstructionTarget(
            target,
            value: null,
            ScanDeconstructionTargetEvaluation);
    }

    private EffectStep ScanDeconstructionTargetWrites(
        IOperation target,
        IOperation value)
    {
        return ScanDeconstructionTarget(
            target,
            value,
            ScanDeconstructionTargetWrite);
    }

    private static EffectStep ScanDeconstructionTarget(
        IOperation target,
        IOperation? value,
        Func<IOperation, IOperation?, EffectStep> leaf)
    {
        if (target is IDeclarationExpressionOperation declaration)
        {
            return ScanDeconstructionTarget(
                declaration.Expression,
                value,
                leaf);
        }
        if (target is not ITupleOperation targetTuple)
        {
            return leaf(target, value);
        }

        var valueTuple = value as ITupleOperation;
        var hasMatchingValueTuple = valueTuple is not null &&
            valueTuple.Elements.Length == targetTuple.Elements.Length;
        var result = EffectStep.Empty;
        for (var index = 0; index < targetTuple.Elements.Length; index++)
        {
            var elementValue = hasMatchingValueTuple
                ? valueTuple!.Elements[index]
                : value;
            result = result.Then(ScanDeconstructionTarget(
                targetTuple.Elements[index],
                elementValue,
                leaf));
            if (!result.CompletesNormally)
            {
                break;
            }
        }
        return result;
    }

    private EffectStep ScanDeconstructionTargetEvaluation(
        IOperation target,
        IOperation? value)
    {
        _ = value;
        return ScanWriteTargetEvaluation(target);
    }

    private EffectStep ScanDeconstructionTargetWrite(
        IOperation target,
        IOperation? value)
    {
        var assignedValue = value!;
        return new EffectStep(
            ScanWriteTarget(
                target,
                assignedValue,
                valueIsStoredDirectly:
                    SymbolEqualityComparer.Default.Equals(
                        target.Type,
                        assignedValue.Type)),
            _completionEvaluator.CanCompleteWriteTarget(target));
    }

    private static bool IsDirectTupleDeconstruction(
        IOperation target,
        IOperation value)
    {
        if (target is IDeclarationExpressionOperation declaration)
        {
            return IsDirectTupleDeconstruction(
                declaration.Expression,
                value);
        }
        if (target is not ITupleOperation targetTuple)
        {
            return SymbolEqualityComparer.Default.Equals(
                target.Type,
                value.Type);
        }
        if (value is not ITupleOperation valueTuple ||
            valueTuple.Elements.Length != targetTuple.Elements.Length)
        {
            return false;
        }

        return targetTuple.Elements.Zip(
            valueTuple.Elements,
            IsDirectTupleDeconstruction).All(static direct => direct);
    }

    private EffectSummary ScanEventAssignment(
        IEventAssignmentOperation eventAssignment)
    {
        if (eventAssignment.EventReference is not IEventReferenceOperation reference)
        {
            return EffectSummaryOperations.Unsupported();
        }

        var result = reference.Instance == null
            ? EffectStep.Empty
            : ScanStep(reference.Instance);
        if (!result.CompletesNormally)
        {
            return result.Summary;
        }

        result = result.Then(ScanStep(eventAssignment.HandlerValue));
        if (!result.CompletesNormally)
        {
            return result.Summary;
        }

        var receiverCheck = new EffectStep(
            PotentialNullReceiver(reference.Instance, eventAssignment),
            !_nullnessEvaluator.IsProvenNull(
                reference.Instance,
                eventAssignment));
        result = result.Then(receiverCheck);
        if (!result.CompletesNormally)
        {
            return result.Summary;
        }

        var accessor = eventAssignment.Adds
            ? reference.Event.AddMethod
            : reference.Event.RemoveMethod;
        if (accessor == null)
        {
            return EffectSummaryOperations.Join(
                result.Summary,
                EffectSummaryOperations.Unsupported());
        }

        var handlerRegions = _conversionOwnership.ClassifyRegion(
            eventAssignment.HandlerValue);
        var receiverRegions = _conversionOwnership.ClassifyRegion(
            reference.Instance);
        var call = _callResolver.Resolve(
            accessor,
            receiverRegions,
            receiverRegions,
            [handlerRegions],
            [eventAssignment.HandlerValue],
            accessor.IsVirtual || accessor.IsAbstract,
            eventAssignment,
            reference.Instance);
        return result.Then(new EffectStep(
            call,
            _completionEvaluator.CanCompleteNormally(eventAssignment))).Summary;
    }

    private EffectSummary ScanAwait(IAwaitOperation awaitOperation)
    {
        if (awaitOperation.Syntax is not AwaitExpressionSyntax awaitSyntax)
        {
            return EffectSummaryOperations.Join(
                ScanStep(awaitOperation.Operation).Summary,
                EffectSummaryOperations.Unsupported());
        }

        var operand = ScanStep(awaitOperation.Operation);
        if (!operand.CompletesNormally)
        {
            return operand.Summary;
        }

        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(_session.Compilation, awaitSyntax.SyntaxTree);
        var info = Microsoft.CodeAnalysis.CSharp.CSharpExtensions
            .GetAwaitExpressionInfo(
            model,
            awaitSyntax);
        if (info.GetAwaiterMethod is not { } getAwaiter)
        {
            return EffectSummaryDomain.Instance.Join(
                operand.Summary,
                EffectSummaryOperations.Unsupported());
        }

        var awaitableReceiver = getAwaiter.ReducedFrom == null
            ? _conversionOwnership.ClassifyRegion(
                awaitOperation.Operation)
            : _conversionOwnership.ClassifyCallArgumentRegion(
                awaitOperation.Operation);
        var awaitableCheck = getAwaiter.IsStatic ||
            getAwaiter.ReducedFrom != null
                ? EffectStep.Empty
                : new EffectStep(
                    PotentialNullReceiver(
                        awaitOperation.Operation,
                        awaitOperation),
                    !_nullnessEvaluator.IsProvenNull(
                        awaitOperation.Operation,
                        awaitOperation));
        var getAwaiterSummary = _callResolver.Resolve(
            getAwaiter,
            awaitableReceiver,
            awaitableReceiver,
            [],
            [],
            dispatchUncertain: getAwaiter.IsVirtual || getAwaiter.IsAbstract,
            awaitOperation,
            awaitOperation.Operation);
        var getAwaiterStep = awaitableCheck.Then(new EffectStep(
            getAwaiterSummary,
            _completionEvaluator.CanCompleteInvocation(
                getAwaiter,
                awaitOperation.Operation,
                awaitOperation)));
        var result = operand.Then(getAwaiterStep);
        if (!result.CompletesNormally)
        {
            return result.Summary;
        }

        if (getAwaiter.ReturnType.IsReferenceType)
        {
            var nullability = _handlerReachability
                .GetReturnNullability(getAwaiter);
            result = result.Then(new EffectStep(
                nullability == ExceptionHandlerReachability
                    .ReturnNullability.NonNull
                    ? EffectSummary.Empty
                    : Throw(
                        FrameworkTypeMetadataNames.NullReferenceException),
                nullability != ExceptionHandlerReachability
                    .ReturnNullability.Null));
            if (!result.CompletesNormally)
            {
                return result.Summary;
            }
        }

        var awaiter = EffectRegionSet.Create(
            EffectRegionId.Fresh(awaitOperation.Syntax.SpanStart));
        if (info.IsCompletedProperty?.GetMethod is not { } isCompleted ||
            info.GetResultMethod is not { } getResult)
        {
            return result.WithSummary(
                EffectSummaryDomain.Instance.Join(
                    result.Summary,
                    EffectSummaryOperations.Unsupported())).Summary;
        }

        var isCompletedStep = ScanAwaitProtocolCall(
            isCompleted,
            awaiter,
            awaitOperation);
        result = result.Then(isCompletedStep);
        if (!result.CompletesNormally)
        {
            return result.Summary;
        }

        // The continuation registration is conditional on IsCompleted.  Its
        // effects are therefore possible, rather than an unconditional step.
        // Join its summary without making a possibly skipped throw block the
        // path that resumes directly to GetResult.
        var continuation = _session.KnownSymbols.FindAwaitContinuationMethod(
            getAwaiter.ReturnType);
        if (continuation == null)
        {
            return result.WithSummary(
                EffectSummaryDomain.Instance.Join(
                    result.Summary,
                    EffectSummaryOperations.Unsupported())).Summary;
        }

        var continuationStep = ScanAwaitProtocolCall(
            continuation,
            awaiter,
            awaitOperation);
        result = result.WithSummary(
            EffectSummaryDomain.Instance.Join(
                result.Summary,
                continuationStep.Summary));

        return result.Then(ScanAwaitProtocolCall(
            getResult,
            awaiter,
            awaitOperation)).Summary;
    }

    private EffectStep ScanAwaitProtocolCall(
        IMethodSymbol method,
        EffectRegionSet receiver,
        IOperation origin)
    {
        var effectiveReceiver = method.IsStatic
            ? EffectRegionSet.Empty
            : receiver;
        var summary = _callResolver.Resolve(
            method,
            effectiveReceiver,
            effectiveReceiver,
            [],
            [],
            dispatchUncertain: method.IsVirtual || method.IsAbstract,
            origin,
            instance: null);
        return new EffectStep(
            summary,
            _completionEvaluator.CanMethodCompleteNormally(method));
    }

    private EffectSummary ScanWith(IWithOperation withOperation)
    {
        EffectStep clone;
        bool? copyConstructionCompletesNormally = null;
        if (withOperation.CloneMethod is { } cloneMethod)
        {
            var copyConstructor = OperationCompletionEvaluator
                .GetRecordCopyConstructor(cloneMethod);
            if (copyConstructor == null)
            {
                clone = ScanCallStep(
                    cloneMethod,
                    withOperation.Operand,
                    [],
                    [],
                    [],
                    dispatchUncertain: false,
                    withOperation);
            }
            else
            {
                copyConstructionCompletesNormally =
                    _completionEvaluator.CanCompleteWithClone(withOperation);
                clone = ScanRecordCopyConstruction(
                    withOperation.Operand,
                    copyConstructor,
                    withOperation,
                    copyConstructionCompletesNormally.Value);
            }
        }
        else
        {
            clone = ScanStep(withOperation.Operand);
        }

        clone = new EffectStep(
            clone.Summary,
            clone.CompletesNormally &&
                (copyConstructionCompletesNormally ??
                 _completionEvaluator.CanCompleteWithClone(withOperation)));
        return withOperation.Initializer != null && clone.CompletesNormally
            ? clone.Then(ScanStep(withOperation.Initializer)).Summary
            : clone.Summary;
    }

    private EffectStep ScanRecordCopyConstruction(
        IOperation original,
        IMethodSymbol copyConstructor,
        IOperation origin,
        bool completesNormally)
    {
        var result = ScanStep(original);
        if (!result.CompletesNormally)
        {
            return result;
        }

        result = result.Then(new EffectStep(
            PotentialNullReceiver(
                original,
                origin),
            !_nullnessEvaluator.IsProvenNull(
                original,
                origin)));
        if (!result.CompletesNormally)
        {
            return result;
        }

        var receiver = EffectRegionSet.Create(
            EffectRegionId.Fresh(origin.Syntax.SpanStart));
        var originalRegion = _conversionOwnership.ClassifyRegion(
            original);
        var construction = _callResolver.Resolve(
            copyConstructor,
            receiver,
            receiver,
            [originalRegion],
            [original],
            dispatchUncertain: false,
            origin,
            instance: null);
        return result.Then(new EffectStep(
            EffectSummaryOperations.Join(
                EffectSummaryOperations.Allocate(
                    EffectAllocationKind.Managed),
                construction),
            completesNormally));
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

    private EffectSummary ScanIncrementOrDecrement(
        IIncrementOrDecrementOperation increment)
    {
        return ScanReadModifyWrite(
            increment.Target,
            () => EffectStep.Empty,
            () => _conversionEffects.SkipsLiftedOperator(increment)
                ? EffectSummary.Empty
                : EffectSummaryOperations.Join(
                    _conversionEffects.CheckedOverflow(
                        increment.IsChecked,
                        increment),
                    ResolveOperatorEffects(
                        increment.OperatorMethod,
                        [increment.Target],
                        increment)),
            () => _completionEvaluator.CanCompleteIncrementValue(increment),
            increment.Target);
    }

    private EffectSummary ScanBinary(IBinaryOperation binary)
    {
        var left = ScanStep(binary.LeftOperand);
        if (!left.CompletesNormally)
        {
            return left.Summary;
        }

        var isConditional = binary.OperatorKind is
            BinaryOperatorKind.ConditionalAnd or
            BinaryOperatorKind.ConditionalOr;
        if (isConditional && binary.OperatorMethod != null)
        {
            var truthOperator = ConditionalTruthOperatorFacts.Resolve(binary);
            if (truthOperator == null)
            {
                return EffectSummaryOperations.Join(
                    left.Summary,
                    EffectSummaryOperations.Unsupported());
            }

            left = left.Then(new EffectStep(
                ResolveOperatorEffects(
                    truthOperator,
                    [binary.LeftOperand],
                    binary),
                _completionEvaluator.CanMethodCompleteNormally(
                    truthOperator)));
            if (!left.CompletesNormally)
            {
                return left.Summary;
            }
            if (ConditionalTruthOperatorFacts.ReturnsConstant(
                    _session.Compilation,
                    truthOperator,
                    out var truthResult) &&
                truthResult)
            {
                return left.Summary;
            }
        }
        if (isConditional &&
            TryGetBoolean(binary, binary.LeftOperand, out var leftValue))
        {
            var shortCircuits = ConditionalTruthOperatorFacts
                .SkipsRightOperand(binary.OperatorKind, leftValue);
            if (shortCircuits)
            {
                return left.Summary;
            }
        }

        var right = ScanStep(binary.RightOperand);
        var result = EffectSummaryOperations.Join(
            left.Summary,
            right.Summary);
        if (!right.CompletesNormally)
        {
            return result;
        }

        var operatorEffect = _conversionEffects.SkipsLiftedOperator(binary)
            ? EffectSummary.Empty
            : ResolveOperatorEffects(
                binary.OperatorMethod,
                [binary.LeftOperand, binary.RightOperand],
                binary);
        return EffectSummaryOperations.Join(
            result,
            StringConcatenationEffectResolver.Resolve(
                binary,
                _session.Compilation,
                _callResolver,
                _abstractFlow,
                _conversionOwnership.ClassifyRegion),
            BuiltInDelegateCombinationAllocation(binary),
            IntegralDivisionExceptions(binary.OperatorKind, binary.Type,
                binary.LeftOperand, binary.RightOperand, binary),
            _conversionEffects.CheckedOverflow(binary.IsChecked, binary),
            operatorEffect);
    }

    private static EffectSummary BuiltInDelegateCombinationAllocation(
        IBinaryOperation binary)
    {
        var isCombination = binary.OperatorKind is
            BinaryOperatorKind.Add or BinaryOperatorKind.Subtract;
        var isBuiltIn = binary.OperatorMethod == null ||
            binary.OperatorMethod.MethodKind == MethodKind.BuiltinOperator;
        return binary.Type?.TypeKind == TypeKind.Delegate &&
            isCombination &&
            isBuiltIn
            ? EffectSummaryOperations.Allocate(
                EffectAllocationKind.Managed)
            : EffectSummary.Empty;
    }

    private EffectSummary ScanConditional(IConditionalOperation conditional)
    {
        var condition = ScanStep(conditional.Condition);
        if (!condition.CompletesNormally)
        {
            return condition.Summary;
        }

        if (conditional.WhenFalse is not { } whenFalse)
        {
            return EffectSummaryOperations.Join(
                condition.Summary,
                Scan(conditional.WhenTrue),
                EffectSummaryOperations.Unsupported());
        }

        if (TryGetBoolean(
                conditional,
                conditional.Condition,
                out var conditionValue))
        {
            return EffectSummaryOperations.Join(
                condition.Summary,
                Scan(conditionValue
                    ? conditional.WhenTrue
                    : whenFalse));
        }

        return EffectSummaryOperations.Join(
            condition.Summary,
            Scan(conditional.WhenTrue),
            Scan(whenFalse));
    }

    private EffectSummary ScanCoalesce(ICoalesceOperation coalesce)
    {
        var value = ScanStep(coalesce.Value);
        if (!value.CompletesNormally ||
            _nullnessEvaluator.IsProvenNonNull(
                coalesce.Value,
                coalesce))
        {
            return value.Summary;
        }

        return EffectSummaryOperations.Join(
            value.Summary,
            Scan(coalesce.WhenNull));
    }

    private bool TryGetBoolean(
        IOperation origin,
        IOperation value,
        out bool result)
    {
        if (value.ConstantValue is { HasValue: true, Value: bool constant })
        {
            result = constant;
            return true;
        }

        if (_abstractFlow?.TryEvaluate(origin, value, out var abstractValue) ==
            true && abstractValue.TryGetBoolean(out result))
        {
            return true;
        }

        result = false;
        return false;
    }

    private EffectSummary ScanInterpolatedString(
        IInterpolatedStringOperation interpolation)
    {
        if (interpolation.ConstantValue.HasValue)
        {
            return EffectSummary.Empty;
        }

        var result = EffectStep.Empty;
        var defersFormatting = StringConcatenationEffectResolver
            .DefersInterpolationFormatting(
                interpolation,
                _session.Compilation);
        foreach (var part in interpolation.Parts)
        {
            if (part is not IInterpolationOperation value)
            {
                continue;
            }

            result = result.Then(ScanStep(value.Expression));
            if (!result.CompletesNormally)
            {
                return result.Summary;
            }

            if (value.Alignment != null)
            {
                result = result.Then(ScanStep(value.Alignment));
            }
            if (result.CompletesNormally && value.FormatString != null)
            {
                result = result.Then(ScanStep(value.FormatString));
            }
            if (!result.CompletesNormally)
            {
                return result.Summary;
            }
            if (defersFormatting)
            {
                continue;
            }
            if (value.Alignment != null || value.FormatString != null)
            {
                result = result.Then(new EffectStep(
                    EffectSummaryOperations.Unsupported(),
                    CompletesNormally: true));
                continue;
            }

            var formattedValue =
                StringConcatenationEffectResolver.ResolveFormattedValueEffects(
                    value.Expression,
                    value,
                    _session.Compilation,
                    _callResolver,
                    _abstractFlow,
                    _conversionOwnership.ClassifyRegion,
                    _completionEvaluator);
            result = result.Then(new EffectStep(
                formattedValue.Summary,
                formattedValue.CompletesNormally));
            if (!result.CompletesNormally)
            {
                return result.Summary;
            }
        }
        return result.Then(new EffectStep(
            EffectSummaryOperations.Allocate(EffectAllocationKind.Managed),
            CompletesNormally: true)).Summary;
    }

    private EffectSummary ScanUnary(IUnaryOperation unary)
    {
        var operand = ScanStep(unary.Operand);
        if (!operand.CompletesNormally)
        {
            return operand.Summary;
        }

        var operation = _conversionEffects.SkipsLiftedOperator(unary)
            ? EffectSummary.Empty
            : EffectSummaryOperations.Join(
                _conversionEffects.CheckedOverflow(unary.IsChecked, unary),
                ResolveOperatorEffects(
                    unary.OperatorMethod,
                    [unary.Operand],
                    unary));
        return operand.Then(new EffectStep(
            operation,
            _completionEvaluator.CanCompleteNormally(unary))).Summary;
    }

    private EffectSummary ScanConversion(IConversionOperation operation)
    {
        if (!string.Equals(operation.Syntax.Language, LanguageNames.CSharp, StringComparison.Ordinal))
        {
            return EffectSummaryOperations.Join(
                Scan(operation.Operand),
                EffectSummaryOperations.Unsupported());
        }

        var operand = ScanStep(operation.Operand);
        if (!operand.CompletesNormally)
        {
            return operand.Summary;
        }

        var conversion = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetConversion(operation);
        var operatorEffect = _conversionEffects
            .SkipsLiftedOperator(operation)
                ? EffectSummary.Empty
                : ResolveOperatorEffects(
                    operation.OperatorMethod,
                    [operation.Operand],
                    operation);
        var conversionEffect = EffectSummaryOperations.Join(
            _conversionEffects.Classify(operation, conversion),
            operatorEffect);
        return operand.Then(new EffectStep(
            conversionEffect,
            _completionEvaluator.CanCompleteNormally(operation))).Summary;
    }

    private EffectSummary ResolveOperatorEffects(
        IMethodSymbol? method,
        ImmutableArray<IOperation?> operands,
        IOperation origin)
    {
        return ResolveOperatorEffects(
            method,
            [.. operands.Select(
                operand => _conversionOwnership
                    .ClassifyCallArgumentRegion(operand))],
            operands,
            origin);
    }

    private EffectSummary ResolveOperatorEffects(
        IMethodSymbol? method,
        ImmutableArray<EffectRegionSet> operandRegions,
        ImmutableArray<IOperation?> operands,
        IOperation origin)
    {
        return _callResolver.ResolveOperator(
            method,
            EffectRegionSet.Empty,
            operandRegions,
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

    private EffectSummary ScanCoreOperationTail(IOperation operation)
    {
        return operation switch
        {
            IDelegateCreationOperation allocation =>
                ScanDelegateCreation(allocation),
            IAnonymousObjectCreationOperation allocation =>
                ScanManagedAllocation(allocation),
            IThrowOperation thrown when IsSourceThrow(thrown) =>
                ScanThrow(thrown),
            IInterpolatedStringOperation interpolation =>
                ScanInterpolatedString(interpolation),
            IThrowOperation => EffectSummary.Empty,
            IBinaryOperation binary => ScanBinary(binary),
            IUnaryOperation unary => ScanUnary(unary),
            IConversionOperation conversion => ScanConversion(conversion),
            IConditionalOperation conditional => ScanConditional(conditional),
            ICoalesceOperation coalesce => ScanCoalesce(coalesce),
            IConditionalAccessOperation conditional =>
                ScanConditionalAccess(conditional),
            ISwitchExpressionOperation switchExpression =>
                ScanSwitchExpression(switchExpression),
            IListPatternOperation listPattern =>
                ScanListPattern(listPattern),
            IIsPatternOperation isPattern => ScanChildren(isPattern),
            ITupleOperation tuple => ScanChildren(tuple),
            IPropertySubpatternOperation propertySubpattern =>
                ScanPropertySubpattern(propertySubpattern),
            IPatternOperation => ScanDefaultPattern(operation),
            IWithOperation withOperation => ScanWith(withOperation),
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
    }
}
