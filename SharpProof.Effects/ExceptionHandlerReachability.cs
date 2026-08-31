using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.Effects;

internal sealed class ExceptionHandlerReachability(
    Compilation compilation,
    IMethodSymbol caller,
    ManagedFlowResult? abstractFlow,
    Func<IOperation?, bool> canCompleteNormally,
    Func<IMethodSymbol, bool> canMethodCompleteNormally,
    Func<ICompoundAssignmentOperation, bool> canCompoundValueComplete,
    Func<IIncrementOrDecrementOperation, bool> canIncrementValueComplete,
    Func<IWithOperation, bool> canWithCloneComplete,
    Func<IListPatternOperation, IReadOnlyList<IMethodSymbol>>
        getReachableListPatternMembers,
    ResolvedApiSpecTable apiSpecs,
    EffectKnownSymbols knownSymbols,
    Func<IMethodSymbol, bool> isKnownNonThrowing)
{
    private readonly Dictionary<CatchClauseSyntax, CatchReachability> _cache = new();
    private readonly INamedTypeSymbol? _exceptionType =
        compilation.GetTypeByMetadataName(FrameworkTypeMetadataNames.Exception);
    private readonly INamedTypeSymbol? _nullReferenceExceptionType =
        compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.NullReferenceException);
    private readonly INamedTypeSymbol? _argumentNullExceptionType =
        compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.ArgumentNullException);
    private readonly INamedTypeSymbol? _typeInitializationExceptionType =
        compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.TypeInitializationException);
    private readonly INamedTypeSymbol? _switchExpressionExceptionType =
        compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.SwitchExpressionException);
    private readonly DefiniteOperationFacts _staticInitializationFacts =
        new(compilation, CancellationToken.None);

    internal bool IsReachable(CatchClauseSyntax target, bool inFilter)
    {
        var reachability = GetReachability(target);
        return inFilter ? reachability.Filter : reachability.Handler;
    }

    private CatchReachability GetReachability(CatchClauseSyntax target)
    {
        if (_cache.TryGetValue(target, out var cached))
        {
            return cached;
        }
        if (target.Parent is not TryStatementSyntax @try)
        {
            return new CatchReachability(Filter: true, Handler: true);
        }

        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(compilation, @try.SyntaxTree);
        var protectedBlock = model.GetOperation(@try.Block);
        if (protectedBlock == null)
        {
            return new CatchReachability(Filter: true, Handler: true);
        }
        var potential = GetPotentialExceptions(protectedBlock);
        var filterReachable = potential.Unknown &&
            CanUnknownReach(target, @try, model) ||
            potential.Known.Any(type =>
                CanKnownReach(type, target, @try, model));
        var result = new CatchReachability(
            filterReachable,
            filterReachable &&
            GetFilterSelection(target, model) != CatchSelection.Never);
        _cache.Add(target, result);
        return result;
    }

    private PotentialExceptions GetPotentialExceptions(
        IOperation protectedBlock)
    {
        return GetPotentialExceptions(
            protectedBlock,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
            depth: 0,
            keepEscaping: false);
    }

    private PotentialExceptions GetPotentialExceptions(
        IOperation root,
        HashSet<IMethodSymbol> activeMethods,
        int depth,
        bool keepEscaping)
    {
        var known = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(
            SymbolEqualityComparer.Default);
        var unknown = false;
        var remaining = new Stack<IOperation>();
        var scheduledSwitchBodies = new HashSet<ISwitchCaseOperation>();
        var scheduledGotoLabels = new HashSet<ILabelSymbol>(
            SymbolEqualityComparer.Default);
        var forcedGotoOperations = new HashSet<IOperation>();
        var switchCaseReachability = new Dictionary<
            ISwitchCaseOperation,
            SwitchCaseReachability>();
        remaining.Push(root);
        while (remaining.Count != 0)
        {
            var operation = remaining.Pop();
            if (ManagedAbstractFlow.IsCompileTimeUnreachable(
                    compilation,
                    operation) &&
                !forcedGotoOperations.Contains(operation) &&
                operation is not IBranchOperation
                {
                    Syntax: GotoStatementSyntax
                })
            {
                continue;
            }
            if (operation is IAnonymousFunctionOperation or
                ILocalFunctionOperation)
            {
                continue;
            }
            if (operation is IBranchOperation branch &&
                branch.Syntax is GotoStatementSyntax)
            {
                var targetCase = GetSwitchGotoTargetCase(branch);
                if (targetCase != null &&
                    scheduledSwitchBodies.Add(targetCase))
                {
                    PushSequential(targetCase.Body);
                }
                if (targetCase != null)
                {
                    continue;
                }
                var continuation = GetGotoTargetContinuation(branch);
                if (continuation != null)
                {
                    foreach (var targetOperation in continuation.SelectMany(
                                 static item => item.DescendantsAndSelf()))
                    {
                        forcedGotoOperations.Add(targetOperation);
                    }
                    if (scheduledGotoLabels.Add(branch.Target))
                    {
                        PushSequential(continuation);
                    }
                    continue;
                }
            }
            if (operation is ISwitchExpressionOperation switchExpression)
            {
                if (SwitchExpressionFacts.HasReachableUnmatchedPath(
                        switchExpression,
                        canCompleteNormally,
                        DefiniteOperationFacts.IsDefinitelyNonNull(
                            switchExpression.Value) ||
                        abstractFlow?.ProvesNonNull(
                            switchExpression,
                            switchExpression.Value) == true))
                {
                    Add(
                        _switchExpressionExceptionType is { } exceptionType
                            ? new PotentialExceptions(
                                ImmutableHashSet.Create<INamedTypeSymbol>(
                                    SymbolEqualityComparer.Default,
                                    exceptionType),
                                Unknown: false)
                            : UnknownPotential,
                        switchExpression);
                }
                PushChildren(switchExpression);
                continue;
            }
            if (operation is IThrowOperation thrown)
            {
                if (thrown.Exception is not { } exception)
                {
                    Add(
                        GetRethrowExceptions(
                            thrown,
                            activeMethods,
                            depth),
                        thrown);
                    continue;
                }

                Add(
                    GetPotentialExceptions(
                        exception,
                        activeMethods,
                        depth,
                        keepEscaping),
                    exception);
                var operandCompletes = canCompleteNormally(exception);
                if (operandCompletes &&
                    (abstractFlow?.ProvesNull(thrown, exception) == true ||
                     exception.ConstantValue is
                     { HasValue: true, Value: null }) &&
                    _nullReferenceExceptionType is { } nullReferenceException)
                {
                    Add(
                        new PotentialExceptions(
                            ImmutableHashSet.Create<INamedTypeSymbol>(
                                SymbolEqualityComparer.Default,
                                nullReferenceException),
                            Unknown: false),
                        thrown);
                }
                else if (operandCompletes &&
                    DefiniteOperationFacts.UnwrapHarmlessValue(exception).Type
                    is INamedTypeSymbol type)
                {
                    Add(
                        new PotentialExceptions(
                            ImmutableHashSet.Create<INamedTypeSymbol>(
                                SymbolEqualityComparer.Default,
                                type),
                            Unknown: false),
                        thrown);
                }
                continue;
            }
            if (operation is IInvocationOperation invocation)
            {
                var prerequisitesComplete =
                    invocation.Instance is not { } receiver ||
                    canCompleteNormally(receiver);
                prerequisitesComplete &= invocation.Arguments.All(argument =>
                    canCompleteNormally(argument.Value));
                var dereferenceCompletes = prerequisitesComplete;
                if (prerequisitesComplete &&
                    invocation.Instance is { } instance &&
                    invocation.TargetMethod.ReducedFrom == null)
                {
                    Add(
                        GetPotentialNullReceiver(
                            invocation,
                            instance,
                            out dereferenceCompletes),
                        invocation);
                }
                if (dereferenceCompletes)
                {
                    var initializationCompletes =
                        AddStaticInitializationPotential(
                        invocation.TargetMethod.ReducedFrom ??
                            invocation.TargetMethod,
                        invocation,
                        Add);
                    if (initializationCompletes)
                    {
                        Add(
                            invocation.IsVirtual
                                ? UnknownPotential
                                : GetCallableExceptions(
                                    invocation.TargetMethod,
                                    activeMethods,
                                    depth + 1),
                            invocation);
                    }
                }
                PushChildren(invocation);
                continue;
            }
            if (operation is IDeconstructionAssignmentOperation deconstruction)
            {
                if (canCompleteNormally(deconstruction.Value))
                {
                    Add(UnknownPotential, deconstruction);
                }
                remaining.Push(deconstruction.Value);
                continue;
            }
            if (operation is IWithOperation withOperation)
            {
                if (canCompleteNormally(withOperation.Operand) &&
                    withOperation.CloneMethod is { } clone)
                {
                    var dereferenceCompletes = true;
                    if (withOperation.Operand.Type?.IsReferenceType == true)
                    {
                        Add(
                            GetPotentialNullReceiver(
                                withOperation,
                                withOperation.Operand,
                                out dereferenceCompletes),
                            withOperation);
                    }
                    if (dereferenceCompletes)
                    {
                        var copyConstructor = OperationCompletionEvaluator
                            .GetRecordCopyConstructor(clone);
                        Add(
                            clone.IsVirtual || clone.IsAbstract
                                ? UnknownPotential
                                : GetCallableExceptions(
                                    copyConstructor ?? clone,
                                    activeMethods,
                                    depth + 1),
                            withOperation);
                    }
                }
                PushChildren(withOperation);
                continue;
            }
            if (operation is IEventAssignmentOperation eventAssignment)
            {
                if (eventAssignment.EventReference is not
                    IEventReferenceOperation eventReference)
                {
                    Add(UnknownPotential, eventAssignment);
                    PushChildren(eventAssignment);
                    continue;
                }
                var prerequisitesComplete =
                    eventReference.Instance is not { } receiver ||
                    canCompleteNormally(receiver);
                prerequisitesComplete &=
                    canCompleteNormally(eventAssignment.HandlerValue);
                var dereferenceCompletes = prerequisitesComplete;
                if (prerequisitesComplete &&
                    eventReference.Instance is { } instance)
                {
                    Add(
                        GetPotentialNullReceiver(
                            eventAssignment,
                            instance,
                            out dereferenceCompletes),
                        eventAssignment);
                }
                if (dereferenceCompletes)
                {
                    var accessor = eventAssignment.Adds
                        ? eventReference.Event.AddMethod
                        : eventReference.Event.RemoveMethod;
                    if (AddStaticInitializationPotential(
                            eventReference.Event,
                            eventAssignment,
                            Add))
                    {
                        Add(
                            accessor == null || accessor.IsVirtual ||
                            accessor.IsAbstract
                                ? UnknownPotential
                                : GetCallableExceptions(
                                    accessor,
                                    activeMethods,
                                    depth + 1),
                            eventAssignment);
                    }
                }
                PushChildren(eventAssignment);
                continue;
            }
            if (operation is ISimpleAssignmentOperation simple)
            {
                if (simple.Target is IPropertyReferenceOperation property &&
                    CanEvaluatePropertyTarget(property) &&
                    canCompleteNormally(simple.Value))
                {
                    var dereferenceCompletes = true;
                    if (property.Instance is { } instance)
                    {
                        Add(
                            GetPotentialNullReceiver(
                                property,
                                instance,
                                out dereferenceCompletes),
                            simple);
                    }
                    if (dereferenceCompletes)
                    {
                        if (AddStaticInitializationPotential(
                                property.Property,
                                simple,
                                Add))
                        {
                            AddPropertySetterExceptions(
                                property,
                                simple,
                                activeMethods,
                                depth,
                                Add);
                        }
                    }
                }
                if (simple.Target is IFieldReferenceOperation
                    { Field.IsStatic: true } field &&
                    canCompleteNormally(simple.Value))
                {
                    AddStaticInitializationPotential(
                        field.Field,
                        simple,
                        Add);
                }
                if (simple.Target is IFieldReferenceOperation
                    { Instance: { } instanceField } instanceTarget &&
                    canCompleteNormally(instanceField) &&
                    canCompleteNormally(simple.Value))
                {
                    Add(
                        GetPotentialNullReceiver(
                            instanceTarget,
                            instanceField,
                            out _),
                        simple);
                }
                if (simple.Target is IArrayElementReferenceOperation array &&
                    canCompleteNormally(array.ArrayReference) &&
                    array.Indices.All(canCompleteNormally) &&
                    canCompleteNormally(simple.Value))
                {
                    Add(
                        GetPotentialNullReceiver(
                            array,
                            array.ArrayReference,
                            out var dereferenceCompletes),
                        simple);
                    if (dereferenceCompletes)
                    {
                        Add(UnknownPotential, simple);
                    }
                }
                PushChildren(simple);
                continue;
            }
            if (operation is ICoalesceAssignmentOperation coalesce)
            {
                var targetCompletes = canCompleteNormally(coalesce.Target);
                var targetIsNonNull =
                    DefiniteOperationFacts.IsDefinitelyNonNull(
                        coalesce.Target) ||
                    abstractFlow?.ProvesNonNull(
                        coalesce,
                        coalesce.Target) == true;
                if (coalesce.Target is IPropertyReferenceOperation property &&
                    targetCompletes &&
                    !targetIsNonNull &&
                    canCompleteNormally(coalesce.Value))
                {
                    AddPropertySetterExceptions(
                        property,
                        coalesce,
                        activeMethods,
                        depth,
                        Add);
                }
                PushChildren(coalesce);
                continue;
            }
            if (operation is ICompoundAssignmentOperation compound)
            {
                var targetCompletes = canCompleteNormally(compound.Target);
                var inConversionCompletes = targetCompletes &&
                    AddCompoundCallablePotential(
                        compound.InConversion.MethodSymbol,
                        compound,
                        activeMethods,
                        depth,
                        Add);
                var priorPhasesComplete = inConversionCompletes &&
                    canCompleteNormally(compound.Value);
                var operatorCompletes = priorPhasesComplete &&
                    AddCompoundCallablePotential(
                        compound.OperatorMethod,
                        compound,
                        activeMethods,
                        depth,
                        Add) &&
                    !(compound.OperatorKind is
                            BinaryOperatorKind.Divide or
                            BinaryOperatorKind.Remainder &&
                        compound.Value.ConstantValue is
                        { HasValue: true, Value: 0 });
                if (priorPhasesComplete && CanThrowUnknown(compound))
                {
                    Add(UnknownPotential, compound);
                }
                var outConversionCompletes = operatorCompletes &&
                    AddCompoundCallablePotential(
                        compound.OutConversion.MethodSymbol,
                        compound,
                        activeMethods,
                        depth,
                        Add);
                if (outConversionCompletes &&
                    canCompoundValueComplete(compound) &&
                    compound.Target is IPropertyReferenceOperation property)
                {
                    AddPropertySetterExceptions(
                        property,
                        compound,
                        activeMethods,
                        depth,
                        Add);
                }
                PushChildren(compound);
                continue;
            }
            if (operation is IIncrementOrDecrementOperation increment)
            {
                var priorPhasesComplete =
                    canCompleteNormally(increment.Target);
                var operatorInitializationCompletes = true;
                if (priorPhasesComplete &&
                    increment.OperatorMethod is { } incrementOperator)
                {
                    operatorInitializationCompletes =
                        AddStaticInitializationPotential(
                            incrementOperator,
                            increment,
                            Add);
                    if (operatorInitializationCompletes)
                    {
                        Add(
                            GetOperatorExceptions(
                                incrementOperator,
                                activeMethods,
                                depth),
                            increment);
                    }
                }
                if (CanThrowUnknownAfterPrerequisites(increment))
                {
                    Add(UnknownPotential, increment);
                }
                var operatorCompletes =
                    operatorInitializationCompletes &&
                    canIncrementValueComplete(increment);
                if (priorPhasesComplete && operatorCompletes &&
                    increment.Target is IPropertyReferenceOperation property)
                {
                    AddPropertySetterExceptions(
                        property,
                        increment,
                        activeMethods,
                        depth,
                        Add);
                }
                PushChildren(increment);
                continue;
            }
            if (operation is IObjectCreationOperation creation)
            {
                var argumentsComplete = creation.Arguments.All(argument =>
                    canCompleteNormally(argument.Value));
                if (argumentsComplete)
                {
                    var initializationCompletes = true;
                    if (creation.Constructor is { } constructor &&
                        !IsExceptionType(creation.Type))
                    {
                        initializationCompletes =
                            AddStaticInitializationPotential(
                            constructor,
                            creation,
                            Add);
                    }
                    if (initializationCompletes)
                    {
                        var constructorExceptions =
                            creation.Constructor == null
                                ? UnknownPotential
                                : GetCallableExceptions(
                                    creation.Constructor,
                                    activeMethods,
                                    depth + 1);
                        if (IsExceptionType(creation.Type) &&
                            creation.Constructor is
                            { DeclaringSyntaxReferences.Length: 0 })
                        {
                            constructorExceptions = EmptyPotential;
                        }
                        Add(
                            constructorExceptions,
                            creation);
                    }
                }
                PushChildren(creation);
                continue;
            }
            if (operation is IInterpolationOperation interpolation)
            {
                if (canCompleteNormally(interpolation.Expression) &&
                    (interpolation.Alignment == null ||
                     canCompleteNormally(interpolation.Alignment)) &&
                    (interpolation.FormatString == null ||
                     canCompleteNormally(interpolation.FormatString)))
                {
                    Add(
                        interpolation.Alignment != null ||
                        interpolation.FormatString != null
                            ? UnknownPotential
                            : GetFormattedValueExceptions(
                                interpolation.Expression,
                                interpolation,
                                activeMethods,
                                depth),
                        interpolation);
                }
                PushChildren(interpolation);
                continue;
            }
            if (operation is IBinaryOperation concatenation &&
                StringConcatenationEffectResolver
                    .IsBuiltInStringConcatenation(concatenation))
            {
                if (canCompleteNormally(concatenation.LeftOperand) &&
                    canCompleteNormally(concatenation.RightOperand))
                {
                    Add(
                        GetFormattedValueExceptions(
                            concatenation.LeftOperand,
                            concatenation,
                            activeMethods,
                            depth),
                        concatenation);
                    Add(
                        GetFormattedValueExceptions(
                            concatenation.RightOperand,
                            concatenation,
                            activeMethods,
                            depth),
                        concatenation);
                }
                PushChildren(concatenation);
                continue;
            }
            if (operation is IBinaryOperation binary &&
                binary.OperatorMethod is { } binaryOperator)
            {
                var priorPhasesComplete =
                    canCompleteNormally(binary.LeftOperand);
                if (binary.OperatorKind is
                        BinaryOperatorKind.ConditionalAnd or
                        BinaryOperatorKind.ConditionalOr)
                {
                    var truthOperator =
                        ConditionalTruthOperatorFacts.Resolve(binary);
                    if (priorPhasesComplete && truthOperator != null)
                    {
                        var initializationCompletes =
                            AddStaticInitializationPotential(
                                truthOperator,
                                binary,
                                Add);
                        if (initializationCompletes)
                        {
                            Add(
                                GetOperatorExceptions(
                                    truthOperator,
                                    activeMethods,
                                    depth),
                                binary);
                        }
                        priorPhasesComplete = initializationCompletes &&
                            canMethodCompleteNormally(truthOperator);
                    }
                    else if (priorPhasesComplete)
                    {
                        Add(UnknownPotential, binary);
                        priorPhasesComplete = false;
                    }
                }

                if (priorPhasesComplete &&
                    canCompleteNormally(binary.RightOperand))
                {
                    if (AddStaticInitializationPotential(
                            binaryOperator,
                            binary,
                            Add))
                    {
                        Add(
                            GetOperatorExceptions(
                                binaryOperator,
                                activeMethods,
                                depth),
                            binary);
                    }
                }
                if (CanThrowUnknownAfterPrerequisites(binary))
                {
                    Add(UnknownPotential, binary);
                }
                PushChildren(binary);
                continue;
            }
            if (operation is IUnaryOperation unary &&
                unary.OperatorMethod is { } unaryOperator)
            {
                if (canCompleteNormally(unary.Operand))
                {
                    if (AddStaticInitializationPotential(
                            unaryOperator,
                            unary,
                            Add))
                    {
                        Add(
                            GetOperatorExceptions(
                                unaryOperator,
                                activeMethods,
                                depth),
                            unary);
                    }
                }
                if (CanThrowUnknownAfterPrerequisites(unary))
                {
                    Add(UnknownPotential, unary);
                }
                PushChildren(unary);
                continue;
            }
            if (operation is IConversionOperation conversion &&
                conversion.OperatorMethod is { } conversionOperator)
            {
                if (canCompleteNormally(conversion.Operand))
                {
                    if (AddStaticInitializationPotential(
                            conversionOperator,
                            conversion,
                            Add))
                    {
                        Add(
                            GetOperatorExceptions(
                                conversionOperator,
                                activeMethods,
                                depth),
                            conversion);
                    }
                }
                if (CanThrowUnknownAfterPrerequisites(conversion))
                {
                    Add(UnknownPotential, conversion);
                }
                PushChildren(conversion);
                continue;
            }
            if (operation is IUsingOperation or IUsingDeclarationOperation)
            {
                Add(
                    GetUsingDisposalExceptions(
                        operation,
                        activeMethods,
                        depth),
                    operation);
                PushChildren(operation);
                continue;
            }
            if (operation is ITryOperation nestedTry)
            {
                Add(
                    GetNestedTryExceptions(
                        nestedTry,
                        activeMethods,
                        depth),
                    nestedTry);
                continue;
            }
            if (operation is IForEachLoopOperation forEach)
            {
                Add(
                    GetForEachExceptions(
                        forEach,
                        activeMethods,
                        depth,
                        out var reachesBody),
                    forEach);
                remaining.Push(forEach.Collection);
                if (reachesBody)
                {
                    remaining.Push(forEach.LoopControlVariable);
                    remaining.Push(forEach.Body);
                    foreach (var nextVariable in forEach.NextVariables)
                    {
                        remaining.Push(nextVariable);
                    }
                }
                continue;
            }
            if (operation is IPropertyReferenceOperation propertyReference)
            {
                if (propertyReference.Parent is ISimpleAssignmentOperation
                        enclosingAssignment &&
                    ReferenceEquals(
                        enclosingAssignment.Target,
                        propertyReference))
                {
                    PushChildren(propertyReference);
                    continue;
                }
                var prerequisitesComplete =
                    propertyReference.Instance is not { } receiver ||
                    canCompleteNormally(receiver);
                prerequisitesComplete &= propertyReference.Arguments.All(
                    argument => canCompleteNormally(argument.Value));
                var dereferenceCompletes = prerequisitesComplete;
                if (prerequisitesComplete &&
                    propertyReference.Instance is { } instance)
                {
                    Add(
                        GetPotentialNullReceiver(
                            propertyReference,
                            instance,
                            out dereferenceCompletes),
                        propertyReference);
                }
                if (dereferenceCompletes)
                {
                    var accessors = GetAccessors(propertyReference).ToArray();
                    var initializationCompletes = true;
                    if (accessors.Length != 0)
                    {
                        initializationCompletes =
                            AddStaticInitializationPotential(
                            propertyReference.Property,
                            propertyReference,
                            Add);
                    }
                    if (initializationCompletes)
                    {
                        foreach (var accessor in accessors)
                        {
                            Add(
                                accessor == null || accessor.IsVirtual ||
                                accessor.IsAbstract
                                    ? UnknownPotential
                                    : accessor.DeclaringSyntaxReferences.Length == 0 &&
                                        propertyReference.Property.ContainingType
                                            ?.IsRefLikeType == true
                                    ? EmptyPotential
                                    : GetCallableExceptions(
                                        accessor,
                                        activeMethods,
                                        depth + 1),
                                propertyReference);
                        }
                    }
                }
                PushChildren(propertyReference);
                continue;
            }
            if (operation is IListPatternOperation listPattern)
            {
                var members = getReachableListPatternMembers(listPattern);
                foreach (var member in members)
                {
                    Add(
                        SwitchExpressionFacts
                            .IsCompilerIntrinsicListPatternMember(
                                compilation,
                                listPattern,
                                member)
                            ? EmptyPotential
                            : member.IsVirtual || member.IsAbstract
                            ? UnknownPotential
                            : GetCallableExceptions(
                                member,
                                activeMethods,
                                depth + 1),
                        listPattern);
                }
                PushSequential(listPattern.Patterns);
                continue;
            }
            if (operation is IFieldReferenceOperation fieldReference)
            {
                if (fieldReference.Instance is { } fieldInstance)
                {
                    Add(
                        GetPotentialNullReceiver(
                            fieldReference,
                            fieldInstance,
                            out _),
                        fieldReference);
                }
                else
                {
                    if (fieldReference.Parent is not
                            ISimpleAssignmentOperation enclosingAssignment ||
                        !ReferenceEquals(
                            enclosingAssignment.Target,
                            fieldReference))
                    {
                        AddStaticInitializationPotential(
                            fieldReference.Field,
                            fieldReference,
                            Add);
                    }
                }
                PushChildren(fieldReference);
                continue;
            }
            if (operation is IArrayElementReferenceOperation element)
            {
                if (canCompleteNormally(element.ArrayReference) &&
                    element.Indices.All(canCompleteNormally))
                {
                    Add(
                        GetPotentialNullReceiver(
                            element,
                            element.ArrayReference,
                            out var receiverCompletes),
                        element);
                    if (receiverCompletes)
                    {
                        Add(UnknownPotential, element);
                    }
                }
                PushChildren(element);
                continue;
            }
            if (operation is ILockOperation @lock)
            {
                if (canCompleteNormally(@lock.LockedValue))
                {
                    var definitelyNull = IsDefinitelyNull(
                        @lock,
                        @lock.LockedValue);
                    var definitelyNonNull =
                        abstractFlow?.ProvesNonNull(
                            @lock,
                            @lock.LockedValue) == true ||
                        DefiniteOperationFacts.IsDefinitelyNonNull(
                            @lock.LockedValue);
                    if (!definitelyNonNull)
                    {
                        Add(
                            _argumentNullExceptionType is { } argumentNull
                                ? new PotentialExceptions(
                                    ImmutableHashSet.Create<INamedTypeSymbol>(
                                        SymbolEqualityComparer.Default,
                                        argumentNull),
                                    Unknown: false)
                                : UnknownPotential,
                            @lock);
                    }
                    if (!definitelyNull)
                    {
                        Add(UnknownPotential, @lock);
                    }
                }
                PushChildren(@lock);
                continue;
            }
            if (operation is IAwaitOperation awaitOperation)
            {
                if (canCompleteNormally(awaitOperation.Operation))
                {
                    var model = SharpProof.Frontend.Host
                        .CompilationModelProvider.GetSemanticModel(
                            compilation,
                            awaitOperation.Syntax.SyntaxTree);
                    var info = awaitOperation.Syntax is
                        AwaitExpressionSyntax awaitSyntax
                            ? Microsoft.CodeAnalysis.CSharp.CSharpExtensions
                                .GetAwaitExpressionInfo(model, awaitSyntax)
                            : default;
                    var getAwaiter = info.GetAwaiterMethod;
                    var phaseCompletes = true;
                    if (getAwaiter == null)
                    {
                        Add(UnknownPotential, awaitOperation);
                    }
                    else
                    {
                        var dereferenceCompletes = true;
                        if (!getAwaiter.IsStatic &&
                            getAwaiter.ReducedFrom == null)
                        {
                            Add(
                                GetPotentialNullReceiver(
                                    awaitOperation,
                                    awaitOperation.Operation,
                                    out dereferenceCompletes),
                                awaitOperation);
                        }
                        phaseCompletes = dereferenceCompletes &&
                            AddStaticInitializationPotential(
                                getAwaiter.ReducedFrom ?? getAwaiter,
                                awaitOperation,
                                Add);
                        if (phaseCompletes)
                        {
                            Add(
                                getAwaiter.IsVirtual || getAwaiter.IsAbstract
                                    ? UnknownPotential
                                    : GetCallableExceptions(
                                        getAwaiter,
                                        activeMethods,
                                        depth + 1),
                                awaitOperation);
                            phaseCompletes =
                                canMethodCompleteNormally(getAwaiter);
                            if (phaseCompletes &&
                                getAwaiter.ReturnType.IsReferenceType)
                            {
                                var returnNullability =
                                    GetReturnNullability(getAwaiter);
                                if (returnNullability !=
                                        ReturnNullability.NonNull &&
                                    _nullReferenceExceptionType is
                                    { } nullAwaiter)
                                {
                                    Add(
                                        new PotentialExceptions(
                                            ImmutableHashSet.Create<
                                                INamedTypeSymbol>(
                                                SymbolEqualityComparer.Default,
                                                nullAwaiter),
                                            Unknown: false),
                                        awaitOperation);
                                }
                                if (returnNullability ==
                                    ReturnNullability.Null)
                                {
                                    phaseCompletes = false;
                                }
                            }
                        }
                    }
                    var isCompleted = info.IsCompletedProperty?.GetMethod;
                    if (phaseCompletes)
                    {
                        Add(
                            isCompleted == null || isCompleted.IsVirtual ||
                            isCompleted.IsAbstract
                                ? UnknownPotential
                                : GetCallableExceptions(
                                    isCompleted,
                                    activeMethods,
                                    depth + 1),
                            awaitOperation);
                        phaseCompletes = isCompleted == null ||
                            canMethodCompleteNormally(isCompleted);
                    }
                    if (phaseCompletes && getAwaiter != null)
                    {
                        var continuation =
                            knownSymbols.FindAwaitContinuationMethod(
                                getAwaiter.ReturnType);
                        Add(
                            continuation == null ||
                            continuation.IsVirtual ||
                            continuation.IsAbstract
                                ? UnknownPotential
                                : GetCallableExceptions(
                                    continuation,
                                    activeMethods,
                                    depth + 1),
                            awaitOperation);
                    }
                    var getResult = info.GetResultMethod;
                    if (phaseCompletes)
                    {
                        Add(
                            getResult == null || getResult.IsVirtual ||
                            getResult.IsAbstract
                                ? UnknownPotential
                                : GetCallableExceptions(
                                    getResult,
                                    activeMethods,
                                    depth + 1),
                            awaitOperation);
                    }
                }
                PushChildren(awaitOperation);
                continue;
            }
            if (operation is IMethodReferenceOperation methodReference &&
                methodReference.Instance is { } methodInstance &&
                !methodReference.Method.IsStatic)
            {
                Add(
                    GetPotentialNullReceiver(
                        methodReference,
                        methodInstance,
                        out _),
                    methodReference);
                PushChildren(methodReference);
                continue;
            }
            if (CanThrowUnknownAfterPrerequisites(operation))
            {
                Add(UnknownPotential, operation);
            }
            PushChildren(operation);
        }
        return new PotentialExceptions(known.ToImmutable(), unknown);

        void Add(PotentialExceptions potential, IOperation origin)
        {
            if (keepEscaping)
            {
                potential = KeepEscaping(potential, origin);
            }
            known.UnionWith(potential.Known);
            unknown |= potential.Unknown;
        }

        void PushChildren(IOperation operation)
        {
            PushChildrenCore(
                operation,
                remaining,
                scheduledSwitchBodies,
                switchCaseReachability);
        }

        void PushSequential(IEnumerable<IOperation> children)
        {
            var reachable = new List<IOperation>();
            foreach (var child in children)
            {
                reachable.Add(child);
                if (!canCompleteNormally(child))
                {
                    break;
                }
            }
            PushAll(reachable);
        }

        void PushAll(IEnumerable<IOperation> children)
        {
            foreach (var child in children.Reverse())
            {
                remaining.Push(child);
            }
        }
    }

    // Keep this large control-flow dispatcher out of the captured traversal
    // closure. CA1508 otherwise constructs an expensive interprocedural flow
    // graph for the local function during every qualifying build.
    private void PushChildrenCore(
        IOperation operation,
        Stack<IOperation> remaining,
        HashSet<ISwitchCaseOperation> scheduledSwitchBodies,
        Dictionary<ISwitchCaseOperation, SwitchCaseReachability>
            switchCaseReachability)
    {
        switch (operation)
        {
            case INameOfOperation or ITypeOfOperation or ISizeOfOperation:
                return;
            case IBlockOperation block:
                PushSequentialCore(block.Operations, remaining);
                return;
            case ISimpleAssignmentOperation assignment:
                var inputs = GetSimpleAssignmentTargetInputs(
                    assignment.Target).ToArray();
                if (inputs.All(canCompleteNormally))
                {
                    remaining.Push(assignment.Value);
                }
                PushSequentialCore(inputs, remaining);
                return;
            case IBinaryOperation
            {
                OperatorMethod: not null,
                OperatorKind: BinaryOperatorKind.ConditionalAnd or
                        BinaryOperatorKind.ConditionalOr
            } binary:
                if (canCompleteNormally(binary.LeftOperand) &&
                    ConditionalTruthOperatorFacts.Resolve(binary) is
                    { } truthOperator &&
                    canMethodCompleteNormally(truthOperator))
                {
                    remaining.Push(binary.RightOperand);
                }
                remaining.Push(binary.LeftOperand);
                return;
            case IBinaryOperation
            {
                OperatorMethod: null,
                OperatorKind: BinaryOperatorKind.ConditionalAnd or
                        BinaryOperatorKind.ConditionalOr
            } binary:
                var leftCompletes = canCompleteNormally(binary.LeftOperand);
                var leftConstant = binary.LeftOperand.ConstantValue is
                { HasValue: true, Value: bool leftValue }
                        ? leftValue
                        : (bool?)null;
                var evaluatesRight = leftCompletes &&
                    (binary.OperatorKind == BinaryOperatorKind.ConditionalAnd
                        ? leftConstant != false
                        : leftConstant != true);
                if (evaluatesRight)
                {
                    remaining.Push(binary.RightOperand);
                }
                remaining.Push(binary.LeftOperand);
                return;
            case IConditionalOperation conditional:
                if (!canCompleteNormally(conditional.Condition))
                {
                    remaining.Push(conditional.Condition);
                    return;
                }
                var condition = conditional.Condition.ConstantValue is
                { HasValue: true, Value: bool conditionValue }
                        ? conditionValue
                        : (bool?)null;
                if (condition != true &&
                    conditional.WhenFalse is { } whenFalse)
                {
                    remaining.Push(whenFalse);
                }
                if (condition != false)
                {
                    remaining.Push(conditional.WhenTrue);
                }
                remaining.Push(conditional.Condition);
                return;
            case ICoalesceOperation coalesce:
                var valueCompletes = canCompleteNormally(coalesce.Value);
                var definitelyNonNull =
                    DefiniteOperationFacts.IsDefinitelyNonNull(
                        coalesce.Value) ||
                    abstractFlow?.ProvesNonNull(
                        coalesce,
                        coalesce.Value) == true;
                if (valueCompletes && !definitelyNonNull)
                {
                    remaining.Push(coalesce.WhenNull);
                }
                remaining.Push(coalesce.Value);
                return;
            case ICoalesceAssignmentOperation coalesce:
                var targetCompletes = canCompleteNormally(coalesce.Target);
                var targetIsNonNull =
                    DefiniteOperationFacts.IsDefinitelyNonNull(
                        coalesce.Target) ||
                    abstractFlow?.ProvesNonNull(
                        coalesce,
                        coalesce.Target) == true;
                if (targetCompletes && !targetIsNonNull)
                {
                    remaining.Push(coalesce.Value);
                }
                remaining.Push(coalesce.Target);
                return;
            case ICompoundAssignmentOperation compound:
                if (canCompleteNormally(compound.Target) &&
                    (compound.InConversion.MethodSymbol == null ||
                     canMethodCompleteNormally(
                         compound.InConversion.MethodSymbol)))
                {
                    remaining.Push(compound.Value);
                }
                remaining.Push(compound.Target);
                return;
            case IConditionalAccessOperation access:
                var receiverCompletes = canCompleteNormally(
                    access.Operation);
                var receiverIsNull =
                    DefiniteOperationFacts.IsDefinitelyNull(
                        access.Operation) ||
                    abstractFlow?.ProvesNull(
                        access,
                        access.Operation) == true;
                if (receiverCompletes && !receiverIsNull)
                {
                    remaining.Push(access.WhenNotNull);
                }
                remaining.Push(access.Operation);
                return;
            case IWithOperation withOperation:
                if (canWithCloneComplete(withOperation) &&
                    withOperation.Initializer is { } initializer)
                {
                    remaining.Push(initializer);
                }
                remaining.Push(withOperation.Operand);
                return;
            case IObjectCreationOperation creation:
                if (creation.Initializer != null &&
                    creation.Arguments.All(argument =>
                        canCompleteNormally(argument.Value)) &&
                    creation.Constructor is { } constructor &&
                    canMethodCompleteNormally(constructor))
                {
                    remaining.Push(creation.Initializer);
                }
                PushSequentialCore(creation.Arguments, remaining);
                return;
            case ILockOperation @lock:
                if (canCompleteNormally(@lock.LockedValue) &&
                    !IsDefinitelyNull(@lock, @lock.LockedValue))
                {
                    remaining.Push(@lock.Body);
                }
                remaining.Push(@lock.LockedValue);
                return;
            case ISwitchOperation @switch:
                if (canCompleteNormally(@switch.Value))
                {
                    var constant = @switch.Value.ConstantValue;
                    var cases = GetReachableSwitchCases(
                        @switch,
                        constant.HasValue,
                        constant.Value,
                        scheduledSwitchBodies,
                        switchCaseReachability);
                    PushAllCore(cases, remaining);
                }
                remaining.Push(@switch.Value);
                return;
            case ISwitchCaseOperation @case
                when switchCaseReachability.TryGetValue(
                    @case,
                    out var reachability):
                if (reachability.BodyReachable)
                {
                    PushSequentialCore(@case.Body, remaining);
                }
                PushAllCore(reachability.Clauses, remaining);
                return;
            case ISwitchExpressionOperation @switch:
                if (canCompleteNormally(@switch.Value))
                {
                    PushAllCore(
                        SwitchExpressionFacts.GetReachableArms(
                            @switch,
                            canCompleteNormally,
                            DefiniteOperationFacts.IsDefinitelyNonNull(
                                @switch.Value) ||
                            abstractFlow?.ProvesNonNull(
                                @switch,
                                @switch.Value) == true),
                        remaining);
                }
                remaining.Push(@switch.Value);
                return;
            default:
                PushSequentialCore(operation.ChildOperations, remaining);
                return;
        }
    }

    private void PushSequentialCore(
        IEnumerable<IOperation> children,
        Stack<IOperation> remaining)
    {
        var reachable = new List<IOperation>();
        foreach (var child in children)
        {
            reachable.Add(child);
            if (!canCompleteNormally(child))
            {
                break;
            }
        }
        PushAllCore(reachable, remaining);
    }

    private static void PushAllCore(
        IEnumerable<IOperation> children,
        Stack<IOperation> remaining)
    {
        foreach (var child in children.Reverse())
        {
            remaining.Push(child);
        }
    }

    private ISwitchCaseOperation[] GetReachableSwitchCases(
        ISwitchOperation @switch,
        bool hasConstant,
        object? value,
        HashSet<ISwitchCaseOperation> scheduledSwitchBodies,
        Dictionary<ISwitchCaseOperation, SwitchCaseReachability>
            switchCaseReachability)
    {
        var selected = new Dictionary<
            ISwitchCaseOperation,
            SwitchCaseReachability>();
        var inputDefinitelyNonNull =
            DefiniteOperationFacts.IsDefinitelyNonNull(@switch.Value) ||
            abstractFlow?.ProvesNonNull(@switch, @switch.Value) == true;
        ISwitchCaseOperation? defaultCase = null;
        var definiteMatch = false;
        foreach (var @case in @switch.Cases)
        {
            var reachableClauses = new List<ICaseClauseOperation>();
            var bodyReachable = false;
            var stopsSelection = false;
            foreach (var clause in @case.Clauses)
            {
                if (clause is IDefaultCaseClauseOperation)
                {
                    defaultCase = @case;
                    continue;
                }
                var patternSelection = clause is
                    IPatternCaseClauseOperation patternClause
                        ? GetPatternSelection(
                            patternClause.Pattern,
                            @switch.Value.Type,
                            hasConstant,
                            value,
                            inputDefinitelyNonNull)
                        : SwitchSelection.Never;
                var clauseSelection = clause switch
                {
                    ISingleValueCaseClauseOperation single
                        when hasConstant &&
                            single.Value.ConstantValue is
                            { HasValue: true } item =>
                        Equals(value, item.Value)
                            ? SwitchSelection.Always
                            : SwitchSelection.Never,
                    IPatternCaseClauseOperation pattern =>
                        ApplySwitchGuard(
                            GetPatternSelection(
                                pattern.Pattern,
                                @switch.Value.Type,
                                hasConstant,
                                value,
                                inputDefinitelyNonNull),
                            pattern.Guard),
                    _ => SwitchSelection.Maybe
                };
                if (clauseSelection != SwitchSelection.Never)
                {
                    reachableClauses.Add(clause);
                    bodyReachable |= CanCaseClauseReachBody(
                        clause,
                        clauseSelection);
                }
                stopsSelection |= clauseSelection == SwitchSelection.Always ||
                    clause is IPatternCaseClauseOperation barrierClause &&
                    SwitchExpressionFacts.IsPatternEvaluationUnavoidable(
                        barrierClause.Pattern,
                        @switch.Value.Type,
                        inputDefinitelyNonNull) &&
                    !canCompleteNormally(barrierClause.Pattern) ||
                    patternSelection == SwitchSelection.Always &&
                    clause is IPatternCaseClauseOperation
                    { Guard: not null } guarded &&
                    !canCompleteNormally(guarded.Guard);
                if (stopsSelection)
                {
                    break;
                }
            }
            if (reachableClauses.Count != 0)
            {
                selected[@case] = new SwitchCaseReachability(
                    @case,
                    reachableClauses,
                    bodyReachable);
            }
            if (stopsSelection)
            {
                definiteMatch = true;
                break;
            }
        }
        if (!definiteMatch && defaultCase != null)
        {
            if (selected.TryGetValue(defaultCase, out var existingDefault))
            {
                selected[defaultCase] = existingDefault with
                {
                    BodyReachable = true
                };
            }
            else
            {
                selected[defaultCase] = new SwitchCaseReachability(
                    defaultCase,
                    [],
                    BodyReachable: true);
            }
        }

        foreach (var @case in @switch.Cases)
        {
            if (!selected.TryGetValue(@case, out var reachability))
            {
                continue;
            }
            switchCaseReachability[@case] = reachability;
            if (reachability.BodyReachable)
            {
                scheduledSwitchBodies.Add(@case);
            }
        }
        return @switch.Cases.Where(selected.ContainsKey).ToArray();
    }

    private static ISwitchCaseOperation? GetSwitchGotoTargetCase(
        IBranchOperation branch)
    {
        var target = branch.Target.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .FirstOrDefault(static syntax => syntax is SwitchLabelSyntax);
        if (target == null)
        {
            return null;
        }
        ISwitchOperation? @switch = null;
        for (var current = branch.Parent;
             current != null;
             current = current.Parent)
        {
            if (current is ISwitchOperation candidate)
            {
                @switch = candidate;
                break;
            }
        }
        return @switch?.Cases.FirstOrDefault(candidate =>
            candidate.Syntax.Span.Contains(target.Span));
    }

    private IOperation[]? GetGotoTargetContinuation(
        IBranchOperation branch)
    {
        var target = branch.Target.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .FirstOrDefault(static syntax => syntax is LabeledStatementSyntax);
        if (target == null)
        {
            return null;
        }
        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(compilation, target.SyntaxTree);
        var labeled = model.GetOperation(target);
        if (labeled == null)
        {
            return null;
        }
        var labeledStatement = target is LabeledStatementSyntax labeledSyntax
            ? model.GetOperation(labeledSyntax.Statement)
            : null;
        var labeledInvocations = target.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(syntax => model.GetOperation(syntax))
            .Where(static operation => operation != null)
            .Cast<IOperation>()
            .ToArray();
        if (labeledInvocations.Length == 0 &&
            target.AncestorsAndSelf()
                .OfType<BaseMethodDeclarationSyntax>()
                .FirstOrDefault() is { } methodSyntax)
        {
            labeledInvocations = methodSyntax.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(invocation => invocation.SpanStart > target.Span.End)
                .Select(syntax => model.GetOperation(syntax))
                .Where(static operation => operation != null)
                .Cast<IOperation>()
                .Take(1)
                .ToArray();
        }

        IOperation[] IncludeLabeledStatement(IEnumerable<IOperation> operations)
        {
            var result = operations.ToList();
            if (labeledStatement != null &&
                !result.Any(operation => ReferenceEquals(operation, labeledStatement)))
            {
                result.Insert(1, labeledStatement);
            }
            foreach (var invocation in labeledInvocations.Reverse())
            {
                if (!result.Any(operation => ReferenceEquals(operation, invocation)))
                {
                    result.Insert(1, invocation);
                }
            }
            return result.ToArray();
        }
        var sequenceEntry = labeled;
        while (sequenceEntry.Parent is ILabeledOperation outerLabel)
        {
            sequenceEntry = outerLabel;
        }
        if (sequenceEntry.Parent is IBlockOperation block)
        {
            var index = block.Operations.IndexOf(sequenceEntry);
            return index < 0
                ? null
                : IncludeLabeledStatement(block.Operations.Skip(index));
        }
        if (sequenceEntry.Parent is ISwitchCaseOperation @case)
        {
            var index = @case.Body.IndexOf(sequenceEntry);
            return index < 0
                ? null
                : IncludeLabeledStatement(@case.Body.Skip(index));
        }
        return [sequenceEntry];
    }

    private bool CanCaseClauseReachBody(
        ICaseClauseOperation clause,
        SwitchSelection selection)
    {
        if (selection == SwitchSelection.Never)
        {
            return false;
        }
        if (clause is not IPatternCaseClauseOperation pattern)
        {
            return true;
        }
        if (!canCompleteNormally(pattern.Pattern))
        {
            return false;
        }
        if (pattern.Guard == null)
        {
            return true;
        }
        return pattern.Guard.ConstantValue is
        { HasValue: true, Value: bool guard }
                ? guard
                : canCompleteNormally(pattern.Guard);
    }

    private static SwitchSelection GetPatternSelection(
        IPatternOperation pattern,
        ITypeSymbol? inputType,
        bool hasConstant,
        object? value,
        bool inputDefinitelyNonNull)
    {
        var selection = hasConstant
            ? SwitchExpressionFacts.GetPatternSelection(pattern, value)
            : SwitchExpressionFacts.GetPatternSelectionForUnknownValue(
                pattern,
                inputType,
                inputDefinitelyNonNull);
        return selection switch
        {
            SwitchExpressionSelection.Never => SwitchSelection.Never,
            SwitchExpressionSelection.Maybe => SwitchSelection.Maybe,
            SwitchExpressionSelection.Always => SwitchSelection.Always,
            _ => throw new InvalidOperationException(
                "Unknown switch-pattern selection.")
        };
    }

    private static SwitchSelection ApplySwitchGuard(
        SwitchSelection selection,
        IOperation? guard)
    {
        if (selection == SwitchSelection.Never || guard == null)
        {
            return selection;
        }
        return guard.ConstantValue is { HasValue: true, Value: bool value }
            ? value
                ? selection
                : SwitchSelection.Never
            : SwitchSelection.Maybe;
    }

    private PotentialExceptions GetNestedTryExceptions(
        ITryOperation nestedTry,
        HashSet<IMethodSymbol> activeMethods,
        int depth)
    {
        if (nestedTry.Syntax is not TryStatementSyntax syntax)
        {
            return UnknownPotential;
        }
        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(compilation, syntax.SyntaxTree);
        var body = GetPotentialExceptions(
            nestedTry.Body,
            activeMethods,
            depth,
            keepEscaping: false);
        var escapingBody = FromThrowSet(
            EffectExceptionFlow.KeepEscapingThroughTry(
                EffectThrowSet.Create(body.Known, body.Unknown),
                syntax,
                compilation));
        var result = escapingBody;
        var finallyReachable = canCompleteNormally(nestedTry.Body) ||
            CanExitAbruptlyWithoutExceptions(
                nestedTry.Body,
                nestedTry.Body) ||
            escapingBody.Unknown || !escapingBody.Known.IsEmpty;
        foreach (var catchOperation in nestedTry.Catches)
        {
            if (catchOperation.Syntax is not CatchClauseSyntax @catch)
            {
                return UnknownPotential;
            }
            var filterReachable = body.Unknown &&
                CanUnknownReach(@catch, syntax, model) ||
                body.Known.Any(thrown =>
                    CanKnownReach(thrown, @catch, syntax, model));
            if (!filterReachable ||
                GetFilterSelection(@catch, model) == CatchSelection.Never)
            {
                continue;
            }
            result = Union(
                result,
                GetPotentialExceptions(
                    catchOperation.Handler,
                    activeMethods,
                    depth,
                    keepEscaping: false));
            finallyReachable |= canCompleteNormally(
                catchOperation.Handler) ||
                CanExitAbruptly(
                    catchOperation.Handler,
                    catchOperation.Handler);
        }
        if (nestedTry.Finally is not { } finallyOperation ||
            !finallyReachable)
        {
            return result;
        }
        var finallyExceptions = GetPotentialExceptions(
            finallyOperation,
            activeMethods,
            depth,
            keepEscaping: false);
        return canCompleteNormally(finallyOperation)
            ? Union(result, finallyExceptions)
            : finallyExceptions;
    }

    internal EffectThrowSet ResolveRethrow(IThrowOperation thrown)
    {
        var potential = GetRethrowExceptions(
            thrown,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
            depth: 0);
        return EffectThrowSet.Create(potential.Known, potential.Unknown);
    }

    private PotentialExceptions GetRethrowExceptions(
        IThrowOperation thrown,
        HashSet<IMethodSymbol> activeMethods,
        int depth)
    {
        ICatchClauseOperation? catchOperation = null;
        for (var current = thrown.Parent; current != null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return UnknownPotential;
            }
            if (current is ICatchClauseOperation @catch)
            {
                catchOperation = @catch;
                break;
            }
        }

        if (catchOperation?.Syntax is not CatchClauseSyntax target ||
            target.Parent is not TryStatementSyntax @try)
        {
            return UnknownPotential;
        }

        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(compilation, @try.SyntaxTree);
        if (model.GetOperation(@try.Block) is not { } protectedBlock)
        {
            return UnknownPotential;
        }

        var incoming = GetPotentialExceptions(
            protectedBlock,
            activeMethods,
            depth,
            keepEscaping: false);
        return new PotentialExceptions(
            incoming.Known
                .Where(type => CanKnownReach(type, target, @try, model))
                .ToImmutableHashSet<INamedTypeSymbol>(
                    SymbolEqualityComparer.Default),
            incoming.Unknown && CanUnknownReach(target, @try, model));
    }

    private PotentialExceptions GetPotentialNullReceiver(
        IOperation origin,
        IOperation instance,
        out bool dereferenceCompletes)
    {
        if (!canCompleteNormally(instance))
        {
            dereferenceCompletes = false;
            return EmptyPotential;
        }
        if (instance.Type?.IsValueType == true)
        {
            dereferenceCompletes = true;
            return EmptyPotential;
        }
        var definitelyNull =
            abstractFlow?.ProvesNull(origin, instance) == true ||
            instance.ConstantValue is { HasValue: true, Value: null };
        var definitelyNonNull =
            abstractFlow?.ProvesNonNull(origin, instance) == true ||
            DefiniteOperationFacts.IsDefinitelyNonNull(instance);
        dereferenceCompletes = !definitelyNull;
        if (definitelyNonNull)
        {
            return EmptyPotential;
        }
        if (_nullReferenceExceptionType is not { } nullReferenceException)
        {
            return UnknownPotential;
        }
        return new PotentialExceptions(
            ImmutableHashSet.Create<INamedTypeSymbol>(
                SymbolEqualityComparer.Default,
                nullReferenceException),
            Unknown: false);
    }

    private bool AddStaticInitializationPotential(
        ISymbol member,
        IOperation origin,
        Action<PotentialExceptions, IOperation> add)
    {
        member = OperationCompletionEvaluator
            .NormalizeStaticInitializationMember(member);
        if ((!member.IsStatic && member is not IMethodSymbol
            { MethodKind: MethodKind.Constructor }) ||
            member is IFieldSymbol { IsConst: true } ||
            OperationCompletionEvaluator
                .CanAssumeStaticInitializationComplete(caller, member) ||
            member.ContainingType is not { } type ||
            !EffectMethodNodeBuilder.HasPotentialStaticInitialization(
                type,
                apiSpecs))
        {
            return true;
        }
        add(
            _typeInitializationExceptionType is { } typeInitialization
                ? new PotentialExceptions(
                    ImmutableHashSet.Create<INamedTypeSymbol>(
                        SymbolEqualityComparer.Default,
                        typeInitialization),
                    Unknown: false)
                : UnknownPotential,
            origin);
        return !OperationCompletionEvaluator
                .RequiresStaticInitializationCompletion(member) ||
            StaticInitializationMayComplete(type);
    }

    private bool StaticInitializationMayComplete(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers())
        {
            var isStaticInitializable = member switch
            {
                IFieldSymbol field => field.IsStatic && !field.IsConst,
                IPropertySymbol property => property.IsStatic,
                IEventSymbol @event => @event.IsStatic,
                _ => false
            };
            if (!isStaticInitializable)
            {
                continue;
            }
            foreach (var reference in member.DeclaringSyntaxReferences)
            {
                var expression = EffectProjections.GetInitializerExpression(
                    reference.GetSyntax());
                if (expression == null)
                {
                    continue;
                }
                var model = SharpProof.Frontend.Host.CompilationModelProvider
                    .GetSemanticModel(compilation, expression.SyntaxTree);
                var operation = model.GetOperation(expression);
                if (operation != null &&
                    !_staticInitializationFacts.MayCompleteNormally(operation))
                {
                    return false;
                }
            }
        }
        return type.StaticConstructors.All(canMethodCompleteNormally);
    }

    private bool IsDefinitelyNull(IOperation origin, IOperation value)
    {
        return abstractFlow?.ProvesNull(origin, value) == true ||
            value.ConstantValue is { HasValue: true, Value: null } ||
            DefiniteOperationFacts.IsDefinitelyNull(value);
    }

    private static IEnumerable<IMethodSymbol?> GetAccessors(
        IPropertyReferenceOperation property)
    {
        if (property.Parent is ISimpleAssignmentOperation simple &&
            ReferenceEquals(simple.Target, property))
        {
            yield break;
        }
        if ((property.Parent is ICoalesceAssignmentOperation coalesce &&
             ReferenceEquals(coalesce.Target, property)) ||
            (property.Parent is ICompoundAssignmentOperation compound &&
             ReferenceEquals(compound.Target, property)) ||
            (property.Parent is IIncrementOrDecrementOperation increment &&
             ReferenceEquals(increment.Target, property)))
        {
            yield return property.Property.GetMethod;
            yield break;
        }
        yield return property.Property.GetMethod;
    }

    private bool CanEvaluatePropertyTarget(
        IPropertyReferenceOperation property)
    {
        return (property.Instance is not { } instance ||
                canCompleteNormally(instance)) &&
            property.Arguments.All(argument =>
                canCompleteNormally(argument.Value));
    }

    private static IEnumerable<IOperation>
        GetSimpleAssignmentTargetInputs(IOperation target)
    {
        switch (target)
        {
            case IPropertyReferenceOperation property:
                if (property.Instance != null)
                {
                    yield return property.Instance;
                }
                foreach (var argument in property.Arguments)
                {
                    yield return argument.Value;
                }
                yield break;
            case IFieldReferenceOperation field:
                if (field.Instance != null)
                {
                    yield return field.Instance;
                }
                yield break;
            case IArrayElementReferenceOperation array:
                yield return array.ArrayReference;
                foreach (var index in array.Indices)
                {
                    yield return index;
                }
                yield break;
            default:
                foreach (var child in target.ChildOperations)
                {
                    yield return child;
                }
                yield break;
        }
    }

    private void AddPropertySetterExceptions(
        IPropertyReferenceOperation property,
        IOperation origin,
        HashSet<IMethodSymbol> activeMethods,
        int depth,
        Action<PotentialExceptions, IOperation> add)
    {
        var setter = property.Property.SetMethod;
        add(
            setter == null || setter.IsAbstract || setter.IsVirtual
                ? UnknownPotential
                : GetCallableExceptions(
                    setter,
                    activeMethods,
                    depth + 1),
            origin);
    }

    private PotentialExceptions GetUsingDisposalExceptions(
        IOperation operation,
        HashSet<IMethodSymbol> activeMethods,
        int depth)
    {
        if (operation is IUsingOperation { IsAsynchronous: true } or
            IUsingDeclarationOperation { IsAsynchronous: true })
        {
            return UnknownPotential;
        }
        var scopeExitReachable = operation switch
        {
            IUsingOperation @using => CanExit(@using.Body),
            IUsingDeclarationOperation declaration =>
                CanReachDeclarationDisposal(declaration),
            _ => false
        };
        var resources = operation switch
        {
            IUsingOperation @using => @using.Resources,
            IUsingDeclarationOperation declaration =>
                declaration.DeclarationGroup,
            _ => null
        };
        if (resources == null)
        {
            return EmptyPotential;
        }
        var result = EmptyPotential;
        if (resources is IVariableDeclarationGroupOperation group)
        {
            var acquired = new List<(
                ITypeSymbol Type,
                IOperation Resource,
                IOperation Origin)>();
            var acquisitionFailed = false;
            foreach (var declarator in group.Declarations
                         .SelectMany(static declaration =>
                             declaration.Declarators))
            {
                var resource = declarator.Initializer?.Value;
                if (!canCompleteNormally(resource))
                {
                    acquisitionFailed = true;
                    break;
                }
                if (resource != null)
                {
                    acquired.Add((
                        declarator.Symbol.Type,
                        resource,
                        declarator));
                }
            }
            if (!scopeExitReachable && !acquisitionFailed)
            {
                return EmptyPotential;
            }
            foreach (var item in acquired.AsEnumerable().Reverse())
            {
                var disposal = GetDisposalExceptions(
                    item.Type,
                    item.Resource,
                    item.Origin,
                    activeMethods,
                    depth);
                result = Union(result, disposal);
                if (!CanDisposalUnwind(
                        item.Type,
                        item.Resource,
                        item.Origin,
                        disposal))
                {
                    break;
                }
            }
            return result;
        }
        return scopeExitReachable
            ? GetDisposalExceptions(
                resources.Type,
                resources,
                operation,
                activeMethods,
                depth)
            : EmptyPotential;
    }

    internal bool CanExit(IOperation operation)
    {
        return canCompleteNormally(operation) ||
            CanExitAbruptly(operation, operation);
    }

    internal bool CanExitAbruptly(
        IOperation operation,
        IOperation scope)
    {
        var potential = GetPotentialExceptions(operation);
        var abrupt = potential.Unknown || !potential.Known.IsEmpty ||
            CanExitAbruptlyWithoutExceptions(operation, scope);
        return abrupt;
    }

    private bool CanExitAbruptlyWithoutExceptions(
        IOperation operation,
        IOperation scope)
    {
        return CanReachAbruptExit(operation, operation, scope, depth: 0);
    }

    private bool CanReachAbruptExit(
        IOperation operation,
        IOperation root,
        IOperation scope,
        int depth)
    {
        if (depth > 256)
        {
            return true;
        }
        if (!ReferenceEquals(operation, root) &&
            HasNestedCallableParent(operation, root))
        {
            return false;
        }
        if (operation is IReturnOperation returned)
        {
            return returned.ReturnedValue == null ||
                canCompleteNormally(returned.ReturnedValue);
        }
        if (operation is IBranchOperation branch)
        {
            return BranchLeavesScope(branch, scope);
        }
        if (operation is ITryOperation nestedTry)
        {
            var finallyAbrupt = nestedTry.Finally != null &&
                CanReachAbruptExit(
                    nestedTry.Finally,
                    root,
                    scope,
                    depth + 1);
            if (nestedTry.Finally != null &&
                !canCompleteNormally(nestedTry.Finally))
            {
                return finallyAbrupt;
            }
            if (CanReachAbruptExit(
                    nestedTry.Body,
                    root,
                    scope,
                    depth + 1))
            {
                return true;
            }
            foreach (var @catch in nestedTry.Catches)
            {
                if (@catch.Syntax is CatchClauseSyntax syntax &&
                    IsReachable(syntax, inFilter: false) &&
                    CanReachAbruptExit(
                        @catch.Handler,
                        root,
                        scope,
                        depth + 1))
                {
                    return true;
                }
            }
            return finallyAbrupt;
        }
        if (operation is IConditionalOperation conditional)
        {
            if (CanReachAbruptExit(
                    conditional.Condition,
                    root,
                    scope,
                    depth + 1))
            {
                return true;
            }
            if (!canCompleteNormally(conditional.Condition))
            {
                return false;
            }
            var condition = conditional.Condition.ConstantValue is
            { HasValue: true, Value: bool value }
                    ? value
                    : (bool?)null;
            return condition != false &&
                    CanReachAbruptExit(
                        conditional.WhenTrue,
                        root,
                        scope,
                        depth + 1) ||
                condition != true && conditional.WhenFalse != null &&
                    CanReachAbruptExit(
                        conditional.WhenFalse,
                        root,
                        scope,
                        depth + 1);
        }
        if (operation is IBinaryOperation
            {
                OperatorMethod: null,
                OperatorKind: BinaryOperatorKind.ConditionalAnd or
                    BinaryOperatorKind.ConditionalOr
            } binary)
        {
            var leftAbrupt = CanReachAbruptExit(
                binary.LeftOperand,
                root,
                scope,
                depth + 1);
            if (leftAbrupt || !canCompleteNormally(binary.LeftOperand))
            {
                return leftAbrupt;
            }
            var left = binary.LeftOperand.ConstantValue is
            { HasValue: true, Value: bool value }
                    ? value
                    : (bool?)null;
            var reachesRight = binary.OperatorKind ==
                    BinaryOperatorKind.ConditionalAnd
                ? left != false
                : left != true;
            return reachesRight && CanReachAbruptExit(
                binary.RightOperand,
                root,
                scope,
                depth + 1);
        }
        if (operation is ICoalesceOperation coalesce)
        {
            var valueAbrupt = CanReachAbruptExit(
                coalesce.Value,
                root,
                scope,
                depth + 1);
            if (valueAbrupt || !canCompleteNormally(coalesce.Value))
            {
                return valueAbrupt;
            }
            var nonNull = DefiniteOperationFacts.IsDefinitelyNonNull(
                    coalesce.Value) ||
                abstractFlow?.ProvesNonNull(
                    coalesce,
                    coalesce.Value) == true;
            return !nonNull && CanReachAbruptExit(
                coalesce.WhenNull,
                root,
                scope,
                depth + 1);
        }
        if (operation is IConditionalAccessOperation access)
        {
            var receiverAbrupt = CanReachAbruptExit(
                access.Operation,
                root,
                scope,
                depth + 1);
            if (receiverAbrupt || !canCompleteNormally(access.Operation))
            {
                return receiverAbrupt;
            }
            var isNull = DefiniteOperationFacts.IsDefinitelyNull(
                    access.Operation) ||
                abstractFlow?.ProvesNull(
                    access,
                    access.Operation) == true;
            return !isNull && CanReachAbruptExit(
                access.WhenNotNull,
                root,
                scope,
                depth + 1);
        }
        foreach (var child in operation.ChildOperations)
        {
            if (CanReachAbruptExit(child, root, scope, depth + 1))
            {
                return true;
            }
            if (!canCompleteNormally(child))
            {
                return false;
            }
        }
        return false;
    }

    private static bool BranchLeavesScope(
        IBranchOperation branch,
        IOperation scope)
    {
        SyntaxNode? target = branch.Syntax switch
        {
            BreakStatementSyntax => branch.Syntax.Ancestors().FirstOrDefault(
                static ancestor => ancestor is WhileStatementSyntax or
                    DoStatementSyntax or ForStatementSyntax or
                    CommonForEachStatementSyntax or SwitchStatementSyntax),
            ContinueStatementSyntax => branch.Syntax.Ancestors().FirstOrDefault(
                static ancestor => ancestor is WhileStatementSyntax or
                    DoStatementSyntax or ForStatementSyntax or
                    CommonForEachStatementSyntax),
            GotoStatementSyntax => branch.Target.DeclaringSyntaxReferences
                .Select(static reference => reference.GetSyntax())
                .FirstOrDefault(),
            _ => null
        };
        return target == null ||
            target.SyntaxTree != scope.Syntax.SyntaxTree ||
            !scope.Syntax.Span.Contains(target.Span);
    }

    private bool CanReachDeclarationDisposal(
        IUsingDeclarationOperation declaration)
    {
        if (declaration.Parent is not IBlockOperation block)
        {
            return true;
        }
        var index = block.Operations.IndexOf(declaration);
        if (index < 0)
        {
            return true;
        }
        var pending = new Queue<int>();
        var visited = new HashSet<int>();
        pending.Enqueue(index + 1);
        while (pending.Count != 0)
        {
            var operationIndex = pending.Dequeue();
            if (operationIndex >= block.Operations.Length)
            {
                return true;
            }
            if (!visited.Add(operationIndex))
            {
                continue;
            }
            var operation = block.Operations[operationIndex];
            var internalBranches = GetInternalGotoTargets(
                operation,
                block,
                index + 1);
            if (internalBranches.LeavesActiveLifetime)
            {
                return true;
            }
            foreach (var target in internalBranches.Targets)
            {
                pending.Enqueue(target);
            }
            if (CanExitAbruptly(operation, block))
            {
                return true;
            }
            if (operation is IUsingDeclarationOperation laterUsing &&
                !CanDisposalsCompleteNormally(laterUsing))
            {
                continue;
            }
            if ((canCompleteNormally(operation) ||
                    operation is ILabeledOperation labeled &&
                    labeled.ChildOperations.All(canCompleteNormally)) &&
                !internalBranches.HasUnconditionalGoto)
            {
                pending.Enqueue(operationIndex + 1);
            }
        }
        return false;
    }


    private bool CanDisposalsCompleteNormally(
        IUsingDeclarationOperation declaration)
    {
        return declaration.DeclarationGroup.Declarations
            .SelectMany(static item => item.Declarators)
            .Reverse()
            .All(declarator => CanDisposalCompleteNormally(
                declarator.Symbol.Type,
                declarator.Initializer?.Value,
                declarator));
    }

    private bool CanDisposalCompleteNormally(
        ITypeSymbol? resourceType,
        IOperation? resource,
        IOperation origin)
    {
        if (resourceType == null || resource == null ||
            IsDefinitelyNullResource(origin, resource))
        {
            return true;
        }
        var dispose = UsingDisposalEffectResolver.ResolveDispose(
            compilation,
            caller,
            GetConcreteResourceType(resourceType, resource));
        return dispose == null ||
            UsingDisposalEffectResolver.IsDispatchUncertain(dispose) ||
            canMethodCompleteNormally(dispose);
    }

    private bool CanDisposalUnwind(
        ITypeSymbol? resourceType,
        IOperation resource,
        IOperation origin,
        PotentialExceptions exceptions)
    {
        if (IsDefinitelyNullResource(origin, resource))
        {
            return true;
        }
        var dispose = resourceType == null
            ? null
            : UsingDisposalEffectResolver.ResolveDispose(
                compilation,
                caller,
                GetConcreteResourceType(resourceType, resource));
        return dispose == null ||
            UsingDisposalEffectResolver.IsDispatchUncertain(dispose) ||
            canMethodCompleteNormally(dispose) ||
            exceptions.Unknown || !exceptions.Known.IsEmpty;
    }

    private bool IsDefinitelyNullResource(
        IOperation origin,
        IOperation resource)
    {
        return resource.ConstantValue is { HasValue: true, Value: null } ||
            abstractFlow?.ProvesNull(origin, resource) == true;
    }

    private static ITypeSymbol GetConcreteResourceType(
        ITypeSymbol declaredType,
        IOperation resource)
    {
        resource = DefiniteOperationFacts.UnwrapHarmlessValue(resource);
        return declaredType is INamedTypeSymbol
        {
            TypeKind: TypeKind.Interface
        } &&
        resource.Type is INamedTypeSymbol
        {
            TypeKind: not TypeKind.Interface
        } concrete
            ? concrete
            : declaredType;
    }

    private static InternalGotoTargets GetInternalGotoTargets(
        IOperation operation,
        IBlockOperation scope,
        int firstActiveOperation)
    {
        var branches = operation.DescendantsAndSelf()
            .OfType<IBranchOperation>()
            .Where(branch =>
                branch.Syntax is GotoStatementSyntax)
            .ToArray();
        var allTargets = branches
            .SelectMany(static branch =>
                branch.Target.DeclaringSyntaxReferences)
            .Select(static reference => reference.GetSyntax())
            .Where(target =>
                target.SyntaxTree == scope.Syntax.SyntaxTree &&
                scope.Syntax.Span.Contains(target.Span))
            .Select(target => scope.Operations.IndexOf(
                scope.Operations.FirstOrDefault(candidate =>
                    candidate.Syntax.Span.Contains(target.Span) ||
                    candidate.Syntax.Span.IntersectsWith(target.Span) ||
                    target.Span.Contains(candidate.Syntax.Span)) ??
                scope.Operations.First(candidate =>
                    candidate.Syntax.Span.Start >= target.Span.Start)))
            .Distinct()
            .ToArray();
        return new InternalGotoTargets(
            allTargets.Where(target =>
                target >= firstActiveOperation).ToArray(),
            branches.Any(branch =>
                IsUnconditionalAtOperationLevel(branch, operation)),
            allTargets.Any(target => target < firstActiveOperation));
    }

    private static bool IsUnconditionalAtOperationLevel(
        IBranchOperation branch,
        IOperation operation)
    {
        if (ReferenceEquals(branch, operation))
        {
            return true;
        }
        for (var parent = branch.Parent;
             parent != null;
             parent = parent.Parent)
        {
            if (ReferenceEquals(parent, operation))
            {
                return true;
            }
            if (parent is not ILabeledOperation)
            {
                return false;
            }
        }
        return false;
    }

    private PotentialExceptions GetForEachExceptions(
        IForEachLoopOperation forEach,
        HashSet<IMethodSymbol> activeMethods,
        int depth,
        out bool reachesBody)
    {
        reachesBody = false;
        if (!canCompleteNormally(forEach.Collection))
        {
            return EmptyPotential;
        }
        if (forEach.IsAsynchronous)
        {
            reachesBody = true;
            return UnknownPotential;
        }
        if (forEach.Syntax is not CommonForEachStatementSyntax syntax)
        {
            reachesBody = true;
            return UnknownPotential;
        }
        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(compilation, syntax.SyntaxTree);
        var info = model.GetForEachStatementInfo(syntax);
        var result = EmptyPotential;
        if (info.GetEnumeratorMethod is { } getEnumerator)
        {
            if (!getEnumerator.IsStatic && getEnumerator.ReducedFrom == null)
            {
                result = Union(
                    result,
                    GetPotentialNullReceiver(
                        forEach,
                        forEach.Collection,
                        out var receiverCompletes));
                if (!receiverCompletes)
                {
                    return result;
                }
            }
            result = Union(
                result,
                GetImplicitCallableExceptions(
                    getEnumerator,
                    forEach,
                    activeMethods,
                    depth,
                    out var getEnumeratorCompletes));
            if (!getEnumeratorCompletes)
            {
                return result;
            }
            if (getEnumerator.ReturnType.IsReferenceType)
            {
                var returnNullability = GetReturnNullability(getEnumerator);
                if (returnNullability != ReturnNullability.NonNull &&
                    _nullReferenceExceptionType is { } nullReceiver)
                {
                    result = Union(
                        result,
                        new PotentialExceptions(
                            ImmutableHashSet.Create<INamedTypeSymbol>(
                                SymbolEqualityComparer.Default,
                                nullReceiver),
                            Unknown: false));
                }
                if (returnNullability == ReturnNullability.Null)
                {
                    return result;
                }
            }
        }
        else if (forEach.Collection.Type is IArrayTypeSymbol &&
            forEach.Collection is { } collection)
        {
            result = Union(
                result,
                GetPotentialNullReceiver(
                    forEach,
                    collection,
                    out var receiverCompletes));
            if (!receiverCompletes)
            {
                return result;
            }
        }
        if (info.MoveNextMethod is not { } moveNext)
        {
            reachesBody = true;
            return result;
        }
        var moveNextExceptions = GetImplicitCallableExceptions(
            moveNext,
            forEach,
            activeMethods,
            depth,
            out var moveNextCompletes);
        result = Union(result, moveNextExceptions);
        if (moveNextCompletes &&
            info.CurrentProperty?.GetMethod is { } getCurrent)
        {
            result = Union(
                result,
                GetImplicitCallableExceptions(
                    getCurrent,
                    forEach,
                    activeMethods,
                    depth,
                    out reachesBody));
        }
        else
        {
            reachesBody = moveNextCompletes;
        }
        if ((moveNextCompletes || moveNextExceptions.Unknown ||
             !moveNextExceptions.Known.IsEmpty) &&
            info.DisposeMethod is { } dispose)
        {
            result = Union(
                result,
                GetImplicitCallableExceptions(
                    dispose,
                    forEach,
                    activeMethods,
                    depth,
                    out _));
        }
        return result;
    }

    private PotentialExceptions GetImplicitCallableExceptions(
        IMethodSymbol method,
        IOperation origin,
        HashSet<IMethodSymbol> activeMethods,
        int depth,
        out bool completesNormally)
    {
        var result = EmptyPotential;
        var initializationCompletes = AddStaticInitializationPotential(
            method.ReducedFrom ?? method,
            origin,
            (potential, _) => result = Union(result, potential));
        completesNormally = initializationCompletes &&
            canMethodCompleteNormally(method);
        if (!initializationCompletes)
        {
            return result;
        }

        return Union(
            result,
            method.IsAbstract || method.IsVirtual ||
            method.ContainingType?.TypeKind == TypeKind.Interface
                ? UnknownPotential
                : GetCallableExceptions(
                    method,
                    activeMethods,
                    depth + 1));
    }

    private PotentialExceptions GetFormattedValueExceptions(
        IOperation operand,
        IOperation origin,
        HashSet<IMethodSymbol> activeMethods,
        int depth)
    {
        if (!StringConcatenationEffectResolver.TryResolveFormattedValueMethod(
                operand,
                origin,
                compilation,
                abstractFlow,
                out var target,
                out var dispatchUncertain))
        {
            return EmptyPotential;
        }
        if (target == null || dispatchUncertain)
        {
            return UnknownPotential;
        }

        var result = EmptyPotential;
        if (!AddStaticInitializationPotential(
                target,
                origin,
                (potential, _) => result = Union(result, potential)))
        {
            return result;
        }
        return Union(
            result,
            GetCallableExceptions(target, activeMethods, depth + 1));
    }

    private ReturnNullability GetReturnNullability(IMethodSymbol method)
    {
        method = method.OriginalDefinition;
        if (method.DeclaringSyntaxReferences.Length != 1)
        {
            return ReturnNullability.MaybeNull;
        }
        try
        {
            var declaration = method.DeclaringSyntaxReferences[0].GetSyntax();
            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(compilation, declaration.SyntaxTree);
            var directBody = GetBodyOperation(declaration, model);
            var root = model.GetOperation(declaration) ?? directBody;
            if (root == null)
            {
                return ReturnNullability.MaybeNull;
            }
            var returnedValues = root.DescendantsAndSelf()
                .OfType<IReturnOperation>()
                .Where(returned =>
                    returned.ReturnedValue != null &&
                    !ManagedAbstractFlow.IsCompileTimeUnreachable(
                        compilation,
                        returned) &&
                    !HasNestedCallableParent(returned, root))
                .Select(static returned => returned.ReturnedValue!)
                .ToArray();
            if (returnedValues.Length == 0 && directBody != null &&
                declaration is BaseMethodDeclarationSyntax
                { ExpressionBody: not null } or
                    AccessorDeclarationSyntax { ExpressionBody: not null } or
                    LocalFunctionStatementSyntax { ExpressionBody: not null })
            {
                returnedValues = [directBody];
            }
            if (returnedValues.Length == 0)
            {
                return ReturnNullability.MaybeNull;
            }
            if (returnedValues.All(
                    DefiniteOperationFacts.IsDefinitelyNonNull))
            {
                return ReturnNullability.NonNull;
            }
            return returnedValues.All(DefiniteOperationFacts.IsDefinitelyNull)
                ? ReturnNullability.Null
                : ReturnNullability.MaybeNull;
        }
        catch (ArgumentException)
        {
            return ReturnNullability.MaybeNull;
        }
    }

    private static bool HasNestedCallableParent(
        IOperation operation,
        IOperation root)
    {
        for (var parent = operation.Parent;
             parent != null && !ReferenceEquals(parent, root);
             parent = parent.Parent)
        {
            if (parent is IAnonymousFunctionOperation or
                ILocalFunctionOperation)
            {
                return true;
            }
        }
        return false;
    }

    private enum ReturnNullability
    {
        Null,
        NonNull,
        MaybeNull
    }

    private PotentialExceptions GetDisposalExceptions(
        ITypeSymbol? resourceType,
        IOperation? resource,
        IOperation origin,
        HashSet<IMethodSymbol> activeMethods,
        int depth)
    {
        if (resourceType == null || resource == null ||
            !canCompleteNormally(resource) ||
            resource.ConstantValue is { HasValue: true, Value: null } ||
            abstractFlow?.ProvesNull(origin, resource) == true)
        {
            return EmptyPotential;
        }
        var dispose = UsingDisposalEffectResolver.ResolveDispose(
            compilation,
            caller,
            GetConcreteResourceType(resourceType, resource));
        return dispose == null ||
            UsingDisposalEffectResolver.IsDispatchUncertain(dispose)
                ? UnknownPotential
                : GetCallableExceptions(
                    dispose,
                    activeMethods,
                    depth + 1);
    }

    private PotentialExceptions GetCallableExceptions(
        IMethodSymbol method,
        HashSet<IMethodSymbol> activeMethods,
        int depth)
    {
        method = method.OriginalDefinition;
        if (isKnownNonThrowing(method) ||
            method is
            {
                MethodKind: MethodKind.Constructor,
                IsImplicitlyDeclared: true
            })
        {
            return EmptyPotential;
        }
        if (depth > 32 ||
            method.DeclaringSyntaxReferences.Length != 1)
        {
            return UnknownPotential;
        }
        if (!activeMethods.Add(method))
        {
            return EmptyPotential;
        }

        try
        {
            var declaration = method.DeclaringSyntaxReferences[0].GetSyntax();
            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(compilation, declaration.SyntaxTree);
            var operation = model.GetOperation(declaration) ??
                GetBodyOperation(declaration, model);
            return operation == null
                ? UnknownPotential
                : GetPotentialExceptions(
                    operation,
                    activeMethods,
                    depth,
                    keepEscaping: true);
        }
        catch (ArgumentException)
        {
            return UnknownPotential;
        }
        finally
        {
            activeMethods.Remove(method);
        }
    }

    internal bool CanMethodThrow(IMethodSymbol method)
    {
        var potential = GetCallableExceptions(
            method,
            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
            depth: 0);
        return potential.Unknown || !potential.Known.IsEmpty;
    }

    private bool AddCompoundCallablePotential(
        IMethodSymbol? method,
        IOperation origin,
        HashSet<IMethodSymbol> activeMethods,
        int depth,
        Action<PotentialExceptions, IOperation> add)
    {
        if (method == null)
        {
            return true;
        }
        if (!AddStaticInitializationPotential(method, origin, add))
        {
            return false;
        }

        add(GetOperatorExceptions(method, activeMethods, depth), origin);
        return canMethodCompleteNormally(method);
    }

    private PotentialExceptions GetOperatorExceptions(
        IMethodSymbol method,
        HashSet<IMethodSymbol> activeMethods,
        int depth)
    {
        return method.IsAbstract || method.IsVirtual
            ? UnknownPotential
            : GetCallableExceptions(method, activeMethods, depth + 1);
    }

    private PotentialExceptions KeepEscaping(
        PotentialExceptions potential,
        IOperation origin)
    {
        if (potential.Known.IsEmpty && !potential.Unknown)
        {
            return potential;
        }
        var summary = EffectExceptionFlow.KeepEscaping(
            EffectSummaryOperations.Throw(
                EffectThrowSet.Create(
                    potential.Known,
                    potential.Unknown)),
            origin,
            compilation);
        return FromThrowSet(summary.Throws);
    }

    private static PotentialExceptions FromThrowSet(EffectThrowSet throws)
    {
        return new PotentialExceptions(
            throws.Types.ToImmutableHashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default),
            throws.IncludesUnknown);
    }

    private static PotentialExceptions Union(
        PotentialExceptions left,
        PotentialExceptions right)
    {
        return new PotentialExceptions(
            left.Known.Union(right.Known),
            left.Unknown || right.Unknown);
    }

    private static IOperation? GetBodyOperation(
        SyntaxNode declaration,
        SemanticModel model)
    {
        var body = declaration switch
        {
            BaseMethodDeclarationSyntax method =>
                (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression,
            AccessorDeclarationSyntax accessor =>
                (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression,
            LocalFunctionStatementSyntax local =>
                (SyntaxNode?)local.Body ?? local.ExpressionBody?.Expression,
            _ => null
        };
        return body == null ? null : model.GetOperation(body);
    }

    private static PotentialExceptions EmptyPotential =>
        new(
            ImmutableHashSet.Create<INamedTypeSymbol>(
                SymbolEqualityComparer.Default),
            Unknown: false);

    private bool IsExceptionType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol named &&
            _exceptionType is { } exception &&
            EffectTypeFacts.IsDerivedFrom(named, exception);
    }

    private static PotentialExceptions UnknownPotential =>
        new(
            ImmutableHashSet.Create<INamedTypeSymbol>(
                SymbolEqualityComparer.Default),
            Unknown: true);

    private static bool CanThrowUnknown(IOperation operation)
    {
        return operation is
            IInvocationOperation or
            IDynamicInvocationOperation or
            IFunctionPointerInvocationOperation or
            IObjectCreationOperation or
            IArrayCreationOperation or
            IArrayElementReferenceOperation or
            IPropertyReferenceOperation or
            ILockOperation or
            IConversionOperation
            { IsChecked: true, OperatorMethod: null } or
            ICompoundAssignmentOperation
            {
                IsChecked: true,
                OperatorMethod: null
            } or
            ICompoundAssignmentOperation
            {
                OperatorMethod: null,
                OperatorKind: BinaryOperatorKind.Divide or
                    BinaryOperatorKind.Remainder
            } or
            IBinaryOperation
            {
                IsChecked: true,
                OperatorMethod: null
            } or
            IBinaryOperation
            {
                OperatorMethod: null,
                OperatorKind: BinaryOperatorKind.Divide or
                    BinaryOperatorKind.Remainder
            } or
            IUnaryOperation
            { IsChecked: true, OperatorMethod: null } or
            IIncrementOrDecrementOperation
            { IsChecked: true, OperatorMethod: null };
    }

    private bool CanThrowUnknownAfterPrerequisites(IOperation operation)
    {
        if (!CanThrowUnknown(operation))
        {
            return false;
        }
        return operation switch
        {
            IConversionOperation conversion =>
                canCompleteNormally(conversion.Operand),
            IBinaryOperation binary =>
                canCompleteNormally(binary.LeftOperand) &&
                canCompleteNormally(binary.RightOperand),
            IIncrementOrDecrementOperation increment =>
                canCompleteNormally(increment.Target),
            IArrayCreationOperation array => array.DimensionSizes.All(
                canCompleteNormally),
            IArrayElementReferenceOperation element =>
                canCompleteNormally(element.ArrayReference) &&
                element.Indices.All(canCompleteNormally),
            ILockOperation @lock =>
                canCompleteNormally(@lock.LockedValue),
            _ => operation.ChildOperations.All(canCompleteNormally)
        };
    }

    private bool CanKnownReach(
        INamedTypeSymbol thrown,
        CatchClauseSyntax target,
        TryStatementSyntax @try,
        SemanticModel model)
    {
        foreach (var @catch in @try.Catches)
        {
            if (!CatchesKnownType(@catch, thrown, model))
            {
                continue;
            }
            if (@catch.Span == target.Span)
            {
                return true;
            }
            if (GetFilterSelection(@catch, model) == CatchSelection.Always)
            {
                return false;
            }
        }
        return false;
    }

    private bool CanUnknownReach(
        CatchClauseSyntax target,
        TryStatementSyntax @try,
        SemanticModel model)
    {
        foreach (var @catch in @try.Catches)
        {
            if (@catch.Span == target.Span)
            {
                return true;
            }
            if (CatchesAllExceptions(@catch, model) &&
                GetFilterSelection(@catch, model) == CatchSelection.Always)
            {
                return false;
            }
        }
        return false;
    }

    private static bool CatchesKnownType(
        CatchClauseSyntax @catch,
        INamedTypeSymbol thrown,
        SemanticModel model)
    {
        if (@catch.Declaration == null)
        {
            return true;
        }
        return model.GetTypeInfo(@catch.Declaration.Type).Type is
            INamedTypeSymbol caught &&
            EffectTypeFacts.IsDerivedFrom(thrown, caught);
    }

    private bool CatchesAllExceptions(
        CatchClauseSyntax @catch,
        SemanticModel model)
    {
        return @catch.Declaration == null ||
            _exceptionType != null &&
            SymbolEqualityComparer.Default.Equals(
                model.GetTypeInfo(@catch.Declaration.Type).Type,
                _exceptionType);
    }

    private CatchSelection GetFilterSelection(
        CatchClauseSyntax @catch,
        SemanticModel model)
    {
        if (@catch.Filter == null)
        {
            return CatchSelection.Always;
        }
        var selection = model.GetConstantValue(
            @catch.Filter.FilterExpression) switch
        {
            { HasValue: true, Value: true } => CatchSelection.Always,
            { HasValue: true, Value: false } => CatchSelection.Never,
            _ => CatchSelection.Maybe
        };
        if (selection != CatchSelection.Maybe)
        {
            return selection;
        }
        var operation = model.GetOperation(@catch.Filter.FilterExpression);
        return operation != null && !canCompleteNormally(operation)
            ? CatchSelection.Never
            : CatchSelection.Maybe;
    }

    private readonly record struct CatchReachability(
        bool Filter,
        bool Handler);

    private readonly record struct PotentialExceptions(
        ImmutableHashSet<INamedTypeSymbol> Known,
        bool Unknown);

    private sealed record InternalGotoTargets(
        IReadOnlyList<int> Targets,
        bool HasUnconditionalGoto,
        bool LeavesActiveLifetime);

    private enum CatchSelection
    {
        Never,
        Maybe,
        Always
    }

    private enum SwitchSelection
    {
        Never,
        Maybe,
        Always
    }

    private sealed record SwitchCaseReachability(
        ISwitchCaseOperation Case,
        IReadOnlyList<ICaseClauseOperation> Clauses,
        bool BodyReachable);
}
