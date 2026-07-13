using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Analyzer.Engine.Rules;
using PurityAnalysisState = SharpProof.Analyzer.Engine.PurityAnalysisEngine.PurityAnalysisState;

namespace SharpProof.Analyzer.Engine;

internal static class PurityConcreteReceiverResolver
{
    internal static bool IsTrustedFreshArrayFactoryOperation(
        IOperation? operation,
        Compilation compilation,
        out IMethodSymbol factoryMethod)
    {
        return IsTrustedArrayFactoryOperation(
            operation,
            compilation,
            PurityAnalysisEngine.IsTrustedGeneratedFreshOwnedArrayReturningMember,
            out factoryMethod);
    }

    internal static bool IsTrustedNonEscapingArrayFactoryOperation(
        IOperation? operation,
        Compilation compilation,
        out IMethodSymbol factoryMethod)
    {
        return IsTrustedArrayFactoryOperation(
            operation,
            compilation,
            PurityAnalysisEngine.IsTrustedGeneratedNonEscapingArrayReturningMember,
            out factoryMethod);
    }

    private static bool IsTrustedArrayFactoryOperation(
        IOperation? operation,
        Compilation compilation,
        Func<IMethodSymbol, Compilation, bool> isTrustedFactory,
        out IMethodSymbol factoryMethod)
    {
        var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
        if (unwrappedOperation is IInvocationOperation invocation &&
            invocation.Type is IArrayTypeSymbol &&
            isTrustedFactory(invocation.TargetMethod.OriginalDefinition, compilation))
        {
            factoryMethod = invocation.TargetMethod;
            return true;
        }

        factoryMethod = null!;
        return false;
    }

    internal static bool IsArrayCollectionExpressionOperation(IOperation? operation)
    {
        var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
        return unwrappedOperation is ICollectionExpressionOperation collectionExpression &&
               collectionExpression.Type is IArrayTypeSymbol;
    }

    internal static bool TryResolveKnownConcreteType(
        IOperation? operation,
        PurityAnalysisState currentState,
        Compilation? compilation,
        out INamedTypeSymbol concreteType)
    {
        operation = UnwrapConversionsAndParentheses(operation);

        if (operation != null &&
            TryResolveKnownSystemTypeRuntimeReceiver(operation, compilation, out concreteType))
            return true;

        if (operation is IObjectCreationOperation objectCreationOperation &&
            objectCreationOperation.Type is INamedTypeSymbol createdType &&
            createdType.TypeKind is TypeKind.Class or TypeKind.Struct)
        {
            concreteType = createdType;
            return true;
        }

        if (operation is ILocalReferenceOperation localReference &&
            currentState.TryGetLocalConcreteType(localReference.Local, out concreteType))
            return true;

        if (operation is IFlowCaptureReferenceOperation flowCaptureReference &&
            currentState.TryGetFlowCaptureConcreteType(flowCaptureReference.Id, out concreteType))
            return true;

        if (PurityAnalysisEngine.TryResolveTrackedSymbol(operation, currentState) is ILocalSymbol capturedLocalSymbol &&
            currentState.TryGetLocalConcreteType(capturedLocalSymbol, out concreteType))
            return true;

        if (operation is IConditionalOperation conditionalOperation &&
            TryResolveCommonConcreteType(
                conditionalOperation.WhenTrue,
                conditionalOperation.WhenFalse,
                currentState,
                compilation,
                out concreteType))
            return true;

        if (operation is ICoalesceOperation coalesceOperation &&
            TryResolveCommonConcreteType(
                coalesceOperation.Value,
                coalesceOperation.WhenNull,
                currentState,
                compilation,
                out concreteType))
            return true;

        concreteType = null!;
        return false;
    }

    private static bool TryResolveCommonConcreteType(
        IOperation? first,
        IOperation? second,
        PurityAnalysisState currentState,
        Compilation? compilation,
        out INamedTypeSymbol concreteType)
    {
        if (TryResolveKnownConcreteType(first, currentState, compilation, out var firstType) &&
            TryResolveKnownConcreteType(second, currentState, compilation, out var secondType) &&
            SymbolEqualityComparer.Default.Equals(firstType, secondType))
        {
            concreteType = firstType;
            return true;
        }

        concreteType = null!;
        return false;
    }

    internal static bool TryResolveKnownSystemTypeRuntimeReceiver(
        IOperation operation,
        Compilation? compilation,
        out INamedTypeSymbol concreteType)
    {
        concreteType = null!;

        if (operation is ITypeOfOperation)
            return TryGetRuntimeTypeSymbol(operation.Type, compilation, out concreteType);

        if (operation is not IInvocationOperation invocationOperation ||
            invocationOperation.TargetMethod is not { } targetMethod)
            return false;

        if (IsObjectGetTypeMethod(targetMethod) || IsTypeGetTypeFromHandleMethod(targetMethod))
            return TryGetRuntimeTypeSymbol(invocationOperation.Type, compilation, out concreteType);

        return false;
    }

    internal static bool IsKnownSystemTypeRuntimeReceiver(IOperation? operation)
    {
        operation = UnwrapConversionsAndParentheses(operation);

        if (operation == null) return false;

        return operation is ITypeOfOperation ||
               (operation is IInvocationOperation invocationOperation &&
                invocationOperation.TargetMethod is { } targetMethod &&
                (IsObjectGetTypeMethod(targetMethod) || IsTypeGetTypeFromHandleMethod(targetMethod)));
    }

    internal static bool TryGetRuntimeTypeSymbol(
        ITypeSymbol? typeSymbol,
        Compilation? compilation,
        out INamedTypeSymbol concreteType)
    {
        concreteType = null!;

        if (typeSymbol == null || !IsSystemTypeSymbol(typeSymbol)) return false;

        if (compilation?.GetTypeByMetadataName("System.RuntimeType") is INamedTypeSymbol runtimeTypeFromCompilation)
        {
            concreteType = runtimeTypeFromCompilation;
            return true;
        }

        var containingAssembly = typeSymbol.ContainingAssembly;
        if (containingAssembly == null ||
            containingAssembly.GetTypeByMetadataName("System.RuntimeType") is not INamedTypeSymbol runtimeType)
            return false;

        concreteType = runtimeType;
        return true;
    }

    internal static IMethodSymbol? ResolveMethodTargetForConcreteReceiver(
        IMethodSymbol targetMethod,
        INamedTypeSymbol exactReceiverType)
    {
        var originalTarget = targetMethod.OriginalDefinition;
        if (targetMethod.ContainingType?.TypeKind == TypeKind.Interface)
        {
            var interfaceImplementation =
                exactReceiverType.FindImplementationForInterfaceMember(targetMethod) as IMethodSymbol
                ?? exactReceiverType.FindImplementationForInterfaceMember(originalTarget) as IMethodSymbol;
            if (interfaceImplementation != null) return interfaceImplementation;

            return !originalTarget.IsAbstract ||
                   TypeHierarchyEnumeration.HasMethodBody(originalTarget, CancellationToken.None)
                ? originalTarget
                : null;
        }

        if (!(originalTarget.IsVirtual || originalTarget.IsAbstract || originalTarget.IsOverride))
            return originalTarget;

        for (var type = exactReceiverType; type != null; type = type.BaseType)
            foreach (var member in type.GetMembers())
                if (member is IMethodSymbol method &&
                    (SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, originalTarget) ||
                     TypeHierarchyEnumeration.OverridesTargetMethod(method, originalTarget) ||
                     TypeHierarchyEnumeration.ExplicitlyImplements(method, originalTarget)))
                    return method;

        return !originalTarget.IsAbstract
            ? originalTarget
            : null;
    }

    internal static IMethodSymbol? ResolvePropertyAccessorTargetForConcreteReceiver(
        IPropertySymbol propertySymbol,
        INamedTypeSymbol exactReceiverType,
        bool preferSetter)
    {
        if (propertySymbol.ContainingType?.TypeKind == TypeKind.Interface)
        {
            var implementation = exactReceiverType.FindImplementationForInterfaceMember(propertySymbol) ??
                                 (preferSetter
                                     ? propertySymbol.SetMethod == null
                                         ? null
                                         : exactReceiverType.FindImplementationForInterfaceMember(propertySymbol
                                             .SetMethod)
                                     : propertySymbol.GetMethod == null
                                         ? null
                                         : exactReceiverType.FindImplementationForInterfaceMember(propertySymbol
                                             .GetMethod));
            return GetAccessorFromImplementation(implementation, preferSetter);
        }

        for (var current = exactReceiverType; current != null; current = current.BaseType)
        {
            var implementation = current
                .GetMembers(propertySymbol.Name)
                .OfType<IPropertySymbol>()
                .FirstOrDefault(property =>
                    SymbolEqualityComparer.Default.Equals(property.OriginalDefinition,
                        propertySymbol.OriginalDefinition) ||
                    DispatchedMemberResolution.OverridesProperty(property, propertySymbol));
            if (implementation == null) continue;

            return preferSetter ? implementation.SetMethod : implementation.GetMethod;
        }

        return preferSetter ? propertySymbol.SetMethod : propertySymbol.GetMethod;
    }

    private static bool IsObjectGetTypeMethod(IMethodSymbol methodSymbol)
    {
        return !methodSymbol.IsStatic &&
               methodSymbol.Parameters.Length == 0 &&
               methodSymbol.Name == nameof(GetType) &&
               methodSymbol.ContainingType?.SpecialType == SpecialType.System_Object;
    }

    private static bool IsTypeGetTypeFromHandleMethod(IMethodSymbol methodSymbol)
    {
        return methodSymbol.IsStatic &&
               methodSymbol.Parameters.Length == 1 &&
               methodSymbol.Name == nameof(Type.GetTypeFromHandle) &&
               IsSystemTypeSymbol(methodSymbol.ContainingType) &&
               methodSymbol.Parameters[0].Type.SpecialType == SpecialType.System_RuntimeTypeHandle;
    }

    private static bool IsSystemTypeSymbol(ITypeSymbol? typeSymbol)
    {
        return typeSymbol != null &&
               string.Equals(typeSymbol.ToDisplayString(), "System.Type", StringComparison.Ordinal);
    }

    private static IMethodSymbol? GetAccessorFromImplementation(ISymbol? implementation, bool preferSetter)
    {
        if (implementation is IPropertySymbol propertyImplementation)
            return preferSetter ? propertyImplementation.SetMethod : propertyImplementation.GetMethod;

        return implementation as IMethodSymbol;
    }

    internal static bool IsDefinitelyNullValue(
        IOperation? valueOperation,
        PurityAnalysisState currentState)
    {
        valueOperation = UnwrapConversionsAndParentheses(valueOperation);

        if (valueOperation is ILiteralOperation literalOperation &&
            literalOperation.ConstantValue.HasValue &&
            literalOperation.ConstantValue.Value == null)
            return true;

        if (valueOperation is IDefaultValueOperation defaultValueOperation &&
            defaultValueOperation.Type?.IsReferenceType == true)
            return true;

        if (valueOperation is ILocalReferenceOperation localReference)
            return currentState.IsDefinitelyNullLocalSymbol(localReference.Local);

        if (PurityAnalysisEngine.TryResolveTrackedSymbol(valueOperation, currentState) is ILocalSymbol capturedLocal)
            return currentState.IsDefinitelyNullLocalSymbol(capturedLocal);

        return false;
    }

    private static IOperation? UnwrapConversionsAndParentheses(IOperation? operation)
    {
        while (true)
        {
            operation = PurityAnalysisEngine.SkipImplicitConversions(operation);
            switch (operation)
            {
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }

    internal static bool IsArrayEmptyFactory(IMethodSymbol methodSymbol)
    {
        return methodSymbol.Name == "Empty" &&
               methodSymbol.Parameters.Length == 0 &&
               methodSymbol.ContainingType?.SpecialType == SpecialType.System_Array;
    }
}
