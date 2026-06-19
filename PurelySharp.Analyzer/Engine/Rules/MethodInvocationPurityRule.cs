using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using PurelySharp.Analyzer.Engine;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace PurelySharp.Analyzer.Engine.Rules
{

    internal class MethodInvocationPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Invocation);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IInvocationOperation invocationOperation))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] WARNING: Called with non-invocation.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var invokedMethodSymbol = invocationOperation.TargetMethod;
            if (invokedMethodSymbol == null)
            {
                PurityAnalysisEngine.LogDebug("  [MIR] Cannot resolve target method. Assuming impure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    invocationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "unsupported_operation",
                        nameof(MethodInvocationPurityRule),
                        invocationOperation));
            }

            if (IsCompilerGeneratedArrayForeachInvocation(invocationOperation, context))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] Compiler-generated array foreach member is treated as pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (invocationOperation.Instance != null && IsDynamicInvocationReceiver(invocationOperation.Instance))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] Invocation on dynamic instance is treated as conservative impure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    invocationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        nameof(MethodInvocationPurityRule),
                        invocationOperation,
                        symbol: invokedMethodSymbol));
            }


            if (invokedMethodSymbol.Name == "Invoke" && invokedMethodSymbol.ContainingType?.TypeKind == TypeKind.Delegate)
            {

                PurityAnalysisEngine.LogDebug("  [MIR-DEL-S] === Simplified Delegate Invocation Check Start ===");
                PurityAnalysisEngine.LogDebug($"  [MIR-DEL-S] Invoked Symbol: {invokedMethodSymbol.ContainingType.Name}.Invoke()");

                if (invocationOperation.Instance == null)
                {
                    PurityAnalysisEngine.LogDebug("  [MIR-DEL-S] Instance is NULL (static delegate?). Assuming impure.");
                    return PurityAnalysisEngine.ImpureResult(
                        invocationOperation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "unresolved_delegate_target",
                            nameof(MethodInvocationPurityRule),
                            invocationOperation,
                            symbol: invokedMethodSymbol));
                }

                PurityAnalysisEngine.PurityAnalysisResult result = PurityAnalysisEngine.PurityAnalysisResult.Pure;
                IOperation delegateInstanceOp = invocationOperation.Instance;
                PurityAnalysisEngine.LogDebug($"  [MIR-DEL-S] Analyzing Delegate Instance Op: {delegateInstanceOp.Kind} | Syntax: {delegateInstanceOp.Syntax}");

                var potentialTargets = PurityAnalysisEngine.ResolvePotentialTargets(delegateInstanceOp, currentState, context.SemanticModel);
                if (potentialTargets != null)
                {
                    PurityAnalysisEngine.LogDebug($"  [MIR-DEL-S] Resolved {potentialTargets.Value.MethodSymbols.Count} target(s) for delegate invocation.");
                    if (potentialTargets.Value.IsUnresolved || potentialTargets.Value.MethodSymbols.IsEmpty)
                    {
                        PurityAnalysisEngine.LogDebug("  [MIR-DEL-S] --> Resolved target set is empty or explicitly unresolved. Treating as unresolved delegate target.");
                        result = PurityAnalysisEngine.ImpureResult(
                            delegateInstanceOp.Syntax,
                            PurityAnalysisEngine.PurityEvidence.Create(
                                "unresolved_delegate_target",
                                nameof(MethodInvocationPurityRule),
                                delegateInstanceOp,
                                symbol: invokedMethodSymbol));
                    }
                    else
                    {
                        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;
                        foreach (var targetMethod in potentialTargets.Value.MethodSymbols)
                        {
                            PurityAnalysisEngine.LogDebug($"  [MIR-DEL-S] Checking Potential Target: {targetMethod.ToDisplayString()}");
                            var targetPurity = PurityAnalysisEngine.GetCalleePurity(targetMethod, context);
                            PurityAnalysisEngine.LogDebug($"  [MIR-DEL-S] Potential Target Purity Result: IsPure={targetPurity.IsPure}");
                            if (!targetPurity.IsPure)
                            {
                                if (CanTreatFreshMutableObjectReturningNestedCallableInvocationAsPure(targetMethod, targetPurity))
                                {
                                    PurityAnalysisEngine.LogDebug("  [MIR-DEL-S] --> PURE target deferred to caller return/ownership analysis.");
                                    continue;
                                }

                                PurityAnalysisEngine.LogDebug("  [MIR-DEL-S] --> IMPURE target found. Invocation is impure.");
                                result = targetPurity.WithCallee(targetMethod, invocationOperation.Syntax);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    PurityAnalysisEngine.LogDebug($"  [MIR-DEL-S] --> IMPURE (Could not resolve delegate targets for {delegateInstanceOp.Kind}). Fallback to PS0002 at instance op.");
                    result = PurityAnalysisEngine.ImpureResult(
                        delegateInstanceOp.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "unresolved_delegate_target",
                            nameof(MethodInvocationPurityRule),
                            delegateInstanceOp,
                            symbol: invokedMethodSymbol));
                }

                PurityAnalysisEngine.LogDebug($"  [MIR-DEL-S] Final Result for Delegate Invocation: IsPure={result.IsPure}");
                PurityAnalysisEngine.LogDebug("  [MIR-DEL-S] === Simplified Delegate Invocation Check End ===");
                if (result.IsPure)
                {
                    foreach (var argument in invocationOperation.Arguments)
                    {
                        var argumentResult = PurityAnalysisEngine.CheckSingleOperation(argument.Value, context, currentState);
                        if (!argumentResult.IsPure)
                        {
                            PurityAnalysisEngine.LogDebug("  [MIR-DEL-S] --> IMPURE (Delegate invocation argument is impure)");
                            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                                argumentResult.ImpureSyntaxNode ?? argument.Value.Syntax,
                                argumentResult.Evidence);
                        }
                    }
                }

                return result;
            }



            if (invokedMethodSymbol.IsExtensionMethod &&
                invocationOperation.Arguments.Length > 0 &&
                IsDynamicInvocationReceiver(invocationOperation.Arguments[0].Value))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] Extension invocation on dynamic receiver is treated as conservative impure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    invocationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        nameof(MethodInvocationPurityRule),
                        invocationOperation,
                        symbol: invokedMethodSymbol));
            }

            if (invokedMethodSymbol.IsExtensionMethod &&
                SymbolEqualityComparer.Default.Equals(invokedMethodSymbol.ContainingType?.OriginalDefinition, context.SemanticModel.Compilation.GetTypeByMetadataName("System.Linq.Enumerable")))
            {
                PurityAnalysisEngine.LogDebug($"  [MIR] Detected LINQ Enumerable extension method: {invokedMethodSymbol.Name}. Checking source and delegate arguments.");




                if (invocationOperation.Arguments.Length > 0)
                {
                    var sourceArgument = invocationOperation.Arguments[0];
                    if (IsImmediateFreshArrayLinqSource(sourceArgument.Value, context.SemanticModel.Compilation))
                    {
                        PurityAnalysisEngine.LogDebug("  [MIR]   LINQ source is an immediate reviewed fresh array producer; skipping conservative source re-analysis.");
                    }
                    else
                    {
                    PurityAnalysisEngine.LogDebug($"  [MIR]   Checking LINQ source argument purity: {sourceArgument.Value.Kind}");
                    var sourceResult = PurityAnalysisEngine.CheckSingleOperation(sourceArgument.Value, context, currentState);

                    if (!sourceResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug($"  [MIR] --> IMPURE (LINQ source argument was impure)");

                        return sourceResult;
                    }

                    var sourceEnumeratorResult = CheckLinqSourceEnumeratorPurity(sourceArgument.Value, context, currentState);
                    if (!sourceEnumeratorResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (LINQ source GetEnumerator was impure)");
                        return sourceEnumeratorResult;
                    }
                    }
                }
                else
                {
                    PurityAnalysisEngine.LogDebug($"  [MIR]   WARNING: LINQ method {invokedMethodSymbol.Name} called with no arguments? Assuming impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        invocationOperation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "unsupported_operation",
                            nameof(MethodInvocationPurityRule),
                            invocationOperation,
                            symbol: invokedMethodSymbol));
                }


                PurityAnalysisEngine.LogDebug("  [MIR]   LINQ source was pure. Checking remaining arguments...");
                for (int argumentIndex = 1; argumentIndex < invocationOperation.Arguments.Length; argumentIndex++)
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
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var dispatchWasProvenPure = false;
            if (IsPotentiallyDispatchedMethod(invokedMethodSymbol, context.SemanticModel.Compilation)
                && (invokedMethodSymbol.IsStatic
                    ? invocationOperation.Instance == null
                    : invocationOperation.Instance != null
                        && !IsBaseReference(invocationOperation.Instance)))
            {
                var exactReceiverType = GetTrackedLocalReceiverType(invocationOperation.Instance, currentState);
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


            if (invokedMethodSymbol.IsStatic && invokedMethodSymbol.ContainingType != null)
            {
                var staticOriginalDefinitionSymbol = invokedMethodSymbol.OriginalDefinition;
                if (PurityAnalysisEngine.HasImpureAttribute(staticOriginalDefinitionSymbol))
                {
                    PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE ([Impure] boundary attribute)");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        invocationOperation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "impure_boundary_attribute",
                            nameof(MethodInvocationPurityRule),
                            invocationOperation,
                            symbol: staticOriginalDefinitionSymbol,
                            catalogSource: "attribute"));
                }

            }

            if (invokedMethodSymbol.IsStatic && invokedMethodSymbol.ContainingType != null)
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
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    invocationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "impure_boundary_attribute",
                        nameof(MethodInvocationPurityRule),
                        invocationOperation,
                        symbol: originalDefinitionSymbol,
                        catalogSource: "attribute"));
            }

            var trustedMetadataPurity = PurityAnalysisEngine.GetTrustedMethodPurityMetadata(
                originalDefinitionSymbol,
                context.SemanticModel.Compilation);
            var hasTrustedGeneratedPurity = trustedMetadataPurity.HasTrustedGeneratedPurity;
            var generatedPurity = trustedMetadataPurity.GeneratedPurity;
            var allowsKnownPureFallback = trustedMetadataPurity.AllowsKnownPureFallback;

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
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            argument.Syntax,
                            PurityAnalysisEngine.PurityEvidence.Create(
                                "mutable_state_write",
                                nameof(MethodInvocationPurityRule),
                                argument,
                                syntaxNode: argument.Syntax,
                                symbol: PurityAnalysisEngine.TryResolveSymbol(argument.Value) ?? originalDefinitionSymbol));
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
                         (allowsKnownPureFallback && PurityAnalysisEngine.IsKnownPureBCLMember(originalDefinitionSymbol)) ||
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

            if (IsKnownDelegateInvokingBclMethod(originalDefinitionSymbol))
            {
                foreach (var argument in invocationOperation.Arguments)
                {
                    var delegateTargetResult = CheckDelegateArgumentTargetPurity(argument, context, currentState);
                    if (!delegateTargetResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (delegate-invoking BCL argument target was impure or unresolved)");
                        return delegateTargetResult;
                    }
                }
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

            if (TryCheckSemanticallyPureParsePurity(invocationOperation, out var semanticParseResult))
            {
                return semanticParseResult;
            }

            if (PurityAnalysisEngine.HasPureExternalAttribute(originalDefinitionSymbol))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> PURE ([PureExternal] boundary attribute)");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            string methodDisplayString = originalDefinitionSymbol.ToDisplayString();
            PurityAnalysisEngine.LogDebug($"  [MIR] Analyzing regular call to: {methodDisplayString} | Syntax: {invocationOperation.Syntax}");


            if (TryCheckArrayAsReadOnlyOwnedLocalArrayPurity(invocationOperation, currentState, out var arrayAsReadOnlyResult))
            {
                return arrayAsReadOnlyResult;
            }

            if (PurityAnalysisEngine.TryGetSemanticKnownImpureCatalogSource(invocationOperation, out var semanticCatalogSource))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (semantic current-culture-sensitive invocation)");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    invocationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "catalog_hit",
                        nameof(MethodInvocationPurityRule),
                        invocationOperation,
                        symbol: originalDefinitionSymbol,
                        catalogSource: semanticCatalogSource));
            }

            if (PurityAnalysisEngine.IsInvariantCultureDeterministicParseInvocation(invocationOperation))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> PURE (deterministic parse with CultureInfo.InvariantCulture)");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
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
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    invocationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        GetCatalogHitCategory(originalDefinitionSymbol),
                        nameof(MethodInvocationPurityRule),
                        invocationOperation,
                        symbol: originalDefinitionSymbol,
                        catalogSource: knownImpureMemberSource));
            }

            if (hasTrustedGeneratedPurity)
            {
                if (generatedPurity.IsPure)
                {
                    PurityAnalysisEngine.LogDebug("  [MIR] --> PURE (trusted generated purity summary)");
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }

                if (generatedPurity.IsImpure)
                {
                    PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (trusted generated purity summary)");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        invocationOperation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            generatedPurity.PrimaryCategory,
                            nameof(MethodInvocationPurityRule),
                            invocationOperation,
                            symbol: originalDefinitionSymbol,
                        catalogSource: "generated_purity_summary"));
                }
            }

            PurityAnalysisEngine.LogDebug($"  [MIR] Checking IsKnownImpure with signature: '{originalDefinitionSymbol.ToDisplayString()}'");
            if (knownImpureMemberSource != null)
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (Known Impure)");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    invocationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        GetCatalogHitCategory(originalDefinitionSymbol),
                        nameof(MethodInvocationPurityRule),
                        invocationOperation,
                        symbol: originalDefinitionSymbol,
                        catalogSource: knownImpureMemberSource));
            }

            if (PurityAnalysisEngine.IsInConfiguredImpureNamespaceOrType(originalDefinitionSymbol) &&
                !isExplicitlyPure &&
                !PurityAnalysisEngine.IsConfiguredKnownPureMember(originalDefinitionSymbol))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (In configured impure NS/Type and not explicitly Pure)");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    invocationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        GetCatalogHitCategory(originalDefinitionSymbol),
                        nameof(MethodInvocationPurityRule),
                        invocationOperation,
                        symbol: originalDefinitionSymbol,
                        catalogSource: "known_impure_namespace_or_type"));
            }

            PurityAnalysisEngine.LogDebug($"  [MIR] Checking IsKnownPureBCLMember with signature: '{originalDefinitionSymbol.ToDisplayString()}'");
            if (allowsKnownPureFallback && PurityAnalysisEngine.IsKnownPureBCLMember(originalDefinitionSymbol))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> PURE (Known Pure BCL)");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (PurityAnalysisEngine.IsInImpureNamespaceOrType(originalDefinitionSymbol) && !isExplicitlyPure)
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (In Impure NS/Type and not explicitly Pure)");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    invocationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        GetCatalogHitCategory(originalDefinitionSymbol),
                        nameof(MethodInvocationPurityRule),
                        invocationOperation,
                        symbol: originalDefinitionSymbol,
                        catalogSource: "known_impure_namespace_or_type"));
            }

            if (invocationOperation.Type is IArrayTypeSymbol &&
                PurityAnalysisEngine.IsKnownFreshOwnedArrayReturningMember(
                    originalDefinitionSymbol,
                    context.SemanticModel.Compilation))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> PURE (reviewed fresh owned array-returning member)");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (IsUntrustedMetadataOnlyMethod(originalDefinitionSymbol))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (Metadata-only external method without purity boundary)");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    invocationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "unknown_external_call",
                        nameof(MethodInvocationPurityRule),
                        invocationOperation,
                        symbol: originalDefinitionSymbol,
                        catalogSource: "metadata"));
            }

            PurityAnalysisEngine.LogDebug($"  [MIR] Performing purity check for: {methodDisplayString}");

            if (SymbolEqualityComparer.Default.Equals(
                    originalDefinitionSymbol,
                    context.ContainingMethodSymbol.OriginalDefinition))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] Direct self-recursive invocation is purity-neutral.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (dispatchWasProvenPure)
            {
                PurityAnalysisEngine.LogDebug("  [MIR] --> PURE (dispatch candidates were proven pure after receiver and argument validation)");
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

        private static bool IsUntrustedMetadataOnlyMethod(IMethodSymbol methodSymbol)
        {
            if (methodSymbol.DeclaringSyntaxReferences.Length > 0 || methodSymbol.IsAbstract)
            {
                return false;
            }

            var assemblyName = methodSymbol.ContainingAssembly?.Identity.Name;
            return !IsFrameworkAssemblyName(assemblyName);
        }

        private static bool IsFrameworkAssemblyName(string? assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName))
            {
                return false;
            }

            var name = assemblyName!;
            return name == "mscorlib" ||
                name == "netstandard" ||
                name == "System" ||
                name == "System.Private.CoreLib" ||
                name.StartsWith("System.", StringComparison.Ordinal) ||
                name.StartsWith("Microsoft.", StringComparison.Ordinal);
        }

        private static bool TryCheckArrayAsReadOnlyOwnedLocalArrayPurity(
            IInvocationOperation invocationOperation,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            out PurityAnalysisEngine.PurityAnalysisResult result)
        {
            result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

            if (PurityAnalysisEngine.IsArrayAsReadOnlyOwnedLocalArrayInvocation(invocationOperation, currentState))
            {
                PurityAnalysisEngine.LogDebug("  [MIR] Array.AsReadOnly over tracked fresh local array is treated as pure.");
                return true;
            }

            return false;
        }

        private static bool IsPotentiallyDispatchedMethod(IMethodSymbol methodSymbol, Compilation compilation)
        {
            if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface ||
                methodSymbol.IsAbstract)
            {
                return true;
            }

            if (!methodSymbol.IsVirtual && !methodSymbol.IsOverride)
            {
                return false;
            }

            if (GeneratedPurityCatalog.TryCanMetadataMethodBeOverridden(methodSymbol, compilation, out var canBeOverridden))
            {
                return canBeOverridden;
            }

            return !methodSymbol.IsSealed;
        }

        private static bool IsPureOutArgumentTarget(IOperation? operation)
        {
            operation = PurityAnalysisEngine.SkipImplicitConversions(operation);

            if (operation is IConversionOperation conversionOperation)
            {
                return IsPureOutArgumentTarget(conversionOperation.Operand);
            }

            return operation is ILocalReferenceOperation ||
                operation is IDeclarationExpressionOperation ||
                operation is IDiscardOperation;
        }

        private static bool IsDeclarationOrDiscardOutArgumentTarget(IOperation? operation)
        {
            operation = PurityAnalysisEngine.SkipImplicitConversions(operation);

            if (operation is IConversionOperation conversionOperation)
            {
                return IsDeclarationOrDiscardOutArgumentTarget(conversionOperation.Operand);
            }

            return operation is IDeclarationExpressionOperation ||
                operation is IDiscardOperation;
        }

        private static bool IsDeconstructOutArgumentMethod(IMethodSymbol methodSymbol)
        {
            if (methodSymbol.Name != "Deconstruct")
            {
                return false;
            }

            var parameters = methodSymbol.ReducedFrom?.Parameters ?? methodSymbol.Parameters;
            var startIndex = methodSymbol.ReducedFrom?.IsExtensionMethod == true ? 1 : 0;
            if (parameters.Length <= startIndex)
            {
                return false;
            }

            for (var index = startIndex; index < parameters.Length; index++)
            {
                if (parameters[index].RefKind != RefKind.Out)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsDispatchAnalyzedOutArgumentMethod(IMethodSymbol methodSymbol)
        {
            if (methodSymbol.Name != "TryGetValue")
            {
                return false;
            }

            var typeDefinition = methodSymbol.ContainingType?.OriginalDefinition.ToDisplayString();
            return typeDefinition is
                "System.Collections.Generic.Dictionary<TKey, TValue>" or
                "System.Collections.Generic.HashSet<T>" or
                "System.Collections.Generic.SortedSet<T>" or
                "System.Collections.Generic.SortedDictionary<TKey, TValue>" or
                "System.Collections.Generic.SortedList<TKey, TValue>" or
                "System.Collections.Immutable.ImmutableDictionary<TKey, TValue>" or
                "System.Collections.Immutable.ImmutableHashSet<T>" or
                "System.Collections.Immutable.ImmutableSortedSet<T>" or
                "System.Collections.Immutable.ImmutableSortedDictionary<TKey, TValue>";
        }

        private static bool IsSemanticallyPureOutArgumentMethod(IMethodSymbol methodSymbol)
        {
            var originalDefinition = methodSymbol.OriginalDefinition;
            return IsBooleanTryParseMethod(originalDefinition) ||
                IsEnumTryParseMethod(originalDefinition);
        }

        private static bool IsKnownDelegateInvokingBclMethod(IMethodSymbol methodSymbol)
        {
            var typeDefinition = methodSymbol.ContainingType?.OriginalDefinition.ToDisplayString();
            return typeDefinition switch
            {
                "System.Collections.Generic.List<T>" => methodSymbol.Name is
                    "ConvertAll" or
                    "Exists" or
                    "Find" or
                    "FindAll" or
                    "FindIndex" or
                    "FindLast" or
                    "FindLastIndex" or
                    "ForEach" or
                    "RemoveAll" or
                    "TrueForAll",
                "System.Array" => methodSymbol.Name is
                    "ConvertAll" or
                    "Exists" or
                    "Find" or
                    "FindAll" or
                    "FindIndex" or
                    "FindLast" or
                    "FindLastIndex" or
                    "ForEach" or
                    "TrueForAll",
                _ => false
            };
        }

        private static bool TryCheckStringEnumerableJoinPurity(
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            out PurityAnalysisEngine.PurityAnalysisResult result)
        {
            result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

            var methodSymbol = invocationOperation.TargetMethod.OriginalDefinition;
            if (!IsStringEnumerableJoinOverload(methodSymbol))
            {
                return false;
            }

            var enumerableArgument = invocationOperation.Arguments[1].Value;
            var enumerablePurity = CheckLinqSourceEnumeratorPurity(enumerableArgument, context, currentState);
            if (!enumerablePurity.IsPure)
            {
                result = enumerablePurity;
                return true;
            }

            return true;
        }

        private static bool IsStringEnumerableJoinOverload(IMethodSymbol methodSymbol)
        {
            if (methodSymbol.Name != "Join" ||
                methodSymbol.ContainingType?.SpecialType != SpecialType.System_String ||
                methodSymbol.IsGenericMethod ||
                methodSymbol.Parameters.Length != 2)
            {
                return false;
            }

            if (methodSymbol.Parameters[0].Type.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            return methodSymbol.Parameters[1].Type is INamedTypeSymbol enumerableType &&
                enumerableType.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T &&
                enumerableType.TypeArguments[0].SpecialType == SpecialType.System_String;
        }

        private static INamedTypeSymbol? GetTrackedLocalReceiverType(
            IOperation? invocationInstance,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            return PurityAnalysisEngine.TryResolveKnownConcreteType(invocationInstance, currentState, out var concreteType)
                ? concreteType
                : null;
        }

        private static INamedTypeSymbol? GetStableInitializerReceiverType(
            IOperation? invocationInstance,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            var normalizedInstance = NormalizeReceiverOperation(invocationInstance);
            if (normalizedInstance is not IFieldReferenceOperation fieldReference ||
                !fieldReference.Field.IsReadOnly ||
                !TryGetReceiverInitializerOperation(fieldReference, context, out var initializerOperation))
            {
                return null;
            }

            if (PurityAnalysisEngine.TryResolveKnownConcreteType(initializerOperation, currentState, out var concreteType))
            {
                return concreteType;
            }

            return GetKnownReceiverType(initializerOperation);
        }

        private static bool IsCompilerGeneratedArrayForeachInvocation(
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context)
        {
            if (invocationOperation.TargetMethod.Parameters.Length != 0 ||
                !IsArrayForeachSyntax(invocationOperation.Syntax, context))
            {
                return false;
            }

            return invocationOperation.TargetMethod.Name switch
            {
                nameof(IDisposable.Dispose) => invocationOperation.TargetMethod.ContainingType?.SpecialType == SpecialType.System_IDisposable,
                "GetEnumerator" => invocationOperation.TargetMethod.ContainingType?.ToDisplayString() == "System.Collections.IEnumerable",
                "MoveNext" => invocationOperation.TargetMethod.ContainingType?.ToDisplayString() == "System.Collections.IEnumerator",
                _ => false,
            };
        }

        private static bool IsArrayForeachSyntax(SyntaxNode syntax, PurityAnalysisContext context)
        {
            if (!syntax.IsKind(SyntaxKind.IdentifierName) &&
                !syntax.IsKind(SyntaxKind.SimpleMemberAccessExpression) &&
                !syntax.IsKind(SyntaxKind.ElementAccessExpression))
            {
                return false;
            }

            return TryGetForeachCollectionType(syntax.Parent, context.SemanticModel) is IArrayTypeSymbol;
        }

        private static ITypeSymbol? TryGetForeachCollectionType(SyntaxNode? syntaxNode, SemanticModel semanticModel)
        {
            return syntaxNode switch
            {
                Microsoft.CodeAnalysis.CSharp.Syntax.ForEachStatementSyntax forEachStatement =>
                    semanticModel.GetTypeInfo(forEachStatement.Expression).Type,
                Microsoft.CodeAnalysis.CSharp.Syntax.ForEachVariableStatementSyntax forEachVariableStatement =>
                    semanticModel.GetTypeInfo(forEachVariableStatement.Expression).Type,
                _ => null,
            };
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckDispatchedInvocationPurity(
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context,
            INamedTypeSymbol? knownReceiverType,
            bool hasExactReceiverType)
        {
            var invokedMethodSymbol = invocationOperation.TargetMethod;
            if (invokedMethodSymbol == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(invocationOperation.Syntax);
            }

            var candidateMethods = ResolvePotentialDispatchTargets(
                invokedMethodSymbol,
                context.SemanticModel,
                knownReceiverType,
                invocationOperation.Instance,
                hasExactReceiverType)
                .Where(method => !method.IsAbstract && !method.IsExtern)
                .ToImmutableHashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

            if (CanHaveExternalDispatchTargets(invokedMethodSymbol, invocationOperation, knownReceiverType, hasExactReceiverType))
            {
                var isTypeParameterReceiver = invocationOperation.Instance?.Type?.TypeKind == TypeKind.TypeParameter;
                var hasConcreteImplementationCandidate =
                    invokedMethodSymbol.ContainingType?.TypeKind == TypeKind.Interface &&
                    !isTypeParameterReceiver &&
                    candidateMethods.Any(method => method.ContainingType?.TypeKind != TypeKind.Interface);

                if (!hasConcreteImplementationCandidate)
                {
                    PurityAnalysisEngine.LogDebug($"  [MIR] Method {invokedMethodSymbol.ContainingType?.Name}.{invokedMethodSymbol.Name} can dispatch to unknown external targets; treating as impure conservatively.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        invocationOperation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "unknown_external_call",
                            nameof(MethodInvocationPurityRule),
                            invocationOperation,
                            symbol: invokedMethodSymbol));
                }
            }

            if (candidateMethods.Count == 0)
            {
                PurityAnalysisEngine.LogDebug($"  [MIR] No concrete dispatch candidates found for {invokedMethodSymbol.Name}; assuming pure when external dispatch is impossible.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            foreach (var candidateMethod in candidateMethods)
            {
                PurityAnalysisEngine.LogDebug($"  [MIR]   Evaluating dispatch candidate: {candidateMethod.ToDisplayString()}");
                if (SymbolEqualityComparer.Default.Equals(
                        candidateMethod.OriginalDefinition,
                        context.ContainingMethodSymbol.OriginalDefinition))
                {
                    PurityAnalysisEngine.LogDebug("  [MIR]   Direct self-recursive dispatch candidate is purity-neutral.");
                    continue;
                }

                var candidatePurity = PurityAnalysisEngine.GetCalleePurity(candidateMethod, context);
                if (!candidatePurity.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"  [MIR] --> IMPURE dispatch candidate found: {candidateMethod.ToDisplayString()}");
                    return candidatePurity.WithCallee(candidateMethod, invocationOperation.Syntax);
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static bool TryCheckEqualityComparerDispatchPurity(
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context,
            out PurityAnalysisEngine.PurityAnalysisResult result)
        {
            result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

            var methodSymbol = invocationOperation.TargetMethod;
            if (!TryGetEqualityComparerElementType(methodSymbol, out var elementType))
            {
                return false;
            }

            if (IsBuiltinValueEquality(elementType))
            {
                return true;
            }

            if (methodSymbol.Name == nameof(object.Equals) && methodSymbol.Parameters.Length == 2)
            {
                if (TryGetIEquatableEqualsImplementation(elementType, out var equalsImplementation))
                {
                    result = CheckResolvedEqualityImplementation(
                        equalsImplementation,
                        invocationOperation,
                        context);
                    return true;
                }

                if (TryGetObjectOverride(elementType, nameof(object.Equals), parameterCount: 1, out var objectEqualsOverride))
                {
                    result = CheckResolvedEqualityImplementation(
                        objectEqualsOverride,
                        invocationOperation,
                        context);
                    return true;
                }
            }
            else if (methodSymbol.Name == nameof(object.GetHashCode) && methodSymbol.Parameters.Length == 1)
            {
                if (TryGetObjectOverride(elementType, nameof(object.GetHashCode), parameterCount: 0, out var getHashCodeOverride))
                {
                    result = CheckResolvedEqualityImplementation(
                        getHashCodeOverride,
                        invocationOperation,
                        context);
                    return true;
                }
            }
            else
            {
                return false;
            }

            result = PurityAnalysisEngine.PurityAnalysisResult.Impure(
                invocationOperation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "unknown_external_call",
                    nameof(MethodInvocationPurityRule),
                    invocationOperation,
                    symbol: methodSymbol));
            return true;
        }

        private static bool TryCheckComparerDispatchPurity(
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context,
            out PurityAnalysisEngine.PurityAnalysisResult result)
        {
            result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

            var methodSymbol = invocationOperation.TargetMethod;
            if (!TryGetComparerElementType(methodSymbol, out var elementType))
            {
                return false;
            }

            result = CheckDefaultComparisonDispatchPurity(elementType, invocationOperation, context);
            return true;
        }

        private static bool TryCheckNullableComparisonDispatchPurity(
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context,
            out PurityAnalysisEngine.PurityAnalysisResult result)
        {
            result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

            var methodSymbol = invocationOperation.TargetMethod;
            if (methodSymbol.ContainingType?.ToDisplayString() != "System.Nullable" ||
                !methodSymbol.IsGenericMethod ||
                methodSymbol.TypeArguments.Length != 1 ||
                methodSymbol.Parameters.Length != 2)
            {
                return false;
            }

            var valueType = methodSymbol.TypeArguments[0];
            if (methodSymbol.Name == "Compare")
            {
                result = CheckDefaultComparisonDispatchPurity(valueType, invocationOperation, context);
                return true;
            }

            if (methodSymbol.Name == "Equals")
            {
                result = CheckDefaultEqualityDispatchPurity(valueType, invocationOperation, context);
                return true;
            }

            return false;
        }

        private static bool TryCheckCollectionEqualityDispatchPurity(
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            out PurityAnalysisEngine.PurityAnalysisResult result)
        {
            result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

            var methodSymbol = invocationOperation.TargetMethod;
            if (!TryGetDefaultEqualityCollectionElementType(methodSymbol, out var elementType, out var requiresHashCode))
            {
                return false;
            }

            var receiverComparerResult = CheckHashSetReceiverComparerPurity(invocationOperation, context);
            if (!receiverComparerResult.IsPure)
            {
                result = receiverComparerResult;
                return true;
            }

            if (IsHashSetRelationMethod(methodSymbol) &&
                invocationOperation.Arguments.Length > 0)
            {
                result = CheckLinqSourceEnumeratorPurity(invocationOperation.Arguments[0].Value, context, currentState);
                if (!result.IsPure)
                {
                    return true;
                }
            }

            result = CheckDefaultEqualityDispatchPurity(elementType, invocationOperation, context, requiresHashCode);
            return true;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckHashSetReceiverComparerPurity(
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context)
        {
            var methodSymbol = invocationOperation.TargetMethod;
            if (methodSymbol.ContainingType?.OriginalDefinition.ToDisplayString() != "System.Collections.Generic.HashSet<T>")
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var receiverOperation = PurityAnalysisEngine.SkipImplicitConversions(invocationOperation.Instance) ??
                invocationOperation.Instance;
            var constructionResult = CheckKnownHashSetConstructionComparerPurity(
                receiverOperation,
                invocationOperation,
                context);
            if (!constructionResult.IsPure)
            {
                return constructionResult;
            }

            if (receiverOperation?.Type is INamedTypeSymbol receiverType)
            {
                return CheckHashSetSubtypeConstructorComparerPurity(receiverType, invocationOperation, context);
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckKnownHashSetConstructionComparerPurity(
            IOperation? receiverOperation,
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context)
        {
            var unwrappedReceiver = PurityAnalysisEngine.SkipImplicitConversions(receiverOperation) ?? receiverOperation;
            if (unwrappedReceiver is IObjectCreationOperation objectCreationOperation)
            {
                return CheckHashSetObjectCreationComparerPurity(objectCreationOperation, invocationOperation, context);
            }

            if (TryGetReceiverInitializerOperation(unwrappedReceiver, context, out var initializerOperation) &&
                PurityAnalysisEngine.SkipImplicitConversions(initializerOperation) is IObjectCreationOperation initializerObjectCreation)
            {
                return CheckHashSetObjectCreationComparerPurity(initializerObjectCreation, invocationOperation, context);
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static bool TryGetReceiverInitializerOperation(
            IOperation? receiverOperation,
            PurityAnalysisContext context,
            out IOperation initializerOperation)
        {
            ISymbol? receiverSymbol = receiverOperation switch
            {
                IFieldReferenceOperation fieldReference => fieldReference.Field,
                IPropertyReferenceOperation propertyReference => propertyReference.Property,
                _ => null
            };

            if (receiverSymbol == null)
            {
                initializerOperation = null!;
                return false;
            }

            foreach (var syntaxReference in receiverSymbol.DeclaringSyntaxReferences)
            {
                SyntaxNode? initializerSyntax = syntaxReference.GetSyntax(context.CancellationToken) switch
                {
                    VariableDeclaratorSyntax variableDeclarator => variableDeclarator.Initializer?.Value,
                    PropertyDeclarationSyntax propertyDeclaration => propertyDeclaration.Initializer?.Value,
                    _ => null
                };

                if (initializerSyntax == null)
                {
                    continue;
                }

                var semanticModel = context.SemanticModel.Compilation.GetSemanticModel(initializerSyntax.SyntaxTree);
                var operation = semanticModel.GetOperation(initializerSyntax, context.CancellationToken);
                if (operation != null)
                {
                    initializerOperation = operation;
                    return true;
                }
            }

            initializerOperation = null!;
            return false;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckHashSetObjectCreationComparerPurity(
            IObjectCreationOperation objectCreationOperation,
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context)
        {
            if (objectCreationOperation.Type is not INamedTypeSymbol objectType ||
                objectType.OriginalDefinition.ToDisplayString() != "System.Collections.Generic.HashSet<T>")
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            foreach (var argument in objectCreationOperation.Arguments)
            {
                var value = PurityAnalysisEngine.SkipImplicitConversions(argument.Value) ?? argument.Value;
                if (value?.Type == null ||
                    argument.Parameter?.Type is not INamedTypeSymbol parameterType ||
                    !IsEqualityComparerType(parameterType) &&
                    (value.Type is not INamedTypeSymbol namedValueType ||
                     !IsComparerOrDerivedInterface(namedValueType)))
                {
                    continue;
                }

                var comparerArgumentResult = PurityAnalysisEngine.CheckSingleOperation(value, context, PurityAnalysisEngine.PurityAnalysisState.Pure);
                if (!comparerArgumentResult.IsPure)
                {
                    return comparerArgumentResult;
                }

                var comparerResult = CheckComparerValuePurity(value, invocationOperation, context);
                if (!comparerResult.IsPure)
                {
                    return comparerResult;
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckHashSetSubtypeConstructorComparerPurity(
            INamedTypeSymbol receiverType,
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context)
        {
            if (receiverType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.HashSet<T>" ||
                !DerivesFromHashSet(receiverType))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            foreach (var constructor in receiverType.InstanceConstructors)
            {
                foreach (var syntaxReference in constructor.DeclaringSyntaxReferences)
                {
                    if (syntaxReference.GetSyntax(context.CancellationToken) is not ConstructorDeclarationSyntax constructorSyntax ||
                        constructorSyntax.Initializer == null)
                    {
                        continue;
                    }

                    foreach (var argument in constructorSyntax.Initializer.ArgumentList.Arguments)
                    {
                        var semanticModel = context.SemanticModel.Compilation.GetSemanticModel(argument.SyntaxTree);
                        var argumentOperation = semanticModel.GetOperation(argument.Expression, context.CancellationToken);
                        var value = PurityAnalysisEngine.SkipImplicitConversions(argumentOperation) ?? argumentOperation;
                        if (value?.Type is not INamedTypeSymbol namedValueType ||
                            !IsComparerOrDerivedInterface(namedValueType))
                        {
                            continue;
                        }

                        var comparerResult = CheckComparerValuePurity(value, invocationOperation, context);
                        if (!comparerResult.IsPure)
                        {
                            return comparerResult;
                        }
                    }
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static bool DerivesFromHashSet(INamedTypeSymbol typeSymbol)
        {
            for (var baseType = typeSymbol.BaseType; baseType != null; baseType = baseType.BaseType)
            {
                if (baseType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.HashSet<T>")
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryCheckCollectionComparisonDispatchPurity(
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context,
            out PurityAnalysisEngine.PurityAnalysisResult result)
        {
            result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

            var methodSymbol = invocationOperation.TargetMethod;
            if (!TryGetDefaultComparisonCollectionKeyType(methodSymbol, out var keyType))
            {
                return false;
            }

            var receiverComparerResult = CheckSortedCollectionReceiverComparerPurity(invocationOperation, context);
            if (!receiverComparerResult.IsPure)
            {
                result = receiverComparerResult;
                return true;
            }

            result = CheckDefaultComparisonDispatchPurity(keyType, invocationOperation, context);
            return true;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckSortedCollectionReceiverComparerPurity(
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context)
        {
            var methodSymbol = invocationOperation.TargetMethod;
            if (!IsConcreteSortedCollectionType(methodSymbol.ContainingType))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var receiverOperation = PurityAnalysisEngine.SkipImplicitConversions(invocationOperation.Instance) ??
                invocationOperation.Instance;
            var constructionResult = CheckKnownSortedCollectionConstructionComparerPurity(
                receiverOperation,
                invocationOperation,
                context);
            if (!constructionResult.IsPure)
            {
                return constructionResult;
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckKnownSortedCollectionConstructionComparerPurity(
            IOperation? receiverOperation,
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context)
        {
            var unwrappedReceiver = PurityAnalysisEngine.SkipImplicitConversions(receiverOperation) ?? receiverOperation;
            if (unwrappedReceiver is IObjectCreationOperation objectCreationOperation)
            {
                return CheckSortedCollectionObjectCreationComparerPurity(objectCreationOperation, invocationOperation, context);
            }

            if (TryGetReceiverInitializerOperation(unwrappedReceiver, context, out var initializerOperation) &&
                PurityAnalysisEngine.SkipImplicitConversions(initializerOperation) is IObjectCreationOperation initializerObjectCreation)
            {
                return CheckSortedCollectionObjectCreationComparerPurity(initializerObjectCreation, invocationOperation, context);
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckSortedCollectionObjectCreationComparerPurity(
            IObjectCreationOperation objectCreationOperation,
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context)
        {
            if (!IsConcreteSortedCollectionType(objectCreationOperation.Type))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            foreach (var argument in objectCreationOperation.Arguments)
            {
                var value = PurityAnalysisEngine.SkipImplicitConversions(argument.Value) ?? argument.Value;
                if (value?.Type == null ||
                    argument.Parameter?.Type is not INamedTypeSymbol parameterType ||
                    !IsComparerType(parameterType) &&
                    (value.Type is not INamedTypeSymbol namedValueType ||
                     !IsComparerOrDerivedInterface(namedValueType)))
                {
                    continue;
                }

                var comparerArgumentResult = PurityAnalysisEngine.CheckSingleOperation(value, context, PurityAnalysisEngine.PurityAnalysisState.Pure);
                if (!comparerArgumentResult.IsPure)
                {
                    return comparerArgumentResult;
                }

                var comparerResult = CheckComparerValuePurity(value, invocationOperation, context);
                if (!comparerResult.IsPure)
                {
                    return comparerResult;
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static bool IsConcreteSortedCollectionType(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol is not INamedTypeSymbol namedType)
            {
                return false;
            }

            return namedType.OriginalDefinition.ToDisplayString() is
                "System.Collections.Generic.SortedDictionary<TKey, TValue>" or
                "System.Collections.Generic.SortedList<TKey, TValue>" or
                "System.Collections.Generic.SortedSet<T>";
        }

        private static bool TryCheckLinqDefaultEqualityDispatchPurity(
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context,
            out PurityAnalysisEngine.PurityAnalysisResult result)
        {
            result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

            var methodSymbol = invocationOperation.TargetMethod;
            if (!TryGetLinqDefaultEqualityDispatchType(methodSymbol, out var equalityType))
            {
                return false;
            }

            if (!IsLinqDefaultEqualityOverload(invocationOperation))
            {
                return false;
            }

            result = CheckDefaultEqualityDispatchPurity(equalityType, invocationOperation, context);
            return true;
        }

        private static bool TryCheckLinqDefaultComparisonDispatchPurity(
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context,
            out PurityAnalysisEngine.PurityAnalysisResult result)
        {
            result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

            var methodSymbol = invocationOperation.TargetMethod;
            if (!TryGetLinqDefaultComparisonDispatchType(methodSymbol, out var comparisonType))
            {
                return false;
            }

            if (!IsLinqDefaultComparisonOverload(invocationOperation))
            {
                return false;
            }

            result = CheckDefaultComparisonDispatchPurity(comparisonType, invocationOperation, context);
            return true;
        }

        private static bool TryGetLinqDefaultComparisonDispatchType(
            IMethodSymbol methodSymbol,
            out ITypeSymbol comparisonType)
        {
            comparisonType = null!;

            if (methodSymbol.ContainingType?.OriginalDefinition.ToDisplayString() != "System.Linq.Enumerable" ||
                methodSymbol.Name is not ("OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" or "Min" or "Max"))
            {
                return false;
            }

            if (methodSymbol.Name is "Min" or "Max")
            {
                if (methodSymbol.TypeArguments.Length != 1)
                {
                    return false;
                }

                comparisonType = methodSymbol.TypeArguments[0];
                return true;
            }

            if (methodSymbol.TypeArguments.Length < 2)
            {
                return false;
            }

            comparisonType = methodSymbol.TypeArguments[1];
            return true;
        }

        private static bool IsLinqDefaultComparisonOverload(IInvocationOperation invocationOperation)
        {
            var methodSymbol = invocationOperation.TargetMethod;
            if (TryGetComparerArgumentIndex(methodSymbol, out var comparerArgumentIndex))
            {
                return invocationOperation.Arguments.Length > comparerArgumentIndex &&
                    IsNullOrDefaultComparerArgument(invocationOperation.Arguments[comparerArgumentIndex]);
            }

            return true;
        }

        private static bool TryGetLinqDefaultEqualityDispatchType(
            IMethodSymbol methodSymbol,
            out ITypeSymbol equalityType)
        {
            equalityType = null!;

            if (methodSymbol.ContainingType?.OriginalDefinition.ToDisplayString() != "System.Linq.Enumerable")
            {
                return false;
            }

            if (methodSymbol.Name is "GroupBy" or "ToLookup")
            {
                if (methodSymbol.TypeArguments.Length < 2)
                {
                    return false;
                }

                equalityType = methodSymbol.TypeArguments[1];
                return true;
            }

            if (methodSymbol.Name is "Join" or "GroupJoin")
            {
                if (methodSymbol.TypeArguments.Length < 3)
                {
                    return false;
                }

                equalityType = methodSymbol.TypeArguments[2];
                return true;
            }

            if (methodSymbol.Name is not ("Contains" or "SequenceEqual" or "Distinct" or "Except" or "Intersect" or "Union") ||
                methodSymbol.TypeArguments.Length != 1)
            {
                return false;
            }

            equalityType = methodSymbol.TypeArguments[0];
            return true;
        }

        private static bool IsLinqDefaultEqualityOverload(IInvocationOperation invocationOperation)
        {
            var methodSymbol = invocationOperation.TargetMethod;
            if (TryGetEqualityComparerArgumentIndex(methodSymbol, out var comparerArgumentIndex))
            {
                return invocationOperation.Arguments.Length > comparerArgumentIndex &&
                    IsNullOrDefaultComparerArgument(invocationOperation.Arguments[comparerArgumentIndex]);
            }

            return true;
        }

        private static bool TryGetComparerArgumentIndex(
            IMethodSymbol methodSymbol,
            out int argumentIndex)
        {
            argumentIndex = -1;

            for (int i = 0; i < methodSymbol.Parameters.Length; i++)
            {
                if (IsComparerType(methodSymbol.Parameters[i].Type))
                {
                    argumentIndex = i;
                    return true;
                }
            }

            return false;
        }

        private static bool IsComparerType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IComparer<T>";
        }

        private static bool TryGetEqualityComparerArgumentIndex(
            IMethodSymbol methodSymbol,
            out int argumentIndex)
        {
            argumentIndex = -1;

            for (int i = 0; i < methodSymbol.Parameters.Length; i++)
            {
                if (IsEqualityComparerType(methodSymbol.Parameters[i].Type))
                {
                    argumentIndex = i;
                    return true;
                }
            }

            return false;
        }

        private static bool IsEqualityComparerType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEqualityComparer<T>";
        }

        private static bool TryCheckStringComparisonPurity(
            IInvocationOperation invocationOperation,
            out PurityAnalysisEngine.PurityAnalysisResult result)
        {
            result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

            var methodSymbol = invocationOperation.TargetMethod?.OriginalDefinition;
            if (methodSymbol?.ContainingType?.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            if (methodSymbol.Name == "Contains" &&
                methodSymbol.Parameters.Length == 1 &&
                methodSymbol.Parameters[0].Type.SpecialType == SpecialType.System_String)
            {
                return true;
            }

            if (methodSymbol.Name is "ToLower" or "ToUpper" &&
                methodSymbol.Parameters.Length == 0)
            {
                result = PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    invocationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "reflection_environment_source",
                        nameof(MethodInvocationPurityRule),
                        invocationOperation,
                        symbol: methodSymbol,
                        catalogSource: "string_default_culture_casing"));
                return true;
            }

            if (methodSymbol.Name is "Contains" or "StartsWith" or "EndsWith" or "Equals" or "IndexOf")
            {
                var comparisonParameterIndex = GetStringComparisonParameterIndex(methodSymbol);
                if (comparisonParameterIndex >= 0 && comparisonParameterIndex < invocationOperation.Arguments.Length)
                {
                    if (IsDeterministicStringComparison(invocationOperation.Arguments[comparisonParameterIndex].Value))
                    {
                        return true;
                    }

                    result = PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        invocationOperation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "reflection_environment_source",
                            nameof(MethodInvocationPurityRule),
                            invocationOperation,
                            symbol: methodSymbol,
                            catalogSource: "string_current_culture_comparison"));
                    return true;
                }
            }

            if (methodSymbol.Name is "StartsWith" or "EndsWith" &&
                methodSymbol.Parameters.Length == 1 &&
                methodSymbol.Parameters[0].Type.SpecialType == SpecialType.System_String)
            {
                result = PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    invocationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "reflection_environment_source",
                        nameof(MethodInvocationPurityRule),
                        invocationOperation,
                        symbol: methodSymbol,
                        catalogSource: "string_default_culture_comparison"));
                return true;
            }

            return false;
        }

        private static bool TryCheckSemanticallyPureParsePurity(
            IInvocationOperation invocationOperation,
            out PurityAnalysisEngine.PurityAnalysisResult result)
        {
            result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

            var methodSymbol = invocationOperation.TargetMethod?.OriginalDefinition;
            if (methodSymbol == null)
            {
                return false;
            }

            return IsBooleanParseMethod(methodSymbol) ||
                IsBooleanTryParseMethod(methodSymbol) ||
                IsEnumTryParseMethod(methodSymbol) ||
                IsIPAddressParseMethod(methodSymbol);
        }

        private static bool IsBooleanParseMethod(IMethodSymbol methodSymbol)
        {
            return methodSymbol.ContainingType?.SpecialType == SpecialType.System_Boolean &&
                methodSymbol.Name == "Parse" &&
                methodSymbol.Parameters.Length == 1 &&
                IsStringOrReadOnlySpanOfChar(methodSymbol.Parameters[0].Type);
        }

        private static bool IsBooleanTryParseMethod(IMethodSymbol methodSymbol)
        {
            return methodSymbol.ContainingType?.SpecialType == SpecialType.System_Boolean &&
                methodSymbol.Name == "TryParse" &&
                methodSymbol.Parameters.Length == 2 &&
                IsStringOrReadOnlySpanOfChar(methodSymbol.Parameters[0].Type) &&
                methodSymbol.Parameters[1].RefKind == RefKind.Out &&
                methodSymbol.Parameters[1].Type.SpecialType == SpecialType.System_Boolean;
        }

        private static bool IsEnumTryParseMethod(IMethodSymbol methodSymbol)
        {
            if (methodSymbol.ContainingType?.ToDisplayString() != "System.Enum" ||
                methodSymbol.Name != "TryParse" ||
                !methodSymbol.IsGenericMethod ||
                methodSymbol.TypeParameters.Length != 1 ||
                methodSymbol.Parameters.Length is not (2 or 3) ||
                !IsStringOrReadOnlySpanOfChar(methodSymbol.Parameters[0].Type))
            {
                return false;
            }

            if (methodSymbol.Parameters.Length == 3 &&
                methodSymbol.Parameters[1].Type.SpecialType != SpecialType.System_Boolean)
            {
                return false;
            }

            var outParameter = methodSymbol.Parameters[methodSymbol.Parameters.Length - 1];
            return outParameter.RefKind == RefKind.Out &&
                SymbolEqualityComparer.Default.Equals(outParameter.Type, methodSymbol.TypeParameters[0]);
        }

        private static bool IsIPAddressParseMethod(IMethodSymbol methodSymbol)
        {
            return methodSymbol.ContainingType?.ToDisplayString() == "System.Net.IPAddress" &&
                methodSymbol.Name == "Parse" &&
                methodSymbol.Parameters.Length == 1 &&
                IsStringOrReadOnlySpanOfChar(methodSymbol.Parameters[0].Type);
        }

        private static int GetStringComparisonParameterIndex(IMethodSymbol methodSymbol)
        {
            for (int i = 0; i < methodSymbol.Parameters.Length; i++)
            {
                if (methodSymbol.Parameters[i].Type.ToDisplayString() == "System.StringComparison")
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsReadOnlySpanOfChar(ITypeSymbol typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.ToDisplayString() == "System.ReadOnlySpan<T>" &&
                namedType.TypeArguments.Length == 1 &&
                namedType.TypeArguments[0].SpecialType == SpecialType.System_Char;
        }

        private static bool IsStringOrReadOnlySpanOfChar(ITypeSymbol typeSymbol)
        {
            return typeSymbol.SpecialType == SpecialType.System_String ||
                IsReadOnlySpanOfChar(typeSymbol);
        }

        private static bool IsDeterministicStringComparison(IOperation? operation)
        {
            var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
            return unwrappedOperation?.ConstantValue.HasValue == true &&
                unwrappedOperation.ConstantValue.Value is int comparison &&
                comparison is 2 or 3 or 4 or 5;
        }

        private static bool TryCheckMemoryExtensionsDefaultEqualityDispatchPurity(
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context,
            out PurityAnalysisEngine.PurityAnalysisResult result)
        {
            result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

            var methodSymbol = invocationOperation.TargetMethod;
            if (methodSymbol.ContainingType?.OriginalDefinition.ToDisplayString() != "System.MemoryExtensions" ||
                methodSymbol.Name is not ("SequenceEqual" or "Contains" or "IndexOf" or "LastIndexOf" or "StartsWith" or "EndsWith") ||
                methodSymbol.Parameters.Length != 2)
            {
                return false;
            }

            if (methodSymbol.TypeArguments.Length < 1)
            {
                return false;
            }

            var elementType = methodSymbol.TypeArguments[0];
            if (elementType.TypeKind == TypeKind.TypeParameter)
            {
                return false;
            }

            result = CheckDefaultEqualityDispatchPurity(elementType, invocationOperation, context);
            return true;
        }

        private static bool TryCheckHashCodeCombineDispatchPurity(
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context,
            out PurityAnalysisEngine.PurityAnalysisResult result)
        {
            result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

            var methodSymbol = invocationOperation.TargetMethod;
            if (methodSymbol.ContainingType?.ToDisplayString() != "System.HashCode" ||
                methodSymbol.Name != "Combine" ||
                !methodSymbol.IsGenericMethod ||
                methodSymbol.TypeArguments.Length == 0)
            {
                return false;
            }

            foreach (var typeArgument in methodSymbol.TypeArguments)
            {
                result = CheckDefaultHashDispatchPurity(typeArgument, invocationOperation, context);
                if (!result.IsPure)
                {
                    return true;
                }
            }

            return true;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckResolvedEqualityImplementation(
            IMethodSymbol implementation,
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context)
        {
            if (implementation.DeclaringSyntaxReferences.Length == 0 &&
                !PurityAnalysisEngine.HasTrustedGeneratedPurityCoverage(implementation, context.SemanticModel.Compilation) &&
                !PurityAnalysisEngine.IsKnownPureBCLMember(implementation) &&
                !PurityAnalysisEngine.HasPureExternalAttribute(implementation))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    invocationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "unknown_external_call",
                        nameof(MethodInvocationPurityRule),
                        invocationOperation,
                        symbol: implementation));
            }

            var implementationPurity = PurityAnalysisEngine.GetCalleePurity(implementation.OriginalDefinition, context);
            return implementationPurity.IsPure
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : implementationPurity.WithCallee(implementation.OriginalDefinition, invocationOperation.Syntax);
        }

        private static bool TryGetEqualityComparerElementType(
            IMethodSymbol methodSymbol,
            out ITypeSymbol elementType)
        {
            elementType = null!;

            if (methodSymbol.ContainingType is not INamedTypeSymbol containingType ||
                containingType.TypeArguments.Length != 1 ||
                containingType.OriginalDefinition.ToDisplayString() != "System.Collections.Generic.EqualityComparer<T>")
            {
                return false;
            }

            if ((methodSymbol.Name == nameof(object.Equals) && methodSymbol.Parameters.Length == 2) ||
                (methodSymbol.Name == nameof(object.GetHashCode) && methodSymbol.Parameters.Length == 1))
            {
                elementType = containingType.TypeArguments[0];
                return true;
            }

            return false;
        }

        private static bool TryGetComparerElementType(
            IMethodSymbol methodSymbol,
            out ITypeSymbol elementType)
        {
            elementType = null!;

            if (methodSymbol.ContainingType is not INamedTypeSymbol containingType ||
                containingType.TypeArguments.Length != 1 ||
                containingType.OriginalDefinition.ToDisplayString() != "System.Collections.Generic.Comparer<T>")
            {
                return false;
            }

            if (methodSymbol.Name == "Compare" && methodSymbol.Parameters.Length == 2)
            {
                elementType = containingType.TypeArguments[0];
                return true;
            }

            return false;
        }

        private static bool TryGetDefaultEqualityCollectionElementType(
            IMethodSymbol methodSymbol,
            out ITypeSymbol elementType,
            out bool requiresHashCode)
        {
            elementType = null!;
            requiresHashCode = false;

            if (methodSymbol.ContainingType is not INamedTypeSymbol containingType ||
                methodSymbol.Parameters.Length < 1)
            {
                return false;
            }

            if (containingType.SpecialType == SpecialType.System_Array &&
                methodSymbol.IsGenericMethod &&
                methodSymbol.TypeArguments.Length == 1 &&
                methodSymbol.Parameters.Length >= 2 &&
                methodSymbol.Name is "IndexOf" or "LastIndexOf")
            {
                elementType = methodSymbol.TypeArguments[0];
                return true;
            }

            var typeDefinition = containingType.OriginalDefinition.ToDisplayString();
            if (containingType.TypeArguments.Length == 2 &&
                typeDefinition == "System.Collections.Generic.Dictionary<TKey, TValue>" &&
                methodSymbol.Name is "ContainsKey" or "TryGetValue")
            {
                elementType = containingType.TypeArguments[0];
                requiresHashCode = true;
                return true;
            }

            if (containingType.TypeArguments.Length == 2 &&
                typeDefinition == "System.Collections.Immutable.ImmutableDictionary<TKey, TValue>" &&
                methodSymbol.Name is "ContainsKey" or "TryGetValue" or "Add" or "Remove" or "SetItem")
            {
                elementType = containingType.TypeArguments[0];
                requiresHashCode = true;
                return true;
            }

            if (containingType.TypeArguments.Length == 2 &&
                (typeDefinition == "System.Collections.Generic.Dictionary<TKey, TValue>" ||
                 typeDefinition == "System.Collections.Generic.SortedDictionary<TKey, TValue>") &&
                methodSymbol.Name == "ContainsValue")
            {
                elementType = containingType.TypeArguments[1];
                return true;
            }

            if (containingType.TypeArguments.Length != 1)
            {
                return false;
            }

            var usesDefaultEquality =
                typeDefinition == "System.Collections.Generic.List<T>" ||
                typeDefinition == "System.Collections.Immutable.ImmutableList<T>" ||
                typeDefinition == "System.Collections.Generic.Queue<T>" ||
                typeDefinition == "System.Collections.Generic.Stack<T>" ||
                typeDefinition == "System.Collections.Generic.HashSet<T>" ||
                typeDefinition == "System.Collections.Immutable.ImmutableHashSet<T>";
            if (!usesDefaultEquality)
            {
                return false;
            }

            var isDefaultEqualityLookup =
                methodSymbol.Name == "Contains" ||
                methodSymbol.Name == "IndexOf" ||
                methodSymbol.Name == "LastIndexOf" ||
                methodSymbol.Name == "TryGetValue";
            var isImmutableHashSetUpdate =
                typeDefinition == "System.Collections.Immutable.ImmutableHashSet<T>" &&
                methodSymbol.Name is "Add" or "Remove";
            var isImmutableListRemove =
                typeDefinition == "System.Collections.Immutable.ImmutableList<T>" &&
                methodSymbol.Name == "Remove";
            var isHashSetRelation = IsHashSetRelationMethod(methodSymbol);
            if (!isDefaultEqualityLookup && !isImmutableHashSetUpdate && !isImmutableListRemove && !isHashSetRelation)
            {
                return false;
            }

            elementType = containingType.TypeArguments[0];
            requiresHashCode =
                typeDefinition == "System.Collections.Generic.HashSet<T>" ||
                typeDefinition == "System.Collections.Immutable.ImmutableHashSet<T>";
            return true;
        }

        private static bool IsHashSetRelationMethod(IMethodSymbol methodSymbol)
        {
            var typeDefinition = methodSymbol.ContainingType?.OriginalDefinition.ToDisplayString();
            return (typeDefinition == "System.Collections.Generic.HashSet<T>" ||
                    typeDefinition == "System.Collections.Immutable.ImmutableHashSet<T>") &&
                methodSymbol.Name is "SetEquals" or "Overlaps" or "IsSubsetOf" or "IsSupersetOf" or "IsProperSubsetOf" or "IsProperSupersetOf";
        }

        private static bool TryGetDefaultComparisonCollectionKeyType(
            IMethodSymbol methodSymbol,
            out ITypeSymbol keyType)
        {
            keyType = null!;

            if (methodSymbol.ContainingType is not INamedTypeSymbol containingType ||
                methodSymbol.Name is not ("ContainsKey" or "TryGetValue" or "BinarySearch" or "SequenceCompareTo" or "Contains" or "Add" or "Remove" or "SetItem" or "IndexOfKey"))
            {
                return false;
            }

            var typeDefinition = containingType.OriginalDefinition.ToDisplayString();
            if (containingType.SpecialType == SpecialType.System_Array &&
                methodSymbol.IsGenericMethod &&
                methodSymbol.Name == "BinarySearch" &&
                methodSymbol.TypeArguments.Length == 1 &&
                methodSymbol.Parameters.Length >= 2)
            {
                keyType = methodSymbol.TypeArguments[0];
                return true;
            }

            if (typeDefinition == "System.MemoryExtensions" &&
                methodSymbol.IsGenericMethod &&
                methodSymbol.Name is "BinarySearch" or "SequenceCompareTo" &&
                methodSymbol.Parameters.Length == 2)
            {
                keyType = methodSymbol.Name == "BinarySearch"
                    ? methodSymbol.Parameters[1].Type
                    : methodSymbol.TypeArguments[0];
                return true;
            }

            if (containingType.TypeArguments.Length == 2 &&
                (typeDefinition == "System.Collections.Generic.SortedDictionary<TKey, TValue>" ||
                 typeDefinition == "System.Collections.Generic.SortedList<TKey, TValue>") &&
                methodSymbol.Name is "ContainsKey" or "TryGetValue" or "IndexOfKey")
            {
                keyType = containingType.TypeArguments[0];
                return true;
            }

            if (containingType.TypeArguments.Length == 2 &&
                typeDefinition == "System.Collections.Immutable.ImmutableSortedDictionary<TKey, TValue>" &&
                methodSymbol.Name is "ContainsKey" or "TryGetValue" or "Add" or "Remove" or "SetItem")
            {
                keyType = containingType.TypeArguments[0];
                return true;
            }

            if (containingType.TypeArguments.Length == 1 &&
                typeDefinition == "System.Collections.Generic.SortedSet<T>" &&
                methodSymbol.Name is "Contains" or "TryGetValue")
            {
                keyType = containingType.TypeArguments[0];
                return true;
            }

            if (containingType.TypeArguments.Length == 1 &&
                typeDefinition == "System.Collections.Immutable.ImmutableSortedSet<T>" &&
                methodSymbol.Name is "Contains" or "TryGetValue" or "Add" or "Remove")
            {
                keyType = containingType.TypeArguments[0];
                return true;
            }

            if (containingType.TypeArguments.Length == 1 &&
                typeDefinition == "System.Collections.Generic.List<T>" &&
                methodSymbol.Name == "BinarySearch" &&
                methodSymbol.Parameters.Length == 1)
            {
                keyType = containingType.TypeArguments[0];
                return true;
            }

            return false;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckDefaultHashDispatchPurity(
            ITypeSymbol elementType,
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context)
        {
            if (IsBuiltinValueEquality(elementType))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (!TryGetObjectOverride(elementType, nameof(object.GetHashCode), parameterCount: 0, out var getHashCodeOverride))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    invocationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "unknown_external_call",
                        nameof(MethodInvocationPurityRule),
                        invocationOperation,
                        symbol: invocationOperation.TargetMethod));
            }

            return CheckResolvedEqualityImplementation(
                getHashCodeOverride,
                invocationOperation,
                context);
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckDefaultEqualityDispatchPurity(
            ITypeSymbol elementType,
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context,
            bool requiresHashCode = false)
        {
            if (IsBuiltinValueEquality(elementType))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (requiresHashCode)
            {
                if (!TryGetObjectOverride(elementType, nameof(object.GetHashCode), parameterCount: 0, out var getHashCodeOverride))
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        invocationOperation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "unknown_external_call",
                            nameof(MethodInvocationPurityRule),
                            invocationOperation,
                            symbol: invocationOperation.TargetMethod));
                }

                var hashPurity = CheckResolvedEqualityImplementation(
                    getHashCodeOverride,
                    invocationOperation,
                    context);
                if (!hashPurity.IsPure)
                {
                    return hashPurity;
                }
            }

            if (TryGetIEquatableEqualsImplementation(elementType, out var equalsImplementation))
            {
                return CheckResolvedEqualityImplementation(
                    equalsImplementation,
                    invocationOperation,
                    context);
            }

            if (TryGetObjectOverride(elementType, nameof(object.Equals), parameterCount: 1, out var objectEqualsOverride))
            {
                return CheckResolvedEqualityImplementation(
                    objectEqualsOverride,
                    invocationOperation,
                    context);
            }

            if (elementType is INamedTypeSymbol { TypeKind: TypeKind.Class, IsSealed: true })
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                invocationOperation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "unknown_external_call",
                    nameof(MethodInvocationPurityRule),
                    invocationOperation,
                    symbol: invocationOperation.TargetMethod));
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckDelegateArgumentTargetPurity(
            IArgumentOperation argument,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (argument.Parameter?.Type?.TypeKind != TypeKind.Delegate)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var potentialTargets = PurityAnalysisEngine.ResolvePotentialTargets(
                argument.Value,
                currentState,
                context.SemanticModel);
            if (potentialTargets == null ||
                potentialTargets.Value.IsUnresolved ||
                potentialTargets.Value.MethodSymbols.Count == 0)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    argument.Value.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "unresolved_delegate_target",
                        nameof(MethodInvocationPurityRule),
                        argument,
                        syntaxNode: argument.Value.Syntax,
                        symbol: PurityAnalysisEngine.TryResolveSymbol(argument.Value) ?? argument.Parameter));
            }

            foreach (var targetMethod in potentialTargets.Value.MethodSymbols)
            {
                var targetPurity = PurityAnalysisEngine.GetCalleePurity(targetMethod, context);
                if (!targetPurity.IsPure)
                {
                    return targetPurity.WithCallee(targetMethod, argument.Value.Syntax);
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckDefaultComparisonDispatchPurity(
            ITypeSymbol keyType,
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context)
        {
            if (IsBuiltinValueComparison(keyType))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (TryGetIComparableCompareToImplementation(keyType, out var compareToImplementation))
            {
                return CheckResolvedEqualityImplementation(
                    compareToImplementation,
                    invocationOperation,
                    context);
            }

            if (TryGetIComparableObjectCompareToImplementation(keyType, out var objectCompareToImplementation))
            {
                return CheckResolvedEqualityImplementation(
                    objectCompareToImplementation,
                    invocationOperation,
                    context);
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                invocationOperation.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "unknown_external_call",
                    nameof(MethodInvocationPurityRule),
                    invocationOperation,
                    symbol: invocationOperation.TargetMethod));
        }

        private static bool TryGetIEquatableEqualsImplementation(
            ITypeSymbol elementType,
            out IMethodSymbol implementation)
        {
            implementation = null!;

            if (elementType is not INamedTypeSymbol namedType)
            {
                return false;
            }

            foreach (var interfaceType in namedType.AllInterfaces)
            {
                if (interfaceType.OriginalDefinition.ToDisplayString() != "System.IEquatable<T>" ||
                    interfaceType.TypeArguments.Length != 1 ||
                    !SymbolEqualityComparer.Default.Equals(interfaceType.TypeArguments[0], elementType))
                {
                    continue;
                }

                var interfaceEquals = interfaceType
                    .GetMembers(nameof(IEquatable<object>.Equals))
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(method => method.Parameters.Length == 1);
                if (interfaceEquals == null)
                {
                    continue;
                }

                var foundImplementation = namedType.FindImplementationForInterfaceMember(interfaceEquals) as IMethodSymbol;
                if (foundImplementation != null)
                {
                    implementation = foundImplementation;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetIComparableCompareToImplementation(
            ITypeSymbol keyType,
            out IMethodSymbol implementation)
        {
            implementation = null!;

            if (keyType is not INamedTypeSymbol namedType)
            {
                return false;
            }

            foreach (var interfaceType in namedType.AllInterfaces)
            {
                if (interfaceType.OriginalDefinition.ToDisplayString() != "System.IComparable<T>" ||
                    interfaceType.TypeArguments.Length != 1 ||
                    !SymbolEqualityComparer.Default.Equals(interfaceType.TypeArguments[0], keyType))
                {
                    continue;
                }

                var interfaceCompareTo = interfaceType
                    .GetMembers(nameof(IComparable<object>.CompareTo))
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(method => method.Parameters.Length == 1);
                if (interfaceCompareTo == null)
                {
                    continue;
                }

                var foundImplementation = namedType.FindImplementationForInterfaceMember(interfaceCompareTo) as IMethodSymbol;
                if (foundImplementation != null)
                {
                    implementation = foundImplementation;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetIComparableObjectCompareToImplementation(
            ITypeSymbol keyType,
            out IMethodSymbol implementation)
        {
            implementation = null!;

            if (keyType is not INamedTypeSymbol namedType)
            {
                return false;
            }

            foreach (var interfaceType in namedType.AllInterfaces)
            {
                if (interfaceType.ToDisplayString() != "System.IComparable")
                {
                    continue;
                }

                var interfaceCompareTo = interfaceType
                    .GetMembers(nameof(IComparable.CompareTo))
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(method => method.Parameters.Length == 1);
                if (interfaceCompareTo == null)
                {
                    continue;
                }

                var foundImplementation = namedType.FindImplementationForInterfaceMember(interfaceCompareTo) as IMethodSymbol;
                if (foundImplementation != null)
                {
                    implementation = foundImplementation;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetObjectOverride(
            ITypeSymbol elementType,
            string memberName,
            int parameterCount,
            out IMethodSymbol implementation)
        {
            implementation = null!;

            if (elementType is not INamedTypeSymbol namedType)
            {
                return false;
            }

            implementation = namedType
                .GetMembers(memberName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(method => method.IsOverride && method.Parameters.Length == parameterCount);
            return implementation != null;
        }

        private static bool IsBuiltinValueEquality(ITypeSymbol elementType)
        {
            if (elementType.TypeKind == TypeKind.Enum)
            {
                return true;
            }

            return elementType.SpecialType is
                SpecialType.System_Boolean or
                SpecialType.System_Byte or
                SpecialType.System_SByte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64 or
                SpecialType.System_Single or
                SpecialType.System_Double or
                SpecialType.System_Decimal or
                SpecialType.System_Char or
                SpecialType.System_String;
        }

        private static bool IsBuiltinValueComparison(ITypeSymbol keyType)
        {
            if (keyType.TypeKind == TypeKind.Enum)
            {
                return true;
            }

            return keyType.SpecialType is
                SpecialType.System_Boolean or
                SpecialType.System_Byte or
                SpecialType.System_SByte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64 or
                SpecialType.System_UInt64 or
                SpecialType.System_Single or
                SpecialType.System_Double or
                SpecialType.System_Decimal or
                SpecialType.System_Char or
                SpecialType.System_String;
        }

        private static bool CanHaveExternalOverrides(IMethodSymbol methodSymbol, INamedTypeSymbol? knownReceiverType)
        {
            if (methodSymbol.IsSealed)
            {
                return false;
            }

            if (!methodSymbol.IsVirtual)
            {
                return false;
            }

            if (methodSymbol.DeclaredAccessibility == Accessibility.Private ||
                methodSymbol.DeclaredAccessibility == Accessibility.Internal ||
                methodSymbol.DeclaredAccessibility == Accessibility.ProtectedAndInternal)
            {
                return false;
            }

            if (methodSymbol.ContainingType == null || methodSymbol.ContainingType.TypeKind != TypeKind.Class)
            {
                return false;
            }

            if (methodSymbol.ContainingType.IsSealed)
            {
                return false;
            }

            if (knownReceiverType != null &&
                knownReceiverType.IsSealed &&
                (SymbolEqualityComparer.Default.Equals(knownReceiverType.OriginalDefinition, methodSymbol.ContainingType.OriginalDefinition) ||
                 DerivesFrom(knownReceiverType, methodSymbol.ContainingType)))
            {
                return false;
            }

            return IsTypeEffectivelyExternallyAccessible(methodSymbol.ContainingType);
        }

        private static bool CanHaveExternalDispatchTargets(
            IMethodSymbol methodSymbol,
            IInvocationOperation invocationOperation,
            INamedTypeSymbol? knownReceiverType,
            bool hasExactReceiverType)
        {
            if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface)
            {
                return CanHaveExternalInterfaceImplementations(
                    methodSymbol.ContainingType,
                    invocationOperation.Instance,
                    knownReceiverType,
                    hasExactReceiverType);
            }

            if (hasExactReceiverType &&
                knownReceiverType != null &&
                (SymbolEqualityComparer.Default.Equals(knownReceiverType.OriginalDefinition, methodSymbol.ContainingType?.OriginalDefinition) ||
                 DerivesFrom(knownReceiverType, methodSymbol.ContainingType)))
            {
                return false;
            }

            return CanHaveExternalOverrides(methodSymbol, knownReceiverType);
        }

        private static bool CanHaveExternalInterfaceImplementations(
            INamedTypeSymbol interfaceSymbol,
            IOperation? invocationInstance,
            INamedTypeSymbol? knownReceiverType,
            bool hasExactReceiverType)
        {
            if (!CanInterfaceHaveExternalImplementations(interfaceSymbol))
            {
                return false;
            }

            var concreteReceiverType = GetKnownReceiverType(invocationInstance) ?? knownReceiverType;
            if (concreteReceiverType == null)
            {
                return true;
            }

            if (hasExactReceiverType)
            {
                return false;
            }

            if (IsAllocationOnlyInterfaceReceiver(invocationInstance))
            {
                return false;
            }

            if (!IsTypeEffectivelyExternallyAccessible(concreteReceiverType))
            {
                return false;
            }

            if (concreteReceiverType.TypeKind == TypeKind.Interface &&
                SymbolEqualityComparer.Default.Equals(
                    concreteReceiverType.OriginalDefinition,
                    interfaceSymbol.OriginalDefinition))
            {
                return true;
            }

            if (concreteReceiverType.TypeKind == TypeKind.Struct)
            {
                return false;
            }

            if (concreteReceiverType.TypeKind == TypeKind.Class && concreteReceiverType.IsSealed)
            {
                return false;
            }

            return true;
        }

        private static bool CanInterfaceHaveExternalImplementations(INamedTypeSymbol interfaceSymbol)
        {
            if (!IsTypeEffectivelyExternallyAccessible(interfaceSymbol))
            {
                return false;
            }

            foreach (var baseInterface in interfaceSymbol.AllInterfaces)
            {
                if (!IsTypeEffectivelyExternallyAccessible(baseInterface))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsDynamicInvocationReceiver(IOperation? operation)
        {
            var current = operation;

            while (current != null)
            {
                current = NormalizeReceiverOperation(current);
                if (current == null)
                {
                    return false;
                }

                if (current.Type?.TypeKind == TypeKind.Dynamic)
                {
                    return true;
                }

                if (current is IConditionalAccessOperation conditionalAccess)
                {
                    current = conditionalAccess.Operation;
                    continue;
                }

                if (TryGetAsConversion(current, out var asOperand, out _))
                {
                    if (asOperand?.Type?.TypeKind == TypeKind.Dynamic)
                    {
                        return true;
                    }

                    current = asOperand;
                    continue;
                }

                if (current is IConversionOperation conversion)
                {
                    current = conversion.Operand;
                    continue;
                }

                if (current is IParenthesizedOperation parenthesized)
                {
                    current = parenthesized.Operand;
                    continue;
                }

                break;
            }

            return false;
        }

        private static INamedTypeSymbol? GetKnownReceiverType(IOperation? invocationInstance)
        {
            var current = invocationInstance;

            while (true)
            {
                current = NormalizeReceiverOperation(current);

                if (current == null)
                {
                    return null;
                }

                if (current is IConditionalAccessOperation conditionalAccess)
                {
                    current = conditionalAccess.Operation;
                    continue;
                }

                if (current is IConditionalOperation conditional)
                {
                    var whenTrueType = GetKnownReceiverType(conditional.WhenTrue);
                    var whenFalseType = GetKnownReceiverType(conditional.WhenFalse);

                    if (whenTrueType != null &&
                        whenFalseType != null &&
                        SymbolEqualityComparer.Default.Equals(whenTrueType, whenFalseType))
                    {
                        return whenTrueType;
                    }

                    return current.Type as INamedTypeSymbol;
                }

                if (TryGetAsConversion(current, out var asOperand, out var asTargetType))
                {
                    if (asTargetType != null)
                    {
                        var operandType = asOperand?.Type as INamedTypeSymbol;
                        if (operandType != null &&
                            ImplementsInterface(operandType, asTargetType))
                        {
                            current = asOperand;
                            continue;
                        }

                        if (asOperand?.Type is ITypeParameterSymbol typeParameter)
                        {
                            var constrainedType = ResolveConstrainedSealedType(typeParameter);
                            if (constrainedType != null &&
                                ImplementsInterface(constrainedType, asTargetType))
                            {
                                current = asOperand;
                                continue;
                            }
                        }
                    }

                    return asTargetType;
                }

                if (current is IConversionOperation conversion)
                {
                    current = conversion.Operand;
                    continue;
                }

                if (current is IParenthesizedOperation parenthesized)
                {
                    current = parenthesized.Operand;
                    continue;
                }

                if (current.Type is ITypeParameterSymbol typeParameterSymbol)
                {
                    var constrainedSealedType = ResolveConstrainedSealedType(typeParameterSymbol);
                    if (constrainedSealedType != null)
                    {
                        return constrainedSealedType;
                    }

                    return null;
                }

                break;
            }

            return current?.Type as INamedTypeSymbol;
        }

        private static INamedTypeSymbol? GetKnownStaticInterfaceReceiverType(IMethodSymbol invokedMethodSymbol)
        {
            if (!invokedMethodSymbol.IsStatic ||
                invokedMethodSymbol.ContainingType?.TypeKind != TypeKind.Interface ||
                invokedMethodSymbol.ContainingType is not INamedTypeSymbol interfaceType ||
                interfaceType.TypeArguments.IsEmpty)
            {
                return null;
            }

            var interfaceArg = interfaceType.TypeArguments[0];

            if (interfaceArg is INamedTypeSymbol namedType)
            {
                return namedType.TypeKind is TypeKind.Class or TypeKind.Struct
                    ? namedType
                    : null;
            }

            if (interfaceArg is ITypeParameterSymbol typeParameter)
            {
                return ResolveConstrainedSealedType(typeParameter);
            }

            return null;
        }

        private static INamedTypeSymbol? ResolveConstrainedSealedType(ITypeParameterSymbol typeParameter)
        {
            return ResolveConstrainedSealedType(typeParameter, new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default));
        }

        private static INamedTypeSymbol? ResolveConstrainedSealedType(
            ITypeParameterSymbol typeParameter,
            HashSet<ITypeParameterSymbol> visitedTypeParameters)
        {
            if (!visitedTypeParameters.Add(typeParameter))
            {
                return null;
            }

            INamedTypeSymbol? constrainedType = null;

            foreach (var constraintType in typeParameter.ConstraintTypes)
            {
                INamedTypeSymbol? resolvedConstraintType = null;

                if (constraintType is ITypeParameterSymbol nestedTypeParameter)
                {
                    resolvedConstraintType = ResolveConstrainedSealedType(nestedTypeParameter, visitedTypeParameters);
                }
                else if (constraintType is INamedTypeSymbol namedType)
                {
                    if (namedType.TypeKind == TypeKind.Interface)
                    {
                        continue;
                    }

                    if (namedType.TypeKind != TypeKind.Class &&
                        constraintType.TypeKind != TypeKind.Struct ||
                        !namedType.IsSealed)
                    {
                        return null;
                    }

                    resolvedConstraintType = namedType;
                }

                if (resolvedConstraintType == null)
                {
                    continue;
                }

                if (constrainedType != null &&
                    !SymbolEqualityComparer.Default.Equals(constrainedType, resolvedConstraintType))
                {
                    return null;
                }

                constrainedType = resolvedConstraintType;
            }

            return constrainedType;
        }

        private static bool IsTypeEffectivelyExternallyAccessible(INamedTypeSymbol typeSymbol)
        {
            for (var current = typeSymbol; current != null; current = current.ContainingType)
            {
                if (current.DeclaredAccessibility == Accessibility.Private ||
                    current.DeclaredAccessibility == Accessibility.Internal)
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<IMethodSymbol> ResolvePotentialDispatchTargets(
            IMethodSymbol invokedMethodSymbol,
            SemanticModel semanticModel,
            INamedTypeSymbol? knownReceiverType,
            IOperation? invocationInstance,
            bool hasExactReceiverType)
        {
            var compilation = semanticModel.Compilation;
            var target = invokedMethodSymbol.OriginalDefinition;
            var interfaceImplementationTarget = invokedMethodSymbol.ContainingType?.TypeKind == TypeKind.Interface
                ? invokedMethodSymbol
                : target;
            var targets = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

            if (target.ContainingType?.TypeKind == TypeKind.Interface)
            {
                if (knownReceiverType != null && ImplementsInterface(knownReceiverType, target.ContainingType))
                {
                    if (hasExactReceiverType)
                    {
                        var exactImplementation = knownReceiverType.FindImplementationForInterfaceMember(interfaceImplementationTarget) as IMethodSymbol;
                        if (exactImplementation != null)
                        {
                            targets.Add(exactImplementation.OriginalDefinition);
                        }
                        else if (!target.IsAbstract || HasMethodBody(target))
                        {
                            targets.Add(target.OriginalDefinition);
                        }

                        return targets;
                    }

                    if (IsAllocationOnlyInterfaceReceiver(invocationInstance))
                    {
                        var implementation = knownReceiverType.FindImplementationForInterfaceMember(interfaceImplementationTarget) as IMethodSymbol;
                        if (implementation != null)
                        {
                            targets.Add(implementation.OriginalDefinition);
                        }
                        else if (!target.IsAbstract || HasMethodBody(target))
                        {
                            targets.Add(target.OriginalDefinition);
                        }

                        return targets;
                    }

                    if (knownReceiverType.TypeKind == TypeKind.Struct ||
                        (knownReceiverType.TypeKind == TypeKind.Class && knownReceiverType.IsSealed))
                    {
                        var implementation = knownReceiverType.FindImplementationForInterfaceMember(interfaceImplementationTarget) as IMethodSymbol;
                        if (implementation != null)
                        {
                            targets.Add(implementation.OriginalDefinition);
                        }
                        else if (!target.IsAbstract || HasMethodBody(target))
                        {
                            targets.Add(target.OriginalDefinition);
                        }

                        return targets;
                    }
                    var requiresInterfaceReceiverConstraint = knownReceiverType.TypeKind == TypeKind.Interface;

                    foreach (var type in EnumerateAllNamedTypes(compilation.Assembly.GlobalNamespace))
                    {
                        if (requiresInterfaceReceiverConstraint)
                        {
                            if (!ImplementsInterface(type, knownReceiverType))
                            {
                                continue;
                            }
                        }
                        else
                        {
                            if (!SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, knownReceiverType.OriginalDefinition) &&
                                !DerivesFrom(type, knownReceiverType))
                            {
                                continue;
                            }
                        }

                        if (!ImplementsInterface(type, target.ContainingType))
                        {
                            continue;
                        }

                        if (type.Kind == SymbolKind.NamedType &&
                            (type.TypeKind == TypeKind.Interface ||
                             type.TypeKind == TypeKind.Struct ||
                             type.TypeKind == TypeKind.Class))
                        {
                            var implementation = type.FindImplementationForInterfaceMember(target) as IMethodSymbol;
                            if (implementation != null)
                            {
                                targets.Add(implementation.OriginalDefinition);
                            }
                        }
                    }

                    if (!target.IsAbstract || HasMethodBody(target))
                    {
                        targets.Add(target);
                    }

                    return targets;
                }

                foreach (var type in EnumerateAllNamedTypes(compilation.Assembly.GlobalNamespace))
                {
                    if (!ImplementsInterface(type, target.ContainingType))
                    {
                        continue;
                    }

                    if (type.Kind == SymbolKind.NamedType && (type.TypeKind == TypeKind.Interface || type.TypeKind == TypeKind.Struct || type.TypeKind == TypeKind.Class))
                    {
                        var implementation = type.FindImplementationForInterfaceMember(target) as IMethodSymbol;
                        if (implementation != null)
                        {
                            targets.Add(implementation.OriginalDefinition);
                        }
                    }
                }

                if (!target.IsAbstract || HasMethodBody(target))
                {
                    targets.Add(target);
                }

                return targets;
            }

            if (target.IsVirtual || target.IsAbstract || target.IsOverride)
            {
                var baseType = target.ContainingType;
                if (baseType != null)
                {
                    if (hasExactReceiverType &&
                        knownReceiverType != null &&
                        (SymbolEqualityComparer.Default.Equals(knownReceiverType.OriginalDefinition, baseType.OriginalDefinition) ||
                         DerivesFrom(knownReceiverType, baseType)))
                    {
                        var exactReceiverTarget = ResolveDispatchTargetForSealedReceiver(target, knownReceiverType);
                        if (exactReceiverTarget != null)
                        {
                            targets.Add(exactReceiverTarget.OriginalDefinition);
                        }

                        return targets;
                    }

                    if (knownReceiverType != null &&
                        knownReceiverType.IsSealed &&
                        (SymbolEqualityComparer.Default.Equals(knownReceiverType.OriginalDefinition, baseType.OriginalDefinition) ||
                         DerivesFrom(knownReceiverType, baseType)))
                    {
                        var sealedReceiverTarget = ResolveDispatchTargetForSealedReceiver(target, knownReceiverType);
                        if (sealedReceiverTarget != null)
                        {
                            targets.Add(sealedReceiverTarget.OriginalDefinition);
                        }

                        return targets;
                    }

                    foreach (var type in EnumerateAllNamedTypes(compilation.Assembly.GlobalNamespace))
                    {
                        if (!DerivesFrom(type, baseType))
                        {
                            continue;
                        }

                        foreach (var member in type.GetMembers())
                        {
                            if (member is IMethodSymbol method &&
                                OverridesTargetMethod(method, target))
                            {
                                targets.Add(method.OriginalDefinition);
                            }
                        }
                    }
                }

                if (!target.IsAbstract)
                {
                    targets.Add(target);
                }

                return targets;
            }

            targets.Add(target);
            return targets;
        }

        private static IMethodSymbol? ResolveDispatchTargetForSealedReceiver(IMethodSymbol targetMethod, INamedTypeSymbol sealedReceiverType)
        {
            for (var type = sealedReceiverType; type != null; type = type.BaseType)
            {
                foreach (var member in type.GetMembers())
                {
                    if (member is IMethodSymbol method &&
                        (SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, targetMethod.OriginalDefinition) ||
                         OverridesTargetMethod(method, targetMethod) ||
                         ExplicitlyImplements(method, targetMethod)))
                    {
                        return method;
                    }
                }
            }

            if (!targetMethod.IsAbstract)
            {
                return targetMethod;
            }

            return null;
        }

        private static bool ExplicitlyImplements(IMethodSymbol methodSymbol, IMethodSymbol interfaceMethod)
        {
            foreach (var implemented in methodSymbol.ExplicitInterfaceImplementations)
            {
                if (SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, interfaceMethod.OriginalDefinition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ImplementsInterface(INamedTypeSymbol type, INamedTypeSymbol interfaceSymbol)
        {
            if (interfaceSymbol == null)
            {
                return false;
            }

            return type.AllInterfaces.Any(
                i => SymbolEqualityComparer.Default.Equals(
                    i.OriginalDefinition,
                    interfaceSymbol.OriginalDefinition));
        }

        private static bool IsAllocationOnlyInterfaceReceiver(IOperation? invocationInstance)
        {
            var current = invocationInstance;

            while (current != null)
            {
                current = NormalizeReceiverOperation(current);

                if (current is IConditionalAccessOperation conditionalAccess)
                {
                    current = conditionalAccess.Operation;
                    continue;
                }

                if (current is IConversionOperation conversion)
                {
                    current = conversion.Operand;
                    continue;
                }

                if (current is IParenthesizedOperation parenthesized)
                {
                    current = parenthesized.Operand;
                    continue;
                }

                if (TryGetAsConversion(current, out var asOperand, out _))
                {
                    current = asOperand;
                    continue;
                }

                return current is IObjectCreationOperation;
            }

            return false;
        }

        private static IOperation? NormalizeReceiverOperation(IOperation? operation)
        {
            if (operation is not IConditionalAccessInstanceOperation)
            {
                return operation;
            }

            for (var current = operation.Parent; current != null; current = current.Parent)
            {
                if (current is IConditionalAccessOperation conditionalAccess)
                {
                    return conditionalAccess.Operation;
                }
            }

            return operation;
        }

        private static bool HasMethodBody(IMethodSymbol methodSymbol)
        {
            if (methodSymbol.DeclaringSyntaxReferences.Length == 0)
            {
                return false;
            }

            foreach (var syntaxReference in methodSymbol.DeclaringSyntaxReferences)
            {
                var methodSyntax = syntaxReference.GetSyntax();
                if (methodSyntax is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax methodDeclaration &&
                    (methodDeclaration.Body != null || methodDeclaration.ExpressionBody != null))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsBaseReference(IOperation? operation)
        {
            return operation is IInstanceReferenceOperation instanceReference &&
                instanceReference.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance &&
                operation.Syntax.IsKind(SyntaxKind.BaseExpression);
        }

        private static bool TryGetAsConversion(
            IOperation? operation,
            out IOperation? operand,
            out INamedTypeSymbol? targetType)
        {
            if (operation is IConversionOperation conversion &&
                conversion.Syntax.IsKind(SyntaxKind.AsExpression))
            {
                operand = conversion.Operand;
                targetType = conversion.Type as INamedTypeSymbol;
                return true;
            }

            operand = null;
            targetType = null;
            return false;
        }

        private static string GetCatalogHitCategory(ISymbol symbol)
        {
            var containingType = symbol.ContainingType?.ToDisplayString() ?? string.Empty;
            var containingNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

            if (containingType == "System.Threading.Interlocked" ||
                containingType == "System.Threading.Monitor" ||
                containingType == "System.Threading.Mutex" ||
                containingType == "System.Threading.Semaphore" ||
                containingType == "System.Threading.SemaphoreSlim" ||
                containingType == "System.Collections.Immutable.ImmutableInterlocked")
            {
                return "synchronization";
            }

            if (containingNamespace.StartsWith("System.Reflection", StringComparison.Ordinal) ||
                containingType.StartsWith("System.Reflection.", StringComparison.Ordinal) ||
                containingType == "System.Type" ||
                containingType == "System.Runtime.Loader.AssemblyLoadContext" ||
                containingType == "System.Environment" ||
                containingType == "System.DateTime" ||
                containingType == "System.DateTimeOffset" ||
                containingType == "System.TimeProvider" ||
                containingType == "System.TimeZoneInfo" ||
                containingType == "System.Diagnostics.Stopwatch")
            {
                return "reflection_environment_source";
            }

            return "catalog_hit";
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckLinqSourceEnumeratorPurity(
            IOperation sourceOperation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            var unwrappedSource = PurityAnalysisEngine.SkipImplicitConversions(sourceOperation) ?? sourceOperation;
            var sourceType = PurityAnalysisEngine.TryResolveKnownConcreteType(unwrappedSource, currentState, out var concreteType)
                ? (ITypeSymbol)concreteType
                : unwrappedSource.Type;
            if (sourceType == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            foreach (var getEnumerator in EnumerateSourceGetEnumeratorImplementations(sourceType))
            {
                var enumeratorPurity = PurityAnalysisEngine.GetCalleePurity(getEnumerator.OriginalDefinition, context);
                if (!enumeratorPurity.IsPure)
                {
                    return enumeratorPurity.WithCallee(getEnumerator, unwrappedSource.Syntax);
                }

                var runtimePurity = CheckLinqEnumeratorRuntimeMemberPurity(
                    getEnumerator,
                    sourceType,
                    context,
                    unwrappedSource.Syntax);
                if (!runtimePurity.IsPure)
                {
                    return runtimePurity;
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckLinqEnumeratorRuntimeMemberPurity(
            IMethodSymbol getEnumerator,
            ITypeSymbol sourceType,
            PurityAnalysisContext context,
            SyntaxNode callSite)
        {
            foreach (var enumeratorType in EnumerateLinqReturnedEnumeratorTypes(getEnumerator, sourceType, context.SemanticModel))
            {
                foreach (var runtimeMember in EnumerateLinqEnumeratorRuntimeMembers(enumeratorType))
                {
                    var runtimePurity = PurityAnalysisEngine.GetCalleePurity(runtimeMember.OriginalDefinition, context);
                    if (!runtimePurity.IsPure)
                    {
                        return runtimePurity.WithCallee(runtimeMember.OriginalDefinition, callSite);
                    }
                }
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateLinqReturnedEnumeratorTypes(
            IMethodSymbol getEnumerator,
            ITypeSymbol sourceType,
            SemanticModel semanticModel)
        {
            var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            AddConcreteLinqEnumeratorType(getEnumerator.ReturnType, seen);
            AddNestedLinqEnumeratorTypes(sourceType, seen);

            foreach (var syntaxReference in getEnumerator.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax() is not MethodDeclarationSyntax methodDeclaration)
                {
                    continue;
                }

                if (methodDeclaration.ExpressionBody?.Expression != null)
                {
                    AddConcreteLinqEnumeratorType(
                        GetLinqExpressionType(methodDeclaration.ExpressionBody.Expression, semanticModel),
                        seen);
                }

                if (methodDeclaration.Body == null)
                {
                    continue;
                }

                foreach (var returnStatement in methodDeclaration.Body.DescendantNodes().OfType<ReturnStatementSyntax>())
                {
                    if (returnStatement.Expression == null)
                    {
                        continue;
                    }

                    AddConcreteLinqEnumeratorType(
                        GetLinqExpressionType(returnStatement.Expression, semanticModel),
                        seen);
                }
            }

            return seen;
        }

        private static void AddNestedLinqEnumeratorTypes(
            ITypeSymbol sourceType,
            HashSet<INamedTypeSymbol> enumeratorTypes)
        {
            if (sourceType is not INamedTypeSymbol namedSourceType)
            {
                return;
            }

            foreach (var nestedType in EnumerateLinqNestedTypes(namedSourceType))
            {
                if (nestedType.DeclaringSyntaxReferences.Length == 0 ||
                    !IsLinqEnumeratorType(nestedType))
                {
                    continue;
                }

                enumeratorTypes.Add(nestedType.OriginalDefinition);
            }
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateLinqNestedTypes(INamedTypeSymbol typeSymbol)
        {
            foreach (var nestedType in typeSymbol.GetTypeMembers())
            {
                yield return nestedType;
                foreach (var descendant in EnumerateLinqNestedTypes(nestedType))
                {
                    yield return descendant;
                }
            }
        }

        private static bool IsLinqEnumeratorType(INamedTypeSymbol typeSymbol)
        {
            return typeSymbol.AllInterfaces.Any(interfaceType =>
                interfaceType.OriginalDefinition.SpecialType == SpecialType.System_Collections_IEnumerator ||
                interfaceType.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerator_T);
        }

        private static ITypeSymbol? GetLinqExpressionType(ExpressionSyntax expression, SemanticModel semanticModel)
        {
            var operation = semanticModel.GetOperation(expression);
            while (operation is IConversionOperation conversion)
            {
                operation = conversion.Operand;
            }

            return operation?.Type ?? semanticModel.GetTypeInfo(expression).Type;
        }

        private static void AddConcreteLinqEnumeratorType(
            ITypeSymbol? type,
            HashSet<INamedTypeSymbol> enumeratorTypes)
        {
            if (type is INamedTypeSymbol namedType &&
                namedType.TypeKind != TypeKind.Interface &&
                namedType.DeclaringSyntaxReferences.Length > 0)
            {
                enumeratorTypes.Add(namedType.OriginalDefinition);
            }
        }

        private static IEnumerable<IMethodSymbol> EnumerateLinqEnumeratorRuntimeMembers(INamedTypeSymbol enumeratorType)
        {
            foreach (var moveNext in enumeratorType
                         .GetMembers("MoveNext")
                         .OfType<IMethodSymbol>()
                         .Where(method => method.Parameters.Length == 0 && method.DeclaringSyntaxReferences.Length > 0))
            {
                yield return moveNext;
            }

            foreach (var currentGetter in enumeratorType
                         .GetMembers("Current")
                         .OfType<IPropertySymbol>()
                         .Select(property => property.GetMethod)
                         .Where(method => method != null && method.DeclaringSyntaxReferences.Length > 0))
            {
                yield return currentGetter!;
            }

            foreach (var dispose in enumeratorType
                         .GetMembers("Dispose")
                         .OfType<IMethodSymbol>()
                         .Where(method => method.Parameters.Length == 0 && method.DeclaringSyntaxReferences.Length > 0))
            {
                yield return dispose;
            }
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckComparerValuePurity(
            IOperation value,
            IInvocationOperation invocationOperation,
            PurityAnalysisContext context)
        {
            value = PurityAnalysisEngine.SkipImplicitConversions(value) ?? value;
            if (value.Type == null || IsNullOrDefaultComparerValue(value))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (IsTrustedGeneratedPureStringComparerSingleton(value, context))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var foundImplementation = false;
            foreach (var comparisonMethod in EnumerateComparerImplementations(value.Type))
            {
                foundImplementation = true;
                var comparisonPurity = PurityAnalysisEngine.GetCalleePurity(comparisonMethod.OriginalDefinition, context);
                if (!comparisonPurity.IsPure)
                {
                    return comparisonPurity.WithCallee(comparisonMethod, invocationOperation.Syntax);
                }
            }

            if (!foundImplementation && IsUnresolvedComparerDispatch(value.Type))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    invocationOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "unknown_external_call",
                        nameof(MethodInvocationPurityRule),
                        invocationOperation,
                        syntaxNode: invocationOperation.Syntax,
                        symbol: PurityAnalysisEngine.TryResolveSymbol(value) ?? invocationOperation.TargetMethod));
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckLinqComparerArgumentPurity(
            IArgumentOperation argument,
            PurityAnalysisContext context)
        {
            var value = PurityAnalysisEngine.SkipImplicitConversions(argument.Value) ?? argument.Value;
            if (value.Type == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (IsNullOrDefaultComparerValue(value))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (IsTrustedGeneratedPureStringComparerSingleton(value, context))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var foundImplementation = false;
            foreach (var comparisonMethod in EnumerateComparerImplementations(value.Type))
            {
                foundImplementation = true;
                var comparisonPurity = PurityAnalysisEngine.GetCalleePurity(comparisonMethod.OriginalDefinition, context);
                if (!comparisonPurity.IsPure)
                {
                    return comparisonPurity.WithCallee(comparisonMethod, value.Syntax);
                }
            }

            if (!foundImplementation && IsUnresolvedComparerDispatch(value.Type))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    value.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "unknown_external_call",
                        nameof(MethodInvocationPurityRule),
                        argument,
                        syntaxNode: value.Syntax,
                        symbol: PurityAnalysisEngine.TryResolveSymbol(value) ?? argument.Parameter));
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static bool IsNullOrDefaultComparerArgument(IArgumentOperation argument)
        {
            var value = PurityAnalysisEngine.SkipImplicitConversions(argument.Value) ?? argument.Value;
            return IsNullOrDefaultComparerValue(value);
        }

        private static bool IsNullOrDefaultComparerValue(IOperation value)
        {
            value = PurityAnalysisEngine.SkipImplicitConversions(value) ?? value;

            if (value.ConstantValue.HasValue && value.ConstantValue.Value == null)
            {
                return true;
            }

            if (IsDefaultComparerSingleton(value))
            {
                return true;
            }

            return value is IDefaultValueOperation;
        }

        private static bool IsDefaultComparerSingleton(IOperation value)
        {
            return value is IPropertyReferenceOperation propertyReference &&
                propertyReference.Property.Name == "Default" &&
                propertyReference.Property.ContainingType is INamedTypeSymbol containingType &&
                containingType.OriginalDefinition.ToDisplayString() is
                    "System.Collections.Generic.EqualityComparer<T>" or
                    "System.Collections.Generic.Comparer<T>";
        }

        private static bool IsTrustedGeneratedPureStringComparerSingleton(
            IOperation value,
            PurityAnalysisContext context)
        {
            value = PurityAnalysisEngine.SkipImplicitConversions(value) ?? value;
            if (value is not IPropertyReferenceOperation
                {
                    Property:
                    {
                        IsStatic: true,
                        Name: "Ordinal" or "OrdinalIgnoreCase",
                        ContainingType: { } containingType,
                        GetMethod: { } getterSymbol
                    }
                })
            {
                return false;
            }

            if (containingType.OriginalDefinition.ToDisplayString() != "System.StringComparer" ||
                getterSymbol.Locations.FirstOrDefault()?.IsInMetadata != true)
            {
                return false;
            }

            return PurityAnalysisEngine.TryGetTrustedGeneratedPurity(
                getterSymbol.OriginalDefinition,
                context.SemanticModel.Compilation,
                out var generatedPurity) &&
                generatedPurity.IsPure;
        }

        private static IEnumerable<IMethodSymbol> EnumerateSourceGetEnumeratorImplementations(ITypeSymbol sourceType)
        {
            var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

            foreach (var getEnumerator in sourceType
                         .GetMembers("GetEnumerator")
                         .OfType<IMethodSymbol>()
                         .Where(method => method.Parameters.Length == 0 && method.DeclaringSyntaxReferences.Length > 0))
            {
                if (seen.Add(getEnumerator.OriginalDefinition))
                {
                    yield return getEnumerator;
                }
            }

            if (sourceType is not INamedTypeSymbol namedSourceType)
            {
                yield break;
            }

            foreach (var interfaceType in namedSourceType.AllInterfaces)
            {
                if (!IsEnumerableInterface(interfaceType))
                {
                    continue;
                }

                foreach (var interfaceGetEnumerator in interfaceType
                             .GetMembers("GetEnumerator")
                             .OfType<IMethodSymbol>()
                             .Where(method => method.Parameters.Length == 0))
                {
                    var implementation = namedSourceType.FindImplementationForInterfaceMember(interfaceGetEnumerator) as IMethodSymbol;
                    if (implementation == null || implementation.DeclaringSyntaxReferences.Length == 0)
                    {
                        continue;
                    }

                    if (seen.Add(implementation.OriginalDefinition))
                    {
                        yield return implementation;
                    }
                }
            }
        }

        private static bool IsEnumerableInterface(INamedTypeSymbol typeSymbol)
        {
            var originalDefinition = typeSymbol.OriginalDefinition;
            return originalDefinition.SpecialType == SpecialType.System_Collections_IEnumerable ||
                originalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T;
        }

        private static bool IsImmediateFreshArrayLinqSource(
            IOperation sourceOperation,
            Compilation compilation)
        {
            var unwrappedSource = PurityAnalysisEngine.SkipImplicitConversions(sourceOperation) ?? sourceOperation;
            if (unwrappedSource is not IInvocationOperation invocationOperation ||
                invocationOperation.Type is not IArrayTypeSymbol)
            {
                return false;
            }

            var originalDefinition = invocationOperation.TargetMethod.OriginalDefinition;
            return PurityAnalysisEngine.IsKnownFreshOwnedArrayReturningMember(originalDefinition, compilation) ||
                PurityAnalysisEngine.IsKnownPureBCLArrayFactoryOperation(unwrappedSource, out _);
        }

        private static IEnumerable<IMethodSymbol> EnumerateComparerImplementations(ITypeSymbol comparerType)
        {
            if (comparerType is not INamedTypeSymbol namedComparerType)
            {
                yield break;
            }

            var seen = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var interfaceType in namedComparerType.AllInterfaces)
            {
                if (!IsComparerInterface(interfaceType))
                {
                    continue;
                }

                foreach (var interfaceMethod in interfaceType.GetMembers().OfType<IMethodSymbol>())
                {
                    var implementation = namedComparerType.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol;
                    if (implementation == null || implementation.DeclaringSyntaxReferences.Length == 0)
                    {
                        continue;
                    }

                    if (seen.Add(implementation.OriginalDefinition))
                    {
                        yield return implementation;
                    }
                }
            }
        }

        private static bool IsComparerInterface(INamedTypeSymbol typeSymbol)
        {
            var displayString = typeSymbol.OriginalDefinition.ToDisplayString();
            return displayString == "System.Collections.Generic.IEqualityComparer<T>" ||
                displayString == "System.Collections.Generic.IComparer<T>";
        }

        private static bool IsUnresolvedComparerDispatch(ITypeSymbol comparerType)
        {
            if (comparerType is ITypeParameterSymbol typeParameter)
            {
                return typeParameter.ConstraintTypes
                    .OfType<INamedTypeSymbol>()
                    .Any(IsComparerOrDerivedInterface);
            }

            if (comparerType is not INamedTypeSymbol namedComparerType)
            {
                return false;
            }

            if (IsComparerInterface(namedComparerType))
            {
                return true;
            }

            if (namedComparerType.TypeKind != TypeKind.Interface && !namedComparerType.IsAbstract)
            {
                return false;
            }

            return IsComparerOrDerivedInterface(namedComparerType);
        }

        private static bool IsComparerOrDerivedInterface(INamedTypeSymbol typeSymbol)
        {
            return IsComparerInterface(typeSymbol) || typeSymbol.AllInterfaces.Any(IsComparerInterface);
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateAllNamedTypes(INamespaceSymbol root)
        {
            foreach (var member in root.GetMembers())
            {
                if (member is INamespaceSymbol ns)
                {
                    foreach (var inner in EnumerateAllNamedTypes(ns))
                    {
                        yield return inner;
                    }
                }
                else if (member is INamedTypeSymbol type)
                {
                    yield return type;
                    foreach (var nested in EnumerateNestedTypes(type))
                    {
                        yield return nested;
                    }
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol type)
        {
            foreach (var member in type.GetTypeMembers())
            {
                yield return member;
                foreach (var nested in EnumerateNestedTypes(member))
                {
                    yield return nested;
                }
            }
        }

        private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol potentialBase)
        {
            for (var t = type.BaseType; t != null; t = t.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, potentialBase.OriginalDefinition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool OverridesTargetMethod(IMethodSymbol method, IMethodSymbol target)
        {
            var current = method.OverriddenMethod;
            while (current != null)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, target.OriginalDefinition))
                {
                    return true;
                }

                current = current.OverriddenMethod;
            }

            return false;
        }

    }
}
