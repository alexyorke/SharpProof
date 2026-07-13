using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class ReturnStatementPurityRule : IPurityRule
{
    private static bool IsAllowedTrustedArrayReturn(
        IOperation? returnedValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IMethodSymbol methodSymbol)
    {
        return IsAllowedTrustedArrayReturn(
            returnedValue,
            semanticModel,
            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
            cancellationToken,
            out methodSymbol);
    }

    private static bool IsAllowedTrustedArrayReturn(
        IOperation? returnedValue,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken,
        out IMethodSymbol methodSymbol)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unwrappedReturnedValue = PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions(returnedValue);
        if (PurityAnalysisEngine.IsTrustedNonEscapingArrayFactoryOperation(
                unwrappedReturnedValue,
                semanticModel.Compilation,
                out methodSymbol))
            return true;

        if (unwrappedReturnedValue is IInvocationOperation invocationOperation &&
            invocationOperation.Type is IArrayTypeSymbol)
        {
            var originalDefinition = invocationOperation.TargetMethod.OriginalDefinition;
            if (PurityAnalysisEngine.IsTrustedGeneratedNonEscapingArrayReturningMember(
                    originalDefinition,
                    semanticModel.Compilation))
            {
                methodSymbol = originalDefinition;
                return true;
            }
        }

        if (unwrappedReturnedValue is ILocalReferenceOperation localReference &&
            TryGetStableAllowedTrustedArrayLocalReturn(
                localReference.Local,
                returnedValue!,
                semanticModel,
                visitedLocals,
                cancellationToken,
                out methodSymbol))
            return true;

        if (unwrappedReturnedValue is IConditionalOperation conditionalOperation)
        {
            if (RuleAnalysisHelper.TryGetConstantCondition(conditionalOperation, out var conditionValue))
                return IsAllowedTrustedArrayReturn(
                    conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse,
                    semanticModel,
                    visitedLocals,
                    cancellationToken,
                    out methodSymbol);

            if (IsAllowedTrustedArrayReturn(
                    conditionalOperation.WhenTrue,
                    semanticModel,
                    visitedLocals,
                    cancellationToken,
                    out methodSymbol) &&
                IsAllowedTrustedArrayReturn(
                    conditionalOperation.WhenFalse,
                    semanticModel,
                    new HashSet<ILocalSymbol>(visitedLocals, SymbolEqualityComparer.Default),
                    cancellationToken,
                    out _))
                return true;
        }

        if (unwrappedReturnedValue is ICoalesceOperation coalesceOperation)
            if (IsAllowedTrustedArrayReturn(
                    coalesceOperation.Value,
                    semanticModel,
                    visitedLocals,
                    cancellationToken,
                    out methodSymbol) &&
                IsAllowedTrustedArrayReturn(
                    coalesceOperation.WhenNull,
                    semanticModel,
                    new HashSet<ILocalSymbol>(visitedLocals, SymbolEqualityComparer.Default),
                    cancellationToken,
                    out _))
                return true;

        methodSymbol = null!;
        return false;
    }

    private static bool TryGetStableAllowedTrustedArrayLocalReturn(
        ILocalSymbol localSymbol,
        IOperation returnedValue,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken,
        out IMethodSymbol methodSymbol)
    {
        if (!visitedLocals.Add(localSymbol))
        {
            methodSymbol = null!;
            return false;
        }

        if (!TryGetStableLocalInitializerOperation(
                localSymbol,
                returnedValue,
                semanticModel,
                cancellationToken,
                out var initializerOperation))
        {
            methodSymbol = null!;
            return false;
        }

        initializerOperation = PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions(initializerOperation);
        if (initializerOperation == null)
        {
            methodSymbol = null!;
            return false;
        }

        return IsAllowedTrustedArrayReturn(
            initializerOperation,
            semanticModel,
            visitedLocals,
            cancellationToken,
            out methodSymbol);
    }

    private static bool TryGetStableLocalInitializerOperation(
        ILocalSymbol localSymbol,
        IOperation returnedValue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IOperation initializerOperation)
    {
        var declaratorSyntax = localSymbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault();
        var initializerSyntax = declaratorSyntax?.Initializer?.Value;
        if (declaratorSyntax == null || initializerSyntax == null)
        {
            initializerOperation = null!;
            return false;
        }

        if (RuleAnalysisHelper.HasAssignmentToLocalBetweenDeclarationAndObservation(
                localSymbol,
                returnedValue.Syntax,
                declaratorSyntax,
                semanticModel,
                cancellationToken))
        {
            initializerOperation = null!;
            return false;
        }

        initializerOperation = semanticModel.GetOperation(initializerSyntax, cancellationToken)!;
        return initializerOperation != null;
    }


    private static bool IsPureArrayReturningInvocationReturn(
        IOperation? returnedValue,
        out IMethodSymbol methodSymbol)
    {
        return TryMatchReturnedValueAlternative(
            returnedValue,
            PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions,
            IsPureArrayReturningInvocation,
            out methodSymbol);

        static bool IsPureArrayReturningInvocation(IOperation? operation, out IMethodSymbol methodSymbol)
        {
            if (operation is IInvocationOperation invocationOperation &&
                invocationOperation.Type is IArrayTypeSymbol)
            {
                methodSymbol = invocationOperation.TargetMethod.OriginalDefinition;
                return true;
            }

            methodSymbol = null!;
            return false;
        }
    }

    private static bool TryGetCallerOwnedArrayViewReturn(
        IOperation? returnedValue,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        SemanticModel semanticModel,
        ArrayViewKind expectedKind,
        CancellationToken cancellationToken,
        out IMethodSymbol methodSymbol)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (returnedValue != null &&
            TryResolveReturnedArrayViewSource(
                returnedValue,
                returnedValue,
                semanticModel,
                expectedKind,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                cancellationToken,
                out var sourceOperation,
                out methodSymbol))
            return !PurityAnalysisEngine.IsOwnedArrayValueOrTrustedFactory(
                sourceOperation,
                currentState,
                semanticModel.Compilation);

        methodSymbol = null!;
        return false;
    }

    private static bool TryResolveReturnedArrayViewSource(
        IOperation? candidateOperation,
        IOperation returnedValue,
        SemanticModel semanticModel,
        ArrayViewKind expectedKind,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken,
        out IOperation sourceOperation,
        out IMethodSymbol methodSymbol)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unwrappedOperation = PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions(
            PurityAnalysisEngine.SkipImplicitConversions(candidateOperation));
        if (unwrappedOperation == null)
        {
            sourceOperation = null!;
            methodSymbol = null!;
            return false;
        }

        if (TryMatchArrayViewSource(unwrappedOperation, expectedKind, out sourceOperation, out methodSymbol))
            return true;

        if (TryGetViewSliceSource(unwrappedOperation, expectedKind, out var slicedSource))
            return TryResolveReturnedArrayViewSource(
                slicedSource,
                returnedValue,
                semanticModel,
                expectedKind,
                visitedLocals,
                cancellationToken,
                out sourceOperation,
                out methodSymbol);

        if (unwrappedOperation is ILocalReferenceOperation localReference)
            return TryGetStableArrayViewLocalReturn(
                localReference.Local,
                returnedValue,
                semanticModel,
                expectedKind,
                visitedLocals,
                cancellationToken,
                out sourceOperation,
                out methodSymbol);

        if (unwrappedOperation is IConditionalOperation conditionalOperation)
        {
            if (RuleAnalysisHelper.TryGetConstantCondition(conditionalOperation, out var conditionValue))
                return TryResolveReturnedArrayViewSource(
                    conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse,
                    returnedValue,
                    semanticModel,
                    expectedKind,
                    visitedLocals,
                    cancellationToken,
                    out sourceOperation,
                    out methodSymbol);

            return TryResolveReturnedArrayViewSource(
                       conditionalOperation.WhenTrue,
                       returnedValue,
                       semanticModel,
                       expectedKind,
                       visitedLocals,
                       cancellationToken,
                       out sourceOperation,
                       out methodSymbol) ||
                   TryResolveReturnedArrayViewSource(
                       conditionalOperation.WhenFalse,
                       returnedValue,
                       semanticModel,
                       expectedKind,
                       new HashSet<ILocalSymbol>(visitedLocals, SymbolEqualityComparer.Default),
                       cancellationToken,
                       out sourceOperation,
                       out methodSymbol);
        }

        if (unwrappedOperation is ICoalesceOperation coalesceOperation)
            return TryResolveReturnedArrayViewSource(
                       coalesceOperation.Value,
                       returnedValue,
                       semanticModel,
                       expectedKind,
                       visitedLocals,
                       cancellationToken,
                       out sourceOperation,
                       out methodSymbol) ||
                   TryResolveReturnedArrayViewSource(
                       coalesceOperation.WhenNull,
                       returnedValue,
                       semanticModel,
                       expectedKind,
                       new HashSet<ILocalSymbol>(visitedLocals, SymbolEqualityComparer.Default),
                       cancellationToken,
                       out sourceOperation,
                       out methodSymbol);

        sourceOperation = null!;
        methodSymbol = null!;
        return false;
    }

    private static bool TryMatchArrayViewSource(
        IOperation operation,
        ArrayViewKind expectedKind,
        out IOperation sourceOperation,
        out IMethodSymbol methodSymbol)
    {
        if (expectedKind == ArrayViewKind.ReadOnlyCollection &&
            operation is IInvocationOperation readOnlyInvocation &&
            PurityAnalysisEngine.IsArrayAsReadOnlyInvocation(readOnlyInvocation) &&
            readOnlyInvocation.Arguments.Length == 1)
        {
            sourceOperation = readOnlyInvocation.Arguments[0].Value;
            methodSymbol = readOnlyInvocation.TargetMethod.OriginalDefinition;
            return true;
        }

        if (expectedKind == ArrayViewKind.Span)
        {
            if (operation is IInvocationOperation spanInvocation &&
                IsMemoryExtensionsArrayAsSpan(spanInvocation.TargetMethod.OriginalDefinition) &&
                TryGetArraySpanSource(spanInvocation, out sourceOperation))
            {
                methodSymbol = spanInvocation.TargetMethod.OriginalDefinition;
                return true;
            }

            if (operation is IObjectCreationOperation spanConstruction &&
                IsArrayBackedViewConstructor(spanConstruction.Constructor, ArrayViewKind.Span) &&
                spanConstruction.Arguments.Length > 0)
            {
                sourceOperation = spanConstruction.Arguments[0].Value;
                methodSymbol = spanConstruction.Constructor!;
                return true;
            }
        }

        if (expectedKind == ArrayViewKind.Memory &&
            operation is IObjectCreationOperation memoryConstruction &&
            IsArrayBackedViewConstructor(memoryConstruction.Constructor, ArrayViewKind.Memory) &&
            memoryConstruction.Arguments.Length > 0)
        {
            sourceOperation = memoryConstruction.Arguments[0].Value;
            methodSymbol = memoryConstruction.Constructor!;
            return true;
        }

        sourceOperation = null!;
        methodSymbol = null!;
        return false;
    }

    private static bool IsListAsReadOnlyReturn(
        IOperation? returnedValue,
        out IMethodSymbol methodSymbol)
    {
        return TryMatchReturnedValueAlternative(
            returnedValue,
            PurityAnalysisEngine.SkipImplicitConversions,
            IsListAsReadOnlyInvocation,
            out methodSymbol);

        static bool IsListAsReadOnlyInvocation(IOperation? operation, out IMethodSymbol methodSymbol)
        {
            if (operation is IInvocationOperation invocationOperation &&
                invocationOperation.TargetMethod?.OriginalDefinition is { } targetMethod &&
                targetMethod.Name == "AsReadOnly" &&
                !targetMethod.IsStatic &&
                string.Equals(
                    targetMethod.ContainingType?.OriginalDefinition.ToDisplayString(),
                    "System.Collections.Generic.List<T>",
                    StringComparison.Ordinal))
            {
                methodSymbol = targetMethod;
                return true;
            }

            methodSymbol = null!;
            return false;
        }
    }

    private static bool TryGetViewSliceSource(
        IOperation operation,
        ArrayViewKind expectedKind,
        out IOperation sourceOperation)
    {
        if (operation is not IInvocationOperation invocationOperation ||
            !RuleAnalysisHelper.IsSemanticallyPureSpanLikeSliceInvocation(invocationOperation))
        {
            sourceOperation = null!;
            return false;
        }

        var containingType = invocationOperation.TargetMethod.ContainingType?.OriginalDefinition.ToDisplayString();
        if (expectedKind == ArrayViewKind.Span &&
            containingType is "System.Span<T>" or "System.ReadOnlySpan<T>" &&
            invocationOperation.Instance != null)
        {
            sourceOperation = invocationOperation.Instance;
            return true;
        }

        if (expectedKind == ArrayViewKind.Memory &&
            containingType is "System.Memory<T>" or "System.ReadOnlyMemory<T>" &&
            invocationOperation.Instance != null)
        {
            sourceOperation = invocationOperation.Instance;
            return true;
        }

        sourceOperation = null!;
        return false;
    }

    private static bool TryGetStableArrayViewLocalReturn(
        ILocalSymbol localSymbol,
        IOperation returnedValue,
        SemanticModel semanticModel,
        ArrayViewKind expectedKind,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken,
        out IOperation sourceOperation,
        out IMethodSymbol methodSymbol)
    {
        if (!visitedLocals.Add(localSymbol))
        {
            sourceOperation = null!;
            methodSymbol = null!;
            return false;
        }

        if (!TryGetStableLocalInitializerOperation(
                localSymbol,
                returnedValue,
                semanticModel,
                cancellationToken,
                out var initializerOperation))
        {
            sourceOperation = null!;
            methodSymbol = null!;
            return false;
        }

        return TryResolveReturnedArrayViewSource(
            initializerOperation,
            returnedValue,
            semanticModel,
            expectedKind,
            visitedLocals,
            cancellationToken,
            out sourceOperation,
            out methodSymbol);
    }

    private static bool IsArrayBackedViewConstructor(IMethodSymbol? methodSymbol, ArrayViewKind viewKind)
    {
        if (methodSymbol == null ||
            methodSymbol.MethodKind != MethodKind.Constructor ||
            methodSymbol.ContainingType is not INamedTypeSymbol containingType ||
            methodSymbol.Parameters.Length == 0 ||
            methodSymbol.Parameters[0].Type is not IArrayTypeSymbol)
            return false;

        var typeDefinition = containingType.OriginalDefinition.ToDisplayString();
        return viewKind switch
        {
            ArrayViewKind.Span => typeDefinition is "System.Span<T>" or "System.ReadOnlySpan<T>",
            ArrayViewKind.Memory => typeDefinition is "System.Memory<T>" or "System.ReadOnlyMemory<T>",
            _ => false
        };
    }

    private static bool TryGetArraySpanSource(
        IInvocationOperation invocationOperation,
        out IOperation sourceOperation)
    {
        foreach (var argument in invocationOperation.Arguments)
            if (argument.Parameter?.Type is IArrayTypeSymbol ||
                argument.Value.Type is IArrayTypeSymbol)
            {
                sourceOperation = argument.Value;
                return true;
            }

        if (invocationOperation.Instance != null)
        {
            sourceOperation = invocationOperation.Instance;
            return true;
        }

        if (invocationOperation.Arguments.Length > 0)
        {
            sourceOperation = invocationOperation.Arguments[0].Value;
            return true;
        }

        sourceOperation = null!;
        return false;
    }

    private static bool IsMemoryExtensionsArrayAsSpan(IMethodSymbol methodSymbol)
    {
        return methodSymbol.Name == "AsSpan" &&
               methodSymbol.Parameters.Length >= 1 &&
               methodSymbol.Parameters[0].Type is IArrayTypeSymbol &&
               methodSymbol.ContainingType?.ToDisplayString() == "System.MemoryExtensions";
    }

    private static bool IsOwnedLocalArrayReturn(
        IOperation? returnedValue,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        out ILocalSymbol localSymbol)
    {
        return TryMatchReturnedValueAlternative(
            returnedValue,
            PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions,
            IsOwnedLocalArray,
            out localSymbol);

        bool IsOwnedLocalArray(IOperation? operation, out ILocalSymbol localSymbol)
        {
            if (PurityAnalysisEngine.TryResolveTrackedSymbol(operation, currentState) is ILocalSymbol trackedLocal &&
                (currentState.IsOwnedLocalArraySymbol(trackedLocal) ||
                 (trackedLocal.Type is IArrayTypeSymbol &&
                  PurityAnalysisEngine.HasSymbolicOwnedFactForSymbol(trackedLocal, currentState))))
            {
                localSymbol = trackedLocal;
                return true;
            }

            if (operation is ITupleOperation tupleOperation)
                foreach (var element in tupleOperation.Elements)
                    if (IsOwnedLocalArrayReturn(element, currentState, out localSymbol))
                        return true;

            localSymbol = null!;
            return false;
        }
    }

    private enum ArrayViewKind
    {
        ReadOnlyCollection,
        Span,
        Memory
    }
}
