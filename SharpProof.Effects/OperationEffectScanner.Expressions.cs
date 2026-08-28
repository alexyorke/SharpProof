using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal sealed partial class OperationEffectScanner
{
    private EffectSummary ScanPropertySubpattern(
        IPropertySubpatternOperation propertySubpattern)
    {
        var member = ScanStep(propertySubpattern.Member);
        return member.CompletesNormally
            ? member.Then(ScanStep(propertySubpattern.Pattern)).Summary
            : member.Summary;
    }

    private EffectSummary ScanRecursivePattern(
        IRecursivePatternOperation pattern)
    {
        var result = ScanChildren(pattern);
        if (pattern.DeconstructSymbol is not IMethodSymbol deconstruct)
        {
            return result;
        }

        var governingValue = SwitchExpressionFacts.GetGoverningValue(pattern);
        if (governingValue == null ||
            _nullnessEvaluator.IsProvenNull(governingValue, pattern))
        {
            return result;
        }

        var arguments = Enumerable.Repeat(
                EffectRegionSet.Empty,
                deconstruct.Parameters.Length)
            .ToImmutableArray();
        var actualArguments = Enumerable.Repeat<IOperation?>(
                null,
                deconstruct.Parameters.Length)
            .ToImmutableArray();
        var call = _callResolver.Resolve(
            deconstruct,
            _conversionOwnership.ClassifyRegion(
                governingValue,
                aliasSource: true),
            _conversionOwnership.ClassifyRegion(
                governingValue,
                aliasSource: true),
            arguments,
            actualArguments,
            deconstruct.IsVirtual || deconstruct.IsAbstract,
            pattern,
            governingValue,
            ImmutableArray<IArgumentOperation>.Empty);
        return EffectSummaryDomain.Instance.Join(result, call);
    }

    private EffectSummary ScanDeconstruction(
        IDeconstructionAssignmentOperation deconstruction)
    {
        var value = ScanStep(deconstruction.Value);
        if (!value.CompletesNormally)
        {
            return value.Summary;
        }

        if (!TryGetDeconstructionInfo(deconstruction, out var info))
        {
            return value.Then(new EffectStep(
                EffectSummaryOperations.Unsupported(),
                _completionEvaluator.CanCompleteNormally(deconstruction))).Summary;
        }

        var reached = value;
        var hasPhase = false;
        foreach (var phase in DeconstructionPhaseWalker.Enumerate(info))
        {
            hasPhase = true;
            var summary = phase.Method is { } method
                ? phase.IsRootMethod
                    ? ScanDeconstructionCall(deconstruction, method)
                    : ScanDeconstructionMethod(deconstruction, method)
                : ScanDeconstructionConversion(
                    deconstruction,
                    phase.Conversion!);
            var completes = _completionEvaluator.CanCompleteDeconstructionPhase(
                phase,
                deconstruction.Value,
                deconstruction);
            reached = reached.Then(new EffectStep(
                EffectSummaryOperations.Join(
                    summary,
                    completes
                        ? EffectSummary.Empty
                        : EffectSummaryOperations.MayDiverge()),
                completes));
            if (!reached.CompletesNormally)
            {
                return reached.Summary;
            }
        }

        if (!hasPhase)
        {
            var completes = _completionEvaluator.CanCompleteNormally(deconstruction);
            return reached.Then(new EffectStep(
                completes
                    ? EffectSummaryOperations.Unsupported()
                    : EffectSummaryOperations.MayDiverge(),
                completes)).Summary;
        }

        foreach (var target in FlattenDeconstructionTargets(deconstruction.Target))
        {
            reached = reached.Then(ScanDeconstructionTarget(target));
            if (!reached.CompletesNormally)
            {
                return reached.Summary;
            }
        }

        return reached.Summary;
    }

    private EffectStep ScanDeconstructionTarget(IOperation target)
    {
        var evaluation = ScanWriteTargetEvaluation(target);
        if (!evaluation.CompletesNormally)
        {
            return evaluation;
        }

        var completes = _completionEvaluator.CanCompleteDeconstructionTarget(target);
        return evaluation.Then(new EffectStep(
            EffectSummaryOperations.Join(
                ScanWriteTarget(
                    target,
                    value: null,
                    valueIsStoredDirectly: false),
                completes
                    ? EffectSummary.Empty
                    : EffectSummaryOperations.MayDiverge()),
            completes));
    }

    private EffectSummary ScanDeconstructionCall(
        IDeconstructionAssignmentOperation deconstruction,
        IMethodSymbol method)
    {
        var hasReceiver = method.ReducedFrom != null || !method.IsStatic;
        var receiver = hasReceiver
            ? _conversionOwnership.ClassifyRegion(
                deconstruction.Value,
                aliasSource: true)
            : EffectRegionSet.Empty;
        var targets = FlattenDeconstructionTargets(deconstruction.Target)
            .Take(method.Parameters.Length)
            .ToImmutableArray();
        var argumentRegions = targets
            .Select(target => _conversionOwnership.ClassifyRegion(
                target,
                aliasSource: true))
            .Concat(Enumerable.Repeat(
                EffectRegionSet.Unknown,
                Math.Max(0, method.Parameters.Length - targets.Length)))
            .ToImmutableArray();
        var actualArguments = targets
            .Cast<IOperation?>()
            .Concat(Enumerable.Repeat<IOperation?>(
                null,
                Math.Max(0, method.Parameters.Length - targets.Length)))
            .ToImmutableArray();
        return _callResolver.Resolve(
            method,
            receiver,
            receiver,
            argumentRegions,
            actualArguments,
            method.IsVirtual || method.IsAbstract,
            deconstruction,
            hasReceiver ? deconstruction.Value : null);
    }

    private EffectSummary ScanDeconstructionMethod(
        IDeconstructionAssignmentOperation deconstruction,
        IMethodSymbol method)
    {
        var hasReceiver = method.ReducedFrom != null || !method.IsStatic;
        var arguments = Enumerable.Repeat(
                EffectRegionSet.Unknown,
                method.Parameters.Length)
            .ToImmutableArray();
        var actualArguments = Enumerable.Repeat<IOperation?>(
                null,
                method.Parameters.Length)
            .ToImmutableArray();
        var receiver = hasReceiver
            ? EffectRegionSet.Unknown
            : EffectRegionSet.Empty;
        return _callResolver.Resolve(
            method,
            receiver,
            receiver,
            arguments,
            actualArguments,
            method.IsVirtual || method.IsAbstract,
            deconstruction,
            instance: null);
    }

    private EffectSummary ScanDeconstructionConversion(
        IDeconstructionAssignmentOperation deconstruction,
        IMethodSymbol conversion)
    {
        return _callResolver.ResolveOperator(
            conversion,
            EffectRegionSet.Empty,
            Enumerable.Repeat(
                    EffectRegionSet.Unknown,
                    conversion.Parameters.Length)
                .ToImmutableArray(),
            Enumerable.Repeat<IOperation?>(
                    null,
                    conversion.Parameters.Length)
                .ToImmutableArray(),
            deconstruction);
    }

    private bool TryGetDeconstructionInfo(
        IDeconstructionAssignmentOperation deconstruction,
        out DeconstructionInfo info)
    {
        info = default;
        if (deconstruction.Syntax is not AssignmentExpressionSyntax syntax)
        {
            return false;
        }

        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(_session.Compilation, syntax.SyntaxTree);
        info = model.GetDeconstructionInfo(syntax);
        return true;
    }

    private static IEnumerable<IOperation> FlattenDeconstructionTargets(
        IOperation target)
    {
        if (target is ITupleOperation tuple)
        {
            return tuple.Elements.SelectMany(FlattenDeconstructionTargets);
        }

        return [target];
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

        // C# evaluates the handler expression before dereferencing the event
        // receiver. Preserve effects from a handler factory even when the
        // receiver is definitely null and the accessor cannot be reached.
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
        var call = _callResolver.Resolve(
            accessor,
            _conversionOwnership.ClassifyRegion(reference.Instance),
            _conversionOwnership.ClassifyRegion(reference.Instance),
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
        var operand = ScanStep(awaitOperation.Operation);
        if (!operand.CompletesNormally)
        {
            return operand.Summary;
        }

        var receiver = awaitOperation.Operation;
        var receiverRegion = _conversionOwnership.ClassifyRegion(
            receiver,
            aliasSource: true);
        var nullCheck = new EffectStep(
            PotentialNullReceiver(receiver, awaitOperation),
            _nullnessEvaluator.IsProvenNonNull(receiver, awaitOperation));
        return operand.Then(nullCheck).Summary;
    }

    internal EffectStep ScanAwaitProtocol(
        IAwaitOperation awaitOperation)
    {
        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(
                _session.Compilation,
                awaitOperation.Syntax.SyntaxTree);
        var info = awaitOperation.Syntax is AwaitExpressionSyntax awaitSyntax
            ? Microsoft.CodeAnalysis.CSharp.CSharpExtensions
                .GetAwaitExpressionInfo(model, awaitSyntax)
            : default;
        if (info.GetAwaiterMethod is not { } getAwaiter)
        {
            return new EffectStep(
                EffectSummaryOperations.Unsupported(),
                false);
        }

        var receiver = awaitOperation.Operation;
        var receiverRegion = _conversionOwnership.ClassifyRegion(
            receiver,
            aliasSource: true);
        var awaiter = ClassifyAwaiterRegion(
            getAwaiter,
            receiverRegion);
        var result = ResolveAwaitProtocolStep(
            getAwaiter,
            getAwaiter.ReducedFrom != null || !getAwaiter.IsStatic
                ? receiver
                : null,
            receiverRegion,
            awaitOperation);
        if (!result.CompletesNormally)
        {
            return result;
        }

        if (info.IsCompletedProperty?.GetMethod is { } isCompleted)
        {
            result = result.Then(ResolveAwaitProtocolStep(
                isCompleted,
                instance: null,
                awaiter,
                awaitOperation));
            if (!result.CompletesNormally)
            {
                return result;
            }
        }
        else
        {
            result = result.WithSummary(
                EffectSummaryOperations.Join(
                    result.Summary,
                    EffectSummaryOperations.Unsupported()));
        }

        if (info.GetResultMethod is not { } getResult)
        {
            return result.WithSummary(
                EffectSummaryOperations.Join(
                    result.Summary,
                    EffectSummaryOperations.Unsupported()));
        }

        return result.Then(ResolveAwaitProtocolStep(
            getResult,
            instance: null,
            awaiter,
            awaitOperation));
    }

    private EffectRegionSet ClassifyAwaiterRegion(
        IMethodSymbol getAwaiter,
        EffectRegionSet receiverRegion)
    {
        if (getAwaiter.ReturnType.IsValueType &&
            !getAwaiter.ReturnType.IsRefLikeType)
        {
            return EffectRegionSet.Empty;
        }

        if (getAwaiter.DeclaringSyntaxReferences.Length != 1)
        {
            return EffectRegionSet.Unknown;
        }

        var declaration = getAwaiter.DeclaringSyntaxReferences[0].GetSyntax();
        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(_session.Compilation, declaration.SyntaxTree);
        var root = model.GetOperation(declaration);
        if (root == null)
        {
            return EffectRegionSet.Unknown;
        }

        var returns = root.DescendantsAndSelf()
            .OfType<IReturnOperation>()
            .Where(returnOperation =>
                !ConversionOwnershipClassifier.IsInsideNestedCallable(
                    returnOperation,
                    root))
            .ToArray();
        if (returns.Length == 0)
        {
            return EffectRegionSet.Unknown;
        }

        var regions = EffectRegionSet.Empty;
        foreach (var returnOperation in returns)
        {
            if (returnOperation.ReturnedValue is not { } value)
            {
                return EffectRegionSet.Unknown;
            }

            value = DefiniteOperationFacts.UnwrapHarmlessValue(value);
            var valueRegion = IsAwaiterReceiverAlias(value, getAwaiter)
                ? receiverRegion
                : _conversionOwnership.ClassifyRegion(value, aliasSource: true);
            regions = regions.Union(valueRegion);
        }

        return regions;
    }

    private static bool IsAwaiterReceiverAlias(
        IOperation value,
        IMethodSymbol getAwaiter)
    {
        if (value is IInstanceReferenceOperation)
        {
            return !getAwaiter.IsStatic;
        }

        if (value is not IParameterReferenceOperation parameter ||
            getAwaiter.ReducedFrom is not { } extension ||
            extension.Parameters.Length == 0)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(
            parameter.Parameter,
            extension.Parameters[0]);
    }

    private EffectStep ResolveAwaitProtocolStep(
        IMethodSymbol method,
        IOperation? instance,
        EffectRegionSet receiver,
        IOperation origin)
    {
        var arguments = Enumerable.Repeat(
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
            arguments,
            actualArguments,
            method.IsVirtual || method.IsAbstract,
            origin,
            instance);
        return new EffectStep(
            call,
            _completionEvaluator.CanCompleteInvocation(
                method,
                instance,
                origin));
    }

    private EffectSummary ScanWith(IWithOperation withOperation)
    {
        EffectStep clone;
        if (withOperation.CloneMethod is { } cloneMethod)
        {
            var callMethod = OperationCompletionEvaluator
                .GetRecordCopyConstructor(cloneMethod) ?? cloneMethod;
            clone = ScanCallStep(
                callMethod,
                withOperation.Operand,
                [],
                [],
                [],
                dispatchUncertain: false,
                withOperation);
        }
        else
        {
            clone = ScanStep(withOperation.Operand);
        }

        clone = new EffectStep(
            clone.Summary,
            clone.CompletesNormally &&
                _completionEvaluator.CanCompleteWithClone(withOperation));
        return withOperation.Initializer != null && clone.CompletesNormally
            ? clone.Then(ScanStep(withOperation.Initializer)).Summary
            : clone.Summary;
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
            () => EffectSummaryOperations.Join(
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
        return operands.Then(new EffectStep(
            operation,
            _completionEvaluator.CanCompleteNormally(binary))).Summary;
    }

    private EffectSummary ScanInterpolatedString(
        IInterpolatedStringOperation interpolation)
    {
        if (interpolation.ConstantValue.HasValue)
        {
            return EffectSummary.Empty;
        }

        var result = EffectStep.Empty;
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

            if (value.Alignment != null || value.FormatString != null)
            {
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
                result = result.Then(new EffectStep(
                    EffectSummaryOperations.Unsupported(),
                    CompletesNormally: true));
                continue;
            }

            var formattedValue =
                StringConcatenationEffectResolver.ResolveFormattedValue(
                    value.Expression,
                    value,
                    _session.Compilation,
                    _callResolver,
                    _abstractFlow,
                    _conversionOwnership.ClassifyRegion);
            result = result.Then(new EffectStep(
                formattedValue,
                StringConcatenationEffectResolver
                    .CanFormattedValueCompleteNormally(
                        value.Expression,
                        value,
                        _session.Compilation,
                        _abstractFlow,
                        _completionEvaluator)));
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

        var operation = EffectSummaryOperations.Join(
            _conversionEffects.CheckedOverflow(unary.IsChecked, unary),
            ResolveOperatorEffects(unary.OperatorMethod, [unary.Operand], unary));
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
        var conversionEffect = EffectSummaryOperations.Join(
            _conversionEffects.Classify(operation, conversion),
            ResolveOperatorEffects(operation.OperatorMethod, [operation.Operand], operation));
        return operand.Then(new EffectStep(
            conversionEffect,
            _completionEvaluator.CanCompleteNormally(operation))).Summary;
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

    private EffectSummary ScanCoreOperationTail(IOperation operation)
    {
        return operation switch
        {
            IDelegateCreationOperation allocation =>
                ScanManagedAllocation(allocation),
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
            IConditionalAccessOperation conditional =>
                ScanConditionalAccess(conditional),
            ISwitchExpressionOperation switchExpression =>
                ScanSwitchExpression(switchExpression),
            IRecursivePatternOperation recursivePattern =>
                ScanRecursivePattern(recursivePattern),
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
