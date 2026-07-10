using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Symbolic;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal partial class MethodInvocationPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Invocation);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IInvocationOperation invocationOperation))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var visibilitySyntax = GetVisibilitySyntax(invocationOperation);
            if (ExecutionVisibility.IsInStaticallyUnreachableBranchUsingSmt(
                    visibilitySyntax,
                    context.SemanticModel,
                    context.CancellationToken,
                    context.SmtAnalysis))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var invokedMethodSymbol = invocationOperation.TargetMethod;
            if (invokedMethodSymbol == null)
            {
                return PurityAnalysisEngine.ImpureResult(
                    invocationOperation,
                    "unsupported_operation",
                    nameof(MethodInvocationPurityRule));
            }

            if (TryCheckDoubleDispose(invocationOperation, invokedMethodSymbol, context, currentState, out var doubleDisposeResult))
            {
                return doubleDisposeResult;
            }

            if (TryCheckUseAfterDispose(invocationOperation, invokedMethodSymbol, context, currentState, out var useAfterDisposeResult))
            {
                return useAfterDisposeResult;
            }

            if (TryCheckByRefArgumentBorrowConflict(invocationOperation, context, currentState, out var byRefBorrowConflictResult))
            {
                return byRefBorrowConflictResult;
            }

            if (TryCheckSystemTypeMemberPurity(
                    invocationOperation,
                    context,
                    currentState,
                    nameof(object.Equals),
                    parameterCount: 1,
                    out var earlyTypeEqualityResult))
            {
                return earlyTypeEqualityResult;
            }

            if (TryCheckSystemTypeMemberPurity(
                    invocationOperation,
                    context,
                    currentState,
                    nameof(object.GetHashCode),
                    parameterCount: 0,
                    out var typeHashCodeResult))
            {
                return typeHashCodeResult;
            }

            if (TryCheckStringComparerInvocationPurity(invocationOperation, context, currentState, out var stringComparerResult))
            {
                return stringComparerResult;
            }

            if (TryCheckMetadataMemberOperandPurity(
                    invocationOperation,
                    context,
                    currentState,
                    "System.Enum",
                    static methodSymbol =>
                        (methodSymbol.Name == "HasFlag" && methodSymbol.Parameters.Length == 1) ||
                        (methodSymbol.Name == "ToString" && methodSymbol.Parameters.Length == 0),
                    out var enumResult))
            {
                return enumResult;
            }

            if (TryCheckMetadataMemberOperandPurity(
                    invocationOperation,
                    context,
                    currentState,
                    "System.FormattableString",
                    static methodSymbol =>
                        methodSymbol.Parameters.Length == 1 &&
                        ((methodSymbol.IsStatic && methodSymbol.Name == "Invariant") ||
                            (!methodSymbol.IsStatic && methodSymbol.Name == "ToString")),
                    out var formattableStringResult))
            {
                return formattableStringResult;
            }

            if (TryCheckCompilerGeneratedInterpolatedStringHandlerPurity(invocationOperation, context, currentState, out var interpolatedStringHandlerResult))
            {
                return interpolatedStringHandlerResult;
            }

            if (TryCheckUnsafeReadUnalignedPurity(invocationOperation, context, currentState, out var unsafeReadUnalignedResult))
            {
                return unsafeReadUnalignedResult;
            }

            if (IsCompilerGeneratedArrayForeachInvocation(invocationOperation, context))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (TryCheckArrayInterfaceGetEnumeratorPurity(invocationOperation, context, out var earlyArrayEnumeratorResult))
            {
                return earlyArrayEnumeratorResult;
            }

            if (invocationOperation.Instance != null && IsDynamicInvocationReceiver(invocationOperation.Instance))
            {
                return PurityAnalysisEngine.ImpureResult(
                    invocationOperation,
                    "dynamic_dispatch",
                    nameof(MethodInvocationPurityRule),
                    invokedMethodSymbol);
            }


            if (TryCheckDelegateInvocationPurity(
                    invocationOperation,
                    invokedMethodSymbol,
                    context,
                    currentState,
                    out var delegateInvocationResult))
            {
                return delegateInvocationResult;
            }



            if (invokedMethodSymbol.IsExtensionMethod &&
                invocationOperation.Arguments.Length > 0 &&
                IsDynamicInvocationReceiver(invocationOperation.Arguments[0].Value))
            {
                return PurityAnalysisEngine.ImpureResult(
                    invocationOperation,
                    "dynamic_dispatch",
                    nameof(MethodInvocationPurityRule),
                    invokedMethodSymbol);
            }

            if (IsLinqEnumerableInvocation(invokedMethodSymbol, context.SemanticModel.Compilation))
            {




                var sourceOperation = invocationOperation.Instance;
                var firstRemainingArgumentIndex = 0;
                if (sourceOperation == null && invocationOperation.Arguments.Length > 0)
                {
                    sourceOperation = invocationOperation.Arguments[0].Value;
                    firstRemainingArgumentIndex = 1;
                }

                if (sourceOperation != null)
                {
                    if (IsImmediateFreshArrayLinqSource(sourceOperation, context.SemanticModel.Compilation))
                    {
                    }
                    else
                    {
                        var sourceResult = PurityAnalysisEngine.CheckSingleOperation(sourceOperation, context, currentState);

                        if (!sourceResult.IsPure)
                        {

                            return sourceResult;
                        }

                        var sourceEnumeratorResult = CheckLinqSourceEnumeratorPurity(sourceOperation, context, currentState);
                        if (!sourceEnumeratorResult.IsPure)
                        {
                            return sourceEnumeratorResult;
                        }
                    }
                }
                else
                {
                    if (IsLinqSourceLessFactory(invokedMethodSymbol))
                    {
                    }
                    else
                    {
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            invocationOperation.Syntax,
                            PurityAnalysisEngine.PurityEvidence.Create(
                                "unsupported_operation",
                                nameof(MethodInvocationPurityRule),
                                invocationOperation,
                                symbol: invokedMethodSymbol));
                    }
                }


                for (int argumentIndex = firstRemainingArgumentIndex; argumentIndex < invocationOperation.Arguments.Length; argumentIndex++)
                {
                    var argument = invocationOperation.Arguments[argumentIndex];
                    var parameter = argument.Parameter;
                    var argumentKind = parameter?.Type?.TypeKind == TypeKind.Delegate ? "delegate" : "non-delegate";

                    var argumentResult = PurityAnalysisEngine.CheckSingleOperation(argument.Value, context, currentState);
                    if (!argumentResult.IsPure)
                    {
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            argumentResult.ImpureSyntaxNode ?? argument.Value.Syntax,
                            argumentResult.Evidence);
                    }

                    var delegateTargetResult = CheckDelegateArgumentTargetPurity(argument, context, currentState);
                    if (!delegateTargetResult.IsPure)
                    {
                        return delegateTargetResult;
                    }

                    var comparerResult = CheckLinqComparerArgumentPurity(argument, context);
                    if (!comparerResult.IsPure)
                    {
                        return comparerResult;
                    }

                    var enumerableArgumentResult = CheckLinqSourceEnumeratorPurity(argument.Value, context, currentState);
                    if (!enumerableArgumentResult.IsPure)
                    {
                        return enumerableArgumentResult;
                    }
                }

                if (TryCheckLinqDefaultEqualityDispatchPurity(invocationOperation, context, out var linqEqualityDispatchResult))
                {
                    return linqEqualityDispatchResult;
                }

                if (TryCheckLinqDefaultComparisonDispatchPurity(invocationOperation, context, out var linqComparisonDispatchResult))
                {
                    return linqComparisonDispatchResult;
                }

                var linqMethodName = invokedMethodSymbol.Name;
                if (linqMethodName is "ToList" or "ToDictionary" or "ToHashSet")
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        invocationOperation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "mutable_state_write",
                            nameof(MethodInvocationPurityRule),
                            invocationOperation,
                            symbol: invokedMethodSymbol,
                            catalogSource: "linq_materializer"));
                }

                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var dispatchWasProvenPure = false;
            if (!ShouldDeferToSpecializedDispatchPurity(invokedMethodSymbol) &&
                DispatchedMemberResolution.IsPotentiallyDispatchedMethod(invokedMethodSymbol, context.SemanticModel.Compilation)
                && (invokedMethodSymbol.IsStatic
                    ? invocationOperation.Instance == null
                    : invocationOperation.Instance != null
                        && !IsBaseReference(invocationOperation.Instance)))
            {
                var exactReceiverType = GetTrackedLocalReceiverType(
                    invocationOperation.Instance,
                    currentState,
                    context.SemanticModel.Compilation);
                var hasExactReceiverType = exactReceiverType != null;
                var knownReceiverType = exactReceiverType ??
                    GetStableInitializerReceiverType(invocationOperation.Instance, context, currentState) ??
                    GetKnownReceiverType(invocationOperation.Instance);
                if (knownReceiverType == null)
                {
                    knownReceiverType = GetKnownStaticInterfaceReceiverType(invokedMethodSymbol);
                }

                var dispatchResult = CheckDispatchedInvocationPurity(
                    invocationOperation,
                    context,
                    knownReceiverType,
                    hasExactReceiverType);
                if (!dispatchResult.IsPure)
                {
                    return dispatchResult;
                }

                dispatchWasProvenPure = true;
            }

            if (invocationOperation.Instance != null
                && !IsBaseReference(invocationOperation.Instance)
                && invocationOperation.Instance is not IConditionalAccessInstanceOperation)
            {
                var instanceResult = PurityAnalysisEngine.CheckSingleOperation(invocationOperation.Instance, context, currentState);
                if (!instanceResult.IsPure)
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        instanceResult.ImpureSyntaxNode ?? invocationOperation.Instance.Syntax,
                        instanceResult.Evidence);
                }
            }


            var originalDefinitionSymbol = invokedMethodSymbol.OriginalDefinition;
            if (PurityAnalysisEngine.HasImpureAttribute(originalDefinitionSymbol))
            {
                return PurityAnalysisEngine.ImpureResult(
                    invocationOperation,
                    "impure_boundary_attribute",
                    nameof(MethodInvocationPurityRule),
                    originalDefinitionSymbol,
                    "attribute");
            }

            var trustedMetadataPurity = PurityAnalysisEngine.GetTrustedMethodPurityMetadata(
                originalDefinitionSymbol,
                context.SemanticModel.Compilation);
            var hasTrustedGeneratedPurity = trustedMetadataPurity.HasTrustedGeneratedPurity;
            var generatedPurity = trustedMetadataPurity.GeneratedPurity;
            var allowsKnownPureFallback = trustedMetadataPurity.AllowsKnownPureFallback;
            var isImmutableHashSetCreateRangeWithComparer = IsImmutableHashSetCreateRangeWithComparer(originalDefinitionSymbol);

            // Skip cctor check only when the generated runtime summary already classifies the method as pure,
            // or when a known-pure override has already taken precedence.
            if (invokedMethodSymbol.IsStatic && invokedMethodSymbol.ContainingType != null
                && !(hasTrustedGeneratedPurity && generatedPurity.IsPure)
                && !isImmutableHashSetCreateRangeWithComparer
                && !PurityAnalysisEngine.IsKnownPureBCLMember(
                    originalDefinitionSymbol,
                    context.SemanticModel.Compilation))
            {
                var cctorResult = PurityAnalysisEngine.CheckStaticConstructorPurity(invokedMethodSymbol.ContainingType, context, currentState);
                if (!cctorResult.IsPure)
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        cctorResult.ImpureSyntaxNode ?? invocationOperation.Syntax,
                        cctorResult.Evidence);
                }
            }

            foreach (var argument in invocationOperation.Arguments)
            {
                if (argument.Parameter?.RefKind is RefKind.Out or RefKind.Ref)
                {
                    var allowsTrustedPureRefRead = argument.Parameter.RefKind == RefKind.Ref &&
                        hasTrustedGeneratedPurity &&
                        generatedPurity.IsPure;
                    if (!IsPureOutArgumentTarget(argument.Value) && !allowsTrustedPureRefRead)
                    {
                        return PurityAnalysisEngine.ImpureResult(
                            argument,
                            "mutable_state_write",
                            nameof(MethodInvocationPurityRule),
                            PurityAnalysisEngine.TryResolveSymbol(argument.Value) ?? originalDefinitionSymbol);
                    }

                    if (argument.Parameter.RefKind == RefKind.Out &&
                        IsDeclarationOrDiscardOutArgumentTarget(argument.Value) &&
                        IsDeconstructOutArgumentMethod(invokedMethodSymbol))
                    {
                        continue;
                    }

                    if (argument.Parameter.RefKind == RefKind.Out &&
                        (hasTrustedGeneratedPurity ||
                         (allowsKnownPureFallback &&
                          PurityAnalysisEngine.IsKnownPureBCLMember(
                              originalDefinitionSymbol,
                              context.SemanticModel.Compilation)) ||
                         IsSemanticallyPureOutArgumentMethod(originalDefinitionSymbol) ||
                         IsDispatchAnalyzedOutArgumentMethod(invokedMethodSymbol)))
                    {
                        continue;
                    }
                }

                var argumentResult = PurityAnalysisEngine.CheckSingleOperation(argument.Value, context, currentState);
                if (!argumentResult.IsPure)
                {

                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        argumentResult.ImpureSyntaxNode ?? argument.Value.Syntax,
                        argumentResult.Evidence);
                }
            }

            if (isImmutableHashSetCreateRangeWithComparer)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (TryCheckKnownDelegateInvokingBclInvocationPurity(
                    invocationOperation,
                    originalDefinitionSymbol,
                    context,
                    currentState,
                    out var knownDelegateInvokingBclResult))
            {
                return knownDelegateInvokingBclResult;
            }

            if (TryCheckEqualityComparerDispatchPurity(invocationOperation, context, out var equalityComparerDispatchResult))
            {
                return equalityComparerDispatchResult;
            }

            if (TryCheckComparerDispatchPurity(invocationOperation, context, out var comparerDispatchResult))
            {
                return comparerDispatchResult;
            }

            if (TryCheckNullableComparisonDispatchPurity(invocationOperation, context, out var nullableDispatchResult))
            {
                return nullableDispatchResult;
            }

            if (TryCheckCollectionEqualityDispatchPurity(invocationOperation, context, currentState, out var collectionEqualityDispatchResult))
            {
                return collectionEqualityDispatchResult;
            }

            if (TryCheckMemoryExtensionsDefaultEqualityDispatchPurity(invocationOperation, context, out var memoryExtensionsEqualityDispatchResult))
            {
                return memoryExtensionsEqualityDispatchResult;
            }

            if (TryCheckHashCodeCombineDispatchPurity(invocationOperation, context, out var hashCodeCombineDispatchResult))
            {
                return hashCodeCombineDispatchResult;
            }

            if (TryCheckCollectionComparisonDispatchPurity(invocationOperation, context, out var collectionComparisonDispatchResult))
            {
                return collectionComparisonDispatchResult;
            }

            if (TryCheckStringComparisonPurity(invocationOperation, out var stringComparisonResult))
            {
                return stringComparisonResult;
            }

            if (TryCheckStringEnumerableJoinPurity(invocationOperation, context, currentState, out var stringEnumerableJoinResult))
            {
                return stringEnumerableJoinResult;
            }

            if (TryCheckSemanticallyPureParsePurity(invocationOperation, context, currentState, out var semanticParseResult))
            {
                return semanticParseResult;
            }

            if (invocationOperation.Type is IArrayTypeSymbol &&
                PurityAnalysisEngine.IsTrustedFreshArrayFactoryOperation(
                    invocationOperation,
                    context.SemanticModel.Compilation,
                    out _))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (PurityAnalysisEngine.HasPureExternalAttribute(originalDefinitionSymbol))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            string methodDisplayString = originalDefinitionSymbol.ToDisplayString();


            if (TryCheckArrayAsReadOnlyOwnedLocalArrayPurity(invocationOperation, context, currentState, out var arrayAsReadOnlyResult))
            {
                return arrayAsReadOnlyResult;
            }

            if (TryCheckSpanAndMemoryViewPurity(invocationOperation, context, currentState, out var spanAndMemoryViewResult))
            {
                return spanAndMemoryViewResult;
            }

            if (PurityAnalysisEngine.IsInvariantCultureDeterministicParseInvocation(invocationOperation))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (IsContractGuardInvocation(originalDefinitionSymbol))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (PurityAnalysisEngine.TryGetSemanticKnownImpureCatalogSource(invocationOperation, out var semanticCatalogSource))
            {
                return PurityAnalysisEngine.ImpureResult(
                    invocationOperation,
                    "catalog_hit",
                    nameof(MethodInvocationPurityRule),
                    originalDefinitionSymbol,
                    semanticCatalogSource);
            }

            var knownImpureMemberSource = trustedMetadataPurity.KnownImpureMemberSource;
            var hasConfiguredKnownImpureMember = trustedMetadataPurity.HasConfiguredKnownImpureMember;

            bool isExplicitlyPure = PurityAnalysisEngine.IsPureEnforced(
                invokedMethodSymbol,
                context.EnforcePureAttributeSymbol,
                context.PureAttributeSymbol);
            if (hasConfiguredKnownImpureMember)
            {
                return PurityAnalysisEngine.ImpureResult(
                    invocationOperation,
                    "global_state_write",
                    nameof(MethodInvocationPurityRule),
                    originalDefinitionSymbol,
                    knownImpureMemberSource);
            }

            if (ShouldPreferSemanticImpurityEvidence(knownImpureMemberSource))
            {
                return PurityAnalysisEngine.ImpureResult(
                    invocationOperation,
                    GetCatalogHitCategory(originalDefinitionSymbol),
                    nameof(MethodInvocationPurityRule),
                    originalDefinitionSymbol,
                    knownImpureMemberSource);
            }

            if (hasTrustedGeneratedPurity &&
                !ShouldDeferToSpecializedDispatchPurity(invokedMethodSymbol))
            {
                if (generatedPurity.IsPure)
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }

                if (!generatedPurity.IsPure)
                {
                    return PurityAnalysisEngine.ImpureResult(
                        invocationOperation,
                        generatedPurity.PrimaryCategory,
                        nameof(MethodInvocationPurityRule),
                        originalDefinitionSymbol,
                        "generated_purity_summary");
                }
            }

            if (knownImpureMemberSource != null)
            {
                return PurityAnalysisEngine.ImpureResult(
                    invocationOperation,
                    GetCatalogHitCategory(originalDefinitionSymbol),
                    nameof(MethodInvocationPurityRule),
                    originalDefinitionSymbol,
                    knownImpureMemberSource);
            }

            if (PurityAnalysisEngine.IsInConfiguredImpureNamespaceOrType(originalDefinitionSymbol) &&
                !isExplicitlyPure &&
                !PurityAnalysisEngine.IsConfiguredKnownPureMember(originalDefinitionSymbol))
            {
                return PurityAnalysisEngine.ImpureResult(
                    invocationOperation,
                    GetCatalogHitCategory(originalDefinitionSymbol),
                    nameof(MethodInvocationPurityRule),
                    originalDefinitionSymbol,
                    "known_impure_namespace_or_type");
            }

            if (allowsKnownPureFallback &&
                PurityAnalysisEngine.IsKnownPureBCLMember(
                    originalDefinitionSymbol,
                    context.SemanticModel.Compilation))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (PurityAnalysisEngine.IsInImpureNamespaceOrType(originalDefinitionSymbol) && !isExplicitlyPure)
            {
                return PurityAnalysisEngine.ImpureResult(
                    invocationOperation,
                    GetCatalogHitCategory(originalDefinitionSymbol),
                    nameof(MethodInvocationPurityRule),
                    originalDefinitionSymbol,
                    "known_impure_namespace_or_type");
            }

            if (invocationOperation.Type is IArrayTypeSymbol &&
                PurityAnalysisEngine.IsTrustedGeneratedFreshOwnedArrayReturningMember(
                    originalDefinitionSymbol,
                    context.SemanticModel.Compilation))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (dispatchWasProvenPure)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (allowsKnownPureFallback &&
                PurityAnalysisEngine.TryCreateBclFallbackImpurity(
                    originalDefinitionSymbol,
                    invocationOperation.Syntax,
                    invocationOperation,
                    nameof(MethodInvocationPurityRule),
                    out var bclFallbackResult))
            {
                return bclFallbackResult;
            }

            if (IsUntrustedMetadataOnlyMethod(originalDefinitionSymbol))
            {
                return PurityAnalysisEngine.ImpureResult(
                    invocationOperation,
                    "unknown_external_call",
                    nameof(MethodInvocationPurityRule),
                    originalDefinitionSymbol,
                    "metadata");
            }


            if (SymbolEqualityComparer.Default.Equals(
                    originalDefinitionSymbol,
                    context.ContainingMethodSymbol.OriginalDefinition))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var calleePurity = PurityAnalysisEngine.GetCalleePurity(originalDefinitionSymbol, context);


            if (CanTreatFreshMutableObjectReturningNestedCallableInvocationAsPure(originalDefinitionSymbol, calleePurity))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            return calleePurity.IsPure
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : calleePurity.WithCallee(originalDefinitionSymbol, invocationOperation.Syntax);
        }

        private static SyntaxNode GetVisibilitySyntax(IInvocationOperation invocationOperation)
        {
            return invocationOperation.Syntax is ConditionalAccessExpressionSyntax conditionalAccess
                ? conditionalAccess.WhenNotNull
                : invocationOperation.Syntax;
        }

        private static bool CanTreatFreshMutableObjectReturningNestedCallableInvocationAsPure(
            IMethodSymbol targetMethod,
            PurityAnalysisEngine.PurityAnalysisResult calleePurity)
        {
            return (targetMethod.MethodKind == MethodKind.LocalFunction ||
                    targetMethod.MethodKind == MethodKind.AnonymousFunction ||
                    targetMethod.MethodKind == MethodKind.Ordinary) &&
                !calleePurity.IsPure &&
                string.Equals(calleePurity.Evidence.Category, "mutable_state_escape", StringComparison.Ordinal) &&
                calleePurity.Evidence.CatalogSource.StartsWith("fresh_mutable_object_", StringComparison.Ordinal);
        }


    }
}
