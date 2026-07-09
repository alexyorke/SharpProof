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
            CancellationToken cancellationToken,
            CompilationPurityService? purityService = null)
        {

            cancellationToken.ThrowIfCancellationRequested();
            var activeSmtAnalysis = smtAnalysis ?? throw new ArgumentNullException(nameof(smtAnalysis));
            var indent = new string(' ', visited.Count * 2);
            LogDebug($"{indent}>> Enter DeterminePurity: {methodSymbol.ToDisplayString()}");



            if (purityCache.TryGetValue(methodSymbol, out var cachedResult))
            {
                LogDebug($"{indent}  Purity CACHED: {cachedResult.IsPure} for {methodSymbol.ToDisplayString()}");
                LogDebug($"{indent}<< Exit DeterminePurity (Cached): {methodSymbol.ToDisplayString()}");
                return cachedResult;
            }


            if (!visited.Add(methodSymbol))
            {
                LogDebug($"{indent}  Recursion DETECTED for {methodSymbol.ToDisplayString()}. Assuming impure for this path.");
                var recursiveResult = PurityAnalysisResult.ImpureUnknownLocation.WithEvidence(
                    PurityEvidence.Create(
                        "unsupported_operation",
                        ruleName: "RecursivePurityAnalysis",
                        symbol: methodSymbol,
                        catalogSource: "recursive_call"));
                purityCache[methodSymbol] = recursiveResult;
                LogDebug($"{indent}<< Exit DeterminePurity (Recursion): {methodSymbol.ToDisplayString()}");
                return recursiveResult;
            }

            try
            {
                var declaringSyntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);

                if (HasImpureAttribute(methodSymbol))
                {
                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is marked [Impure].");
                    var explicitlyImpureResult = ImpureResult(
                        declaringSyntax,
                        "impure_boundary_attribute",
                        symbol: methodSymbol,
                        catalogSource: "attribute");
                    purityCache[methodSymbol] = explicitlyImpureResult;
                    LogDebug($"{indent}<< Exit DeterminePurity ([Impure]): {methodSymbol.ToDisplayString()}");
                    return explicitlyImpureResult;
                }

                if (HasPureExternalAttribute(methodSymbol))
                {
                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is marked [PureExternal].");
                    purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                    LogDebug($"{indent}<< Exit DeterminePurity ([PureExternal]): {methodSymbol.ToDisplayString()}");
                    return PurityAnalysisResult.Pure;
                }

                if (IsInConfiguredImpureNamespaceOrType(methodSymbol) && !IsConfiguredKnownPureMember(methodSymbol))
                {
                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is in a configured impure namespace/type.");
                    var configuredImpureResult = ImpureResult(
                        declaringSyntax,
                        "catalog_hit",
                        "KnownImpureMethod",
                        methodSymbol,
                        "known_impure_namespace_or_type");
                    purityCache[methodSymbol] = configuredImpureResult;
                    LogDebug($"{indent}<< Exit DeterminePurity (Configured Impure Namespace/Type): {methodSymbol.ToDisplayString()}");
                    return configuredImpureResult;
                }

                var trustedMetadataPurity = GetTrustedMethodPurityMetadata(methodSymbol, semanticModel.Compilation);
                var knownImpureMemberSource = trustedMetadataPurity.KnownImpureMemberSource;
                var hasConfiguredKnownImpureMember = trustedMetadataPurity.HasConfiguredKnownImpureMember;
                var hasTrustedGeneratedPurity = trustedMetadataPurity.HasTrustedGeneratedPurity;
                var generatedPurity = trustedMetadataPurity.GeneratedPurity;

                if (hasConfiguredKnownImpureMember)
                {
                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is configured known impure.");
                    var configuredKnownImpureResult = ImpureResult(
                        declaringSyntax,
                        "impure_callee",
                        "KnownImpureMethod",
                        methodSymbol,
                        knownImpureMemberSource);
                    purityCache[methodSymbol] = configuredKnownImpureResult;
                    LogDebug($"{indent}<< Exit DeterminePurity (Configured Known Impure): {methodSymbol.ToDisplayString()}");
                    return configuredKnownImpureResult;
                }

                if (hasTrustedGeneratedPurity)
                {
                    if (generatedPurity.IsPure)
                    {
                        LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is trusted pure from generated purity summary.");
                        purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                        return PurityAnalysisResult.Pure;
                    }

                    if (!generatedPurity.IsPure)
                    {
                        LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is trusted impure from generated purity summary.");
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
                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is known impure.");
                    var knownImpureResult = ImpureResult(
                        declaringSyntax,
                        "catalog_hit",
                        "KnownImpureMethod",
                        methodSymbol,
                        knownImpureMemberSource);
                    purityCache[methodSymbol] = knownImpureResult;
                    LogDebug($"{indent}<< Exit DeterminePurity (Known Impure): {methodSymbol.ToDisplayString()}");
                    return knownImpureResult;
                }


                if (!hasTrustedGeneratedPurity && IsKnownPureBCLMember(methodSymbol, semanticModel.Compilation))
                {
                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is known pure BCL member.");
                    purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                    LogDebug($"{indent}<< Exit DeterminePurity (Known Pure): {methodSymbol.ToDisplayString()}");
                    return PurityAnalysisResult.Pure;
                }


                SyntaxNode? bodySyntaxNode = GetBodySyntaxNode(methodSymbol, cancellationToken);


                if (methodSymbol.ReturnsByRef)
                {
                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} returns by ref. IMPURE.");

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
                    LogDebug($"{indent}<< Exit DeterminePurity (ReturnsByRef): {methodSymbol.ToDisplayString()}");
                    return purityCache[methodSymbol];
                }



                if (methodSymbol.IsExtern)
                {
                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is extern. Assuming impure due unknown implementation.");
                    var externResult = ImpureResult(
                        declaringSyntax,
                        "unknown_external_call",
                        "MethodInvocationPurityRule",
                        methodSymbol,
                        "extern");
                    purityCache[methodSymbol] = externResult;
                    LogDebug($"{indent}<< Exit DeterminePurity (Extern): {methodSymbol.ToDisplayString()}");
                    return externResult;
                }

                if (methodSymbol.IsAbstract || bodySyntaxNode == null)
                {
                    if (methodSymbol.DeclaringSyntaxReferences.Length > 0 &&
                        methodSymbol.ContainingType?.Locations.Any(location => !location.IsInMetadata) == true &&
                        (methodSymbol.IsAbstract || methodSymbol.ContainingType?.TypeKind == TypeKind.Interface))
                    {
                        LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is a source contract without an explicit body. Deferring validation to dispatch or implementation sites.");
                        purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                        LogDebug($"{indent}<< Exit DeterminePurity (Source Contract Without Body): {methodSymbol.ToDisplayString()}");
                        return PurityAnalysisResult.Pure;
                    }

                    if (methodSymbol.MethodKind == MethodKind.PropertyGet &&
                        methodSymbol.DeclaringSyntaxReferences.Length > 0 &&
                        methodSymbol.ContainingType?.Locations.Any(location => !location.IsInMetadata) == true &&
                        !methodSymbol.IsAbstract &&
                        methodSymbol.ContainingType?.TypeKind != TypeKind.Interface)
                    {
                        LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is a source property getter without an explicit body. Treating as pure.");
                        purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                        LogDebug($"{indent}<< Exit DeterminePurity (Source Auto Getter): {methodSymbol.ToDisplayString()}");
                        return PurityAnalysisResult.Pure;
                    }

                    if ((methodSymbol.MethodKind == MethodKind.Constructor ||
                         methodSymbol.MethodKind == MethodKind.StaticConstructor) &&
                        !methodSymbol.IsExtern &&
                        methodSymbol.ContainingType?.Locations.Any(location => !location.IsInMetadata) == true)
                    {
                        LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is a source constructor without an explicit body. Treating as pure.");
                        purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                        LogDebug($"{indent}<< Exit DeterminePurity (Source Constructor Without Body): {methodSymbol.ToDisplayString()}");
                        return PurityAnalysisResult.Pure;
                    }

                    if (hasTrustedGeneratedPurity && !generatedPurity.IsPure)
                    {
                        LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} has no body but does have trusted non-pure generated summary evidence.");
                        var generatedNoBodyResult = ImpureResult(
                            declaringSyntax,
                            generatedPurity.PrimaryCategory,
                            "MethodInvocationPurityRule",
                            methodSymbol,
                            "generated_purity_summary");
                        purityCache[methodSymbol] = generatedNoBodyResult;
                        LogDebug($"{indent}<< Exit DeterminePurity (Abstract/NoBody Generated Summary): {methodSymbol.ToDisplayString()}");
                        return generatedNoBodyResult;
                    }

                    if (TryCreateBclFallbackImpurity(
                            methodSymbol,
                            declaringSyntax,
                            operation: null,
                            ruleName: "MethodInvocationPurityRule",
                            out var bclFallbackNoBodyResult))
                    {
                        LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} has no trusted purity evidence. Reporting BCL fallback guess.");
                        purityCache[methodSymbol] = bclFallbackNoBodyResult;
                        LogDebug($"{indent}<< Exit DeterminePurity (Abstract/NoBody BCL Fallback): {methodSymbol.ToDisplayString()}");
                        return bclFallbackNoBodyResult;
                    }

                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is abstract or has no body AND lacks trusted purity evidence. Assuming impure.");
                    var noBodyResult = ImpureResult(
                        declaringSyntax,
                        "unknown_external_call",
                        "MethodInvocationPurityRule",
                        methodSymbol,
                        "no_body");
                    purityCache[methodSymbol] = noBodyResult;
                    LogDebug($"{indent}<< Exit DeterminePurity (Abstract/NoBody): {methodSymbol.ToDisplayString()}");
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
                        LogDebug($"{indent}  Post-CFG: Error getting IOperation for method body: {ex.Message}");
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
                        LogDebug($"{indent}Analyzing body of {methodSymbol.ToDisplayString()} using nested subtree fallback.");
                        result = AnalyzeOperationSubtreePurity(
                            methodBodyIOperation,
                            semanticModel,
                            enforcePureAttributeSymbol,
                            allowSynchronizationAttributeSymbol,
                            visited,
                            methodSymbol,
                            purityCache,
                            activeSmtAnalysis,
                            purityService,
                            cancellationToken);
                    }
                    else
                    {
                        LogDebug($"{indent}Analyzing body of {methodSymbol.ToDisplayString()} using CFG.");
                        result = AnalyzePurityUsingCFGInternal(
                            bodySyntaxNode,
                            semanticModel,
                            enforcePureAttributeSymbol,
                            allowSynchronizationAttributeSymbol,
                            visited,
                            methodSymbol,
                            purityCache,
                            activeSmtAnalysis,
                            purityService,
                            cancellationToken,
                            out mergedDelegateTargetsFromCfg,
                            out mergedOwnedArrayFlowCapturesFromCfg,
                            out mergedOwnedLocalArraysFromCfg,
                            out mergedLocalConcreteTypesFromCfg,
                            out mergedPathStateFromCfg);
                    }

                    LogDebug($"{indent}  CFG Analysis Result for {methodSymbol.ToDisplayString()}: IsPure={result.IsPure}, ImpureNode={result.ImpureSyntaxNode?.Kind()}");
                }


                PurityAnalysisState? postCfgExitResourceState = null;
                if (result.IsPure)
                {
                    LogDebug($"{indent}Post-CFG: CFG Result was Pure. Performing Post-CFG checks for {methodSymbol.ToDisplayString()}.");

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
                            activeSmtAnalysis);


                        LogDebug($"{indent}  Post-CFG: Checking ReturnOperations (with merged delegate map from CFG)...");
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

                                    LogDebug($"{indent}    Post-CFG: Return value IMPURE: {returnOp.ReturnedValue.Syntax}");
                                    result = returnPurity;
                                    goto PostCfgChecksDone;
                                }
                            }
                        }
                        LogDebug($"{indent}  Post-CFG: ReturnOperations check complete (result still pure).");

                        LogDebug($"{indent}  Post-CFG: Checking UsingOperations for implicit Dispose purity...");
                        foreach (var usingOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation).Where(op => op.Kind == OperationKind.Using || op.Kind == OperationKind.UsingDeclaration))
                        {
                            var usingResult = CheckSingleOperation(usingOp, postCfgContext, postCfgReturnState);
                            if (!usingResult.IsPure)
                            {
                                LogDebug($"{indent}    Post-CFG: Using operation is IMPURE: {usingOp.Syntax}");
                                result = usingResult;
                                goto PostCfgChecksDone;
                            }
                        }
                        LogDebug($"{indent}  Post-CFG: UsingOperations check complete (result still pure).");

                        LogDebug($"{indent}  Post-CFG: Checking ForEach enumerator runtime purity...");
                        foreach (var forEachOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation).OfType<IForEachLoopOperation>())
                        {
                            if (ShouldSkipPostCfgDirectPurityProbe(forEachOp, semanticModel, activeSmtAnalysis, cancellationToken))
                            {
                                LogDebug($"{indent}    Post-CFG: Skipping statically unreachable foreach enumerator runtime check: {forEachOp.Syntax}");
                                continue;
                            }

                            var forEachResult = LoopPurityRule.CheckForEachEnumeratorPurity(forEachOp.Collection, postCfgContext);
                            if (!forEachResult.IsPure)
                            {
                                LogDebug($"{indent}    Post-CFG: Foreach enumerator runtime is IMPURE: {forEachOp.Syntax}");
                                result = forEachResult;
                                goto PostCfgChecksDone;
                            }

                            var asyncForEachResult = LoopPurityRule.CheckForEachAsyncEnumeratorPurity(forEachOp.Collection, postCfgContext);
                            if (!asyncForEachResult.IsPure)
                            {
                                LogDebug($"{indent}    Post-CFG: Async foreach enumerator runtime is IMPURE: {forEachOp.Syntax}");
                                result = asyncForEachResult;
                                goto PostCfgChecksDone;
                            }
                        }
                        LogDebug($"{indent}  Post-CFG: ForEach enumerator runtime checks complete (result still pure).");


                        LogDebug($"{indent}  Post-CFG: Checking ThrowOperations...");
                        foreach (var firstThrowOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation).OfType<IThrowOperation>())
                        {
                            if (ExecutionVisibility.IsInStaticallyUnreachableBranchUsingSmt(
                                    firstThrowOp.Syntax,
                                    semanticModel,
                                    cancellationToken,
                                    activeSmtAnalysis))
                            {
                                LogDebug($"{indent}    Post-CFG: Skipping statically unreachable throw: {firstThrowOp.Syntax}");
                                continue;
                            }

                            if (firstThrowOp.Exception != null)
                            {
                                var exResult = CheckSingleOperation(firstThrowOp.Exception, postCfgContext, PurityAnalysisState.Pure);
                                if (!exResult.IsPure)
                                {
                                    LogDebug($"{indent}    Post-CFG: Throw exception expression is IMPURE: {firstThrowOp.Exception.Syntax}");
                                    result = PurityAnalysisResult.Impure(
                                        exResult.ImpureSyntaxNode ?? firstThrowOp.Syntax,
                                        exResult.Evidence);
                                    goto PostCfgChecksDone;
                                }
                            }

                            LogDebug($"{indent}    Post-CFG: Throw operation is IMPURE: {firstThrowOp.Syntax}");
                            result = PurityAnalysisResult.Impure(
                                firstThrowOp.Syntax,
                                PurityEvidence.Create(
                                    "throw",
                                    ruleName: "ThrowOperationPurityRule",
                                    operation: firstThrowOp));
                            goto PostCfgChecksDone;
                        }
                        LogDebug($"{indent}  Post-CFG: ThrowOperations check complete (result still pure).");


                        LogDebug($"{indent}  Post-CFG: Checking Unreachable Code (Try, Catch)...");
                        foreach (var tryOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation).OfType<ITryOperation>())
                        {
                            foreach (var catchClause in tryOp.Catches)
                            {
                                var catchResult = AnalyzeOperationSubtreePurity(catchClause, semanticModel, enforcePureAttributeSymbol, allowSynchronizationAttributeSymbol, visited, methodSymbol, purityCache, activeSmtAnalysis, purityService, cancellationToken);
                                if (!catchResult.IsPure)
                                {
                                    result = catchResult;
                                    goto PostCfgChecksDone;
                                }
                            }
                            if (tryOp.Finally != null)
                            {
                                var finallyResult = AnalyzeOperationSubtreePurity(tryOp.Finally, semanticModel, enforcePureAttributeSymbol, allowSynchronizationAttributeSymbol, visited, methodSymbol, purityCache, activeSmtAnalysis, purityService, cancellationToken);
                                if (!finallyResult.IsPure)
                                {
                                    result = finallyResult;
                                    goto PostCfgChecksDone;
                                }
                            }
                        }

                        LogDebug($"{indent}  Post-CFG: Skipping local function declarations; invoked local functions are checked through callee purity.");

                        LogDebug($"{indent}  Post-CFG: Checking Known Impure Invocations...");
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
                                    LogDebug($"{indent}    Post-CFG: Found semantically known impure invocation IMPURE: {invocationOp.Syntax} calling {invocationOp.TargetMethod.ToDisplayString()}");
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
                                    LogDebug($"{indent}    Post-CFG: Found configured known impure invocation IMPURE: {invocationOp.Syntax} calling {invocationOp.TargetMethod.ToDisplayString()}");
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

                                        LogDebug($"{indent}    Post-CFG: Found generated-summary impure invocation IMPURE: {invocationOp.Syntax} calling {invocationOp.TargetMethod.ToDisplayString()}");
                                        result = invocationRuleResult;
                                        goto PostCfgChecksDone;
                                    }
                                }

                                if (knownImpureSource != null)
                                {
                                    LogDebug($"{indent}    Post-CFG: Found known impure invocation IMPURE: {invocationOp.Syntax} calling {invocationOp.TargetMethod.ToDisplayString()}");
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
                        LogDebug($"{indent}  Post-CFG: Known Impure Invocations check complete (result still pure).");

                        LogDebug($"{indent}  Post-CFG: Checking Dispose invocation lifetime hazards...");
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
                                LogDebug($"{indent}    Post-CFG: Dispose invocation lifetime hazard is IMPURE: {invocationOp.Syntax}");
                                result = invocationResult;
                                goto PostCfgChecksDone;
                            }
                        }
                        LogDebug($"{indent}  Post-CFG: Dispose invocation lifetime checks complete (result still pure).");

                        var directThrowOnlySyntax = TryGetDirectThrowOnlySyntax(bodySyntaxNode);
                        if (directThrowOnlySyntax != null)
                        {
                            LogDebug($"{indent}  Post-CFG: Found direct throw-only body IMPURE: {directThrowOnlySyntax}");
                            result = PurityAnalysisResult.Impure(
                                directThrowOnlySyntax,
                                PurityEvidence.Create(
                                    "throw",
                                    ruleName: "ThrowOperationPurityRule",
                                    syntaxNode: directThrowOnlySyntax));
                            goto PostCfgChecksDone;
                        }


                        LogDebug($"{indent}  Post-CFG: Checking Checked Operations...");
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
                                LogDebug($"{indent}    Post-CFG: Found Checked Operation: {operation.Syntax} with operator method {operatorMethod.Name}");
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
                                    activeSmtAnalysis);
                                var operatorPurity = GetCalleePurity(operatorMethod, contextForOp);

                                if (!operatorPurity.IsPure)
                                {
                                    LogDebug($"{indent}    Post-CFG: Checked operator method '{operatorMethod.Name}' is IMPURE. Operation is Impure.");
                                    result = PurityAnalysisResult.Impure(operation.Syntax);
                                    goto PostCfgChecksDone;
                                }
                            }
                        }
                        LogDebug($"{indent}  Post-CFG: Checked Operations check complete (result still pure).");
                    }
                    else
                    {
                        LogDebug($"{indent}Post-CFG: methodBodyIOperation was null, skipping post-CFG checks.");
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
                LogDebug($"{indent}<< Exit DeterminePurity (Analyzed): {methodSymbol.ToDisplayString()}, Final IsPure={result.IsPure}");
                return result;
            }
            finally
            {
                visited.Remove(methodSymbol);
                LogDebug($"{indent}-- Removed Walker for: {methodSymbol.ToDisplayString()}");
            }
        }
    }
}
