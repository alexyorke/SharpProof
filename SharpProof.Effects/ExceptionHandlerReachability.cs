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
    ResolvedApiSpecTable apiSpecs,
    Func<IMethodSymbol, bool> isKnownNonThrowing)
{
    private readonly Dictionary<CatchClauseSyntax, CatchReachability> _cache = new();
    private readonly INamedTypeSymbol? _exceptionType =
        compilation.GetTypeByMetadataName(FrameworkTypeMetadataNames.Exception);
    private readonly INamedTypeSymbol? _nullReferenceExceptionType =
        compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.NullReferenceException);
    private readonly INamedTypeSymbol? _argumentNullExceptionType =
        compilation.GetTypeByMetadataName("System.ArgumentNullException");
    private readonly INamedTypeSymbol? _typeInitializationExceptionType =
        compilation.GetTypeByMetadataName("System.TypeInitializationException");
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
        var switchCaseReachability = new Dictionary<
            ISwitchCaseOperation,
            SwitchCaseReachability>();
        remaining.Push(root);
        while (remaining.Count != 0)
        {
            var operation = remaining.Pop();
            if (ManagedAbstractFlow.IsCompileTimeUnreachable(
                    compilation,
                    operation))
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
                    if (scheduledGotoLabels.Add(branch.Target))
                    {
                        PushSequential(continuation);
                    }
                    continue;
                }
            }
            if (operation is IThrowOperation thrown)
            {
                if (thrown.Exception is not { } exception)
                {
                    Add(
                        FromThrowSet(
                            EffectExceptionFlow.ResolveRethrow(thrown)),
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
                var eventReference = eventAssignment.EventReference;
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
                var priorPhasesComplete =
                    canCompleteNormally(compound.Target) &&
                    canCompleteNormally(compound.Value);
                var operatorInitializationCompletes = true;
                if (priorPhasesComplete &&
                    compound.OperatorMethod is { } compoundOperator)
                {
                    operatorInitializationCompletes =
                        AddStaticInitializationPotential(
                            compoundOperator,
                            compound,
                            Add);
                    if (operatorInitializationCompletes)
                    {
                        Add(
                            GetOperatorExceptions(
                                compoundOperator,
                                activeMethods,
                                depth),
                            compound);
                    }
                }
                if (CanThrowUnknownAfterPrerequisites(compound))
                {
                    Add(UnknownPotential, compound);
                }
                var operatorCompletes =
                    operatorInitializationCompletes &&
                    canCompoundValueComplete(compound);
                if (priorPhasesComplete && operatorCompletes &&
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
                    if (creation.Constructor is { } constructor)
                    {
                        initializationCompletes =
                            AddStaticInitializationPotential(
                            constructor,
                            creation,
                            Add);
                    }
                    if (initializationCompletes)
                    {
                        Add(
                            creation.Constructor == null
                                ? UnknownPotential
                                : GetCallableExceptions(
                                    creation.Constructor,
                                    activeMethods,
                                    depth + 1),
                            creation);
                    }
                }
                PushChildren(creation);
                continue;
            }
            if (operation is IBinaryOperation binary &&
                binary.OperatorMethod is { } binaryOperator)
            {
                if (canCompleteNormally(binary.LeftOperand) &&
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
            if (operation is IPropertyReferenceOperation property)
            {
                if (property.Parent is ISimpleAssignmentOperation simple &&
                    ReferenceEquals(simple.Target, property))
                {
                    PushChildren(property);
                    continue;
                }
                var prerequisitesComplete =
                    property.Instance is not { } receiver ||
                    canCompleteNormally(receiver);
                prerequisitesComplete &= property.Arguments.All(argument =>
                    canCompleteNormally(argument.Value));
                var dereferenceCompletes = prerequisitesComplete;
                if (prerequisitesComplete &&
                    property.Instance is { } instance)
                {
                    Add(
                        GetPotentialNullReceiver(
                            property,
                            instance,
                            out dereferenceCompletes),
                        property);
                }
                if (dereferenceCompletes)
                {
                    var accessors = GetAccessors(property).ToArray();
                    var initializationCompletes = true;
                    if (accessors.Length != 0)
                    {
                        initializationCompletes =
                            AddStaticInitializationPotential(
                            property.Property,
                            property,
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
                                    : GetCallableExceptions(
                                        accessor,
                                        activeMethods,
                                        depth + 1),
                                property);
                        }
                    }
                }
                PushChildren(property);
                continue;
            }
            if (operation is IFieldReferenceOperation field)
            {
                if (field.Instance is { } fieldInstance)
                {
                    Add(
                        GetPotentialNullReceiver(
                            field,
                            fieldInstance,
                            out _),
                        field);
                }
                else
                {
                    if (field.Parent is not ISimpleAssignmentOperation simple ||
                        !ReferenceEquals(simple.Target, field))
                    {
                        AddStaticInitializationPotential(
                            field.Field,
                            field,
                            Add);
                    }
                }
                PushChildren(field);
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
            switch (operation)
            {
                case INameOfOperation or ITypeOfOperation or
                    ISizeOfOperation:
                    return;
                case IBlockOperation block:
                    PushSequential(block.Operations);
                    return;
                case ISimpleAssignmentOperation assignment:
                    var inputs = GetSimpleAssignmentTargetInputs(
                        assignment.Target).ToArray();
                    if (inputs.All(canCompleteNormally))
                    {
                        remaining.Push(assignment.Value);
                    }
                    PushSequential(inputs);
                    return;
                case IBinaryOperation
                    {
                        OperatorMethod: null,
                        OperatorKind: BinaryOperatorKind.ConditionalAnd or
                            BinaryOperatorKind.ConditionalOr
                    } binary:
                    var leftCompletes = canCompleteNormally(
                        binary.LeftOperand);
                    var leftConstant = binary.LeftOperand.ConstantValue is
                        { HasValue: true, Value: bool leftValue }
                            ? leftValue
                            : (bool?)null;
                    var evaluatesRight = leftCompletes &&
                        (binary.OperatorKind ==
                            BinaryOperatorKind.ConditionalAnd
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
                    var targetCompletes = canCompleteNormally(
                        coalesce.Target);
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
                    PushSequential(creation.Arguments);
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
                        if (@switch.Value.ConstantValue is
                            { HasValue: true } constant)
                        {
                            PushAll(GetReachableSwitchCases(
                                @switch,
                                constant.Value,
                                scheduledSwitchBodies,
                                switchCaseReachability));
                        }
                        else
                        {
                            PushAll(@switch.Cases);
                        }
                    }
                    remaining.Push(@switch.Value);
                    return;
                case ISwitchCaseOperation @case
                    when switchCaseReachability.TryGetValue(
                        @case,
                        out var reachability):
                    if (reachability.BodyReachable)
                    {
                        PushSequential(@case.Body);
                    }
                    PushAll(reachability.Clauses);
                    return;
                case ISwitchExpressionOperation @switch:
                    if (canCompleteNormally(@switch.Value))
                    {
                        if (@switch.Value.ConstantValue is
                            { HasValue: true } constant)
                        {
                            var reachableArms = new List<
                                ISwitchExpressionArmOperation>();
                            foreach (var arm in @switch.Arms)
                            {
                                var pattern = GetPatternSelection(
                                    arm.Pattern,
                                    constant.Value);
                                var selection = GetSwitchArmSelection(
                                    arm,
                                    constant.Value);
                                if (selection != SwitchSelection.Never)
                                {
                                    reachableArms.Add(arm);
                                }
                                if (selection == SwitchSelection.Always ||
                                    pattern == SwitchSelection.Always &&
                                    arm.Guard != null &&
                                    !canCompleteNormally(arm.Guard))
                                {
                                    break;
                                }
                            }
                            PushAll(reachableArms);
                        }
                        else
                        {
                            PushAll(@switch.Arms);
                        }
                    }
                    remaining.Push(@switch.Value);
                    return;
                default:
                    PushSequential(operation.ChildOperations);
                    return;
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
    }

    private static SwitchSelection GetSwitchArmSelection(
        ISwitchExpressionArmOperation arm,
        object? value)
    {
        var pattern = GetPatternSelection(arm.Pattern, value);
        if (pattern == SwitchSelection.Never || arm.Guard == null)
        {
            return pattern;
        }
        return arm.Guard.ConstantValue is { HasValue: true, Value: bool guard }
            ? guard
                ? pattern
                : SwitchSelection.Never
            : SwitchSelection.Maybe;
    }

    private IReadOnlyList<ISwitchCaseOperation> GetReachableSwitchCases(
        ISwitchOperation @switch,
        object? value,
        HashSet<ISwitchCaseOperation> scheduledSwitchBodies,
        Dictionary<ISwitchCaseOperation, SwitchCaseReachability>
            switchCaseReachability)
    {
        var selected = new Dictionary<
            ISwitchCaseOperation,
            SwitchCaseReachability>();
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
                        ? GetPatternSelection(patternClause.Pattern, value)
                        : SwitchSelection.Never;
                var clauseSelection = clause switch
                {
                    ISingleValueCaseClauseOperation single
                        when single.Value.ConstantValue is
                            { HasValue: true } item =>
                        Equals(value, item.Value)
                            ? SwitchSelection.Always
                            : SwitchSelection.Never,
                    IPatternCaseClauseOperation pattern =>
                        ApplySwitchGuard(
                            GetPatternSelection(pattern.Pattern, value),
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

    private IReadOnlyList<IOperation>? GetGotoTargetContinuation(
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
                : block.Operations.Skip(index).ToArray();
        }
        if (sequenceEntry.Parent is ISwitchCaseOperation @case)
        {
            var index = @case.Body.IndexOf(sequenceEntry);
            return index < 0
                ? null
                : @case.Body.Skip(index).ToArray();
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
        if (clause is not IPatternCaseClauseOperation pattern ||
            pattern.Guard == null)
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
        object? value)
    {
        return pattern switch
        {
            IDiscardPatternOperation => SwitchSelection.Always,
            IConstantPatternOperation constant
                when constant.Value.ConstantValue is { HasValue: true } item =>
                Equals(value, item.Value)
                    ? SwitchSelection.Always
                    : SwitchSelection.Never,
            _ => SwitchSelection.Maybe
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
        if ((!member.IsStatic && member is not IMethodSymbol
                { MethodKind: MethodKind.Constructor }) ||
            member is IFieldSymbol { IsConst: true } ||
            SymbolEqualityComparer.Default.Equals(
                caller.ContainingType,
                member.ContainingType) ||
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
        return potential.Unknown || !potential.Known.IsEmpty ||
            CanExitAbruptlyWithoutExceptions(operation, scope);
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
            if (canCompleteNormally(operation) &&
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
            resourceType);
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
                resourceType);
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

    private InternalGotoTargets GetInternalGotoTargets(
        IOperation operation,
        IBlockOperation scope,
        int firstActiveOperation)
    {
        var branches = operation.DescendantsAndSelf()
            .OfType<IBranchOperation>()
            .Where(branch =>
                branch.Syntax is GotoStatementSyntax &&
                (abstractFlow == null || abstractFlow.IsReachable(branch)))
            .ToArray();
        var allTargets = branches
            .SelectMany(static branch =>
                branch.Target.DeclaringSyntaxReferences)
            .Select(static reference => reference.GetSyntax())
            .Where(target =>
                target.SyntaxTree == scope.Syntax.SyntaxTree &&
                scope.Syntax.Span.Contains(target.Span))
            .Select(target => scope.Operations.IndexOf(
                scope.Operations.First(candidate =>
                    candidate.Syntax.Span.Contains(target.Span))))
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
            resourceType);
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
        if (isKnownNonThrowing(method))
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
            throws.Types.ToImmutableHashSet(SymbolEqualityComparer.Default),
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
