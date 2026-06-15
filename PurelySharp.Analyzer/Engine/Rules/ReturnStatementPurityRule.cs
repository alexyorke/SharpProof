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
                PurityAnalysisEngine.LogDebug($"    [ReturnRule] Checking returned value: {returnOperation.ReturnedValue.Syntax} ({returnOperation.ReturnedValue.Kind})");
                var valueResult = PurityAnalysisEngine.CheckSingleOperation(returnOperation.ReturnedValue, context, currentState);
                if (!valueResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value is IMPURE. Return statement is Impure.");
                    return valueResult;
                }
                else if (IsAllowedTrustedArrayReturn(
                             returnOperation.ReturnedValue,
                             context.SemanticModel,
                             out var trustedArrayReturnSymbol))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value flows from trusted array-return source '{trustedArrayReturnSymbol.ToDisplayString()}'. Return statement is Pure.");
                    return valueResult;
                }
                else if (IsKnownPureArrayFactoryReturn(returnOperation.ReturnedValue, out var factoryMethod))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes mutable array from known-pure factory '{factoryMethod.ToDisplayString()}'. Return statement is Impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        returnOperation.ReturnedValue.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "mutable_state_escape",
                            ruleName: nameof(ReturnStatementPurityRule),
                            operation: returnOperation,
                            syntaxNode: returnOperation.ReturnedValue.Syntax,
                            symbol: factoryMethod,
                            catalogSource: "returned_array_factory"));
                }
                else if (IsPureArrayReturningInvocationReturn(returnOperation.ReturnedValue, out var arrayReturningMethod))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes mutable array from known-pure method '{arrayReturningMethod.ToDisplayString()}'. Return statement is Impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        returnOperation.ReturnedValue.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "mutable_state_escape",
                            ruleName: nameof(ReturnStatementPurityRule),
                            operation: returnOperation,
                            syntaxNode: returnOperation.ReturnedValue.Syntax,
                            symbol: arrayReturningMethod,
                            catalogSource: "returned_known_pure_array"));
                }
                else if (IsOwnedLocalArrayReturn(returnOperation.ReturnedValue, currentState, out var localSymbol))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes owned fresh local array '{localSymbol.Name}'. Return statement is Impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        returnOperation.ReturnedValue.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "mutable_state_escape",
                            ruleName: nameof(ReturnStatementPurityRule),
                            operation: returnOperation,
                            syntaxNode: returnOperation.ReturnedValue.Syntax,
                            symbol: localSymbol,
                            catalogSource: "owned_local_array_return"));
                }
                else if (IsCallerOwnedArraySpanReturn(returnOperation.ReturnedValue, currentState, out var spanMethod))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes span view over caller-owned array through '{spanMethod.ToDisplayString()}'. Return statement is Impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        returnOperation.ReturnedValue.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "mutable_state_escape",
                            ruleName: nameof(ReturnStatementPurityRule),
                            operation: returnOperation,
                            syntaxNode: returnOperation.ReturnedValue.Syntax,
                            symbol: spanMethod,
                            catalogSource: "returned_array_span_view"));
                }
                else if (IsCallerOwnedArrayMemoryReturn(returnOperation.ReturnedValue, currentState, out var memoryConstructor))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes memory view over caller-owned array through '{memoryConstructor.ToDisplayString()}'. Return statement is Impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        returnOperation.ReturnedValue.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "mutable_state_escape",
                            ruleName: nameof(ReturnStatementPurityRule),
                            operation: returnOperation,
                            syntaxNode: returnOperation.ReturnedValue.Syntax,
                            symbol: memoryConstructor,
                            catalogSource: "returned_array_memory_view"));
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
                else if (TryFindReturnedInitializerMutableObjectEscape(
                             returnOperation.ReturnedValue,
                             context.SemanticModel,
                             out var nestedObjectEscapeSyntax,
                             out var nestedObjectEscapeSymbol,
                             out var nestedObjectCatalogSource))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned initializer escapes fresh mutable object through '{nestedObjectEscapeSyntax}'. Return statement is Impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        nestedObjectEscapeSyntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "mutable_state_escape",
                            ruleName: nameof(ReturnStatementPurityRule),
                            operation: returnOperation,
                            syntaxNode: nestedObjectEscapeSyntax,
                            symbol: nestedObjectEscapeSymbol,
                            catalogSource: nestedObjectCatalogSource));
                }
                else if (TryFindFreshMutableObjectReturnEscape(
                             returnOperation.ReturnedValue,
                             context.SemanticModel,
                             out var objectEscapeSyntax,
                             out var objectEscapeSymbol,
                             out var objectCatalogSource))
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value escapes fresh mutable object through '{objectEscapeSyntax}'. Return statement is Impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        objectEscapeSyntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "mutable_state_escape",
                            ruleName: nameof(ReturnStatementPurityRule),
                            operation: returnOperation,
                            syntaxNode: objectEscapeSyntax,
                            symbol: objectEscapeSymbol,
                            catalogSource: objectCatalogSource));
                }
                else
                {
                    PurityAnalysisEngine.LogDebug($"    [ReturnRule] Returned value is pure. Return statement is Pure.");
                    return valueResult;
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static bool IsKnownPureArrayFactoryReturn(
            IOperation? returnedValue,
            out IMethodSymbol factoryMethod)
        {
            var unwrappedReturnedValue = PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions(returnedValue);
            if (PurityAnalysisEngine.IsKnownPureBCLArrayFactoryOperation(unwrappedReturnedValue, out factoryMethod))
            {
                return true;
            }

            if (unwrappedReturnedValue is IConditionalOperation conditionalOperation)
            {
                if (TryGetConstantCondition(conditionalOperation, out var conditionValue))
                {
                    return IsKnownPureArrayFactoryReturn(
                        conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse,
                        out factoryMethod);
                }

                return IsKnownPureArrayFactoryReturn(conditionalOperation.WhenTrue, out factoryMethod) ||
                    IsKnownPureArrayFactoryReturn(conditionalOperation.WhenFalse, out factoryMethod);
            }

            if (unwrappedReturnedValue is ICoalesceOperation coalesceOperation)
            {
                return IsKnownPureArrayFactoryReturn(coalesceOperation.Value, out factoryMethod) ||
                    IsKnownPureArrayFactoryReturn(coalesceOperation.WhenNull, out factoryMethod);
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
            if (unwrappedReturnedValue is IInvocationOperation invocationOperation &&
                invocationOperation.Type is IArrayTypeSymbol)
            {
                var originalDefinition = invocationOperation.TargetMethod.OriginalDefinition;
                if (IsArrayEmptyFactory(originalDefinition) ||
                    PurityAnalysisEngine.IsKnownFreshOwnedArrayReturningMember(originalDefinition))
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
                if (TryGetConstantCondition(conditionalOperation, out var conditionValue))
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

            if (HasAssignmentToLocalBetweenDeclarationAndReturn(localSymbol, returnedValue, declaratorSyntax, semanticModel))
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
                invocationOperation.Type is IArrayTypeSymbol &&
                !IsArrayEmptyFactory(invocationOperation.TargetMethod.OriginalDefinition))
            {
                methodSymbol = invocationOperation.TargetMethod.OriginalDefinition;
                return true;
            }

            if (unwrappedReturnedValue is IConditionalOperation conditionalOperation)
            {
                if (TryGetConstantCondition(conditionalOperation, out var conditionValue))
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

        private static bool IsCallerOwnedArraySpanReturn(
            IOperation? returnedValue,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            out IMethodSymbol methodSymbol)
        {
            var unwrappedReturnedValue = PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions(returnedValue);
            if (unwrappedReturnedValue is IInvocationOperation invocationOperation &&
                IsMemoryExtensionsArrayAsSpan(invocationOperation.TargetMethod.OriginalDefinition) &&
                TryGetArraySpanSource(invocationOperation, out var sourceOperation) &&
                !IsAnalyzerOwnedArraySpanSource(sourceOperation, currentState))
            {
                methodSymbol = invocationOperation.TargetMethod.OriginalDefinition;
                return true;
            }

            if (unwrappedReturnedValue is IConditionalOperation conditionalOperation)
            {
                if (TryGetConstantCondition(conditionalOperation, out var conditionValue))
                {
                    return IsCallerOwnedArraySpanReturn(
                        conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse,
                        currentState,
                        out methodSymbol);
                }

                return IsCallerOwnedArraySpanReturn(conditionalOperation.WhenTrue, currentState, out methodSymbol) ||
                    IsCallerOwnedArraySpanReturn(conditionalOperation.WhenFalse, currentState, out methodSymbol);
            }

            if (unwrappedReturnedValue is ICoalesceOperation coalesceOperation)
            {
                return IsCallerOwnedArraySpanReturn(coalesceOperation.Value, currentState, out methodSymbol) ||
                    IsCallerOwnedArraySpanReturn(coalesceOperation.WhenNull, currentState, out methodSymbol);
            }

            methodSymbol = null!;
            return false;
        }

        private static bool IsCallerOwnedArrayMemoryReturn(
            IOperation? returnedValue,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            out IMethodSymbol constructorSymbol)
        {
            var unwrappedReturnedValue = PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions(returnedValue);
            if (unwrappedReturnedValue is IObjectCreationOperation objectCreationOperation &&
                IsMemoryViewConstructor(objectCreationOperation.Constructor) &&
                objectCreationOperation.Arguments.Length > 0 &&
                !IsAnalyzerOwnedArraySpanSource(objectCreationOperation.Arguments[0].Value, currentState))
            {
                constructorSymbol = objectCreationOperation.Constructor!;
                return true;
            }

            if (unwrappedReturnedValue is IConditionalOperation conditionalOperation)
            {
                if (TryGetConstantCondition(conditionalOperation, out var conditionValue))
                {
                    return IsCallerOwnedArrayMemoryReturn(
                        conditionValue ? conditionalOperation.WhenTrue : conditionalOperation.WhenFalse,
                        currentState,
                        out constructorSymbol);
                }

                return IsCallerOwnedArrayMemoryReturn(conditionalOperation.WhenTrue, currentState, out constructorSymbol) ||
                    IsCallerOwnedArrayMemoryReturn(conditionalOperation.WhenFalse, currentState, out constructorSymbol);
            }

            if (unwrappedReturnedValue is ICoalesceOperation coalesceOperation)
            {
                return IsCallerOwnedArrayMemoryReturn(coalesceOperation.Value, currentState, out constructorSymbol) ||
                    IsCallerOwnedArrayMemoryReturn(coalesceOperation.WhenNull, currentState, out constructorSymbol);
            }

            constructorSymbol = null!;
            return false;
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

        private static bool IsAnalyzerOwnedArraySpanSource(
            IOperation? sourceOperation,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            var unwrappedSource = PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions(sourceOperation);
            if (unwrappedSource is ILocalReferenceOperation localReference)
            {
                return currentState.IsOwnedLocalArraySymbol(localReference.Local);
            }

            return PurityAnalysisEngine.IsKnownPureBCLArrayFactoryOperation(unwrappedSource, out var factoryMethod) &&
                IsArrayEmptyFactory(factoryMethod);
        }

        private static bool IsMemoryExtensionsArrayAsSpan(IMethodSymbol methodSymbol)
        {
            return methodSymbol.Name == "AsSpan" &&
                methodSymbol.Parameters.Length >= 1 &&
                methodSymbol.Parameters[0].Type is IArrayTypeSymbol &&
                methodSymbol.ContainingType?.ToDisplayString() == "System.MemoryExtensions";
        }

        private static bool IsArrayEmptyFactory(IMethodSymbol methodSymbol)
        {
            return methodSymbol.Name == "Empty" &&
                methodSymbol.Parameters.Length == 0 &&
                methodSymbol.ContainingType?.SpecialType == SpecialType.System_Array;
        }

        private static bool IsOwnedLocalArrayReturn(
            IOperation? returnedValue,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            out ILocalSymbol localSymbol)
        {
            var unwrappedReturnedValue = PurityAnalysisEngine.UnwrapArrayOwnershipPreservingConversions(returnedValue);
            if (unwrappedReturnedValue is ILocalReferenceOperation localReference &&
                currentState.IsOwnedLocalArraySymbol(localReference.Local))
            {
                localSymbol = localReference.Local;
                return true;
            }

            if (unwrappedReturnedValue is IConditionalOperation conditionalOperation)
            {
                if (TryGetConstantCondition(conditionalOperation, out var conditionValue))
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

                if (IsKnownPureArrayFactoryReturn(assignment.Value, out var factoryMethod))
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

                    if (IsKnownPureArrayFactoryReturn(argument.Value, out var factoryMethod))
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
                IsFreshMutableEscapingReferenceType(objectCreationOperation.Type))
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
                if (TryGetConstantCondition(conditionalOperation, out var conditionValue))
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

            if (HasAssignmentToLocalBetweenDeclarationAndReturn(localSymbol, returnedValue, declaratorSyntax, semanticModel))
            {
                escapeSyntax = null!;
                escapeSymbol = null!;
                catalogSource = string.Empty;
                return false;
            }

            var initializerOperation = PurityAnalysisEngine.SkipImplicitConversions(semanticModel.GetOperation(initializerSyntax));
            if (initializerOperation is IObjectCreationOperation objectCreationOperation &&
                IsFreshMutableEscapingReferenceType(objectCreationOperation.Type))
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

        private static bool HasAssignmentToLocalBetweenDeclarationAndReturn(
            ILocalSymbol localSymbol,
            IOperation returnedValue,
            VariableDeclaratorSyntax declaratorSyntax,
            SemanticModel semanticModel)
        {
            var containingBlock = returnedValue.Syntax.FirstAncestorOrSelf<BlockSyntax>();
            if (containingBlock == null)
            {
                return false;
            }

            var start = declaratorSyntax.Span.End;
            var end = returnedValue.Syntax.SpanStart;
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

        private static bool IsFreshMutableEscapingReferenceType(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol is not INamedTypeSymbol namedType ||
                namedType.TypeKind == TypeKind.Delegate ||
                namedType.IsValueType ||
                namedType.SpecialType == SpecialType.System_String ||
                namedType.DeclaringSyntaxReferences.Length == 0)
            {
                return false;
            }

            return namedType.GetMembers().Any(member =>
                member switch
                {
                    IFieldSymbol field => !field.IsStatic &&
                                          !field.IsReadOnly &&
                                          field.DeclaredAccessibility != Accessibility.Private,
                    IPropertySymbol property => !property.IsStatic &&
                                                property.SetMethod != null &&
                                                !property.SetMethod.IsInitOnly &&
                                                property.SetMethod.DeclaredAccessibility != Accessibility.Private,
                    _ => false
                });
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

        private static bool TryGetConstantCondition(IConditionalOperation conditionalOperation, out bool conditionValue)
        {
            if (conditionalOperation.Condition.ConstantValue.HasValue &&
                conditionalOperation.Condition.ConstantValue.Value is bool constantBool)
            {
                conditionValue = constantBool;
                return true;
            }

            conditionValue = false;
            return false;
        }
    }
}
