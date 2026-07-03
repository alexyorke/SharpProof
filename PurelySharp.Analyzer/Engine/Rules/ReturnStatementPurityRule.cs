using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Immutable;

namespace PurelySharp.Analyzer.Engine.Rules
{

    internal class ReturnStatementPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Return);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IReturnOperation returnOperation))
            {

                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }


            if (returnOperation.ReturnedValue == null)
            {
                PurityAnalysisEngine.LogDebug("    [ReturnRule] No returned value - Pure");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }


            if (returnOperation.ReturnedValue != null)
            {
                var sourceReturnedValue = GetSourceReturnedValueOperation(returnOperation, context.SemanticModel) ?? returnOperation.ReturnedValue;
                PurityAnalysisEngine.LogDebug($"    [ReturnRule] Checking returned value: {returnOperation.ReturnedValue.Syntax} ({returnOperation.ReturnedValue.Kind})");
                var valueResult = PurityAnalysisEngine.CheckSingleOperation(returnOperation.ReturnedValue, context, currentState);
                if (!valueResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value is IMPURE. Return statement is Impure.");
                    return valueResult;
                }
                else if (IsAwaiterFactoryReturn(
                             context.ContainingMethodSymbol,
                             sourceReturnedValue,
                             context.SemanticModel.Compilation))
                {
                    PurityAnalysisEngine.LogDebug("    [ReturnRule] Returned value is a fresh awaiter produced by GetAwaiter(). Defer await-protocol purity to AwaitPurityRule.");
                    return valueResult;
                }
                else if (IsSpanToArrayReturn(sourceReturnedValue, out var spanToArrayMethod))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes a mutable array produced from span method '{spanToArrayMethod.ToDisplayString()}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        returnOperation.ReturnedValue.Syntax,
                        spanToArrayMethod,
                        "returned_span_to_array",
                        currentState);
                }
                else if (IsAllowedTrustedArrayReturn(
                             sourceReturnedValue,
                             context.SemanticModel,
                             out var trustedArrayReturnSymbol))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value flows from trusted array-return source '{trustedArrayReturnSymbol.ToDisplayString()}'. Return statement is Pure.");
                    return valueResult;
                }
                else if (IsKnownPureArrayFactoryReturn(sourceReturnedValue, context.SemanticModel.Compilation, out var factoryMethod))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes mutable array from known-pure factory '{factoryMethod.ToDisplayString()}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        returnOperation.ReturnedValue.Syntax,
                        factoryMethod,
                        "returned_array_factory",
                        currentState);
                }
                else if (IsPureArrayReturningInvocationReturn(sourceReturnedValue, out var arrayReturningMethod))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes mutable array from known-pure method '{arrayReturningMethod.ToDisplayString()}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        returnOperation.ReturnedValue.Syntax,
                        arrayReturningMethod,
                        "returned_known_pure_array",
                        currentState);
                }
                else if (IsOwnedLocalArrayReturn(sourceReturnedValue, currentState, out var localSymbol))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes owned fresh local array '{localSymbol.Name}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        returnOperation.ReturnedValue.Syntax,
                        localSymbol,
                        "owned_local_array_return",
                        currentState);
                }
                else if (TryFindReturnedDelegateOwnedLocalArrayCapture(
                             sourceReturnedValue,
                             context,
                             currentState,
                             out var delegateCaptureSyntax,
                             out var delegateCapturedArrayLocal))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned delegate captures owned fresh local array '{delegateCapturedArrayLocal.Name}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        delegateCaptureSyntax,
                        delegateCapturedArrayLocal,
                        "escaping_closure_owned_array_capture",
                        currentState);
                }
                else if (TryFindReturnedDelegateFreshMutableObjectCapture(
                             sourceReturnedValue,
                             context,
                             currentState,
                             out var objectDelegateCaptureSyntax,
                             out var objectDelegateCapturedLocal))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned delegate captures fresh mutable object '{objectDelegateCapturedLocal.Name}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        objectDelegateCaptureSyntax,
                        objectDelegateCapturedLocal,
                        "escaping_closure_fresh_mutable_object_capture",
                        currentState);
                }
                else if (IsCallerOwnedArrayReadOnlyCollectionReturn(sourceReturnedValue, currentState, context.SemanticModel, out var readOnlyCollectionMethod))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes read-only collection view over caller-owned array through '{readOnlyCollectionMethod.ToDisplayString()}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        returnOperation.ReturnedValue.Syntax,
                        readOnlyCollectionMethod,
                        "returned_array_read_only_view",
                        currentState);
                }
                else if (IsListAsReadOnlyReturn(sourceReturnedValue, out var listAsReadOnlyMethod))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes a read-only view over mutable list storage through '{listAsReadOnlyMethod.ToDisplayString()}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        returnOperation.ReturnedValue.Syntax,
                        listAsReadOnlyMethod,
                        "returned_list_read_only_view",
                        currentState);
                }
                else if (IsCallerOwnedArraySpanReturn(sourceReturnedValue, currentState, context.SemanticModel, out var spanMethod))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes span view over caller-owned array through '{spanMethod.ToDisplayString()}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        returnOperation.ReturnedValue.Syntax,
                        spanMethod,
                        "returned_array_span_view",
                        currentState);
                }
                else if (IsCallerOwnedArrayMemoryReturn(sourceReturnedValue, currentState, context.SemanticModel, out var memoryConstructor))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes memory view over caller-owned array through '{memoryConstructor.ToDisplayString()}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        returnOperation.ReturnedValue.Syntax,
                        memoryConstructor,
                        "returned_array_memory_view",
                        currentState);
                }
                else if (TryFindReturnedInitializerArrayEscape(
                             returnOperation.ReturnedValue,
                             currentState,
                             context.SemanticModel,
                             out var escapeSyntax,
                             out var escapeSymbol,
                             out var catalogSource))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned initializer escapes mutable array through '{escapeSyntax}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        escapeSyntax,
                        escapeSymbol,
                        catalogSource,
                        currentState);
                }
                else if (TryFindReturnedInitializerMutableObjectEscape(
                             returnOperation.ReturnedValue,
                             context.SemanticModel,
                             out var nestedObjectEscapeSyntax,
                             out var nestedObjectEscapeSymbol,
                             out var nestedObjectCatalogSource))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned initializer escapes fresh mutable object through '{nestedObjectEscapeSyntax}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        nestedObjectEscapeSyntax,
                        nestedObjectEscapeSymbol,
                        nestedObjectCatalogSource,
                        currentState);
                }
                else if (TryFindMutableCollectionReturnEscape(
                             returnOperation.ReturnedValue,
                             context.SemanticModel,
                             out var collectionEscapeSyntax,
                             out var collectionEscapeSymbol,
                             out var collectionCatalogSource))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes mutable collection through '{collectionEscapeSyntax}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        collectionEscapeSyntax,
                        collectionEscapeSymbol,
                        collectionCatalogSource,
                        currentState);
                }
                else if (TryFindFreshMutableObjectReturnEscape(
                             returnOperation.ReturnedValue,
                             context.SemanticModel,
                             currentState,
                             out var objectEscapeSyntax,
                             out var objectEscapeSymbol,
                             out var objectCatalogSource))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes fresh mutable object through '{objectEscapeSyntax}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        objectEscapeSyntax,
                        objectEscapeSymbol,
                        objectCatalogSource,
                        currentState);
                }
                else
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value is pure. Return statement is Pure.");
                    return valueResult;
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static bool TryFindReturnedDelegateFreshMutableObjectCapture(
            IOperation? returnedValue,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            out SyntaxNode captureSyntax,
            out ILocalSymbol capturedLocal)
        {
            var unwrappedReturnedValue = PurityAnalysisEngine.SkipImplicitConversions(returnedValue);
            var delegateTarget = unwrappedReturnedValue is IDelegateCreationOperation delegateCreation
                ? PurityAnalysisEngine.SkipImplicitConversions(delegateCreation.Target)
                : unwrappedReturnedValue;

            switch (delegateTarget)
            {
                case IAnonymousFunctionOperation anonymousFunction:
                    return DelegateCreationPurityRule.TryFindCapturedFreshMutableObject(
                        anonymousFunction,
                        currentState,
                        delegateTarget.Syntax,
                        context.SemanticModel,
                        out captureSyntax,
                        out capturedLocal);

                case IFlowAnonymousFunctionOperation flowAnonymousFunction:
                    return DelegateCreationPurityRule.TryFindCapturedFreshMutableObject(
                        flowAnonymousFunction,
                        currentState,
                        delegateTarget.Syntax,
                        context.SemanticModel,
                        out captureSyntax,
                        out capturedLocal);

                case IMethodReferenceOperation methodReference
                    when methodReference.Method.MethodKind == MethodKind.LocalFunction:
                    return DelegateCreationPurityRule.TryFindLocalFunctionCapturedFreshMutableObject(
                        methodReference.Method,
                        currentState,
                        delegateTarget.Syntax,
                        context,
                        out captureSyntax,
                        out capturedLocal);

                default:
                    captureSyntax = null!;
                    capturedLocal = null!;
                    return false;
            }
        }

        private static bool TryFindReturnedDelegateOwnedLocalArrayCapture(
            IOperation? returnedValue,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            out SyntaxNode captureSyntax,
            out ILocalSymbol capturedLocal)
        {
            var unwrappedReturnedValue = PurityAnalysisEngine.SkipImplicitConversions(returnedValue);
            var delegateTarget = unwrappedReturnedValue is IDelegateCreationOperation delegateCreation
                ? PurityAnalysisEngine.SkipImplicitConversions(delegateCreation.Target)
                : unwrappedReturnedValue;

            switch (delegateTarget)
            {
                case IAnonymousFunctionOperation anonymousFunction:
                    return DelegateCreationPurityRule.TryFindCapturedOwnedLocalArray(
                        anonymousFunction,
                        currentState,
                        context.SemanticModel,
                        out captureSyntax,
                        out capturedLocal);

                case IFlowAnonymousFunctionOperation flowAnonymousFunction:
                    return DelegateCreationPurityRule.TryFindCapturedOwnedLocalArray(
                        flowAnonymousFunction,
                        currentState,
                        context.SemanticModel,
                        out captureSyntax,
                        out capturedLocal);

                case IMethodReferenceOperation methodReference
                    when methodReference.Method.MethodKind == MethodKind.LocalFunction:
                    return DelegateCreationPurityRule.TryFindLocalFunctionCapturedOwnedLocalArray(
                        methodReference.Method,
                        context,
                        currentState,
                        out captureSyntax,
                        out capturedLocal);

                default:
                    captureSyntax = null!;
                    capturedLocal = null!;
                    return false;
            }
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CreateMutableStateEscapeResult(
            IReturnOperation returnOperation,
            SyntaxNode escapeSyntax,
            ISymbol escapeSymbol,
            string catalogSource,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (PurityAnalysisEngine.TryCreateReturnEscapeEvidence(
                    returnOperation,
                    escapeSyntax,
                    escapeSymbol,
                    currentState,
                    nameof(ReturnStatementPurityRule),
                    catalogSource,
                    out var escapeEvidence))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    escapeSyntax,
                    escapeEvidence);
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                escapeSyntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "mutable_state_escape",
                    ruleName: nameof(ReturnStatementPurityRule),
                    operation: returnOperation,
                    syntaxNode: escapeSyntax,
                    symbol: escapeSymbol,
                    catalogSource: catalogSource));
        }

        private static IOperation? GetSourceReturnedValueOperation(IReturnOperation returnOperation, SemanticModel semanticModel)
        {
            var expressionSyntax = returnOperation.Syntax switch
            {
                ReturnStatementSyntax returnStatementSyntax => returnStatementSyntax.Expression,
                ArrowExpressionClauseSyntax arrowExpressionClauseSyntax => arrowExpressionClauseSyntax.Expression,
                _ => null,
            };

            return expressionSyntax == null
                ? returnOperation.ReturnedValue
                : semanticModel.GetOperation(expressionSyntax) ?? returnOperation.ReturnedValue;
        }

        private static bool IsAwaiterFactoryReturn(
            IMethodSymbol containingMethodSymbol,
            IOperation? returnedValue,
            Compilation compilation)
        {
            if (containingMethodSymbol.Name != "GetAwaiter" ||
                containingMethodSymbol.Parameters.Length != 0)
            {
                return false;
            }

            var unwrappedReturnedValue = PurityAnalysisEngine.SkipImplicitConversions(returnedValue);
            if (unwrappedReturnedValue is not IObjectCreationOperation objectCreationOperation ||
                objectCreationOperation.Type is not INamedTypeSymbol awaiterType)
            {
                return false;
            }

            if (!SymbolEqualityComparer.Default.Equals(containingMethodSymbol.ReturnType, awaiterType))
            {
                return false;
            }

            return HasAwaiterPattern(awaiterType, compilation);
        }

        private static bool HasAwaiterPattern(INamedTypeSymbol awaiterType, Compilation compilation)
        {
            var hasIsCompleted = awaiterType.GetMembers("IsCompleted")
                .OfType<IPropertySymbol>()
                .Any(property => property.Type.SpecialType == SpecialType.System_Boolean);
            if (!hasIsCompleted)
            {
                return false;
            }

            var hasGetResult = awaiterType.GetMembers("GetResult")
                .OfType<IMethodSymbol>()
                .Any(method => method.Parameters.Length == 0);
            if (!hasGetResult)
            {
                return false;
            }

            var notifyCompletion = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.INotifyCompletion");
            var criticalNotifyCompletion = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ICriticalNotifyCompletion");

            return (notifyCompletion != null && awaiterType.AllInterfaces.Contains(notifyCompletion, SymbolEqualityComparer.Default)) ||
                   (criticalNotifyCompletion != null && awaiterType.AllInterfaces.Contains(criticalNotifyCompletion, SymbolEqualityComparer.Default));
        }

        private static bool IsKnownPureArrayFactoryReturn(
            IOperation? returnedValue,
            Compilation compilation,
            out IMethodSymbol factoryMethod)
        {
            var unwrappedReturnedValue = PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions(returnedValue);
            if (PurityAnalysisEngine.IsTrustedFreshArrayFactoryOperation(unwrappedReturnedValue, compilation, out factoryMethod))
            {
                return true;
            }

            if (unwrappedReturnedValue is IConditionalOperation conditionalOperation)
            {
                if (RuleAnalysisHelper.TryGetConstantCondition(conditionalOperation, out var conditionValue))
                {
                    return IsKnownPureArrayFactoryReturn(
                        conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse,
                        compilation,
                        out factoryMethod);
                }

                return IsKnownPureArrayFactoryReturn(conditionalOperation.WhenTrue, compilation, out factoryMethod) ||
                    IsKnownPureArrayFactoryReturn(conditionalOperation.WhenFalse, compilation, out factoryMethod);
            }

            if (unwrappedReturnedValue is ICoalesceOperation coalesceOperation)
            {
                return IsKnownPureArrayFactoryReturn(coalesceOperation.Value, compilation, out factoryMethod) ||
                    IsKnownPureArrayFactoryReturn(coalesceOperation.WhenNull, compilation, out factoryMethod);
            }

            factoryMethod = null!;
            return false;
        }

        private static bool IsSpanToArrayReturn(
            IOperation? returnedValue,
            out IMethodSymbol methodSymbol)
        {
            var unwrappedReturnedValue = PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions(returnedValue);
            if (unwrappedReturnedValue is IInvocationOperation invocationOperation &&
                invocationOperation.Type is IArrayTypeSymbol &&
                invocationOperation.TargetMethod?.OriginalDefinition is { } targetMethod &&
                targetMethod.Name == "ToArray" &&
                !targetMethod.IsStatic &&
                targetMethod.ContainingType?.OriginalDefinition.ToDisplayString() is "System.Span<T>" or "System.ReadOnlySpan<T>")
            {
                methodSymbol = targetMethod;
                return true;
            }

            if (unwrappedReturnedValue is IConditionalOperation conditionalOperation)
            {
                if (RuleAnalysisHelper.TryGetConstantCondition(conditionalOperation, out var conditionValue))
                {
                    return IsSpanToArrayReturn(
                        conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse,
                        out methodSymbol);
                }

                return IsSpanToArrayReturn(conditionalOperation.WhenTrue, out methodSymbol) ||
                    IsSpanToArrayReturn(conditionalOperation.WhenFalse, out methodSymbol);
            }

            if (unwrappedReturnedValue is ICoalesceOperation coalesceOperation)
            {
                return IsSpanToArrayReturn(coalesceOperation.Value, out methodSymbol) ||
                    IsSpanToArrayReturn(coalesceOperation.WhenNull, out methodSymbol);
            }

            methodSymbol = null!;
            return false;
        }

        private static bool IsAllowedTrustedArrayReturn(
            IOperation? returnedValue,
            SemanticModel semanticModel,
            out IMethodSymbol methodSymbol)
        {
            return IsAllowedTrustedArrayReturn(
                returnedValue,
                semanticModel,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                out methodSymbol);
        }

        private static bool IsAllowedTrustedArrayReturn(
            IOperation? returnedValue,
            SemanticModel semanticModel,
            HashSet<ILocalSymbol> visitedLocals,
            out IMethodSymbol methodSymbol)
        {
            var unwrappedReturnedValue = PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions(returnedValue);
            if (PurityAnalysisEngine.IsTrustedNonEscapingArrayFactoryOperation(
                    unwrappedReturnedValue,
                    semanticModel.Compilation,
                    out methodSymbol))
            {
                return true;
            }

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
                    out methodSymbol))
            {
                return true;
            }

            if (unwrappedReturnedValue is IConditionalOperation conditionalOperation)
            {
                if (RuleAnalysisHelper.TryGetConstantCondition(conditionalOperation, out var conditionValue))
                {
                    return IsAllowedTrustedArrayReturn(
                        conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse,
                        semanticModel,
                        visitedLocals,
                        out methodSymbol);
                }

                if (IsAllowedTrustedArrayReturn(
                        conditionalOperation.WhenTrue,
                        semanticModel,
                        visitedLocals,
                        out methodSymbol) &&
                    IsAllowedTrustedArrayReturn(
                        conditionalOperation.WhenFalse,
                        semanticModel,
                        new HashSet<ILocalSymbol>(visitedLocals, SymbolEqualityComparer.Default),
                        out _))
                {
                    return true;
                }
            }

            if (unwrappedReturnedValue is ICoalesceOperation coalesceOperation)
            {
                if (IsAllowedTrustedArrayReturn(
                        coalesceOperation.Value,
                        semanticModel,
                        visitedLocals,
                        out methodSymbol) &&
                    IsAllowedTrustedArrayReturn(
                        coalesceOperation.WhenNull,
                        semanticModel,
                        new HashSet<ILocalSymbol>(visitedLocals, SymbolEqualityComparer.Default),
                        out _))
                {
                    return true;
                }
            }

            methodSymbol = null!;
            return false;
        }

        private static bool TryGetStableAllowedTrustedArrayLocalReturn(
            ILocalSymbol localSymbol,
            IOperation returnedValue,
            SemanticModel semanticModel,
            HashSet<ILocalSymbol> visitedLocals,
            out IMethodSymbol methodSymbol)
        {
            if (!visitedLocals.Add(localSymbol))
            {
                methodSymbol = null!;
                return false;
            }

            var declaratorSyntax = localSymbol.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax())
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault();
            var initializerSyntax = declaratorSyntax?.Initializer?.Value;
            if (declaratorSyntax == null || initializerSyntax == null)
            {
                methodSymbol = null!;
                return false;
            }

            if (RuleAnalysisHelper.HasAssignmentToLocalBetweenDeclarationAndObservation(localSymbol, returnedValue.Syntax, declaratorSyntax, semanticModel))
            {
                methodSymbol = null!;
                return false;
            }

            var initializerOperation = PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions(semanticModel.GetOperation(initializerSyntax));
            if (initializerOperation == null)
            {
                methodSymbol = null!;
                return false;
            }

            return IsAllowedTrustedArrayReturn(
                initializerOperation,
                semanticModel,
                visitedLocals,
                out methodSymbol);
        }


        private static bool IsPureArrayReturningInvocationReturn(
            IOperation? returnedValue,
            out IMethodSymbol methodSymbol)
        {
            var unwrappedReturnedValue = PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions(returnedValue);
            if (unwrappedReturnedValue is IInvocationOperation invocationOperation &&
                invocationOperation.Type is IArrayTypeSymbol)
            {
                methodSymbol = invocationOperation.TargetMethod.OriginalDefinition;
                return true;
            }

            if (unwrappedReturnedValue is IConditionalOperation conditionalOperation)
            {
                if (RuleAnalysisHelper.TryGetConstantCondition(conditionalOperation, out var conditionValue))
                {
                    return IsPureArrayReturningInvocationReturn(
                        conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse,
                        out methodSymbol);
                }

                return IsPureArrayReturningInvocationReturn(conditionalOperation.WhenTrue, out methodSymbol) ||
                    IsPureArrayReturningInvocationReturn(conditionalOperation.WhenFalse, out methodSymbol);
            }

            if (unwrappedReturnedValue is ICoalesceOperation coalesceOperation)
            {
                return IsPureArrayReturningInvocationReturn(coalesceOperation.Value, out methodSymbol) ||
                    IsPureArrayReturningInvocationReturn(coalesceOperation.WhenNull, out methodSymbol);
            }

            methodSymbol = null!;
            return false;
        }

        private enum ArrayViewKind
        {
            ReadOnlyCollection,
            Span,
            Memory,
        }

        private static bool IsCallerOwnedArrayReadOnlyCollectionReturn(
            IOperation? returnedValue,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            SemanticModel semanticModel,
            out IMethodSymbol methodSymbol)
        {
            return TryGetCallerOwnedArrayViewReturn(
                returnedValue,
                currentState,
                semanticModel,
                ArrayViewKind.ReadOnlyCollection,
                out methodSymbol);
        }

        private static bool IsCallerOwnedArraySpanReturn(
            IOperation? returnedValue,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            SemanticModel semanticModel,
            out IMethodSymbol methodSymbol)
        {
            return TryGetCallerOwnedArrayViewReturn(
                returnedValue,
                currentState,
                semanticModel,
                ArrayViewKind.Span,
                out methodSymbol);
        }

        private static bool IsCallerOwnedArrayMemoryReturn(
            IOperation? returnedValue,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            SemanticModel semanticModel,
            out IMethodSymbol constructorSymbol)
        {
            return TryGetCallerOwnedArrayViewReturn(
                returnedValue,
                currentState,
                semanticModel,
                ArrayViewKind.Memory,
                out constructorSymbol);
        }

        private static bool TryGetCallerOwnedArrayViewReturn(
            IOperation? returnedValue,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            SemanticModel semanticModel,
            ArrayViewKind expectedKind,
            out IMethodSymbol methodSymbol)
        {
            if (returnedValue != null &&
                TryResolveReturnedArrayViewSource(
                    returnedValue,
                    returnedValue,
                    semanticModel,
                    expectedKind,
                    new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                    out var sourceOperation,
                    out methodSymbol))
            {
                return !PurityAnalysisEngine.IsOwnedArrayValueOrTrustedFactory(
                    sourceOperation,
                    currentState,
                    semanticModel.Compilation);
            }

            methodSymbol = null!;
            return false;
        }

        private static bool TryResolveReturnedArrayViewSource(
            IOperation? candidateOperation,
            IOperation returnedValue,
            SemanticModel semanticModel,
            ArrayViewKind expectedKind,
            HashSet<ILocalSymbol> visitedLocals,
            out IOperation sourceOperation,
            out IMethodSymbol methodSymbol)
        {
            var unwrappedOperation = PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions(
                PurityAnalysisEngine.SkipImplicitConversions(candidateOperation));
            if (unwrappedOperation == null)
            {
                sourceOperation = null!;
                methodSymbol = null!;
                return false;
            }

            if (TryMatchArrayViewSource(unwrappedOperation, expectedKind, out sourceOperation, out methodSymbol))
            {
                return true;
            }

            if (TryGetViewSliceSource(unwrappedOperation, expectedKind, out var slicedSource))
            {
                return TryResolveReturnedArrayViewSource(
                    slicedSource,
                    returnedValue,
                    semanticModel,
                    expectedKind,
                    visitedLocals,
                    out sourceOperation,
                    out methodSymbol);
            }

            if (unwrappedOperation is ILocalReferenceOperation localReference)
            {
                return TryGetStableArrayViewLocalReturn(
                    localReference.Local,
                    returnedValue,
                    semanticModel,
                    expectedKind,
                    visitedLocals,
                    out sourceOperation,
                    out methodSymbol);
            }

            if (unwrappedOperation is IConditionalOperation conditionalOperation)
            {
                if (RuleAnalysisHelper.TryGetConstantCondition(conditionalOperation, out var conditionValue))
                {
                    return TryResolveReturnedArrayViewSource(
                        conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse,
                        returnedValue,
                        semanticModel,
                        expectedKind,
                        visitedLocals,
                        out sourceOperation,
                        out methodSymbol);
                }

                return TryResolveReturnedArrayViewSource(
                           conditionalOperation.WhenTrue,
                           returnedValue,
                           semanticModel,
                           expectedKind,
                           visitedLocals,
                           out sourceOperation,
                           out methodSymbol) ||
                       TryResolveReturnedArrayViewSource(
                           conditionalOperation.WhenFalse,
                           returnedValue,
                           semanticModel,
                           expectedKind,
                           new HashSet<ILocalSymbol>(visitedLocals, SymbolEqualityComparer.Default),
                           out sourceOperation,
                           out methodSymbol);
            }

            if (unwrappedOperation is ICoalesceOperation coalesceOperation)
            {
                return TryResolveReturnedArrayViewSource(
                           coalesceOperation.Value,
                           returnedValue,
                           semanticModel,
                           expectedKind,
                           visitedLocals,
                           out sourceOperation,
                           out methodSymbol) ||
                       TryResolveReturnedArrayViewSource(
                           coalesceOperation.WhenNull,
                           returnedValue,
                           semanticModel,
                           expectedKind,
                           new HashSet<ILocalSymbol>(visitedLocals, SymbolEqualityComparer.Default),
                           out sourceOperation,
                           out methodSymbol);
            }

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
                    IsSpanViewConstructor(spanConstruction.Constructor) &&
                    spanConstruction.Arguments.Length > 0)
                {
                    sourceOperation = spanConstruction.Arguments[0].Value;
                    methodSymbol = spanConstruction.Constructor!;
                    return true;
                }
            }

            if (expectedKind == ArrayViewKind.Memory &&
                operation is IObjectCreationOperation memoryConstruction &&
                IsMemoryViewConstructor(memoryConstruction.Constructor) &&
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
            var unwrappedReturnedValue = PurityAnalysisEngine.SkipImplicitConversions(returnedValue);
            if (unwrappedReturnedValue is IInvocationOperation invocationOperation &&
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

            if (unwrappedReturnedValue is IConditionalOperation conditionalOperation)
            {
                if (RuleAnalysisHelper.TryGetConstantCondition(conditionalOperation, out var conditionValue))
                {
                    return IsListAsReadOnlyReturn(
                        conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse,
                        out methodSymbol);
                }

                return IsListAsReadOnlyReturn(conditionalOperation.WhenTrue, out methodSymbol) ||
                    IsListAsReadOnlyReturn(conditionalOperation.WhenFalse, out methodSymbol);
            }

            if (unwrappedReturnedValue is ICoalesceOperation coalesceOperation)
            {
                return IsListAsReadOnlyReturn(coalesceOperation.Value, out methodSymbol) ||
                    IsListAsReadOnlyReturn(coalesceOperation.WhenNull, out methodSymbol);
            }

            methodSymbol = null!;
            return false;
        }

        private static bool TryGetViewSliceSource(
            IOperation operation,
            ArrayViewKind expectedKind,
            out IOperation sourceOperation)
        {
            if (operation is not IInvocationOperation invocationOperation ||
                !IsSemanticallyPureSpanLikeSliceInvocation(invocationOperation))
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

        private static bool IsSemanticallyPureSpanLikeSliceInvocation(IInvocationOperation invocationOperation)
        {
            var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
            if (targetMethod == null ||
                targetMethod.MethodKind != MethodKind.Ordinary ||
                targetMethod.Name != "Slice" ||
                targetMethod.IsStatic)
            {
                return false;
            }

            var containingType = targetMethod.ContainingType?.OriginalDefinition.ToDisplayString();
            if (containingType is not ("System.Span<T>" or "System.ReadOnlySpan<T>" or "System.Memory<T>" or "System.ReadOnlyMemory<T>"))
            {
                return false;
            }

            if (targetMethod.Parameters.Length is not (1 or 2))
            {
                return false;
            }

            return targetMethod.Parameters.All(parameter =>
                parameter.RefKind == RefKind.None &&
                parameter.Type.SpecialType == SpecialType.System_Int32);
        }

        private static bool TryGetStableArrayViewLocalReturn(
            ILocalSymbol localSymbol,
            IOperation returnedValue,
            SemanticModel semanticModel,
            ArrayViewKind expectedKind,
            HashSet<ILocalSymbol> visitedLocals,
            out IOperation sourceOperation,
            out IMethodSymbol methodSymbol)
        {
            if (!visitedLocals.Add(localSymbol))
            {
                sourceOperation = null!;
                methodSymbol = null!;
                return false;
            }

            var declaratorSyntax = localSymbol.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax())
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault();
            var initializerSyntax = declaratorSyntax?.Initializer?.Value;
            if (declaratorSyntax == null || initializerSyntax == null)
            {
                sourceOperation = null!;
                methodSymbol = null!;
                return false;
            }

            if (RuleAnalysisHelper.HasAssignmentToLocalBetweenDeclarationAndObservation(localSymbol, returnedValue.Syntax, declaratorSyntax, semanticModel))
            {
                sourceOperation = null!;
                methodSymbol = null!;
                return false;
            }

            return TryResolveReturnedArrayViewSource(
                semanticModel.GetOperation(initializerSyntax),
                returnedValue,
                semanticModel,
                expectedKind,
                visitedLocals,
                out sourceOperation,
                out methodSymbol);
        }

        private static bool IsMemoryViewConstructor(IMethodSymbol? methodSymbol)
        {
            if (methodSymbol == null ||
                methodSymbol.MethodKind != MethodKind.Constructor ||
                methodSymbol.ContainingType is not INamedTypeSymbol containingType ||
                methodSymbol.Parameters.Length == 0 ||
                methodSymbol.Parameters[0].Type is not IArrayTypeSymbol)
            {
                return false;
            }

            return containingType.OriginalDefinition.ToDisplayString() is "System.Memory<T>" or "System.ReadOnlyMemory<T>";
        }

        private static bool IsSpanViewConstructor(IMethodSymbol? methodSymbol)
        {
            if (methodSymbol == null ||
                methodSymbol.MethodKind != MethodKind.Constructor ||
                methodSymbol.ContainingType is not INamedTypeSymbol containingType ||
                methodSymbol.Parameters.Length == 0 ||
                methodSymbol.Parameters[0].Type is not IArrayTypeSymbol)
            {
                return false;
            }

            return containingType.OriginalDefinition.ToDisplayString() is "System.Span<T>" or "System.ReadOnlySpan<T>";
        }

        private static bool TryGetArraySpanSource(
            IInvocationOperation invocationOperation,
            out IOperation sourceOperation)
        {
            foreach (var argument in invocationOperation.Arguments)
            {
                if (argument.Parameter?.Type is IArrayTypeSymbol ||
                    argument.Value.Type is IArrayTypeSymbol)
                {
                    sourceOperation = argument.Value;
                    return true;
                }
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
            var unwrappedReturnedValue = PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions(returnedValue);
            if (PurityAnalysisEngine.TryResolveTrackedSymbol(unwrappedReturnedValue, currentState) is ILocalSymbol trackedLocal &&
                (currentState.IsOwnedLocalArraySymbol(trackedLocal) ||
                 (trackedLocal.Type is IArrayTypeSymbol &&
                  PurityAnalysisEngine.HasSymbolicOwnedFactForSymbol(trackedLocal, currentState))))
            {
                localSymbol = trackedLocal;
                return true;
            }

            if (unwrappedReturnedValue is IConditionalOperation conditionalOperation)
            {
                if (RuleAnalysisHelper.TryGetConstantCondition(conditionalOperation, out var conditionValue))
                {
                    return IsOwnedLocalArrayReturn(
                        conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse,
                        currentState,
                        out localSymbol);
                }

                return IsOwnedLocalArrayReturn(conditionalOperation.WhenTrue, currentState, out localSymbol) ||
                    IsOwnedLocalArrayReturn(conditionalOperation.WhenFalse, currentState, out localSymbol);
            }

            if (unwrappedReturnedValue is ICoalesceOperation coalesceOperation)
            {
                return IsOwnedLocalArrayReturn(coalesceOperation.Value, currentState, out localSymbol) ||
                    IsOwnedLocalArrayReturn(coalesceOperation.WhenNull, currentState, out localSymbol);
            }

            if (unwrappedReturnedValue is ITupleOperation tupleOperation)
            {
                foreach (var element in tupleOperation.Elements)
                {
                    if (IsOwnedLocalArrayReturn(element, currentState, out localSymbol))
                    {
                        return true;
                    }
                }
            }

            localSymbol = null!;
            return false;
        }

        private static bool TryFindReturnedInitializerArrayEscape(
            IOperation returnedValue,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            SemanticModel semanticModel,
            out SyntaxNode escapeSyntax,
            out ISymbol escapeSymbol,
            out string catalogSource)
        {
            foreach (var assignment in returnedValue.DescendantsAndSelf().OfType<ISimpleAssignmentOperation>())
            {
                if (IsOwnedLocalArrayReturn(assignment.Value, currentState, out var localSymbol))
                {
                    escapeSyntax = assignment.Value.Syntax;
                    escapeSymbol = localSymbol;
                    catalogSource = "owned_local_array_initializer_escape";
                    return true;
                }

                if (IsKnownPureArrayFactoryReturn(assignment.Value, semanticModel.Compilation, out var factoryMethod))
                {
                    escapeSyntax = assignment.Value.Syntax;
                    escapeSymbol = factoryMethod;
                    catalogSource = "array_factory_initializer_escape";
                    return true;
                }
            }

            foreach (var objectCreation in returnedValue.DescendantsAndSelf().OfType<IObjectCreationOperation>())
            {
                if (!IsConstructionWithEscapingParameters(objectCreation, semanticModel))
                {
                    continue;
                }

                foreach (var argument in objectCreation.Arguments)
                {
                    if (IsOwnedLocalArrayReturn(argument.Value, currentState, out var localSymbol))
                    {
                        escapeSyntax = argument.Value.Syntax;
                        escapeSymbol = localSymbol;
                        catalogSource = "owned_local_array_constructor_escape";
                        return true;
                    }

                    if (IsKnownPureArrayFactoryReturn(argument.Value, semanticModel.Compilation, out var factoryMethod))
                    {
                        escapeSyntax = argument.Value.Syntax;
                        escapeSymbol = factoryMethod;
                        catalogSource = "array_factory_constructor_escape";
                        return true;
                    }
                }
            }

            escapeSyntax = null!;
            escapeSymbol = null!;
            catalogSource = string.Empty;
            return false;
        }

        private static bool TryFindMutableCollectionReturnEscape(
            IOperation returnedValue,
            SemanticModel semanticModel,
            out SyntaxNode escapeSyntax,
            out ISymbol escapeSymbol,
            out string catalogSource)
        {
            var unwrappedReturnedValue = PurityAnalysisEngine.SkipImplicitConversions(returnedValue);
            if (unwrappedReturnedValue is IInvocationOperation invocationOperation &&
                PurityAnalysisEngine.IsKnownMutableCollectionBoundaryType(invocationOperation.Type))
            {
                escapeSyntax = invocationOperation.Syntax;
                escapeSymbol = invocationOperation.TargetMethod.OriginalDefinition;
                catalogSource = "returned_mutable_collection_invocation";
                return true;
            }

            if (unwrappedReturnedValue is ILocalReferenceOperation localReference &&
                TryGetStableMutableCollectionLocalEscape(
                    localReference.Local,
                    returnedValue,
                    semanticModel,
                    out escapeSyntax,
                    out escapeSymbol,
                    out catalogSource))
            {
                return true;
            }

            if (unwrappedReturnedValue is IConditionalOperation conditionalOperation)
            {
                if (RuleAnalysisHelper.TryGetConstantCondition(conditionalOperation, out var conditionValue))
                {
                    return TryFindMutableCollectionReturnEscape(
                        conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse,
                        semanticModel,
                        out escapeSyntax,
                        out escapeSymbol,
                        out catalogSource);
                }

                return TryFindMutableCollectionReturnEscape(
                           conditionalOperation.WhenTrue,
                           semanticModel,
                           out escapeSyntax,
                           out escapeSymbol,
                           out catalogSource) ||
                       TryFindMutableCollectionReturnEscape(
                           conditionalOperation.WhenFalse,
                           semanticModel,
                           out escapeSyntax,
                           out escapeSymbol,
                           out catalogSource);
            }

            if (unwrappedReturnedValue is ICoalesceOperation coalesceOperation)
            {
                return TryFindMutableCollectionReturnEscape(
                           coalesceOperation.Value,
                           semanticModel,
                           out escapeSyntax,
                           out escapeSymbol,
                           out catalogSource) ||
                       TryFindMutableCollectionReturnEscape(
                           coalesceOperation.WhenNull,
                           semanticModel,
                           out escapeSyntax,
                           out escapeSymbol,
                           out catalogSource);
            }

            escapeSyntax = null!;
            escapeSymbol = null!;
            catalogSource = string.Empty;
            return false;
        }

        private static bool TryFindReturnedInitializerMutableObjectEscape(
            IOperation returnedValue,
            SemanticModel semanticModel,
            out SyntaxNode escapeSyntax,
            out ISymbol escapeSymbol,
            out string catalogSource)
        {
            foreach (var assignment in returnedValue.DescendantsAndSelf().OfType<ISimpleAssignmentOperation>())
            {
                if (TryFindFreshMutableObjectReturnEscape(
                    assignment.Value,
                    semanticModel,
                    currentState: null,
                    out escapeSyntax,
                    out escapeSymbol,
                    out _))
                {
                    catalogSource = "fresh_mutable_object_initializer_escape";
                    return true;
                }
            }

            foreach (var objectCreation in returnedValue.DescendantsAndSelf().OfType<IObjectCreationOperation>())
            {
                if (!IsConstructionWithEscapingParameters(objectCreation, semanticModel))
                {
                    continue;
                }

                foreach (var argument in objectCreation.Arguments)
                {
                    if (TryFindFreshMutableObjectReturnEscape(
                        argument.Value,
                        semanticModel,
                        currentState: null,
                        out escapeSyntax,
                        out escapeSymbol,
                        out _))
                    {
                        catalogSource = "fresh_mutable_object_constructor_escape";
                        return true;
                    }
                }
            }

            escapeSyntax = null!;
            escapeSymbol = null!;
            catalogSource = string.Empty;
            return false;
        }

        private static bool TryFindFreshMutableObjectReturnEscape(
            IOperation returnedValue,
            SemanticModel semanticModel,
            PurityAnalysisEngine.PurityAnalysisState? currentState,
            out SyntaxNode escapeSyntax,
            out ISymbol escapeSymbol,
            out string catalogSource)
        {
            var unwrappedReturnedValue = PurityAnalysisEngine.SkipImplicitConversions(returnedValue);
            if (unwrappedReturnedValue is IObjectCreationOperation objectCreationOperation &&
                RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(objectCreationOperation.Type))
            {
                escapeSyntax = objectCreationOperation.Syntax;
                escapeSymbol = objectCreationOperation.Constructor ?? (ISymbol)objectCreationOperation.Type!;
                catalogSource = "fresh_mutable_object_return";
                return true;
            }

            if (unwrappedReturnedValue is IInvocationOperation invocationOperation &&
                TryFindNestedCallableFreshMutableObjectReturnEscape(
                    invocationOperation,
                    semanticModel,
                    out escapeSyntax,
                    out escapeSymbol,
                    out catalogSource))
            {
                return true;
            }

            if (unwrappedReturnedValue is ILocalReferenceOperation localReference)
            {
                if (OwnedFreshMutableObjectClassifier.IsOwnedFreshMutableLocal(
                        localReference.Local,
                        returnedValue.Syntax,
                        semanticModel,
                        currentState))
                {
                    escapeSyntax = returnedValue.Syntax;
                    escapeSymbol = localReference.Local;
                    catalogSource = "symbolic_fresh_mutable_object_return";
                    return true;
                }

                if (TryGetStableMutableObjectLocalEscape(
                        localReference.Local,
                        returnedValue,
                        semanticModel,
                        out escapeSyntax,
                        out escapeSymbol,
                        out catalogSource))
                {
                    return true;
                }
            }

            if (unwrappedReturnedValue is ITupleOperation tupleOperation)
            {
                foreach (var element in tupleOperation.Elements)
                {
                    if (TryFindFreshMutableObjectReturnEscape(
                            element,
                            semanticModel,
                            currentState,
                            out escapeSyntax,
                            out escapeSymbol,
                            out catalogSource))
                    {
                        catalogSource = catalogSource switch
                        {
                            "fresh_mutable_object_return" => "fresh_mutable_object_tuple_return",
                            "symbolic_fresh_mutable_object_return" => "symbolic_fresh_mutable_object_tuple_return",
                            _ => catalogSource
                        };
                        return true;
                    }
                }
            }

            if (unwrappedReturnedValue is IConditionalOperation conditionalOperation)
            {
                if (RuleAnalysisHelper.TryGetConstantCondition(conditionalOperation, out var conditionValue))
                {
                    return TryFindFreshMutableObjectReturnEscape(
                        conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse,
                        semanticModel,
                        currentState,
                        out escapeSyntax,
                        out escapeSymbol,
                        out catalogSource);
                }

                return TryFindFreshMutableObjectReturnEscape(
                           conditionalOperation.WhenTrue,
                           semanticModel,
                           currentState,
                           out escapeSyntax,
                           out escapeSymbol,
                           out catalogSource) ||
                       TryFindFreshMutableObjectReturnEscape(
                           conditionalOperation.WhenFalse,
                           semanticModel,
                           currentState,
                           out escapeSyntax,
                           out escapeSymbol,
                           out catalogSource);
            }

            if (unwrappedReturnedValue is ICoalesceOperation coalesceOperation)
            {
                return TryFindFreshMutableObjectReturnEscape(
                           coalesceOperation.Value,
                           semanticModel,
                           currentState,
                           out escapeSyntax,
                           out escapeSymbol,
                           out catalogSource) ||
                       TryFindFreshMutableObjectReturnEscape(
                           coalesceOperation.WhenNull,
                           semanticModel,
                           currentState,
                           out escapeSyntax,
                           out escapeSymbol,
                           out catalogSource);
            }

            escapeSyntax = null!;
            escapeSymbol = null!;
            catalogSource = string.Empty;
            return false;
        }

        private static bool TryFindNestedCallableFreshMutableObjectReturnEscape(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            out SyntaxNode escapeSyntax,
            out ISymbol escapeSymbol,
            out string catalogSource)
        {
            if (!PurityAnalysisEngine.TryGetSingleReturnedValueFromInvocation(
                    invocationOperation,
                    semanticModel,
                    out var returnedOperation,
                    out _,
                    out var returnedSemanticModel) ||
                !TryFindFreshMutableObjectReturnEscape(
                    returnedOperation,
                    returnedSemanticModel,
                    currentState: null,
                    out escapeSyntax,
                    out escapeSymbol,
                    out var nestedCatalogSource))
            {
                escapeSyntax = null!;
                escapeSymbol = null!;
                catalogSource = string.Empty;
                return false;
            }

            catalogSource = nestedCatalogSource.StartsWith("fresh_mutable_object_", StringComparison.Ordinal)
                ? "fresh_mutable_object_nested_callable_return"
                : nestedCatalogSource;
            return true;
        }

        private static bool TryGetStableMutableObjectLocalEscape(
            ILocalSymbol localSymbol,
            IOperation returnedValue,
            SemanticModel semanticModel,
            out SyntaxNode escapeSyntax,
            out ISymbol escapeSymbol,
            out string catalogSource)
        {
            return TryGetStableMutableObjectLocalEscape(
                localSymbol,
                returnedValue,
                semanticModel,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                out escapeSyntax,
            out escapeSymbol,
            out catalogSource);
        }

        private static bool TryGetStableMutableCollectionLocalEscape(
            ILocalSymbol localSymbol,
            IOperation returnedValue,
            SemanticModel semanticModel,
            out SyntaxNode escapeSyntax,
            out ISymbol escapeSymbol,
            out string catalogSource)
        {
            var declaratorSyntax = localSymbol.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax())
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault();
            var initializerSyntax = declaratorSyntax?.Initializer?.Value;
            if (declaratorSyntax == null || initializerSyntax == null)
            {
                escapeSyntax = null!;
                escapeSymbol = null!;
                catalogSource = string.Empty;
                return false;
            }

            if (RuleAnalysisHelper.HasAssignmentToLocalBetweenDeclarationAndObservation(localSymbol, returnedValue.Syntax, declaratorSyntax, semanticModel))
            {
                escapeSyntax = null!;
                escapeSymbol = null!;
                catalogSource = string.Empty;
                return false;
            }

            var initializerOperation = PurityAnalysisEngine.SkipImplicitConversions(semanticModel.GetOperation(initializerSyntax));
            if (initializerOperation != null &&
                TryFindMutableCollectionReturnEscape(
                    initializerOperation,
                    semanticModel,
                    out escapeSyntax,
                    out escapeSymbol,
                    out var nestedCatalogSource))
            {
                catalogSource = nestedCatalogSource == "returned_mutable_collection_invocation"
                    ? "returned_mutable_collection_local"
                    : nestedCatalogSource;
                return true;
            }

            escapeSyntax = null!;
            escapeSymbol = null!;
            catalogSource = string.Empty;
            return false;
        }

        private static bool TryGetStableMutableObjectLocalEscape(
            ILocalSymbol localSymbol,
            IOperation returnedValue,
            SemanticModel semanticModel,
            HashSet<ILocalSymbol> visitedLocals,
            out SyntaxNode escapeSyntax,
            out ISymbol escapeSymbol,
            out string catalogSource)
        {
            if (!visitedLocals.Add(localSymbol))
            {
                escapeSyntax = null!;
                escapeSymbol = null!;
                catalogSource = string.Empty;
                return false;
            }

            var declaratorSyntax = localSymbol.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax())
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault();
            var initializerSyntax = declaratorSyntax?.Initializer?.Value;
            SyntaxNode declarationSyntax;
            if (declaratorSyntax != null && initializerSyntax != null)
            {
                declarationSyntax = declaratorSyntax;
                if (RuleAnalysisHelper.HasAssignmentToLocalBetweenDeclarationAndObservation(localSymbol, returnedValue.Syntax, declaratorSyntax, semanticModel))
                {
                    escapeSyntax = null!;
                    escapeSymbol = null!;
                    catalogSource = string.Empty;
                    return false;
                }
            }
            else if (TryGetDeconstructionElementInitializer(
                         localSymbol,
                         semanticModel,
                         out initializerSyntax,
                         out declarationSyntax))
            {
                if (HasAssignmentToLocalBetweenDeclarationAndObservation(
                        localSymbol,
                        returnedValue.Syntax,
                        declarationSyntax,
                        semanticModel))
                {
                    escapeSyntax = null!;
                    escapeSymbol = null!;
                    catalogSource = string.Empty;
                    return false;
                }
            }
            else
            {
                escapeSyntax = null!;
                escapeSymbol = null!;
                catalogSource = string.Empty;
                return false;
            }

            var initializerOperation = PurityAnalysisEngine.SkipImplicitConversions(semanticModel.GetOperation(initializerSyntax));
            if (initializerOperation is IObjectCreationOperation objectCreationOperation &&
                RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(objectCreationOperation.Type))
            {
                escapeSyntax = returnedValue.Syntax;
                escapeSymbol = objectCreationOperation.Constructor ?? (ISymbol)objectCreationOperation.Type!;
                catalogSource = "fresh_mutable_object_local_return";
                return true;
            }

            if (initializerOperation != null &&
                TryFindReturnedInitializerMutableObjectEscape(
                    initializerOperation,
                    semanticModel,
                    out escapeSyntax,
                    out escapeSymbol,
                    out var nestedCatalogSource))
            {
                catalogSource = nestedCatalogSource switch
                {
                    "fresh_mutable_object_constructor_escape" => "fresh_mutable_object_local_constructor_escape",
                    "fresh_mutable_object_initializer_escape" => "fresh_mutable_object_local_initializer_escape",
                    _ => "fresh_mutable_object_local_escape"
                };
                return true;
            }

            if (initializerOperation is ILocalReferenceOperation localReference)
            {
                return TryGetStableMutableObjectLocalEscape(
                    localReference.Local,
                    returnedValue,
                    semanticModel,
                    visitedLocals,
                    out escapeSyntax,
                    out escapeSymbol,
                    out catalogSource);
            }

            escapeSyntax = null!;
            escapeSymbol = null!;
            catalogSource = string.Empty;
            return false;
        }

        private static bool TryGetDeconstructionElementInitializer(
            ILocalSymbol localSymbol,
            SemanticModel semanticModel,
            out ExpressionSyntax initializerSyntax,
            out SyntaxNode declarationSyntax)
        {
            var designation = localSymbol.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax())
                .OfType<SingleVariableDesignationSyntax>()
                .FirstOrDefault();
            if (designation == null ||
                !TryGetDeconstructionDesignationPath(designation, out var path) ||
                designation.FirstAncestorOrSelf<AssignmentExpressionSyntax>() is not { } assignment ||
                !TryGetTupleElementExpression(assignment.Right, path, out initializerSyntax) ||
                semanticModel.GetDeclaredSymbol(designation) is not ILocalSymbol declaredSymbol ||
                !SymbolEqualityComparer.Default.Equals(declaredSymbol, localSymbol))
            {
                initializerSyntax = null!;
                declarationSyntax = null!;
                return false;
            }

            declarationSyntax = assignment;
            return true;
        }

        private static bool TryGetDeconstructionDesignationPath(
            SingleVariableDesignationSyntax designation,
            out ImmutableArray<int> path)
        {
            var builder = ImmutableArray.CreateBuilder<int>();
            VariableDesignationSyntax current = designation;
            while (current.Parent is ParenthesizedVariableDesignationSyntax parenthesized)
            {
                var index = IndexOfDesignation(parenthesized, current);
                if (index < 0)
                {
                    path = default;
                    return false;
                }

                builder.Insert(0, index);
                current = parenthesized;
            }

            path = builder.ToImmutable();
            return path.Length > 0;
        }

        private static int IndexOfDesignation(
            ParenthesizedVariableDesignationSyntax parenthesized,
            VariableDesignationSyntax designation)
        {
            for (var i = 0; i < parenthesized.Variables.Count; i++)
            {
                if (ReferenceEquals(parenthesized.Variables[i], designation))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool TryGetTupleElementExpression(
            ExpressionSyntax tupleExpression,
            ImmutableArray<int> path,
            out ExpressionSyntax elementExpression)
        {
            elementExpression = tupleExpression;
            foreach (var index in path)
            {
                elementExpression = UnwrapParenthesizedExpression(elementExpression);
                if (elementExpression is not TupleExpressionSyntax tuple ||
                    index < 0 ||
                    index >= tuple.Arguments.Count)
                {
                    elementExpression = null!;
                    return false;
                }

                elementExpression = tuple.Arguments[index].Expression;
            }

            return true;
        }

        private static ExpressionSyntax UnwrapParenthesizedExpression(ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
            }

            return expression;
        }

        private static bool HasAssignmentToLocalBetweenDeclarationAndObservation(
            ILocalSymbol localSymbol,
            SyntaxNode observationSyntax,
            SyntaxNode declarationSyntax,
            SemanticModel semanticModel)
        {
            var containingBlock = observationSyntax.FirstAncestorOrSelf<BlockSyntax>();
            if (containingBlock == null)
            {
                return false;
            }

            var start = declarationSyntax.Span.End;
            var end = observationSyntax.SpanStart;
            if (end <= start)
            {
                return false;
            }

            foreach (var assignment in containingBlock.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.SpanStart < start || assignment.SpanStart >= end)
                {
                    continue;
                }

                var assignedSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
                if (SymbolEqualityComparer.Default.Equals(assignedSymbol, localSymbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsConstructionWithEscapingParameters(
            IObjectCreationOperation objectCreationOperation,
            SemanticModel semanticModel)
        {
            if (objectCreationOperation.Type is not INamedTypeSymbol namedType ||
                objectCreationOperation.Constructor == null)
            {
                return false;
            }

            foreach (var argument in objectCreationOperation.Arguments)
            {
                var parameter = argument.Parameter;
                if (parameter == null)
                {
                    continue;
                }

                if (namedType.IsRecord && HasMatchingRecordProperty(namedType, parameter))
                {
                    return true;
                }

                if (ConstructorStoresParameterInInstanceMember(objectCreationOperation.Constructor, parameter, semanticModel))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ConstructorStoresParameterInInstanceMember(
            IMethodSymbol constructor,
            IParameterSymbol parameter,
            SemanticModel semanticModel)
        {
            foreach (var syntaxReference in constructor.DeclaringSyntaxReferences)
            {
                var constructorSyntax = syntaxReference.GetSyntax();
                var constructorModel = semanticModel.Compilation.GetSemanticModel(constructorSyntax.SyntaxTree);
                foreach (var assignment in constructorSyntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                {
                    if (constructorModel.GetOperation(assignment) is not ISimpleAssignmentOperation assignmentOperation)
                    {
                        continue;
                    }

                    if (PurityAnalysisEngine.SkipImplicitConversions(assignmentOperation.Value) is not IParameterReferenceOperation parameterReference ||
                        !SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, parameter))
                    {
                        continue;
                    }

                    if (assignmentOperation.Target is IFieldReferenceOperation fieldReference &&
                        IsInstanceMemberOfConstructedType(fieldReference.Field, constructor.ContainingType) &&
                        IsThisOrImplicitInstance(fieldReference.Instance))
                    {
                        return true;
                    }

                    if (assignmentOperation.Target is IPropertyReferenceOperation propertyReference &&
                        IsInstanceMemberOfConstructedType(propertyReference.Property, constructor.ContainingType) &&
                        IsThisOrImplicitInstance(propertyReference.Instance))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsInstanceMemberOfConstructedType(ISymbol member, INamedTypeSymbol constructedType)
        {
            return member is IFieldSymbol { IsStatic: false } or IPropertySymbol { IsStatic: false } &&
                SymbolEqualityComparer.Default.Equals(member.ContainingType.OriginalDefinition, constructedType.OriginalDefinition);
        }

        private static bool IsThisOrImplicitInstance(IOperation? instance)
        {
            var unwrappedInstance = PurityAnalysisEngine.SkipImplicitConversions(instance);
            return unwrappedInstance == null ||
                unwrappedInstance is IInstanceReferenceOperation;
        }

        private static bool HasMatchingRecordProperty(INamedTypeSymbol recordType, IParameterSymbol parameter)
        {
            foreach (var member in recordType.GetMembers())
            {
                if (member is IPropertySymbol property &&
                    string.Equals(property.Name, parameter.Name, System.StringComparison.OrdinalIgnoreCase) &&
                    SymbolEqualityComparer.Default.Equals(property.Type, parameter.Type))
                {
                    return true;
                }
            }

            return false;
        }

    }
}
