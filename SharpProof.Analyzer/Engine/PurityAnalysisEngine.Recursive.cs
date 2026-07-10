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
        internal static PurityAnalysisResult DeterminePurityRecursiveInternal(
            IMethodSymbol methodSymbol,
            SemanticModel semanticModel,
            INamedTypeSymbol enforcePureAttributeSymbol,
            INamedTypeSymbol? allowSynchronizationAttributeSymbol,
            HashSet<IMethodSymbol> visited,
            Dictionary<IMethodSymbol, PurityAnalysisResult> purityCache,
            SmtAnalysisService smtAnalysis,
            SharpProofAttributeIdentityPolicy attributePolicy,
            CancellationToken cancellationToken,
            CompilationPurityService? purityService = null)
        {

            cancellationToken.ThrowIfCancellationRequested();
            var activeSmtAnalysis = smtAnalysis ?? throw new ArgumentNullException(nameof(smtAnalysis));
            var indent = new string(' ', visited.Count * 2);



            if (purityCache.TryGetValue(methodSymbol, out var cachedResult))
            {
                return cachedResult;
            }


            if (!visited.Add(methodSymbol))
            {
                var recursiveResult = PurityAnalysisResult.ImpureUnknownLocation.WithEvidence(
                    PurityEvidence.Create(
                        "unsupported_operation",
                        ruleName: "RecursivePurityAnalysis",
                        symbol: methodSymbol,
                        catalogSource: "recursive_call"));
                purityCache[methodSymbol] = recursiveResult;
                return recursiveResult;
            }

            try
            {
                var declaringSyntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);

                if (HasImpureAttribute(methodSymbol))
                {
                    var explicitlyImpureResult = ImpureResult(
                        declaringSyntax,
                        "impure_boundary_attribute",
                        symbol: methodSymbol,
                        catalogSource: "attribute");
                    purityCache[methodSymbol] = explicitlyImpureResult;
                    return explicitlyImpureResult;
                }

                if (HasPureExternalAttribute(methodSymbol))
                {
                    purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                    return PurityAnalysisResult.Pure;
                }

                if (IsInConfiguredImpureNamespaceOrType(methodSymbol) && !IsConfiguredKnownPureMember(methodSymbol))
                {
                    var configuredImpureResult = ImpureResult(
                        declaringSyntax,
                        "catalog_hit",
                        "KnownImpureMethod",
                        methodSymbol,
                        "known_impure_namespace_or_type");
                    purityCache[methodSymbol] = configuredImpureResult;
                    return configuredImpureResult;
                }

                var trustedMetadataPurity = GetTrustedMethodPurityMetadata(methodSymbol, semanticModel.Compilation);
                var knownImpureMemberSource = trustedMetadataPurity.KnownImpureMemberSource;
                var hasConfiguredKnownImpureMember = trustedMetadataPurity.HasConfiguredKnownImpureMember;
                var hasTrustedGeneratedPurity = trustedMetadataPurity.HasTrustedGeneratedPurity;
                var generatedPurity = trustedMetadataPurity.GeneratedPurity;

                if (hasConfiguredKnownImpureMember)
                {
                    var configuredKnownImpureResult = ImpureResult(
                        declaringSyntax,
                        "impure_callee",
                        "KnownImpureMethod",
                        methodSymbol,
                        knownImpureMemberSource);
                    purityCache[methodSymbol] = configuredKnownImpureResult;
                    return configuredKnownImpureResult;
                }

                if (hasTrustedGeneratedPurity)
                {
                    if (generatedPurity.IsPure)
                    {
                        purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                        return PurityAnalysisResult.Pure;
                    }

                    if (!generatedPurity.IsPure)
                    {
                        var generatedResult = ImpureResult(
                            declaringSyntax,
                            generatedPurity.PrimaryCategory,
                            symbol: methodSymbol,
                            catalogSource: "generated_purity_summary");
                        purityCache[methodSymbol] = generatedResult;
                        return generatedResult;
                    }
                }

                if (knownImpureMemberSource != null)
                {
                    var knownImpureResult = ImpureResult(
                        declaringSyntax,
                        "catalog_hit",
                        "KnownImpureMethod",
                        methodSymbol,
                        knownImpureMemberSource);
                    purityCache[methodSymbol] = knownImpureResult;
                    return knownImpureResult;
                }


                if (!hasTrustedGeneratedPurity && IsKnownPureBCLMember(methodSymbol, semanticModel.Compilation))
                {
                    purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                    return PurityAnalysisResult.Pure;
                }


                SyntaxNode? bodySyntaxNode = GetBodySyntaxNode(methodSymbol, cancellationToken);


                if (methodSymbol.ReturnsByRef)
                {

                    SyntaxNode? locationSyntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken)?.DescendantNodesAndSelf()
                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.RefTypeSyntax>()
                        .FirstOrDefault();

                    locationSyntax ??= methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken)?.DescendantNodesAndSelf()
                                            .FirstOrDefault(n => n is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax ins && ins.Identifier.ValueText == methodSymbol.Name)
                                            ?.Parent;

                    var escapeSyntax = locationSyntax ?? bodySyntaxNode;
                    purityCache[methodSymbol] = escapeSyntax == null
                        ? ImpureResult(bodySyntaxNode)
                        : PurityAnalysisResult.Impure(
                            escapeSyntax,
                            CreateByRefReturnEscapeEvidence(methodSymbol, escapeSyntax));
                    return purityCache[methodSymbol];
                }



                if (methodSymbol.IsExtern)
                {
                    var externResult = ImpureResult(
                        declaringSyntax,
                        "unknown_external_call",
                        "MethodInvocationPurityRule",
                        methodSymbol,
                        "extern");
                    purityCache[methodSymbol] = externResult;
                    return externResult;
                }

                if (methodSymbol.IsAbstract || bodySyntaxNode == null)
                {
                    if (methodSymbol.DeclaringSyntaxReferences.Length > 0 &&
                        methodSymbol.ContainingType?.Locations.Any(location => !location.IsInMetadata) == true &&
                        (methodSymbol.IsAbstract || methodSymbol.ContainingType?.TypeKind == TypeKind.Interface))
                    {
                        purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                        return PurityAnalysisResult.Pure;
                    }

                    if (methodSymbol.MethodKind == MethodKind.PropertyGet &&
                        methodSymbol.DeclaringSyntaxReferences.Length > 0 &&
                        methodSymbol.ContainingType?.Locations.Any(location => !location.IsInMetadata) == true &&
                        !methodSymbol.IsAbstract &&
                        methodSymbol.ContainingType?.TypeKind != TypeKind.Interface)
                    {
                        purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                        return PurityAnalysisResult.Pure;
                    }

                    if ((methodSymbol.MethodKind == MethodKind.Constructor ||
                         methodSymbol.MethodKind == MethodKind.StaticConstructor) &&
                        !methodSymbol.IsExtern &&
                        methodSymbol.ContainingType?.Locations.Any(location => !location.IsInMetadata) == true)
                    {
                        purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                        return PurityAnalysisResult.Pure;
                    }

                    if (hasTrustedGeneratedPurity && !generatedPurity.IsPure)
                    {
                        var generatedNoBodyResult = ImpureResult(
                            declaringSyntax,
                            generatedPurity.PrimaryCategory,
                            "MethodInvocationPurityRule",
                            methodSymbol,
                            "generated_purity_summary");
                        purityCache[methodSymbol] = generatedNoBodyResult;
                        return generatedNoBodyResult;
                    }

                    if (TryCreateBclFallbackImpurity(
                            methodSymbol,
                            declaringSyntax,
                            operation: null,
                            ruleName: "MethodInvocationPurityRule",
                            out var bclFallbackNoBodyResult))
                    {
                        purityCache[methodSymbol] = bclFallbackNoBodyResult;
                        return bclFallbackNoBodyResult;
                    }

                    var noBodyResult = ImpureResult(
                        declaringSyntax,
                        "unknown_external_call",
                        "MethodInvocationPurityRule",
                        methodSymbol,
                        "no_body");
                    purityCache[methodSymbol] = noBodyResult;
                    return noBodyResult;
                }


                IOperation? methodBodyIOperation = null;
                if (bodySyntaxNode != null)
                {
                    try
                    {
                        methodBodyIOperation = semanticModel.GetOperation(bodySyntaxNode, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        methodBodyIOperation = null;
                    }
                }

                PurityAnalysisResult result = PurityAnalysisResult.Pure;
                var mergedDelegateTargetsFromCfg = ImmutableDictionary.Create<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default);
                var mergedOwnedArrayFlowCapturesFromCfg = ImmutableHashSet<CaptureId>.Empty;
                var mergedOwnedLocalArraysFromCfg = ImmutableHashSet.Create<ISymbol>(SymbolEqualityComparer.Default);
                var mergedLocalConcreteTypesFromCfg = ImmutableDictionary.Create<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
                var mergedPathStateFromCfg = new SymbolicState();
                if (bodySyntaxNode != null)
                {
                    bool requiresNestedBodyFallback = methodBodyIOperation?.Parent != null;
                    if (requiresNestedBodyFallback && methodBodyIOperation != null)
                    {
                        result = AnalyzeOperationSubtreePurity(
                            methodBodyIOperation,
                            semanticModel,
                            enforcePureAttributeSymbol,
                            allowSynchronizationAttributeSymbol,
                            visited,
                            methodSymbol,
                            purityCache,
                            activeSmtAnalysis,
                            attributePolicy,
                            purityService,
                            cancellationToken);
                    }
                    else
                    {
                        result = AnalyzePurityUsingCFGInternal(
                            bodySyntaxNode,
                            semanticModel,
                            enforcePureAttributeSymbol,
                            allowSynchronizationAttributeSymbol,
                            visited,
                            methodSymbol,
                            purityCache,
                            activeSmtAnalysis,
                            attributePolicy,
                            purityService,
                            cancellationToken,
                            out mergedDelegateTargetsFromCfg,
                            out mergedOwnedArrayFlowCapturesFromCfg,
                            out mergedOwnedLocalArraysFromCfg,
                            out mergedLocalConcreteTypesFromCfg,
                            out mergedPathStateFromCfg);
                    }

                }


                PurityAnalysisState? postCfgExitResourceState = null;
                if (result.IsPure)
                {

                    if (methodBodyIOperation != null)
                    {
                        var pureAttrSymbolForContext = semanticModel.Compilation.GetTypeByMetadataName("SharpProof.Attributes.PureAttribute");
                        var postCfgContext = new Rules.PurityAnalysisContext(
                            semanticModel,
                            enforcePureAttributeSymbol,
                            pureAttrSymbolForContext,
                            allowSynchronizationAttributeSymbol,
                            visited,
                            purityCache,
                            methodSymbol,
                            _purityRules,
                            cancellationToken,
                            purityService,
                            activeSmtAnalysis,
                            attributePolicy);


                        var postCfgReturnState = new PurityAnalysisState(
                            false,
                            null,
                            mergedDelegateTargetsFromCfg,
                            null,
                            ownedLocalArraySymbols: mergedOwnedLocalArraysFromCfg,
                            localConcreteTypes: mergedLocalConcreteTypesFromCfg,
                            pathState: mergedPathStateFromCfg,
                            ownedArrayFlowCaptures: mergedOwnedArrayFlowCapturesFromCfg);
                        postCfgExitResourceState = AddScopeEndResourceDisposeFacts(
                            AddStraightLineResourceActionFacts(
                                postCfgReturnState,
                                methodBodyIOperation,
                                semanticModel,
                                cancellationToken),
                            methodBodyIOperation,
                            cancellationToken);
                        var visibleReturnOperations = ExecutionVisibility.VisibleDescendants(methodBodyIOperation)
                            .OfType<IReturnOperation>()
                            .ToArray();
                        if (visibleReturnOperations.Length == 1)
                        {
                            postCfgExitResourceState = AddReturnedOwnedResourceFacts(
                                postCfgExitResourceState.Value,
                                visibleReturnOperations[0],
                                postCfgExitResourceState.Value);
                        }

                        foreach (var returnOp in visibleReturnOperations)
                        {
                            if (returnOp.ReturnedValue != null)
                            {
                                var returnState = AddCompletedStraightLineUsingDisposeFacts(
                                    postCfgReturnState,
                                    methodBodyIOperation,
                                    returnOp,
                                    cancellationToken);
                                var returnPurity = CheckSingleOperation(returnOp, postCfgContext, returnState);
                                if (!returnPurity.IsPure)
                                {
                                    if (IsImpurityProvenUnreachable(returnPurity, semanticModel, activeSmtAnalysis, cancellationToken))
                                    {
                                        continue;
                                    }

                                    result = returnPurity;
                                    goto PostCfgChecksDone;
                                }
                            }
                        }

                        foreach (var usingOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation).Where(op => op.Kind == OperationKind.Using || op.Kind == OperationKind.UsingDeclaration))
                        {
                            var usingResult = CheckSingleOperation(usingOp, postCfgContext, postCfgReturnState);
                            if (!usingResult.IsPure)
                            {
                                result = usingResult;
                                goto PostCfgChecksDone;
                            }
                        }

                        foreach (var forEachOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation).OfType<IForEachLoopOperation>())
                        {
                            if (ShouldSkipPostCfgDirectPurityProbe(forEachOp, semanticModel, activeSmtAnalysis, cancellationToken))
                            {
                                continue;
                            }

                            var forEachResult = LoopPurityRule.CheckForEachEnumeratorPurity(forEachOp.Collection, postCfgContext);
                            if (!forEachResult.IsPure)
                            {
                                result = forEachResult;
                                goto PostCfgChecksDone;
                            }

                            var asyncForEachResult = LoopPurityRule.CheckForEachAsyncEnumeratorPurity(forEachOp.Collection, postCfgContext);
                            if (!asyncForEachResult.IsPure)
                            {
                                result = asyncForEachResult;
                                goto PostCfgChecksDone;
                            }
                        }


                        foreach (var firstThrowOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation).OfType<IThrowOperation>())
                        {
                            if (ExecutionVisibility.IsInStaticallyUnreachableBranchUsingSmt(
                                    firstThrowOp.Syntax,
                                    semanticModel,
                                    cancellationToken,
                                    activeSmtAnalysis))
                            {
                                continue;
                            }

                            if (firstThrowOp.Exception != null)
                            {
                                var exResult = CheckSingleOperation(firstThrowOp.Exception, postCfgContext, PurityAnalysisState.Pure);
                                if (!exResult.IsPure)
                                {
                                    result = PurityAnalysisResult.Impure(
                                        exResult.ImpureSyntaxNode ?? firstThrowOp.Syntax,
                                        exResult.Evidence);
                                    goto PostCfgChecksDone;
                                }
                            }

                            result = PurityAnalysisResult.Impure(
                                firstThrowOp.Syntax,
                                PurityEvidence.Create(
                                    "throw",
                                    ruleName: "ThrowOperationPurityRule",
                                    operation: firstThrowOp));
                            goto PostCfgChecksDone;
                        }


                        foreach (var tryOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation).OfType<ITryOperation>())
                        {
                            foreach (var catchClause in tryOp.Catches)
                            {
                                var catchResult = AnalyzeOperationSubtreePurity(catchClause, semanticModel, enforcePureAttributeSymbol, allowSynchronizationAttributeSymbol, visited, methodSymbol, purityCache, activeSmtAnalysis, attributePolicy, purityService, cancellationToken);
                                if (!catchResult.IsPure)
                                {
                                    result = catchResult;
                                    goto PostCfgChecksDone;
                                }
                            }
                            if (tryOp.Finally != null)
                            {
                                var finallyResult = AnalyzeOperationSubtreePurity(tryOp.Finally, semanticModel, enforcePureAttributeSymbol, allowSynchronizationAttributeSymbol, visited, methodSymbol, purityCache, activeSmtAnalysis, attributePolicy, purityService, cancellationToken);
                                if (!finallyResult.IsPure)
                                {
                                    result = finallyResult;
                                    goto PostCfgChecksDone;
                                }
                            }
                        }


                        foreach (var invocationOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation).OfType<IInvocationOperation>())
                        {
                            if (ShouldSkipPostCfgDirectPurityProbe(invocationOp, semanticModel, activeSmtAnalysis, cancellationToken))
                            {
                                continue;
                            }

                            var hasSemanticKnownImpureCatalogSource = TryGetSemanticKnownImpureCatalogSource(
                                invocationOp,
                                out var semanticKnownImpureCatalogSource);
                            if (invocationOp.TargetMethod != null &&
                                !IsArrayAsReadOnlyInvocation(invocationOp) &&
                                !IsArrayInterfaceGetEnumeratorInvocation(invocationOp, semanticModel, cancellationToken) &&
                                !IsTransientCharArrayConsumedByStringConstructor(invocationOp, semanticModel))
                            {
                                var targetMethod = invocationOp.TargetMethod.OriginalDefinition;
                                if (hasSemanticKnownImpureCatalogSource)
                                {
                                    result = ImpureResult(
                                        invocationOp,
                                        "catalog_hit",
                                        "MethodInvocationPurityRule",
                                        targetMethod,
                                        semanticKnownImpureCatalogSource);
                                    goto PostCfgChecksDone;
                                }

                                if (IsInvariantCultureDeterministicParseInvocation(invocationOp))
                                {
                                    continue;
                                }

                                var invocationMetadataPurity = GetTrustedMethodPurityMetadata(
                                    targetMethod,
                                    semanticModel.Compilation);
                                var knownImpureSource = invocationMetadataPurity.KnownImpureMemberSource;
                                var hasConfiguredKnownImpure = invocationMetadataPurity.HasConfiguredKnownImpureMember;
                                var postCfgGeneratedPurity = invocationMetadataPurity.GeneratedPurity;
                                var hasTrustedGeneratedPurityForInvocation = invocationMetadataPurity.HasTrustedGeneratedPurity;

                                if (hasConfiguredKnownImpure)
                                {
                                    result = ImpureResult(
                                        invocationOp,
                                        "catalog_hit",
                                        "MethodInvocationPurityRule",
                                        targetMethod,
                                        knownImpureSource);
                                    goto PostCfgChecksDone;
                                }

                                if (hasTrustedGeneratedPurityForInvocation &&
                                    !Rules.MethodInvocationPurityRule.ShouldDeferToSpecializedDispatchPurity(targetMethod))
                                {
                                    if (postCfgGeneratedPurity.IsPure)
                                    {
                                        continue;
                                    }

                                    if (!postCfgGeneratedPurity.IsPure)
                                    {
                                        var invocationRuleResult = CheckSingleOperation(invocationOp, postCfgContext, postCfgReturnState);
                                        if (invocationRuleResult.IsPure)
                                        {
                                            continue;
                                        }

                                        result = invocationRuleResult;
                                        goto PostCfgChecksDone;
                                    }
                                }

                                if (knownImpureSource != null)
                                {
                                    result = ImpureResult(
                                        invocationOp,
                                        "catalog_hit",
                                        "MethodInvocationPurityRule",
                                        targetMethod,
                                        knownImpureSource);
                                    goto PostCfgChecksDone;
                                }
                            }
                        }

                        foreach (var invocationOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation).OfType<IInvocationOperation>())
                        {
                            if (!IsParameterlessDisposeInvocation(invocationOp) ||
                                invocationOp.Syntax.FirstAncestorOrSelf<FinallyClauseSyntax>() != null ||
                                ShouldSkipPostCfgDirectPurityProbe(invocationOp, semanticModel, activeSmtAnalysis, cancellationToken))
                            {
                                continue;
                            }

                            var invocationResult = CheckSingleOperation(invocationOp, postCfgContext, postCfgReturnState);
                            if (!invocationResult.IsPure)
                            {
                                result = invocationResult;
                                goto PostCfgChecksDone;
                            }
                        }

                        var directThrowOnlySyntax = TryGetDirectThrowOnlySyntax(bodySyntaxNode);
                        if (directThrowOnlySyntax != null)
                        {
                            result = PurityAnalysisResult.Impure(
                                directThrowOnlySyntax,
                                PurityEvidence.Create(
                                    "throw",
                                    ruleName: "ThrowOperationPurityRule",
                                    syntaxNode: directThrowOnlySyntax));
                            goto PostCfgChecksDone;
                        }


                        foreach (var operation in ExecutionVisibility.VisibleDescendants(methodBodyIOperation))
                        {
                            if (ShouldSkipPostCfgDirectPurityProbe(operation, semanticModel, activeSmtAnalysis, cancellationToken))
                            {
                                continue;
                            }

                            bool isChecked = false;
                            IMethodSymbol? operatorMethod = null;

                            if (operation is IBinaryOperation binaryOp && binaryOp.IsChecked)
                            {
                                isChecked = true;
                                operatorMethod = binaryOp.OperatorMethod;
                            }
                            else if (operation is IUnaryOperation unaryOp && unaryOp.IsChecked)
                            {
                                isChecked = true;
                                operatorMethod = unaryOp.OperatorMethod;
                            }
                            else if (operation is ICompoundAssignmentOperation compoundAssignmentOp &&
                                     compoundAssignmentOp.OperatorMethod != null &&
                                     ShouldAnalyzeCompoundAssignmentOperator(compoundAssignmentOp.OperatorMethod.OriginalDefinition))
                            {
                                isChecked = true;
                                operatorMethod = compoundAssignmentOp.OperatorMethod.OriginalDefinition;
                            }

                            if (isChecked && operatorMethod != null)
                            {
                                var contextForOp = new Rules.PurityAnalysisContext(
                                    semanticModel,
                                    enforcePureAttributeSymbol,
                                    semanticModel.Compilation.GetTypeByMetadataName("SharpProof.Attributes.PureAttribute"),
                                    allowSynchronizationAttributeSymbol,
                                    visited,
                                    purityCache,
                                    methodSymbol,
                                    _purityRules,
                                    cancellationToken,
                                    purityService,
                                    activeSmtAnalysis,
                                    attributePolicy);
                                var operatorPurity = GetCalleePurity(operatorMethod, contextForOp);

                                if (!operatorPurity.IsPure)
                                {
                                    result = PurityAnalysisResult.Impure(operation.Syntax);
                                    goto PostCfgChecksDone;
                                }
                            }
                        }
                    }
                    else
                    {
                    }
                }

            PostCfgChecksDone:;

                if (result.IsPure &&
                    postCfgExitResourceState.HasValue &&
                    TryCreateMissingOwnedResourceDisposalResult(
                        postCfgExitResourceState.Value,
                        methodSymbol,
                        semanticModel,
                        cancellationToken,
                        out var missingDisposeResult))
                {
                    result = missingDisposeResult;
                }

                purityCache[methodSymbol] = result;
                return result;
            }
            finally
            {
                visited.Remove(methodSymbol);
            }
        }
    }
}
