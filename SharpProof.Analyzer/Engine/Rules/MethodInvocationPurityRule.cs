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
                PurityAnalysisEngine.LogDebug("  [MIR] WARNING: Called with non-invocation.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var visibilitySyntax = GetVisibilitySyntax(invocationOperation);
            if (ExecutionVisibility.IsInStaticallyUnreachableBranchUsingSmt(
                    visibilitySyntax,
                    context.SemanticModel,
                    context.CancellationToken,
                    context.SmtAnalysis))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] Invocation is in an SMT-proven unreachable branch. Treating as pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var invokedMethodSymbol = invocationOperation.TargetMethod;
            if (invokedMethodSymbol == null)
            {
                PurityAnalysisEngine.LogDebug("  [MIR] Cannot resolve target method. Assuming impure.");
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
                PurityAnalysisEngine.LogDebug("  [MIR] Compiler-generated array foreach member is treated as pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (TryCheckArrayInterfaceGetEnumeratorPurity(invocationOperation, context, out var earlyArrayEnumeratorResult))
            {
                return earlyArrayEnumeratorResult;
            }

            if (invocationOperation.Instance != null && IsDynamicInvocationReceiver(invocationOperation.Instance))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] Invocation on dynamic instance is treated as conservative impure.");
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
                PurityAnalysisEngine.LogDebug("  [MIR] Extension invocation on dynamic receiver is treated as conservative impure.");
                return PurityAnalysisEngine.ImpureResult(
                    invocationOperation,
                    "dynamic_dispatch",
                    nameof(MethodInvocationPurityRule),
                    invokedMethodSymbol);
            }

            if (IsLinqEnumerableInvocation(invokedMethodSymbol, context.SemanticModel.Compilation))
            {
                PurityAnalysisEngine.LogDebug($"  [MIR] Detected LINQ Enumerable extension method: {invokedMethodSymbol.Name}. Checking source and delegate arguments.");




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
                        PurityAnalysisEngine.LogDebug("  [MIR]   LINQ source is an immediate reviewed fresh array producer; skipping conservative source re-analysis.");
                    }
                    else
                    {
                        PurityAnalysisEngine.LogDebug($"  [MIR]   Checking LINQ source argument purity: {sourceOperation.Kind}");
                        var sourceResult = PurityAnalysisEngine.CheckSingleOperation(sourceOperation, context, currentState);

                        if (!sourceResult.IsPure)
                        {
                            PurityAnalysisEngine.LogDebug($"  [MIR] --> IMPURE (LINQ source argument was impure)");

                            return sourceResult;
                        }

                        var sourceEnumeratorResult = CheckLinqSourceEnumeratorPurity(sourceOperation, context, currentState);
                        if (!sourceEnumeratorResult.IsPure)
                        {
                            PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (LINQ source GetEnumerator was impure)");
                            return sourceEnumeratorResult;
                        }
                    }
                }
                else
                {
                    if (IsLinqSourceLessFactory(invokedMethodSymbol))
                    {
                        PurityAnalysisEngine.LogDebug($"  [MIR]   LINQ source-less factory method {invokedMethodSymbol.Name}; checking factory arguments only.");
                    }
                    else
                    {
                        PurityAnalysisEngine.LogDebug($"  [MIR]   WARNING: LINQ method {invokedMethodSymbol.Name} called with no enumerable source. Assuming impure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            invocationOperation.Syntax,
                            PurityAnalysisEngine.PurityEvidence.Create(
                                "unsupported_operation",
                                nameof(MethodInvocationPurityRule),
                                invocationOperation,
                                symbol: invokedMethodSymbol));
                    }
                }


                PurityAnalysisEngine.LogDebug("  [MIR]   LINQ source was pure. Checking remaining arguments...");
                for (int argumentIndex = firstRemainingArgumentIndex; argumentIndex < invocationOperation.Arguments.Length; argumentIndex++)
                {
                    var argument = invocationOperation.Arguments[argumentIndex];
                    var parameter = argument.Parameter;
                    var argumentKind = parameter?.Type?.TypeKind == TypeKind.Delegate ? "delegate" : "non-delegate";
                    PurityAnalysisEngine.LogDebug($"  [MIR]   Checking LINQ {argumentKind} argument '{parameter?.Name ?? "<unknown>"}' (Arg Index {argumentIndex}) for operation: {argument.Value.Kind}");

                    var argumentResult = PurityAnalysisEngine.CheckSingleOperation(argument.Value, context, currentState);
                    PurityAnalysisEngine.LogDebug($"  [MIR]   LINQ argument '{parameter?.Name ?? "<unknown>"}' result: IsPure={argumentResult.IsPure}");
                    if (!argumentResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (LINQ method, impure argument detected)");
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            argumentResult.ImpureSyntaxNode ?? argument.Value.Syntax,
                            argumentResult.Evidence);
                    }

                    var delegateTargetResult = CheckDelegateArgumentTargetPurity(argument, context, currentState);
                    if (!delegateTargetResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (LINQ delegate argument target was impure or unresolved)");
                        return delegateTargetResult;
                    }

                    var comparerResult = CheckLinqComparerArgumentPurity(argument, context);
                    if (!comparerResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (LINQ comparer argument has impure comparison implementation)");
                        return comparerResult;
                    }

                    var enumerableArgumentResult = CheckLinqSourceEnumeratorPurity(argument.Value, context, currentState);
                    if (!enumerableArgumentResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (LINQ enumerable argument GetEnumerator was impure)");
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

                PurityAnalysisEngine.LogDebug("  [MIR] LINQ source and all remaining arguments determined to be pure.");
                var linqMethodName = invokedMethodSymbol.Name;
                if (linqMethodName is "ToList" or "ToDictionary" or "ToHashSet")
                {
                    PurityAnalysisEngine.LogDebug($"  [MIR] --> IMPURE (LINQ materializer '{linqMethodName}' creates a mutable collection)");
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

                PurityAnalysisEngine.LogDebug($"  [MIR] Checking potential dispatch candidates for {invokedMethodSymbol.Name}.");
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
                PurityAnalysisEngine.LogDebug($"  [MIR] Checking instance purity for {invocationOperation.Instance.Kind}: {invocationOperation.Instance.Syntax.ToString().Trim()}");
                var instanceResult = PurityAnalysisEngine.CheckSingleOperation(invocationOperation.Instance, context, currentState);
                PurityAnalysisEngine.LogDebug($"  [MIR] Instance check result: IsPure={instanceResult.IsPure}, Node Type={instanceResult.ImpureSyntaxNode?.GetType().Name ?? "NULL"}");
                if (!instanceResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (Instance is impure)");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        instanceResult.ImpureSyntaxNode ?? invocationOperation.Instance.Syntax,
                        instanceResult.Evidence);
                }
            }


            var originalDefinitionSymbol = invokedMethodSymbol.OriginalDefinition;
            if (PurityAnalysisEngine.HasImpureAttribute(originalDefinitionSymbol))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE ([Impure] boundary attribute)");
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
                    PurityAnalysisEngine.LogDebug($"  [MIR] Static method call '{invokedMethodSymbol.Name}' IMPURE due to impure static constructor in {invokedMethodSymbol.ContainingType.Name}.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        cctorResult.ImpureSyntaxNode ?? invocationOperation.Syntax,
                        cctorResult.Evidence);
                }
            }

            PurityAnalysisEngine.LogDebug($"  [MIR] Checking purity of {invocationOperation.Arguments.Length} arguments for {originalDefinitionSymbol.Name}.");
            foreach (var argument in invocationOperation.Arguments)
            {
                if (argument.Parameter?.RefKind is RefKind.Out or RefKind.Ref)
                {
                    var allowsTrustedPureRefRead = argument.Parameter.RefKind == RefKind.Ref &&
                        hasTrustedGeneratedPurity &&
                        generatedPurity.IsPure;
                    if (!IsPureOutArgumentTarget(argument.Value) && !allowsTrustedPureRefRead)
                    {
                        PurityAnalysisEngine.LogDebug($"  [MIR]   By-reference argument '{argument.Syntax}' writes to non-local state.");
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
                        PurityAnalysisEngine.LogDebug($"  [MIR]   Skipping declaration/discard Deconstruct out argument target '{argument.Syntax}'. Callee purity is checked separately.");
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
                        PurityAnalysisEngine.LogDebug($"  [MIR]   Skipping purity check for local/discard out argument target '{argument.Syntax}' on dispatch-analyzed member {originalDefinitionSymbol.ToDisplayString()}.");
                        continue;
                    }
                }

                PurityAnalysisEngine.LogDebug($"  [MIR]   Checking argument: {argument.Value.Kind} | Syntax: {argument.Value.Syntax.ToString().Trim()}");
                var argumentResult = PurityAnalysisEngine.CheckSingleOperation(argument.Value, context, currentState);
                PurityAnalysisEngine.LogDebug($"  [MIR]   Argument check result: IsPure={argumentResult.IsPure}");
                if (!argumentResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (Argument is impure)");

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
                PurityAnalysisEngine.LogDebug("  [MIR] --> PURE (trusted generated fresh array factory)");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (PurityAnalysisEngine.HasPureExternalAttribute(originalDefinitionSymbol))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> PURE ([PureExternal] boundary attribute)");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            string methodDisplayString = originalDefinitionSymbol.ToDisplayString();
            PurityAnalysisEngine.LogDebug($"  [MIR] Analyzing regular call to: {methodDisplayString} | Syntax: {invocationOperation.Syntax}");


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
                PurityAnalysisEngine.LogDebug("  [MIR] --> PURE (deterministic parse with CultureInfo.InvariantCulture)");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (IsContractGuardInvocation(originalDefinitionSymbol))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> PURE (contract guard intrinsic)");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (PurityAnalysisEngine.TryGetSemanticKnownImpureCatalogSource(invocationOperation, out var semanticCatalogSource))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (semantic current-culture-sensitive invocation)");
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
                PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (Configured Known Impure)");
                return PurityAnalysisEngine.ImpureResult(
                    invocationOperation,
                    "global_state_write",
                    nameof(MethodInvocationPurityRule),
                    originalDefinitionSymbol,
                    knownImpureMemberSource);
            }

            if (ShouldPreferSemanticImpurityEvidence(knownImpureMemberSource))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (semantic impurity evidence)");
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
                    PurityAnalysisEngine.LogDebug("  [MIR] --> PURE (trusted generated purity summary)");
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }

                if (!generatedPurity.IsPure)
                {
                    PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (trusted generated purity summary)");
                    return PurityAnalysisEngine.ImpureResult(
                        invocationOperation,
                        generatedPurity.PrimaryCategory,
                        nameof(MethodInvocationPurityRule),
                        originalDefinitionSymbol,
                        "generated_purity_summary");
                }
            }

            PurityAnalysisEngine.LogDebug($"  [MIR] Checking IsKnownImpure with signature: '{originalDefinitionSymbol.ToDisplayString()}'");
            if (knownImpureMemberSource != null)
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (Known Impure)");
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
                PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (In configured impure NS/Type and not explicitly Pure)");
                return PurityAnalysisEngine.ImpureResult(
                    invocationOperation,
                    GetCatalogHitCategory(originalDefinitionSymbol),
                    nameof(MethodInvocationPurityRule),
                    originalDefinitionSymbol,
                    "known_impure_namespace_or_type");
            }

            PurityAnalysisEngine.LogDebug($"  [MIR] Checking IsKnownPureBCLMember with signature: '{originalDefinitionSymbol.ToDisplayString()}'");
            if (allowsKnownPureFallback &&
                PurityAnalysisEngine.IsKnownPureBCLMember(
                    originalDefinitionSymbol,
                    context.SemanticModel.Compilation))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> PURE (Known Pure BCL)");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (PurityAnalysisEngine.IsInImpureNamespaceOrType(originalDefinitionSymbol) && !isExplicitlyPure)
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (In Impure NS/Type and not explicitly Pure)");
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
                PurityAnalysisEngine.LogDebug("  [MIR] --> PURE (reviewed fresh owned array-returning member)");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (dispatchWasProvenPure)
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> PURE (dispatch candidates were proven pure after receiver and argument validation)");
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
                PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (BCL fallback guess only; no trusted purity evidence)");
                return bclFallbackResult;
            }

            if (IsUntrustedMetadataOnlyMethod(originalDefinitionSymbol))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (Metadata-only external method without purity boundary)");
                return PurityAnalysisEngine.ImpureResult(
                    invocationOperation,
                    "unknown_external_call",
                    nameof(MethodInvocationPurityRule),
                    originalDefinitionSymbol,
                    "metadata");
            }

            PurityAnalysisEngine.LogDebug($"  [MIR] Performing purity check for: {methodDisplayString}");

            if (SymbolEqualityComparer.Default.Equals(
                    originalDefinitionSymbol,
                    context.ContainingMethodSymbol.OriginalDefinition))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] Direct self-recursive invocation is purity-neutral.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var calleePurity = PurityAnalysisEngine.GetCalleePurity(originalDefinitionSymbol, context);

            PurityAnalysisEngine.LogDebug($"  [MIR] Callee purity result for {methodDisplayString}: IsPure={calleePurity.IsPure}");

            if (CanTreatFreshMutableObjectReturningNestedCallableInvocationAsPure(originalDefinitionSymbol, calleePurity))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> PURE (deferring fresh mutable local-function return escape analysis to the caller)");
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
