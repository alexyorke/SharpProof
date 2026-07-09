using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.FlowAnalysis;
using System.Collections.Immutable;
using System;
using System.IO;
using System.Globalization;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using SearchLib.Smt;
using System.Threading;

namespace SharpProof.Analyzer.Engine
{

    internal partial class PurityAnalysisEngine
    {
        internal static bool IsTrustedFreshArrayFactoryOperation(
            IOperation? operation,
            Compilation compilation,
            out IMethodSymbol factoryMethod)
        {
            var unwrappedOperation = SkipImplicitConversions(operation);
            if (unwrappedOperation is IInvocationOperation invocation &&
                invocation.Type is IArrayTypeSymbol &&
                IsTrustedGeneratedFreshOwnedArrayReturningMember(
                    invocation.TargetMethod.OriginalDefinition,
                    compilation))
            {
                factoryMethod = invocation.TargetMethod;
                return true;
            }

            factoryMethod = null!;
            return false;
        }

        internal static bool IsTrustedNonEscapingArrayFactoryOperation(
            IOperation? operation,
            Compilation compilation,
            out IMethodSymbol factoryMethod)
        {
            var unwrappedOperation = SkipImplicitConversions(operation);
            if (unwrappedOperation is IInvocationOperation invocation &&
                invocation.Type is IArrayTypeSymbol &&
                IsTrustedGeneratedNonEscapingArrayReturningMember(
                    invocation.TargetMethod.OriginalDefinition,
                    compilation))
            {
                factoryMethod = invocation.TargetMethod;
                return true;
            }

            factoryMethod = null!;
            return false;
        }

        internal static bool IsArrayCollectionExpressionOperation(IOperation? operation)
        {
            var unwrappedOperation = SkipImplicitConversions(operation);
            return unwrappedOperation is ICollectionExpressionOperation collectionExpression &&
                collectionExpression.Type is IArrayTypeSymbol;
        }

        internal static bool TryResolveKnownConcreteType(
            IOperation? operation,
            PurityAnalysisState currentState,
            Compilation? compilation,
            out INamedTypeSymbol concreteType)
        {
            operation = SkipImplicitConversions(operation);

            while (operation is IParenthesizedOperation parenthesizedOperation)
            {
                operation = SkipImplicitConversions(parenthesizedOperation.Operand);
            }

            if (operation is IConversionOperation conversionOperation)
            {
                return TryResolveKnownConcreteType(conversionOperation.Operand, currentState, compilation, out concreteType);
            }

            if (operation != null &&
                TryResolveKnownSystemTypeRuntimeReceiver(operation, compilation, out concreteType))
            {
                return true;
            }

            if (operation is IObjectCreationOperation objectCreationOperation &&
                objectCreationOperation.Type is INamedTypeSymbol createdType &&
                createdType.TypeKind is TypeKind.Class or TypeKind.Struct)
            {
                concreteType = createdType;
                return true;
            }

            if (operation is ILocalReferenceOperation localReference &&
                currentState.TryGetLocalConcreteType(localReference.Local, out concreteType))
            {
                return true;
            }

            if (operation is IFlowCaptureReferenceOperation flowCaptureReference &&
                currentState.TryGetFlowCaptureConcreteType(flowCaptureReference.Id, out concreteType))
            {
                return true;
            }

            if (TryResolveTrackedSymbol(operation, currentState) is ILocalSymbol capturedLocalSymbol &&
                currentState.TryGetLocalConcreteType(capturedLocalSymbol, out concreteType))
            {
                return true;
            }

            if (operation is IConditionalOperation conditionalOperation &&
                TryResolveKnownConcreteType(conditionalOperation.WhenTrue, currentState, compilation, out var whenTrueType) &&
                TryResolveKnownConcreteType(conditionalOperation.WhenFalse, currentState, compilation, out var whenFalseType) &&
                SymbolEqualityComparer.Default.Equals(whenTrueType, whenFalseType))
            {
                concreteType = whenTrueType;
                return true;
            }

            if (operation is ICoalesceOperation coalesceOperation &&
                TryResolveKnownConcreteType(coalesceOperation.Value, currentState, compilation, out var coalesceValueType) &&
                TryResolveKnownConcreteType(coalesceOperation.WhenNull, currentState, compilation, out var coalesceWhenNullType) &&
                SymbolEqualityComparer.Default.Equals(coalesceValueType, coalesceWhenNullType))
            {
                concreteType = coalesceValueType;
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
            {
                return TryGetRuntimeTypeSymbol(operation.Type, compilation, out concreteType);
            }

            if (operation is not IInvocationOperation invocationOperation ||
                invocationOperation.TargetMethod is not { } targetMethod)
            {
                return false;
            }

            if (IsObjectGetTypeMethod(targetMethod) || IsTypeGetTypeFromHandleMethod(targetMethod))
            {
                return TryGetRuntimeTypeSymbol(invocationOperation.Type, compilation, out concreteType);
            }

            return false;
        }

        internal static bool IsKnownSystemTypeRuntimeReceiver(IOperation? operation)
        {
            operation = SkipImplicitConversions(operation);

            while (operation is IParenthesizedOperation parenthesizedOperation)
            {
                operation = SkipImplicitConversions(parenthesizedOperation.Operand);
            }

            if (operation is IConversionOperation conversionOperation)
            {
                return IsKnownSystemTypeRuntimeReceiver(conversionOperation.Operand);
            }

            if (operation == null)
            {
                return false;
            }

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

            if (typeSymbol == null || !IsSystemTypeSymbol(typeSymbol))
            {
                return false;
            }

            if (compilation?.GetTypeByMetadataName("System.RuntimeType") is INamedTypeSymbol runtimeTypeFromCompilation)
            {
                concreteType = runtimeTypeFromCompilation;
                return true;
            }

            var containingAssembly = typeSymbol.ContainingAssembly;
            if (containingAssembly == null ||
                containingAssembly.GetTypeByMetadataName("System.RuntimeType") is not INamedTypeSymbol runtimeType)
            {
                return false;
            }

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
                var interfaceImplementation = exactReceiverType.FindImplementationForInterfaceMember(targetMethod) as IMethodSymbol
                    ?? exactReceiverType.FindImplementationForInterfaceMember(originalTarget) as IMethodSymbol;
                if (interfaceImplementation != null)
                {
                    return interfaceImplementation;
                }

                return !originalTarget.IsAbstract || HasMethodBody(originalTarget)
                    ? originalTarget
                    : null;
            }

            if (!(originalTarget.IsVirtual || originalTarget.IsAbstract || originalTarget.IsOverride))
            {
                return originalTarget;
            }

            for (var type = exactReceiverType; type != null; type = type.BaseType)
            {
                foreach (var member in type.GetMembers())
                {
                    if (member is IMethodSymbol method &&
                        (SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, originalTarget) ||
                         TypeHierarchyEnumeration.OverridesTargetMethod(method, originalTarget) ||
                         TypeHierarchyEnumeration.ExplicitlyImplements(method, originalTarget)))
                    {
                        return method;
                    }
                }
            }

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
                            : exactReceiverType.FindImplementationForInterfaceMember(propertySymbol.SetMethod)
                        : propertySymbol.GetMethod == null
                            ? null
                            : exactReceiverType.FindImplementationForInterfaceMember(propertySymbol.GetMethod));
                return GetAccessorFromImplementation(implementation, preferSetter);
            }

            for (var current = exactReceiverType; current != null; current = current.BaseType)
            {
                var implementation = current
                    .GetMembers(propertySymbol.Name)
                    .OfType<IPropertySymbol>()
                    .FirstOrDefault(property =>
                        SymbolEqualityComparer.Default.Equals(property.OriginalDefinition, propertySymbol.OriginalDefinition) ||
                        DispatchedMemberResolution.OverridesProperty(property, propertySymbol));
                if (implementation == null)
                {
                    continue;
                }

                return preferSetter ? implementation.SetMethod : implementation.GetMethod;
            }

            return preferSetter ? propertySymbol.SetMethod : propertySymbol.GetMethod;
        }

        private static bool IsObjectGetTypeMethod(IMethodSymbol methodSymbol)
        {
            return !methodSymbol.IsStatic &&
                methodSymbol.Parameters.Length == 0 &&
                methodSymbol.Name == nameof(object.GetType) &&
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
            {
                return preferSetter ? propertyImplementation.SetMethod : propertyImplementation.GetMethod;
            }

            return implementation as IMethodSymbol;
        }

        private static bool HasMethodBody(IMethodSymbol methodSymbol)
        {
            return methodSymbol.DeclaringSyntaxReferences.Length > 0;
        }

        private static bool IsDefinitelyNullValue(
            IOperation? valueOperation,
            PurityAnalysisState currentState)
        {
            valueOperation = SkipImplicitConversions(valueOperation);

            while (valueOperation is IParenthesizedOperation parenthesizedOperation)
            {
                valueOperation = SkipImplicitConversions(parenthesizedOperation.Operand);
            }

            if (valueOperation is IConversionOperation conversionOperation)
            {
                return IsDefinitelyNullValue(conversionOperation.Operand, currentState);
            }

            if (valueOperation is ILiteralOperation literalOperation &&
                literalOperation.ConstantValue.HasValue &&
                literalOperation.ConstantValue.Value == null)
            {
                return true;
            }

            if (valueOperation is IDefaultValueOperation defaultValueOperation &&
                defaultValueOperation.Type?.IsReferenceType == true)
            {
                return true;
            }

            if (valueOperation is ILocalReferenceOperation localReference)
            {
                return currentState.IsDefinitelyNullLocalSymbol(localReference.Local);
            }

            if (TryResolveTrackedSymbol(valueOperation, currentState) is ILocalSymbol capturedLocal)
            {
                return currentState.IsDefinitelyNullLocalSymbol(capturedLocal);
            }

            return false;
        }

        private static bool IsArrayEmptyFactory(IMethodSymbol methodSymbol)
        {
            return methodSymbol.Name == "Empty" &&
                methodSymbol.Parameters.Length == 0 &&
                methodSymbol.ContainingType?.SpecialType == SpecialType.System_Array;
        }


    }
}
