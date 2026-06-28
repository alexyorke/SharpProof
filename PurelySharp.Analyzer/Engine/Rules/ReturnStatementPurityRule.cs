using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
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
                        "returned_array_factory");
                }
                else if (IsPureArrayReturningInvocationReturn(sourceReturnedValue, out var arrayReturningMethod))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes mutable array from known-pure method '{arrayReturningMethod.ToDisplayString()}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        returnOperation.ReturnedValue.Syntax,
                        arrayReturningMethod,
                        "returned_known_pure_array");
                }
                else if (IsOwnedLocalArrayReturn(sourceReturnedValue, currentState, out var localSymbol))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes owned fresh local array '{localSymbol.Name}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        returnOperation.ReturnedValue.Syntax,
                        localSymbol,
                        "owned_local_array_return");
                }
                else if (IsCallerOwnedArrayReadOnlyCollectionReturn(sourceReturnedValue, currentState, context.SemanticModel, out var readOnlyCollectionMethod))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes read-only collection view over caller-owned array through '{readOnlyCollectionMethod.ToDisplayString()}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        returnOperation.ReturnedValue.Syntax,
                        readOnlyCollectionMethod,
                        "returned_array_read_only_view");
                }
                else if (IsCallerOwnedArraySpanReturn(sourceReturnedValue, currentState, context.SemanticModel, out var spanMethod))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes span view over caller-owned array through '{spanMethod.ToDisplayString()}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        returnOperation.ReturnedValue.Syntax,
                        spanMethod,
                        "returned_array_span_view");
                }
                else if (IsCallerOwnedArrayMemoryReturn(sourceReturnedValue, currentState, context.SemanticModel, out var memoryConstructor))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes memory view over caller-owned array through '{memoryConstructor.ToDisplayString()}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        returnOperation.ReturnedValue.Syntax,
                        memoryConstructor,
                        "returned_array_memory_view");
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
                        catalogSource);
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
                        nestedObjectCatalogSource);
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
                        collectionCatalogSource);
                }
                else if (TryFindFreshMutableObjectReturnEscape(
                             returnOperation.ReturnedValue,
                             context.SemanticModel,
                             out var objectEscapeSyntax,
                             out var objectEscapeSymbol,
                             out var objectCatalogSource))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes fresh mutable object through '{objectEscapeSyntax}'. Return statement is Impure.");
                    return CreateMutableStateEscapeResult(
                        returnOperation,
                        objectEscapeSyntax,
                        objectEscapeSymbol,
                        objectCatalogSource);
                }
                else
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value is pure. Return statement is Pure.");
                    return valueResult;
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CreateMutableStateEscapeResult(
            IReturnOperation returnOperation,
            SyntaxNode escapeSyntax,
            ISymbol escapeSymbol,
            string catalogSource)
        {
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
                currentState.IsOwnedLocalArraySymbol(trackedLocal))
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

            if (unwrappedReturnedValue is ILocalReferenceOperation localReference &&
                TryGetStableMutableObjectLocalEscape(
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
                    return TryFindFreshMutableObjectReturnEscape(
                        conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse,
                        semanticModel,
                        out escapeSyntax,
                        out escapeSymbol,
                        out catalogSource);
                }

                return TryFindFreshMutableObjectReturnEscape(
                           conditionalOperation.WhenTrue,
                           semanticModel,
                           out escapeSyntax,
                           out escapeSymbol,
                           out catalogSource) ||
                       TryFindFreshMutableObjectReturnEscape(
                           conditionalOperation.WhenFalse,
                           semanticModel,
                           out escapeSyntax,
                           out escapeSymbol,
                           out catalogSource);
            }

            if (unwrappedReturnedValue is ICoalesceOperation coalesceOperation)
            {
                return TryFindFreshMutableObjectReturnEscape(
                           coalesceOperation.Value,
                           semanticModel,
                           out escapeSyntax,
                           out escapeSymbol,
                           out catalogSource) ||
                       TryFindFreshMutableObjectReturnEscape(
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
